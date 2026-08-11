import os
import subprocess
import shutil
import tempfile
import logging
import asyncio
from pathlib import Path
from typing import Optional
from google.genai import types

from app.services.api_client_manager import client_manager
from app.services.admin_crawler_service import ScenarioConfigSchema, ScenarioConfigGeminiSchema, map_gemini_to_standard_config, extract_json_string, parse_and_validate_scenario_config

logger = logging.getLogger(__name__)
MODEL = os.getenv("MODEL_NAME", "gemini-2.5-flash")

def is_ffmpeg_available() -> bool:
    """Kiểm tra xem ffmpeg có sẵn trong hệ thống không."""
    return shutil.which("ffmpeg") is not None


def validate_media_signature(video_path: Path) -> None:
    """Reject renamed arbitrary files before invoking FFmpeg or a cloud provider."""
    header = video_path.read_bytes()[:16]
    is_mp3 = header.startswith(b"ID3") or (len(header) >= 2 and header[0] == 0xFF and header[1] & 0xE0 == 0xE0)
    is_wave = header.startswith(b"RIFF") and header[8:12] == b"WAVE"
    is_ogg = header.startswith(b"OggS")
    is_webm = header.startswith(b"\x1a\x45\xdf\xa3")
    is_iso_media = len(header) >= 8 and header[4:8] == b"ftyp"
    if not any((is_mp3, is_wave, is_ogg, is_webm, is_iso_media)):
        raise ValueError("Tệp media không khớp chữ ký định dạng được hỗ trợ.")

async def extract_audio_from_video(video_path: Path) -> Path:
    """
    Sử dụng FFmpeg để trích xuất âm thanh từ video sang tệp mp3 bất đồng bộ.
    Giúp giảm dung lượng truyền tải đi 90%.
    """
    if not video_path.is_file():
        raise FileNotFoundError(f"Không tìm thấy tệp video tại {video_path}")
        
    descriptor, output_name = tempfile.mkstemp(prefix="reqsim-audio-", suffix=".mp3")
    os.close(descriptor)
    output_audio_path = Path(output_name)
    output_audio_path.unlink(missing_ok=True)
    
    # Câu lệnh FFmpeg để trích xuất audio
    cmd = [
        "ffmpeg",
        "-y",               # Ghi đè file nếu có
        "-i", str(video_path),
        "-vn",              # Bỏ kênh hình ảnh
        "-acodec", "libmp3lame",
        "-ab", "128k",      # Bitrate 128kbps (đủ để nhận diện giọng nói)
        "-ar", "44100",     # Sample rate
        str(output_audio_path)
    ]
    
    logger.info("Extracting audio from media file %s.", video_path.name)
    process = await asyncio.create_subprocess_exec(
        *cmd,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE
    )
    stdout, stderr = await process.communicate()
    
    if process.returncode != 0:
        output_audio_path.unlink(missing_ok=True)
        logger.error(f"FFmpeg trích xuất âm thanh thất bại: {stderr.decode('utf-8', errors='ignore')}")
        raise RuntimeError("FFmpeg trích xuất âm thanh thất bại.")
        
    logger.info("Trích xuất âm thanh thành công vào tệp tạm.")
    return output_audio_path
 
