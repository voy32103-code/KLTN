import os
import time
import logging
import asyncio
from typing import Any, List, Dict
from google import genai
from google.genai.errors import APIError
import httpx

logger = logging.getLogger(__name__)

class GroqResponseShim:
    """Giả lập response object của Gemini để tương thích với code hiện tại."""
    def __init__(self, text: str):
        self.text = text

class ApiClientManager:
    def __init__(self):
        # 1. Đọc và khởi tạo các key Gemini
        self.gemini_keys = []
        raw_keys = os.getenv("GEMINI_API_KEYS")
        if raw_keys:
            self.gemini_keys = [k.strip() for k in raw_keys.split(",") if k.strip()]
        
        # Fallback sang GEMINI_API_KEY nếu GEMINI_API_KEYS trống
        if not self.gemini_keys:
            single_key = os.getenv("GEMINI_API_KEY")
            if single_key:
                self.gemini_keys = [single_key]
                
        self.gemini_clients = [genai.Client(api_key=key) for key in self.gemini_keys]
        self.blocked_until = {}  # dict: index -> timestamp (epoch seconds)
        
        # 2. Đọc Groq key
        self.groq_api_key = os.getenv("GROQ_API_KEY")
        
        logger.info(
            f"ApiClientManager initialized with {len(self.gemini_clients)} Gemini clients. "
            f"Groq API Key configured: {bool(self.groq_api_key)}"
        )

    def _get_active_gemini_client_index(self) -> int:
        """Tìm index của client Gemini đầu tiên không bị block, hoặc fallback sang 0 nếu bị block hết."""
        now = time.time()
        for idx in range(len(self.gemini_clients)):
            until = self.blocked_until.get(idx, 0)
            if now >= until:
                return idx
        
        if self.gemini_clients:
            logger.warning("All Gemini API keys are currently blocked! Falling back to the first key.")
            return 0
        raise RuntimeError("No Gemini API keys are configured.")

    def block_key(self, index: int, duration_seconds: int = 120):
        """Block API key tại index trong 2 phút (120s) khi dính Quota / Rate Limit."""
        until = time.time() + duration_seconds
        self.blocked_until[index] = until
        logger.warning(
            f"Blocked Gemini API key index {index} until {time.strftime('%Y-%m-%d %H:%M:%S', time.localtime(until))} due to rate limit/quota error."
        )

    async def _call_groq(
        self,
        model: str,
        contents: Any,
        system_instruction: str | None,
        temperature: float,
        max_output_tokens: int
    ) -> GroqResponseShim:
        """Gọi Groq Chat Completion API qua httpx."""
        if not self.groq_api_key:
            raise RuntimeError("Groq API Key is not configured.")

        messages = []
        if system_instruction:
            messages.append({"role": "system", "content": system_instruction})

        if isinstance(contents, str):
            messages.append({"role": "user", "content": contents})
        elif isinstance(contents, list):
            for item in contents:
                role = "user" if item.get("role") == "user" else "assistant"
                # Handle parts structure from Gemini
                parts = item.get("parts", [])
                text_content = ""
                if parts and isinstance(parts, list):
                    text_content = parts[0].get("text", "")
                messages.append({"role": role, "content": text_content})

        logger.info(f"Calling Groq API with model: {model}, temperature: {temperature}")
        
        async with httpx.AsyncClient(timeout=60.0) as http_client:
            try:
                response = await http_client.post(
                    "https://api.groq.com/openai/v1/chat/completions",
                    headers={
                        "Authorization": f"Bearer {self.groq_api_key}",
                        "Content-Type": "application/json"
                    },
                    json={
                        "model": model,
                        "messages": messages,
                        "temperature": temperature,
                        "max_tokens": max_output_tokens
                    }
                )
                if response.status_code != 200:
                    logger.error(f"Groq API Error {response.status_code}: {response.text}")
                    raise RuntimeError(f"Groq API returned error {response.status_code}")
                
                data = response.json()
                text = data["choices"][0]["message"]["content"]
                return GroqResponseShim(text=text.strip())
            except Exception as e:
                logger.exception("Failed to query Groq API")
                raise e

    async def generate_content(
        self,
        model: str,
        contents: Any,
        system_instruction: str | None = None,
        temperature: float = 0.65,
        max_output_tokens: int = 350
    ) -> Any:
        """
        Sinh nội dung từ model được chọn (hỗ trợ cả Gemini và Groq).
        """
        model_lower = model.lower() if model else ""
        
        # Nếu là model Llama -> Gọi Groq
        if model_lower.startswith("llama"):
            return await self._call_groq(model, contents, system_instruction, temperature, max_output_tokens)

        # Ngược lại -> Gọi Gemini với cơ chế xoay key và fallback sang Groq nếu tất cả key Gemini lỗi
        attempts = 0
        max_attempts = len(self.gemini_clients)
        
        while attempts < max_attempts:
            idx = self._get_active_gemini_client_index()
            client = self.gemini_clients[idx]
            try:
                logger.info(f"Calling Gemini API with model: {model} using key index {idx}")
                
                # Gọi đồng bộ trong thread pool để tránh block event loop
                config_dict: Dict[str, Any] = {
                    "temperature": temperature,
                    "max_output_tokens": max_output_tokens,
                }
                if system_instruction:
                    config_dict["system_instruction"] = system_instruction

                response = await asyncio.to_thread(
                    client.models.generate_content,
                    model=model,
                    contents=contents,
                    config=config_dict
                )
                return response
            except Exception as e:
                err_str = str(e).lower()
                is_quota_error = any(
                    word in err_str for word in ["429", "403", "quota", "rate limit", "exhausted", "resource_exhausted", "limit exceeded"]
                )
                
                if is_quota_error:
                    self.block_key(idx)
                    attempts += 1
                    if attempts < max_attempts:
                        logger.info(f"Retrying with the next Gemini API key (Attempt {attempts + 1}/{max_attempts})...")
                        continue
                
                logger.error(f"Gemini API call failed: {e}. Fallback to Groq if possible.")
                break

        # Fallback sang Groq
        if self.groq_api_key:
            fallback_model = "llama-3.3-70b-versatile" if "pro" in model_lower else "llama-3.1-8b-instant"
            logger.info(f"All Gemini keys failed. Falling back to Groq: {fallback_model}")
            return await self._call_groq(fallback_model, contents, system_instruction, temperature, max_output_tokens)
            
        raise RuntimeError("All Gemini API keys failed and Groq fallback is unavailable.")

    async def embed_content(self, model: str, contents: List[str]) -> Any:
        """
        Băm vector văn bản. Bắt buộc gọi qua Gemini, tuyệt đối không dùng Groq.
        """
        attempts = 0
        max_attempts = len(self.gemini_clients)
        
        while attempts < max_attempts:
            idx = self._get_active_gemini_client_index()
            client = self.gemini_clients[idx]
            try:
                logger.info(f"Calling Gemini Embedding API with model: {model} using key index {idx}")
                
                # Gọi trong thread pool
                response = await asyncio.to_thread(
                    client.models.embed_content,
                    model=model,
                    contents=contents
                )
                return response
            except Exception as e:
                err_str = str(e).lower()
                is_quota_error = any(
                    word in err_str for word in ["429", "403", "quota", "rate limit", "exhausted", "resource_exhausted", "limit exceeded"]
                )
                
                if is_quota_error:
                    self.block_key(idx)
                    attempts += 1
                    if attempts < max_attempts:
                        continue
                raise e
                
        raise RuntimeError("All Gemini API keys failed to generate embeddings.")

# Khởi tạo instance toàn cục
client_manager = ApiClientManager()
