import os
import time
import logging
import asyncio
from typing import Any, List, Dict
from google import genai
from google.genai import types
from google.genai.errors import APIError
import httpx

logger = logging.getLogger(__name__)


DEFAULT_GEMINI_MODEL_FALLBACKS = (
    "gemini-3.1-flash-lite",
    "gemini-3.5-flash-lite",
    "gemini-2.5-flash-lite",
    "gemini-2.5-flash",
    "gemini-3-flash-preview",
    "gemini-3.5-flash",
    "gemini-3.6-flash",
    "gemini-3.7-flash",
)


class GroqResponseShim:
    """Giả lập response object của Gemini để tương thích với code hiện tại."""
    def __init__(self, text: str):
        self.text = text


class GeneratedContentResponse:
    """Stable provider-neutral text response with the effective model route."""
    def __init__(self, response: Any, effective_model: str, fallback_reason: str | None = None):
        self.text = getattr(response, "text", "") or ""
        self.effective_model = effective_model
        self.fallback_reason = fallback_reason

class ApiClientManager:
    def __init__(self):
        # 1. Đọc và khởi tạo các key Gemini
        self.gemini_keys: list[str] = []
        raw_keys = os.getenv("GEMINI_API_KEYS")
        if raw_keys:
            self.gemini_keys = [k.strip() for k in raw_keys.split(",") if k.strip()]

        # Keep compatibility with the older singular setting.  When both settings
        # are present, the singular key is a real extra rotation candidate instead
        # of being silently ignored.  This is useful while deployments migrate to
        # the comma-separated setting and harmless when both values are identical.
        single_key = (os.getenv("GEMINI_API_KEY") or "").strip()
        if single_key and single_key not in self.gemini_keys:
            self.gemini_keys.append(single_key)
                
        self.gemini_clients = [genai.Client(api_key=key) for key in self.gemini_keys]
        # General key blocks use an integer key. Model-specific quota blocks use
        # (key_index, model_name), so one exhausted Gemini model does not disable
        # the same API key for every other model.
        self.blocked_until: dict[int | tuple[int, str], float] = {}
        configured_fallbacks = os.getenv("GEMINI_MODEL_FALLBACKS", "")
        self.gemini_model_fallbacks = self._unique_gemini_models(
            configured_fallbacks.split(",") if configured_fallbacks else DEFAULT_GEMINI_MODEL_FALLBACKS
        )
        
        # 2. Đọc Groq key
        self.groq_api_key = os.getenv("GROQ_API_KEY")
        
        # 3. Đọc DeepSeek và Mimo keys
        self.deepseek_api_key = os.getenv("DEEPSEEK_API_KEY")
        self.mimo_api_key = os.getenv("MIMO_API_KEY")
        
        # 4. Đọc OpenRouter key
        self.openrouter_api_key = os.getenv("OPENROUTER_API_KEY")

        # 5. Đọc OmniRoute key
        self.omniroute_api_key = os.getenv("OMNIROUTE_API_KEY")

        # Provider fallbacks are configurable so a deployment can replace a
        # retired model without changing the application code.
        self.openrouter_fallback_model = os.getenv(
            "OPENROUTER_FALLBACK_MODEL", "meta-llama/llama-3.3-70b-instruct"
        ).strip()
        self.deepseek_fallback_model = os.getenv(
            "DEEPSEEK_FALLBACK_MODEL", "deepseek-chat"
        ).strip()
        self.mimo_fallback_model = os.getenv(
            "MIMO_FALLBACK_MODEL", "mimo-v2.5pro"
        ).strip()

        logger.info(
            f"ApiClientManager initialized with {len(self.gemini_clients)} Gemini clients. "
            f"Groq API Key configured: {bool(self.groq_api_key)}. "
            f"DeepSeek API Key configured: {bool(self.deepseek_api_key)}. "
            f"Mimo API Key configured: {bool(self.mimo_api_key)}. "
            f"OpenRouter API Key configured: {bool(self.openrouter_api_key)}. "
            f"OmniRoute API Key configured: {bool(self.omniroute_api_key)}."
        )

    @staticmethod
    def _unique_gemini_models(models: Any) -> list[str]:
        unique: list[str] = []
        for value in models:
            model = str(value).strip()
            if model.startswith("gemini-") and model not in unique:
                unique.append(model)
        return unique

    def _gemini_model_candidates(self, requested_model: str) -> list[str]:
        return self._unique_gemini_models([requested_model, *self.gemini_model_fallbacks])

    @staticmethod
    def _with_effective_model(
        response: Any,
        effective_model: str,
        fallback_reason: str | None = None,
    ) -> GeneratedContentResponse:
        return GeneratedContentResponse(response, effective_model, fallback_reason)

    @staticmethod
    def _classify_gemini_error(error: Exception) -> tuple[bool, bool, bool, bool]:
        """Return quota, transient, model-unavailable and credential-error flags."""
        code = error.code if isinstance(error, APIError) else None
        status = str(getattr(error, "status", "") or "").lower()
        message = str(error).lower()

        is_quota_error = (
            code == 429
            or status == "resource_exhausted"
            or any(
                marker in message
                for marker in [
                    "429", "quota", "rate limit", "exhausted",
                    "resource_exhausted", "limit exceeded",
                ]
            )
        )
        is_transient_error = (
            code in {408, 500, 502, 503, 504}
            or status in {"deadline_exceeded", "internal", "unavailable"}
            or any(
                marker in message
                for marker in [
                    "408", "500", "502", "503", "504", "timeout",
                    "timed out", "connection", "unavailable", "deadline",
                ]
            )
        )
        is_model_unavailable = (
            code == 404
            or any(
                marker in message
                for marker in [
                    "404", "model not found", "model is not found",
                    "unsupported model", "not supported for generatecontent",
                ]
            )
        )
        is_credential_error = (
            code == 401
            or status == "unauthenticated"
            or any(
                marker in message
                for marker in [
                    "api key not valid",
                    "invalid api key",
                    "invalid key",
                    "key is invalid",
                    "authentication failed",
                ]
            )
        )
        return is_quota_error, is_transient_error, is_model_unavailable, is_credential_error

    def _get_active_gemini_client_index(self, model: str | None = None) -> int | None:
        """Tìm index của client Gemini đầu tiên không bị block; trả None nếu không có client khả dụng."""
        now = time.time()
        for idx in range(len(self.gemini_clients)):
            general_until = self.blocked_until.get(idx, 0)
            model_until = self.blocked_until.get((idx, model), 0) if model else 0
            if now >= general_until and now >= model_until:
                return idx
        
        if self.gemini_clients:
            logger.warning("All Gemini API keys are currently blocked.")
            return None
        raise RuntimeError("No Gemini API keys are configured.")

    def block_key(
        self,
        index: int,
        duration_seconds: int = 120,
        model: str | None = None,
    ):
        """Temporarily block one Gemini key globally or for a specific model."""
        until = time.time() + duration_seconds
        block_id: int | tuple[int, str] = (index, model) if model else index
        self.blocked_until[block_id] = until
        logger.warning(
            "Blocked Gemini API key index %s%s until %s due to a retryable provider error.",
            index,
            f" for model {model}" if model else "",
            time.strftime('%Y-%m-%d %H:%M:%S', time.localtime(until)),
        )

    async def _call_groq(
        self,
        model: str,
        contents: Any,
        system_instruction: Any,
        temperature: float,
        max_output_tokens: int,
        response_format: dict | None = None
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
        
        payload: Dict[str, Any] = {
            "model": model,
            "messages": messages,
            "temperature": temperature,
            "max_tokens": max_output_tokens
        }
        if response_format:
            payload["response_format"] = response_format

        async with httpx.AsyncClient(timeout=60.0) as http_client:
            try:
                response = await http_client.post(
                    "https://api.groq.com/openai/v1/chat/completions",
                    headers={
                        "Authorization": f"Bearer {self.groq_api_key}",
                        "Content-Type": "application/json"
                    },
                    json=payload
                )
                if response.status_code != 200:
                    logger.error("Groq API returned status %s.", response.status_code)
                    raise RuntimeError(f"Groq API returned error {response.status_code}")
                
                data = response.json()
                text = data["choices"][0]["message"]["content"]
                return GroqResponseShim(text=text.strip())
            except Exception:
                logger.exception("Failed to query Groq API")
                raise

    async def _call_openai_compatible(
        self,
        base_url: str,
        api_key: str | None,
        model: str,
        contents: Any,
        system_instruction: Any,
        temperature: float,
        max_output_tokens: int,
        response_format: dict | None = None
    ) -> GroqResponseShim:
        """Gọi OpenAI Compatible API (DeepSeek, Mimo) qua httpx."""
        if not api_key:
            raise RuntimeError(f"API Key for model {model} is not configured.")

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

        logger.info(f"Calling OpenAI-compatible API ({base_url}) with model: {model}, temperature: {temperature}")
        
        payload: Dict[str, Any] = {
            "model": model,
            "messages": messages,
            "temperature": temperature,
            "max_tokens": max_output_tokens,
            "stream": False
        }
        if response_format:
            payload["response_format"] = response_format

        async with httpx.AsyncClient(timeout=180.0) as http_client:
            try:
                response = await http_client.post(
                    f"{base_url.rstrip('/')}/chat/completions",
                    headers={
                        "Authorization": f"Bearer {api_key}",
                        "Content-Type": "application/json"
                    },
                    json=payload
                )
                if response.status_code != 200:
                    logger.error("OpenAI-compatible API returned status %s.", response.status_code)
                    raise RuntimeError(f"API returned error {response.status_code}")
                
                data = response.json()
                text = data["choices"][0]["message"]["content"]
                return GroqResponseShim(text=text.strip())
            except Exception:
                logger.exception(f"Failed to query model {model} at {base_url}")
                raise

    async def _call_deepseek(
        self,
        model: str,
        contents: Any,
        system_instruction: Any,
        temperature: float,
        max_output_tokens: int,
        response_format: dict | None = None
    ) -> GroqResponseShim:
        # Chuẩn hóa tên model DeepSeek
        m = model.lower().strip().replace(" ", "-")
        if m == "deepseek-v4pro":
            normalized_model = "deepseek-v4-pro"
        elif m == "deepseek-v4flash":
            normalized_model = "deepseek-v4-flash"
        else:
            normalized_model = m

        return await self._call_openai_compatible(
            base_url="https://api.deepseek.com",
            api_key=self.deepseek_api_key,
            model=normalized_model,
            contents=contents,
            system_instruction=system_instruction,
            temperature=temperature,
            max_output_tokens=max_output_tokens,
            response_format=response_format
        )

    async def _call_mimo(
        self,
        model: str,
        contents: Any,
        system_instruction: Any,
        temperature: float,
        max_output_tokens: int,
        response_format: dict | None = None
    ) -> GroqResponseShim:
        # Chuẩn hóa tên model Mimo
        m = model.lower().strip().replace(" ", "-")
        if m == "mimo-v2.5pro":
            normalized_model = "mimo-v2.5-pro"
        else:
            normalized_model = m

        return await self._call_openai_compatible(
            base_url="https://api.xiaomimimo.com/v1",
            api_key=self.mimo_api_key,
            model=normalized_model,
            contents=contents,
            system_instruction=system_instruction,
            temperature=temperature,
            max_output_tokens=max_output_tokens,
            response_format=response_format
        )

    async def _call_openrouter(
        self,
        model: str,
        contents: Any,
        system_instruction: Any,
        temperature: float,
        max_output_tokens: int,
        response_format: dict | None = None
    ) -> GroqResponseShim:
        # Chuẩn hóa tên model OpenRouter
        # Nếu bắt đầu bằng openrouter/, ta bỏ tiền tố đi
        if model.lower().startswith("openrouter/"):
            model_name = model[11:]
        else:
            model_name = model

        return await self._call_openai_compatible(
            base_url="https://openrouter.ai/api/v1",
            api_key=self.openrouter_api_key,
            model=model_name,
            contents=contents,
            system_instruction=system_instruction,
            temperature=temperature,
            max_output_tokens=max_output_tokens,
            response_format=response_format
        )

    async def _try_external_provider_fallbacks(
        self,
        model_lower: str,
        contents: Any,
        system_instruction: Any,
        temperature: float,
        max_output_tokens: int,
        response_format: dict | None,
    ) -> GeneratedContentResponse:
        """Try configured non-Gemini providers after Gemini capacity failure."""
        groq_model = "llama-3.3-70b-versatile" if "pro" in model_lower else "llama-3.1-8b-instant"
        candidates = [
            ("Groq", self.groq_api_key, groq_model, "gemini_capacity_fallback", self._call_groq),
            ("OpenRouter", self.openrouter_api_key, self.openrouter_fallback_model, "openrouter_capacity_fallback", self._call_openrouter),
            ("DeepSeek", self.deepseek_api_key, self.deepseek_fallback_model, "deepseek_capacity_fallback", self._call_deepseek),
            ("Mimo", self.mimo_api_key, self.mimo_fallback_model, "mimo_capacity_fallback", self._call_mimo),
        ]

        attempted_providers: list[str] = []
        for provider_name, api_key, fallback_model, reason, call_provider in candidates:
            if not api_key or not fallback_model:
                continue
            attempted_providers.append(provider_name)
            try:
                response = await call_provider(
                    model=fallback_model,
                    contents=contents,
                    system_instruction=system_instruction,
                    temperature=temperature,
                    max_output_tokens=max_output_tokens,
                    response_format=response_format,
                )
                logger.warning(
                    "%s fallback succeeded after Gemini capacity failure. model=%s",
                    provider_name,
                    fallback_model,
                )
                return self._with_effective_model(response, fallback_model, reason)
            except Exception as error:
                logger.warning(
                    "%s fallback failed after Gemini capacity failure; trying the next provider. Error: %s",
                    provider_name,
                    error,
                )

        providers = ", ".join(attempted_providers) or "none configured"
        raise RuntimeError(
            f"Gemini capacity fallback failed; external providers attempted: {providers}."
        )

    async def generate_content(
        self,
        model: str,
        contents: Any,
        system_instruction: str | None = None,
        temperature: float = 0.65,
        max_output_tokens: int = 350,
        config: types.GenerateContentConfig | None = None
    ) -> Any:
        """
        Sinh nội dung từ model được chọn (hỗ trợ cả Gemini, Groq, DeepSeek, Mimo và OpenRouter).
        """
        model_lower = model.lower() if model else ""
        effective_system_instruction = (
            system_instruction
            or (config.system_instruction if config else None)
        )
        effective_temperature = (
            temperature
            if config is None or config.temperature is None
            else config.temperature
        )
        effective_max_output_tokens = (
            max_output_tokens
            if config is None or config.max_output_tokens is None
            else config.max_output_tokens
        )
        response_format = (
            {"type": "json_object"}
            if config and config.response_mime_type == "application/json"
            else None
        )
        
        # Nếu là model Llama -> Gọi Groq
        if model_lower.startswith("llama"):
            response = await self._call_groq(
                model=model,
                contents=contents,
                system_instruction=effective_system_instruction,
                temperature=effective_temperature,
                max_output_tokens=effective_max_output_tokens,
                response_format=response_format,
            )
            return self._with_effective_model(response, model)

        # Nếu là DeepSeek -> Gọi DeepSeek
        if model_lower.startswith("deepseek"):
            response_format = None
            if config and config.response_mime_type == "application/json":
                response_format = {"type": "json_object"}
            response = await self._call_deepseek(
                model=model,
                contents=contents,
                system_instruction=effective_system_instruction,
                temperature=effective_temperature,
                max_output_tokens=effective_max_output_tokens,
                response_format=response_format
            )
            return self._with_effective_model(response, model)

        # Nếu là Mimo -> Gọi Mimo
        if model_lower.startswith("mimo"):
            response_format = None
            if config and config.response_mime_type == "application/json":
                response_format = {"type": "json_object"}
            response = await self._call_mimo(
                model=model,
                contents=contents,
                system_instruction=effective_system_instruction,
                temperature=effective_temperature,
                max_output_tokens=effective_max_output_tokens,
                response_format=response_format
            )
            return self._with_effective_model(response, model)

        # Nếu là OpenRouter -> Gọi OpenRouter
        if model_lower.startswith("openrouter") or ("/" in model_lower and not model_lower.startswith("omniroute/")):
            response_format = None
            if config and config.response_mime_type == "application/json":
                response_format = {"type": "json_object"}
            response = await self._call_openrouter(
                model=model,
                contents=contents,
                system_instruction=effective_system_instruction,
                temperature=effective_temperature,
                max_output_tokens=effective_max_output_tokens,
                response_format=response_format
            )
            return self._with_effective_model(response, model)

        # Nếu là OmniRoute -> Gọi qua OmniRoute Gateway
        if model_lower.startswith("omniroute/"):
            model_name = model[10:] # Bỏ chữ omniroute/
            response_format = None
            if config and config.response_mime_type == "application/json":
                response_format = {"type": "json_object"}
            response = await self._call_openai_compatible(
                base_url=os.getenv("OMNIROUTE_BASE_URL", "https://api.omniroute.com/v1"),
                api_key=self.omniroute_api_key,
                model=model_name,
                contents=contents,
                system_instruction=effective_system_instruction,
                temperature=effective_temperature,
                max_output_tokens=effective_max_output_tokens,
                response_format=response_format
            )
            return self._with_effective_model(response, model)

        # Ngược lại -> gọi Gemini. Mỗi model thử tất cả key khả dụng trước,
        # sau đó mới chuyển model khi gặp quota, model-unavailable hoặc lỗi tạm thời.
        max_attempts = len(self.gemini_clients)
        fatal_error: Exception | None = None
        requested_model = model
        used_key_rotation = False

        for candidate_model in self._gemini_model_candidates(requested_model):
            attempts = 0
            while attempts < max_attempts:
                idx = self._get_active_gemini_client_index(candidate_model)
                if idx is None:
                    break
                client = self.gemini_clients[idx]
                try:
                    logger.info(
                        "Calling Gemini API with model %s using key index %s (requested model: %s).",
                        candidate_model,
                        idx,
                        requested_model,
                    )

                    # Gọi đồng bộ trong thread pool để tránh block event loop.
                    gen_config = config
                    if gen_config is None:
                        gen_config = types.GenerateContentConfig(
                            temperature=temperature,
                            max_output_tokens=max_output_tokens,
                            system_instruction=system_instruction,
                        )
                    else:
                        if gen_config.temperature is None:
                            gen_config.temperature = temperature
                        if gen_config.max_output_tokens is None:
                            gen_config.max_output_tokens = max_output_tokens
                        if gen_config.system_instruction is None and system_instruction is not None:
                            gen_config.system_instruction = system_instruction

                    response = await asyncio.to_thread(
                        client.models.generate_content,
                        model=candidate_model,
                        contents=contents,
                        config=gen_config,
                    )
                    if candidate_model != requested_model:
                        logger.warning(
                            "Gemini model fallback succeeded: requested=%s effective=%s.",
                            requested_model,
                            candidate_model,
                        )
                    return self._with_effective_model(
                        response,
                        candidate_model,
                        "gemini_model_fallback"
                        if candidate_model != requested_model
                        else "gemini_key_rotation" if used_key_rotation else None,
                    )
                except Exception as e:
                    (
                        is_quota_error,
                        is_transient_error,
                        is_model_unavailable,
                        is_credential_error,
                    ) = self._classify_gemini_error(e)

                    if is_model_unavailable:
                        logger.warning(
                            "Gemini model %s is unavailable; trying the next configured model.",
                            candidate_model,
                        )
                        break

                    if is_quota_error or is_transient_error:
                        used_key_rotation = True
                        self.block_key(
                            idx,
                            duration_seconds=120 if is_quota_error else 15,
                            model=candidate_model,
                        )
                        attempts += 1
                        if attempts < max_attempts:
                            logger.info(
                                "Retrying Gemini model %s with another key (attempt %s/%s).",
                                candidate_model,
                                attempts + 1,
                                max_attempts,
                            )
                        continue

                    if is_credential_error:
                        # An invalid/revoked key cannot recover for any Gemini
                        # model. Block it globally and try the next configured key.
                        used_key_rotation = True
                        self.block_key(idx, duration_seconds=300)
                        attempts += 1
                        if attempts < max_attempts:
                            logger.info(
                                "Retrying Gemini with another key after a credential failure "
                                "(attempt %s/%s).",
                                attempts + 1,
                                max_attempts,
                            )
                        continue

                    # Validation, safety and malformed-request errors are not
                    # availability failures. Switching models could hide the
                    # real defect and consume quota, so stop the model chain.
                    logger.error(
                        "Gemini API rejected a non-retryable request for model %s.",
                        candidate_model,
                        exc_info=True,
                    )
                    fatal_error = e
                    break

            if fatal_error is not None:
                break

        if fatal_error is not None:
            raise RuntimeError(
                "Gemini rejected the request; provider fallback was not attempted."
            ) from fatal_error

        # Fallback sang Groq
        return await self._try_external_provider_fallbacks(
            model_lower=model_lower,
            contents=contents,
            system_instruction=effective_system_instruction,
            temperature=effective_temperature,
            max_output_tokens=effective_max_output_tokens,
            response_format=response_format,
        )

    async def embed_content(self, model: str, contents: List[str]) -> Any:
        """
        Băm vector văn bản. Bắt buộc gọi qua Gemini, tuyệt đối không dùng Groq.
        """
        attempts = 0
        max_attempts = len(self.gemini_clients)
        
        while attempts < max_attempts:
            idx = self._get_active_gemini_client_index(model)
            if idx is None:
                break
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
                (
                    is_quota_error,
                    is_transient_error,
                    _,
                    is_credential_error,
                ) = self._classify_gemini_error(e)

                if is_quota_error or is_transient_error:
                    self.block_key(
                        idx,
                        duration_seconds=120 if is_quota_error else 15,
                        model=model,
                    )
                    attempts += 1
                    if attempts < max_attempts:
                        continue
                if is_credential_error:
                    self.block_key(idx, duration_seconds=300)
                    attempts += 1
                    if attempts < max_attempts:
                        continue
                raise
                
        raise RuntimeError("All Gemini API keys failed to generate embeddings.")

# Khởi tạo instance toàn cục
client_manager = ApiClientManager()