async def generate_scenario_from_video(
    video_file_path: str,
    selected_model: Optional[str] = None
) -> ScenarioConfigSchema:
    """
    Tải video (hoặc audio sau khi nén) lên Gemini API,
    yêu cầu phân tích Multimodal và trích xuất kịch bản dạng JSON.
    """
    video_path = Path(video_file_path)
    if not video_path.is_file():
        raise FileNotFoundError(f"Không tìm thấy tệp media tại {video_path.name}")

    allowed_extensions = {".mp4", ".mov", ".mkv", ".webm", ".mp3", ".wav", ".m4a", ".aac", ".ogg"}
    if video_path.suffix.lower() not in allowed_extensions:
        raise ValueError("Định dạng tệp media không được hỗ trợ.")
    validate_media_signature(video_path)

    model_name = selected_model or MODEL
    if not model_name.lower().startswith("gemini-"):
        raise ValueError("Video ingestion chỉ hỗ trợ mô hình Gemini.")
 
    # Normalize any supported media container to MP3 before it is sent to Gemini.
    media_path = video_path
    temp_audio_created = False
    
    if is_ffmpeg_available():
        try:
            media_path = await extract_audio_from_video(video_path)
            temp_audio_created = True
        except Exception as ex:
            logger.warning(f"Không thể trích xuất audio bằng FFmpeg, sử dụng file media gốc làm fallback: {ex}")
    else:
        logger.warning("Không tìm thấy lệnh ffmpeg trong hệ thống, sử dụng file media gốc để tải lên.")

    # 2. Lấy client Gemini đang hoạt động
    idx = client_manager._get_active_gemini_client_index()
    if idx is None:
        raise RuntimeError("Tất cả Gemini API keys đang tạm thời không khả dụng.")
    client = client_manager.gemini_clients[idx]

    uploaded_file = None
    try:
        # 3. Tải file media lên Gemini (chạy trong thread pool vì đây là gọi I/O)
        logger.info("Uploading media file %s to Gemini.", media_path.name)
        uploaded_file = await asyncio.to_thread(
            client.files.upload,
            file=str(media_path)
        )
        logger.info(f"Đã tải tệp thành công lên Gemini. File Name: {uploaded_file.name}")

        # Đợi file ở trạng thái hoạt động (active) nếu cần (đối với video lớn, Google cần thời gian xử lý)
        # Đối với audio mp3 thường ở trạng thái active ngay lập tức
        await asyncio.sleep(2.0)

        # 4. Gửi prompt đa phương tiện yêu cầu trích xuất kịch bản
        prompt = """Hãy lắng nghe tệp âm thanh/video cuộc họp thảo luận về yêu cầu phần mềm đính kèm.
Trích xuất tất cả các thông tin nghiệp vụ và cấu trúc hóa chúng thành một kịch bản giả lập phỏng vấn (Scenario Config).

Yêu cầu chi tiết:
1. Xác định tên kịch bản (scenario_title) và mã kịch bản (scenario_key).
2. Viết bối cảnh nghiệp vụ (context) ngắn gọn, súc tích (dưới 400 ký tự) để Stakeholder ảo hiểu vai diễn.
3. Trích xuất danh sách các yêu cầu ẩn (requirements) từ nội dung thảo luận:
   - CHỈ TRÍCH XUẤT TỐI ĐA 12 YÊU CẦU QUAN TRỌNG VÀ CỐT LÕI NHẤT để kịch bản ngắn gọn và tránh lỗi vượt quá giới hạn đầu ra (MAX_TOKENS). Không tạo các yêu cầu quá nhỏ nhặt hoặc trùng lặp.
   - Mỗi yêu cầu ẩn phải có mô tả ngắn gọn (trường 'text' dưới 100 ký tự).
   - Chỉ lấy từ 3-5 từ khóa đặc trưng nhất (trường 'keywords'). Không tạo quá nhiều từ khóa chung chung.
   - Chỉ lấy từ 1-2 loại câu hỏi phù hợp nhất (trường 'question_types').
   - Mô tả điều kiện tiết lộ cực kỳ ngắn gọn (trường 'reveal_condition' dưới 80 ký tự).
   - Phân loại độ khó (reveal_difficulty): Dựa trên việc yêu cầu đó dễ phát hiện hay cần hỏi sâu ('Easy', 'Medium', 'Hard').
   - Phân bổ Cổng (gate): 
     - Gate 0: Yêu cầu tổng quan, mục tiêu hệ thống.
     - Gate 1: Yêu cầu chức năng cơ bản cốt lõi.
     - Gate 2: Yêu cầu nâng cao, bảo mật, tích hợp hoặc các quy tắc tài chính.
     - Gate 3: Các quy tắc xử lý ngoại lệ, duyệt thủ công.
     - Gate 4: Yêu cầu phi chức năng (tải hệ thống, hiệu năng, chuẩn WCAG).
    - Thiết lập mối quan hệ phụ thuộc (requires): Nếu yêu cầu B chỉ được nói sau khi sinh viên đã biết yêu cầu A, hãy đưa mã định danh duy nhất (trường 'id', ví dụ: 'R1') của yêu cầu A vào danh sách 'requires' của yêu cầu B.
4. Xây dựng bản đồ gom nhóm từ khóa cho mỗi Gate (gate_keyword_groups) và bản đồ map câu hỏi (question_type_gate_map) thật tinh gọn, súc tích.

Đáp án trả về phải khớp chính xác cấu trúc JSON Schema được cung cấp.
"""
        gen_config = types.GenerateContentConfig(
            response_mime_type="application/json",
            response_schema=ScenarioConfigGeminiSchema,
            temperature=0.25,
            max_output_tokens=20000
        )

        logger.info(f"Đang gọi Gemini ({model_name}) để phân tích tệp video/audio...")
        response = await asyncio.to_thread(
            client.models.generate_content,
            model=model_name,
            contents=[uploaded_file, prompt],
            config=gen_config
        )

        raw_response_text = getattr(response, "text", "") or ""
        
        # Sử dụng hàm bổ trợ để trích xuất JSON sạch
        cleaned_json_text = extract_json_string(raw_response_text)

        try:
            return parse_and_validate_scenario_config(cleaned_json_text)
        except Exception as e:
            logger.error("Scenario response validation failed for media ingestion: %s (response length=%s).", e, len(raw_response_text))
            raise e

    finally:
        # 5. Dọn dẹp tệp tin trên đám mây Gemini để tiết kiệm không gian
        if uploaded_file is not None and uploaded_file.name is not None:
            try:
                logger.info(f"Đang xóa tệp {uploaded_file.name} trên đám mây Gemini...")
                await asyncio.to_thread(
                    client.files.delete,
                    name=uploaded_file.name
                )
                logger.info("Đã xóa tệp tin thành công trên Gemini.")
            except Exception as delete_ex:
                logger.warning(f"Lỗi khi xóa tệp tin trên Gemini: {delete_ex}")
                
        # 6. Dọn dẹp tệp tin audio tạm thời tạo ra cục bộ
        if temp_audio_created and media_path.exists():
            try:
                media_path.unlink()
                logger.info("Removed temporary audio file.")
            except Exception as delete_local_ex:
                logger.warning(f"Không thể xóa tệp audio tạm local: {delete_local_ex}")
