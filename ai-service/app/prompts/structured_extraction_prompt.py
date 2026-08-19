"""
Prompt template cho Structured Requirement Extraction.
Sử dụng few-shot learning để AI trích xuất requirement có cấu trúc.
"""

STRUCTURED_EXTRACTION_PROMPT = """
Bạn là chuyên gia phân tích yêu cầu phần mềm. Nhiệm vụ của bạn là trích xuất các requirement từ cuộc hội thoại giữa sinh viên và stakeholder.

## Quy tắc trích xuất:

1. **Actor** - Người hoặc hệ thống thực hiện hành động:
   - Ví dụ: Khách hàng, Hệ thống, Quản trị viên, Nhân viên, API

2. **Action** - Hành động được thực hiện:
   - Ví dụ: Đặt, Hủy, Xác nhận, Kiểm tra, Gửi, Lưu, Xóa

3. **Object** - Đối tượng bị tác động:
   - Ví dụ: Phòng, Đơn hàng, Tài khoản, Email, Thông báo

4. **Condition** (tùy chọn) - Điều kiện để hành động xảy ra:
   - Ví dụ: Còn phòng trống, Trước 24 giờ, Khi thanh toán thành công

5. **Type** - Phân loại requirement:
   - **FR** (Functional Requirement): Chức năng của hệ thống
   - **NFR** (Non-Functional Requirement): Chất lượng hệ thống (hiệu năng, bảo mật, khả dụng)
   - **BR** (Business Rule): Quy tắc nghiệp vụ

6. **Priority** - Mức độ ưu tiên:
   - **high**: Chức năng cốt lõi, bắt buộc phải có
   - **medium**: Chức năng quan trọng nhưng không critical
   - **low**: Chức năng phụ, nice-to-have

## Few-shot Examples:

### Example 1 - Functional Requirement:
**Câu hội thoại:** "Khách hàng có thể đặt phòng nếu còn phòng trống."

**Kết quả:**
```json
{
  "id": "REQ001",
  "actor": "Khách hàng",
  "action": "Đặt",
  "object": "Phòng",
  "condition": "Còn phòng trống",
  "type": "FR",
  "priority": "high",
  "confidence": 0.95,
  "raw_text": "Khách hàng có thể đặt phòng nếu còn phòng trống"
}
```

### Example 2 - Non-Functional Requirement:
**Câu hội thoại:** "Hệ thống cần phản hồi yêu cầu đặt phòng trong vòng 2 giây."

**Kết quả:**
```json
{
  "id": "REQ002",
  "actor": "Hệ thống",
  "action": "Phản hồi",
  "object": "Yêu cầu đặt phòng",
  "condition": "Trong vòng 2 giây",
  "type": "NFR",
  "priority": "medium",
  "confidence": 0.90,
  "raw_text": "Hệ thống cần phản hồi yêu cầu đặt phòng trong vòng 2 giây"
}
```

### Example 3 - Business Rule:
**Câu hội thoại:** "Khách hàng chỉ được hoàn tiền nếu hủy phòng trước 24 giờ."

**Kết quả:**
```json
{
  "id": "REQ003",
  "actor": "Khách hàng",
  "action": "Hoàn tiền",
  "object": "Đặt phòng",
  "condition": "Hủy trước 24 giờ",
  "type": "BR",
  "priority": "high",
  "confidence": 0.98,
  "raw_text": "Khách hàng chỉ được hoàn tiền nếu hủy phòng trước 24 giờ"
}
```

### Example 4 - Multiple Requirements:
**Câu hội thoại:** "Sau khi đặt phòng thành công, hệ thống gửi email xác nhận cho khách hàng."

**Kết quả:**
```json
[
  {
    "id": "REQ004",
    "actor": "Khách hàng",
    "action": "Đặt",
    "object": "Phòng",
    "condition": "Thành công",
    "type": "FR",
    "priority": "high",
    "confidence": 0.95,
    "raw_text": "Sau khi đặt phòng thành công"
  },
  {
    "id": "REQ005",
    "actor": "Hệ thống",
    "action": "Gửi",
    "object": "Email xác nhận",
    "condition": "Sau khi đặt phòng thành công",
    "type": "FR",
    "priority": "medium",
    "confidence": 0.92,
    "raw_text": "hệ thống gửi email xác nhận cho khách hàng"
  }
]
```

## Hướng dẫn đặc biệt:

1. **Không tự bịa thông tin**: Chỉ trích xuất từ nội dung hội thoại được cung cấp
2. **Một câu có thể chứa nhiều requirement**: Phân tích kỹ và tách riêng
3. **Condition có thể null**: Nếu không có điều kiện rõ ràng, để null
4. **Confidence scoring**:
   - 0.9-1.0: Rõ ràng, đầy đủ thông tin
   - 0.7-0.9: Có thể suy luận hợp lý
   - 0.5-0.7: Mơ hồ, cần xác nhận thêm
   - <0.5: Không chắc chắn, có thể bỏ qua

5. **ID generation**: Sử dụng format REQ001, REQ002... theo thứ tự tăng dần
6. **Ngôn ngữ và chuẩn hóa để chấm điểm**:
   - `raw_text` PHẢI giữ bằng tiếng Việt để hiển thị cho sinh viên.
   - `actor`, `action`, `object`, `condition` PHẢI dùng thuật ngữ tiếng Anh ngắn gọn, chuẩn ngành để đối chiếu Ground Truth nhất quán (ví dụ: `Tester`, `create`, `defect report`, `with reproducible evidence`). Không dịch máy từng chữ và không gộp nhiều requirement độc lập vào một requirement.

---

## Cuộc hội thoại cần phân tích:

{conversation_history}

---

## Yêu cầu đầu ra:

Trả về một JSON array chứa tất cả requirements được trích xuất. Mỗi requirement phải có đầy đủ các trường:
- id (string)
- actor (string)
- action (string)
- object (string)
- condition (string hoặc null)
- type (FR | NFR | BR)
- priority (high | medium | low)
- confidence (float từ 0 đến 1)
- raw_text (string - câu gốc)

**CHỈ TRẢ VỀ JSON, KHÔNG GIẢI THÍCH THÊM.**
"""


