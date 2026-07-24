import re
import os
import logging
import json
from pathlib import Path
from typing import Dict, List, Optional
from pydantic import BaseModel, Field
import httpx
from google.genai import types

from app.services.api_client_manager import client_manager

logger = logging.getLogger(__name__)

SCENARIO_DIR = Path(__file__).resolve().parent.parent / "scenarios"
MODEL = os.getenv("MODEL_NAME", "gemini-2.5-flash")

# ===== Pydantic Schemas for Structured Output =====

class ScenarioRequirementRuleSchema(BaseModel):
    id: str = Field(description="Mã yêu cầu (ví dụ: R1, R2, R3...)")
    text: str = Field(description="Nội dung mô tả chi tiết yêu cầu phần mềm bằng tiếng Việt")
    gate: int = Field(description="Cổng kiểm duyệt của yêu cầu này (0, 1, 2, 3, hoặc 4). Càng phức tạp và nhạy cảm thì gate càng cao.")
    keywords: List[str] = Field(description="Danh sách các từ khóa tiếng Việt và tiếng Anh dùng để kích hoạt yêu cầu này")
    question_types: List[str] = Field(description="Các loại câu hỏi có thể kích hoạt yêu cầu này (ví dụ: OpenEnded, Clarifying, Probing, ConstraintOriented, ExceptionOriented, Closed)")
    reveal_condition: str = Field(description="Mô tả bằng tiếng Việt điều kiện để Stakeholder ảo tiết lộ thông tin này")
    reveal_difficulty: str = Field(description="Mức độ khó khi khai thác yêu cầu này ('Easy', 'Medium', hoặc 'Hard')")
    requires: Optional[List[str]] = Field(default=[], description="Nội dung văn bản (trường text) của các yêu cầu tiên quyết cần mở khóa trước yêu cầu này (nếu có)")

class ScenarioConfigSchema(BaseModel):
    scenario_key: str = Field(description="Mã kịch bản (snake_case, ví dụ: hospital_booking, inventory_control)")
    scenario_title: str = Field(description="Tên kịch bản viết bằng tiếng Anh (ví dụ: Hospital Appointment System)")
    context: str = Field(description="Bối cảnh nghiệp vụ chi tiết của kịch bản dùng để hướng dẫn Stakeholder ảo nhập vai (viết bằng tiếng Việt hoặc tiếng Anh)")
    general_keywords: List[str] = Field(description="Danh sách các từ khóa chung về kịch bản (ví dụ: hệ thống, mục tiêu, quy trình...)")
    gate_keyword_groups: Dict[str, List[str]] = Field(description="Bản đồ gom nhóm từ khóa cho mỗi Gate (ví dụ: {'1': ['môn học', 'lịch'], '2': ['học phí', 'tiền']...})")
    question_type_gate_map: Dict[str, List[int]] = Field(description="Bản đồ liên kết loại câu hỏi đặc thù với các Gate tương ứng (ví dụ: {'ConstraintOriented': [1, 2], 'ExceptionOriented': [3]})")
    max_new_reveals_per_turn: int = Field(default=1, description="Số lượng yêu cầu tối đa được phép tiết lộ trong một lượt chat (thường là 1)")
    requirements: List[ScenarioRequirementRuleSchema] = Field(description="Danh sách các yêu cầu ẩn của kịch bản")

# Sub-components for Gemini schema compatibility (avoids dynamic dict validation errors in SDK)
class GateKeywordGroup(BaseModel):
    gate: str = Field(description="Số thứ tự cổng dưới dạng chuỗi (ví dụ: '1', '2')")
    keywords: List[str] = Field(description="Danh sách các từ khóa tương ứng với cổng này")

class QuestionTypeGateMapItem(BaseModel):
    question_type: str = Field(description="Tên loại câu hỏi (ví dụ: Clarifying, Probing, ConstraintOriented)")
    gates: List[int] = Field(description="Danh sách các cổng liên kết với loại câu hỏi này")

class ScenarioConfigGeminiSchema(BaseModel):
    scenario_key: str = Field(description="Mã kịch bản (snake_case)")
    scenario_title: str = Field(description="Tên kịch bản viết bằng tiếng Anh")
    context: str = Field(description="Bối cảnh nghiệp vụ chi tiết")
    general_keywords: List[str] = Field(description="Từ khóa chung")
    gate_keyword_groups: List[GateKeywordGroup] = Field(description="Gom nhóm từ khóa cho mỗi Gate")
    question_type_gate_map: List[QuestionTypeGateMapItem] = Field(description="Liên kết loại câu hỏi với các Gate")
    max_new_reveals_per_turn: int = Field(default=1)
    requirements: List[ScenarioRequirementRuleSchema] = Field(description="Danh sách yêu cầu ẩn")

def map_gemini_to_standard_config(gemini_config: ScenarioConfigGeminiSchema) -> ScenarioConfigSchema:
    """Chuyển đổi từ Schema tương thích Gemini sang Schema chuẩn có chứa Dictionaries."""
    gate_kw_dict = {item.gate: item.keywords for item in gemini_config.gate_keyword_groups}
    q_type_dict = {item.question_type: item.gates for item in gemini_config.question_type_gate_map}
    
    return ScenarioConfigSchema(
        scenario_key=gemini_config.scenario_key,
        scenario_title=gemini_config.scenario_title,
        context=gemini_config.context,
        general_keywords=gemini_config.general_keywords,
        gate_keyword_groups=gate_kw_dict,
        question_type_gate_map=q_type_dict,
        max_new_reveals_per_turn=gemini_config.max_new_reveals_per_turn,
        requirements=gemini_config.requirements
    )


