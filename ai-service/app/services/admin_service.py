import ipaddress
import logging
from pathlib import Path
from typing import Annotated
from urllib.parse import urlsplit

from fastapi import APIRouter, HTTPException
from pydantic import AnyHttpUrl, BaseModel, Field, StringConstraints, field_validator

from app.services.admin_crawler_service import (
    ScenarioConfigSchema,
    fetch_url_content,
    generate_scenario_from_ba_text,
    save_scenario_config_file,
)
from app.services.video_processing_service import generate_scenario_from_video

logger = logging.getLogger(__name__)
router = APIRouter()

ModelName = Annotated[str, StringConstraints(strip_whitespace=True, min_length=1, max_length=100)]


class CrawlScenarioRequest(BaseModel):
    url: AnyHttpUrl
    selectedModel: ModelName | None = None

    @field_validator("url")
    @classmethod
    def reject_literal_private_addresses(cls, value: AnyHttpUrl) -> AnyHttpUrl:
        host = value.host
        if host:
            try:
                address = ipaddress.ip_address(host)
            except ValueError:
                return value
            if not address.is_global:
                raise ValueError("Private or local network URLs are not allowed.")
        return value


class VideoScenarioRequest(BaseModel):
    videoPath: Annotated[
        str,
        StringConstraints(strip_whitespace=True, min_length=1, max_length=1024),
    ]
    selectedModel: ModelName | None = None


class AdminScenarioResponse(BaseModel):
    success: bool
    message: str
    scenario: ScenarioConfigSchema


@router.post("/admin/crawl-scenario", response_model=AdminScenarioResponse)
async def crawl_scenario(req: CrawlScenarioRequest):
    try:
        raw_url = str(req.url)
        logger.info(
            "Starting scenario crawl from host %s.",
            urlsplit(raw_url).hostname or "unknown",
        )
        raw_text = await fetch_url_content(raw_url)
        config = await generate_scenario_from_ba_text(raw_text, req.selectedModel)
        save_scenario_config_file(config)

        return AdminScenarioResponse(
            success=True,
            message="Scenario created successfully.",
            scenario=config,
        )
    except HTTPException:
        raise
    except Exception:
        logger.exception("Scenario crawl failed.")
        raise HTTPException(
            status_code=500,
            detail="Scenario processing failed.",
        )


@router.post("/admin/upload-video-scenario", response_model=AdminScenarioResponse)
async def upload_video_scenario(req: VideoScenarioRequest):
    try:
        logger.info(
            "Starting scenario creation from video %s.",
            Path(req.videoPath).name,
        )
        config = await generate_scenario_from_video(req.videoPath, req.selectedModel)
        save_scenario_config_file(config)

        return AdminScenarioResponse(
            success=True,
            message="Scenario created successfully.",
            scenario=config,
        )
    except HTTPException:
        raise
    except Exception:
        logger.exception("Video scenario processing failed.")
        raise HTTPException(
            status_code=500,
            detail="Video scenario processing failed.",
        )
