import logging
from typing import Optional
from fastapi import APIRouter, HTTPException
from pydantic import BaseModel, Field

from app.services.admin_crawler_service import (
    fetch_url_content,
    generate_scenario_from_ba_text,
    save_scenario_config_file,
    ScenarioConfigSchema
)
from app.services.video_processing_service import generate_scenario_from_video

logger = logging.getLogger(__name__)
router = APIRouter()

class CrawlScenarioRequest(BaseModel):
    url: str = Field(description="URL chứa tài liệu đặc tả BA hoặc PRD")
    selectedModel: Optional[str] = Field(default=None, description="Mô hình Gemini sử dụng")

class VideoScenarioRequest(BaseModel):
    videoPath: str = Field(description="Đường dẫn tuyệt đối đến tệp video trên hệ thống")
    selectedModel: Optional[str] = Field(default=None, description="Mô hình Gemini sử dụng")

class AdminScenarioResponse(BaseModel):
    success: bool
    message: str
    scenario: ScenarioConfigSchema

@router.get("/test-spec")
async def get_test_spec():
    return """# Đặc tả hệ thống Đặt xe trực tuyến (Online Taxi Booking)
Bối cảnh: Hệ thống cho phép khách hàng đặt xe qua ứng dụng di động một cách nhanh chóng và an toàn.
Yêu cầu chức năng:
1. Khách hàng phải có khả năng tạo tài khoản và đặt chuyến đi trực tuyến.
2. Hệ thống phải tự động tính toán chi phí chuyến đi dựa trên khoảng cách GPS trước khi xác nhận.
3. Khách hàng có thể hủy chuyến đi miễn phí trong vòng 5 phút đầu tiên sau khi tài xế nhận chuyến.
Yêu cầu phi chức năng:
4. Hệ thống phải xử lý được 1000 lượt đặt xe đồng thời trong giờ cao điểm.
5. Giao diện ứng dụng phải dễ sử dụng cho mọi đối tượng khách hàng.
Quy tắc nghiệp vụ ngoại lệ:
6. Nếu không tìm thấy tài x tài xế sau 10 phút, hệ thống phải gửi thông báo xin lỗi kèm mã giảm giá 10% cho chuyến đi tiếp theo.
"""

@router.post("/admin/crawl-scenario", response_model=AdminScenarioResponse)
async def crawl_scenario(req: CrawlScenarioRequest):
    try:
        logger.info(f"Yêu cầu cào dữ liệu từ URL: {req.url}")
        # 1. Tải và làm sạch text từ URL
        raw_text = await fetch_url_content(req.url)
        
        # 2. Gọi Gemini Structured Outputs để sinh kịch bản
        config = await generate_scenario_from_ba_text(raw_text, req.selectedModel)
        
        # 3. Lưu file cấu hình JSON cục bộ
        save_scenario_config_file(config)
        
        return AdminScenarioResponse(
            success=True,
            message=f"Đã cào dữ liệu và tạo kịch bản '{config.scenario_title}' thành công.",
            scenario=config
        )
    except Exception as e:
        logger.exception("Lỗi khi cào và tạo kịch bản từ URL.")
        raise HTTPException(
            status_code=500,
            detail=f"Lỗi khi xử lý cào kịch bản: {str(e)}"
        )

@router.post("/admin/upload-video-scenario", response_model=AdminScenarioResponse)
async def upload_video_scenario(req: VideoScenarioRequest):
    try:
        logger.info(f"Yêu cầu nạp tri thức từ video: {req.videoPath}")
        # 1. Gọi Gemini Multimodal để phân tích và sinh kịch bản từ video
        config = await generate_scenario_from_video(req.videoPath, req.selectedModel)
        
        # 2. Lưu file cấu hình JSON cục bộ
        save_scenario_config_file(config)
        
        return AdminScenarioResponse(
            success=True,
            message=f"Đã phân tích video và tạo kịch bản '{config.scenario_title}' thành công.",
            scenario=config
        )
    except Exception as e:
        logger.exception("Lỗi khi xử lý video và tạo kịch bản.")
        raise HTTPException(
            status_code=500,
            detail=f"Lỗi khi xử lý video: {str(e)}"
        )
