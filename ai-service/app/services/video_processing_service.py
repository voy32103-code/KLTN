import os
import subprocess
import shutil
import logging
import asyncio
from pathlib import Path
from typing import Optional
from google.genai import types

from app.services.api_client_manager import client_manager
from app.services.admin_crawler_service import ScenarioConfigSchema, ScenarioConfigGeminiSchema, map_gemini_to_standard_config

logger = logging.getLogger(__name__)
MODEL = os.getenv("MODEL_NAME", "gemini-2.5-flash")

def is_ffmpeg_available() -> bool:
    """Kiểm tra xem ffmpeg có sẵn trong hệ thống không."""
    return shutil.which("ffmpeg") is not None

async def extract_audio_from_video(video_path: Path) -> Path:
    """
    Sử dụng FFmpeg để trích xuất âm thanh từ video sang tệp mp3 bất đồng bộ.
    Giúp giảm dung lượng truyền tải đi 90%.
    """
    if not video_path.exists():
        raise FileNotFoundError(f"Không tìm thấy tệp video tại {video_path}")
        
    output_audio_path = video_path.with_suffix(".mp3")
    
    # Nếu tệp mp3 đã tồn tại, xóa đi để tạo mới
    if output_audio_path.exists():
        output_audio_path.unlink()
        
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
    
    logger.info(f"Đang trích xuất âm thanh từ video bằng câu lệnh: {' '.join(cmd)}")
    process = await asyncio.create_subprocess_exec(
        *cmd,
        stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.PIPE
    )
    stdout, stderr = await process.communicate()
    
    if process.returncode != 0:
        logger.error(f"FFmpeg trích xuất âm thanh thất bại: {stderr.decode('utf-8', errors='ignore')}")
        raise RuntimeError("FFmpeg trích xuất âm thanh thất bại.")
        
    logger.info(f"Trích xuất âm thanh thành công tại: {output_audio_path}")
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
    if not video_path.exists():
        raise FileNotFoundError(f"Không tìm thấy tệp video tại {video_file_path}")
 
    # 1. Trích xuất âm thanh bằng FFmpeg nếu có thể
    media_path = video_path
    temp_audio_created = False
    
    if is_ffmpeg_available():
        try:
            media_path = await extract_audio_from_video(video_path)
            temp_audio_created = True
        except Exception as ex:
            logger.warning(f"Không thể trích xuất audio bằng FFmpeg, sử dụng file video gốc làm fallback: {ex}")
    else:
        logger.warning("Không tìm thấy lệnh ffmpeg trong hệ thống, sử dụng file video gốc để tải lên.")

    # 2. Lấy client Gemini đang hoạt động
    idx = client_manager._get_active_gemini_client_index()
    client = client_manager.gemini_clients[idx]

    uploaded_file = None
    try:
        # 3. Tải file media lên Gemini (chạy trong thread pool vì đây là gọi I/O)
        logger.info(f"Đang tải tệp {media_path.name} lên Gemini File API...")
        uploaded_file = await asyncio.to_thread(
            client.files.upload,
            file=str(media_path)
        )
        logger.info(f"Đã tải tệp thành công lên Gemini. File Name: {uploaded_file.name}")

        # Đợi file ở trạng thái hoạt động (active) nếu cần (đối với video lớn, Google cần thời gian xử lý)
        # Đối với audio mp3 thường ở trạng thái active ngay lập tức
        await asyncio.sleep(2.0)

        # 4. Gửi prompt đa phương tiện yêu cầu trích xuất kịch bản
        prompt = """Hãy lắng nghe tệp tin âm thanh/video cuộc họp thảo luận về yêu cầu phần mềm đính kèm.
Trích xuất tất cả các thông tin nghiệp vụ và cấu trúc hóa chúng thành một kịch bản giả lập phỏng vấn (Scenario Config).

Yêu cầu chi tiết:
1. Xác định tên kịch bản (scenario_title) và mã kịch bản (scenario_key).
2. Viết bối cảnh nghiệp vụ (context) rõ ràng để Stakeholder ảo hiểu vai diễn.
3. Trích xuất danh sách các yêu cầu ẩn (requirements) từ nội dung thảo luận:
   - CHỈ TRÍCH XUẤT TỐI ĐA 12 YÊU CẦU QUAN TRỌNG VÀ CỐT LÕI NHẤT để tránh kịch bản quá dài và tránh lỗi vượt quá giới hạn đầu ra (MAX_TOKENS). Không tạo các yêu cầu quá nhỏ nhặt hoặc lặp lại.
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

Đáp án trả về phải khớp chính xác cấu trúc JSON Schema được cung cấp.
"""
        model_name = selected_model or MODEL
        gen_config = types.GenerateContentConfig(
            response_mime_type="application/json",
            response_schema=ScenarioConfigGeminiSchema,
            temperature=0.25,
            max_output_tokens=6000
        )

        logger.info(f"Đang gọi Gemini ({model_name}) để phân tích tệp video/audio...")
        response = await asyncio.to_thread(
            client.models.generate_content,
            model=model_name,
            contents=[uploaded_file, prompt],
            config=gen_config
        )

        raw_response_text = getattr(response, "text", "") or ""
        raw_response_text = raw_response_text.strip()
        if raw_response_text.startswith("```"):
            raw_response_text = raw_response_text.split("\n", 1)[1].rsplit("```", 1)[0].strip()

        try:
            gemini_config = ScenarioConfigGeminiSchema.model_validate_json(raw_response_text)
        except Exception as e:
            logger.error(f"Pydantic validation failed for ScenarioConfigGeminiSchema (video). Error: {e}. Raw response (truncated): {raw_response_text[:3000]}")
            raise e
        return map_gemini_to_standard_config(gemini_config)

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
                logger.info(f"Đã dọn dẹp tệp audio tạm local: {media_path}")
            except Exception as delete_local_ex:
                logger.warning(f"Không thể xóa tệp audio tạm local: {delete_local_ex}")