# ===== Crawler & Structuring Logic =====

def clean_html(html: str) -> str:
    """Loại bỏ các thẻ HTML và scripts để giữ lại text sạch."""
    # Loại bỏ script và style
    text = re.sub(r"<(script|style).*?>.*?</\1>", "", html, flags=re.DOTALL | re.IGNORECASE)
    # Loại bỏ các thẻ tag khác
    text = re.sub(r"<.*?>", " ", text)
    # Thu gọn khoảng trắng
    text = re.sub(r"\s+", " ", text).strip()
    return text

async def fetch_url_content(url: str) -> str:
    """Tải nội dung từ URL, hỗ trợ cả HTML và raw text/markdown."""
    headers = {
        "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
    }
    async with httpx.AsyncClient(timeout=30.0, follow_redirects=True) as client:
        response = await client.get(url, headers=headers)
        if response.status_code != 200:
            raise RuntimeError(f"Tải URL thất bại với mã lỗi HTTP {response.status_code}")
        
        # Nếu là raw markdown hoặc text, giữ nguyên. Ngược lại, làm sạch HTML
        content_type = response.headers.get("content-type", "").lower()
        if "html" in content_type:
            return clean_html(response.text)
        return response.text

async def generate_scenario_from_ba_text(raw_text: str, selected_model: Optional[str] = None) -> ScenarioConfigSchema:
    """Gọi Gemini Structured Outputs để phân tích văn bản đặc tả BA thô thành cấu hình kịch bản."""
    prompt = f"""Hãy phân tích tài liệu đặc tả yêu cầu nghiệp vụ (Business Analyst Document) dưới đây.
Trích xuất tất cả các thông tin cần thiết và cấu trúc hóa chúng thành một kịch bản giả lập phỏng vấn (Scenario Config).

Yêu cầu chi tiết:
1. Xác định tên kịch bản (scenario_title) và mã kịch bản (scenario_key).
2. Viết bối cảnh nghiệp vụ (context) rõ ràng để Stakeholder ảo hiểu vai diễn.
3. Trích xuất danh sách các yêu cầu ẩn (requirements) từ tài liệu:
   - Phân loại độ khó (reveal_difficulty): Dựa trên việc yêu cầu đó dễ phát hiện hay cần hỏi sâu.
   - Phân bổ Cổng (gate): 
     - Gate 0: Yêu cầu tổng quan, mục tiêu hệ thống.
     - Gate 1: Yêu cầu chức năng cơ bản cốt lõi.
     - Gate 2: Yêu cầu nâng cao, bảo mật, tích hợp hoặc các quy tắc tài chính.
     - Gate 3: Các quy tắc xử lý ngoại lệ, duyệt thủ công.
     - Gate 4: Yêu cầu phi chức năng (tải hệ thống, hiệu năng, chuẩn WCAG).
   - Thiết lập từ khóa kích hoạt (keywords) tiếng Việt/tiếng Anh tương ứng với mỗi yêu cầu.
   - Thiết lập mối quan hệ phụ thuộc (requires): Nếu yêu cầu B chỉ được nói sau khi sinh viên đã biết yêu cầu A, hãy đưa nội dung văn bản của yêu cầu A vào danh sách 'requires' của yêu cầu B.
4. Xây dựng bản đồ gom nhóm từ khóa cho mỗi Gate (gate_keyword_groups) và bản đồ map câu hỏi (question_type_gate_map).

--- TÀI LIỆU NGHIỆP VỤ ---
{raw_text}
"""

    model_name = selected_model or MODEL
    gen_config = types.GenerateContentConfig(
        response_mime_type="application/json",
        response_schema=ScenarioConfigGeminiSchema,
        temperature=0.2,
        max_output_tokens=6000
    )

    response = await client_manager.generate_content(
        model=model_name,
        contents=prompt,
        config=gen_config
    )

    # Đọc kết quả dạng JSON
    raw_response_text = getattr(response, "text", "") or ""
    # Nếu kết quả bị bọc trong markdown code block, làm sạch nó
    raw_response_text = raw_response_text.strip()
    if raw_response_text.startswith("```"):
        raw_response_text = raw_response_text.split("\n", 1)[1].rsplit("```", 1)[0].strip()
        
    gemini_config = ScenarioConfigGeminiSchema.model_validate_json(raw_response_text)
    return map_gemini_to_standard_config(gemini_config)

def save_scenario_config_file(config: ScenarioConfigSchema) -> Path:
    """Lưu kịch bản mới sinh ra thành file JSON trong thư mục scenarios."""
    SCENARIO_DIR.mkdir(parents=True, exist_ok=True)
    file_path = SCENARIO_DIR / f"{config.scenario_key}.json"
    
    # Chuyển đổi Pydantic model sang dict để dump JSON có thụt lề đẹp
    config_dict = config.model_dump()
    
    with open(file_path, "w", encoding="utf-8") as f:
        json.dump(config_dict, f, ensure_ascii=False, indent=2)
        
    logger.info(f"Đã lưu kịch bản mới thành công tại {file_path}")
    return file_path