def build_extraction_prompt(conversation_history: list[dict]) -> str:
    """
    Build prompt từ conversation history.
    
    Args:
        conversation_history: List of {"role": "Student"|"Stakeholder", "content": str}
    
    Returns:
        Complete prompt string
    """
    # Format conversation
    formatted_conv = []
    for i, msg in enumerate(conversation_history, 1):
        role = msg.get("role", "Unknown")
        content = msg.get("content", "")
        formatted_conv.append(f"[Turn {i}] {role}: {content}")
    
    conv_text = "\n".join(formatted_conv)
    
    # The few-shot examples intentionally contain JSON braces.  ``str.format``
    # would interpret those braces as placeholders and fail before the model is
    # called, so replace only the single explicit conversation marker.
    return STRUCTURED_EXTRACTION_PROMPT.replace("{conversation_history}", conv_text)


# Validation rules
REQUIREMENT_TYPES = {"FR", "NFR", "BR"}
PRIORITY_LEVELS = {"high", "medium", "low"}

def validate_structured_requirement(req: dict) -> tuple[bool, str]:
    """
    Validate a structured requirement dict.
    
    Returns:
        (is_valid, error_message)
    """
    required_fields = {"id", "actor", "action", "object", "type", "priority", "confidence"}
    
    # Check required fields
    missing = required_fields - set(req.keys())
    if missing:
        return False, f"Missing required fields: {missing}"
    
    # Validate type
    if req["type"] not in REQUIREMENT_TYPES:
        return False, f"Invalid type: {req['type']}. Must be one of {REQUIREMENT_TYPES}"
    
    # Validate priority
    if req["priority"] not in PRIORITY_LEVELS:
        return False, f"Invalid priority: {req['priority']}. Must be one of {PRIORITY_LEVELS}"
    
    # Validate confidence
    confidence = req["confidence"]
    if not isinstance(confidence, (int, float)) or not (0 <= confidence <= 1):
        return False, f"Invalid confidence: {confidence}. Must be float between 0 and 1"
    
    # Validate non-empty strings
    for field in ["id", "actor", "action", "object"]:
        if not isinstance(req[field], str) or not req[field].strip():
            return False, f"Field '{field}' must be non-empty string"
    
    return True, "OK"
