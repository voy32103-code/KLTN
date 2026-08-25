"""Queue worker entrypoint for local execution or one GitHub Actions run."""
import asyncio
import logging
import os
import tempfile
from pathlib import Path

import aiofiles
import aiofiles.os
import httpx

from app.services.admin_crawler_service import (
    fetch_url_content_with_spa_fallback,
    generate_scenario_from_ba_text,
)
from app.services.video_processing_service import generate_scenario_from_video

logging.basicConfig(level=os.getenv("LOG_LEVEL", "INFO"))
logger = logging.getLogger(__name__)

BACKEND_URL = os.getenv("INGESTION_BACKEND_URL", "").rstrip("/")
WORKER_KEY = os.getenv("INGESTION_WORKER_KEY", "")
POLL_SECONDS = max(1, int(os.getenv("INGESTION_WORKER_POLL_SECONDS", "3")))
RUN_ONCE = os.getenv("INGESTION_WORKER_RUN_ONCE", "false").lower() in {"1", "true", "yes"}
MAX_DOWNLOAD_BYTES = 250 * 1024 * 1024


def _require_configuration() -> None:
    if not BACKEND_URL.startswith(("http://", "https://")):
        raise RuntimeError("INGESTION_BACKEND_URL must be an absolute URL.")
    if len(WORKER_KEY) < 32 or "change_me" in WORKER_KEY.lower():
        raise RuntimeError("INGESTION_WORKER_KEY must contain at least 32 non-placeholder characters.")


async def _download_artifact(client: httpx.AsyncClient, artifact: dict) -> Path:
    suffix = Path(artifact["originalFileName"]).suffix or ".bin"
    descriptor, filename = tempfile.mkstemp(prefix="reqsim-ingestion-", suffix=suffix)
    os.close(descriptor)
    path = Path(filename)
    try:
        total = 0
        async with client.stream("GET", artifact["downloadUrl"], timeout=90.0) as response:
            response.raise_for_status()
            async with aiofiles.open(path, "wb") as output:
                async for chunk in response.aiter_bytes():
                    total += len(chunk)
                    if total > MAX_DOWNLOAD_BYTES:
                        raise ValueError("artifact_too_large")
                    await output.write(chunk)
        if total == 0:
            raise ValueError("empty_artifact")
        return path
    except Exception:
        await aiofiles.os.remove(path)
        raise


async def _process_job(client: httpx.AsyncClient, job: dict) -> dict:
    source_kind = str(job["sourceKind"]).lower()
    model = job.get("selectedModel")
    if source_kind == "url":
        urls = job.get("urls") or []
        texts = await asyncio.gather(*(fetch_url_content_with_spa_fallback(url) for url in urls))
        scenario = await generate_scenario_from_ba_text("\n\n".join(texts), model)
        scenario.source_urls = urls
        return scenario.model_dump()
    if source_kind == "audio":
        artifact = job.get("artifact")
        if not artifact:
            raise ValueError("missing_artifact")
        source_path = await _download_artifact(client, artifact)
        try:
            scenario = await generate_scenario_from_video(str(source_path), model)
            return scenario.model_dump()
        finally:
            await aiofiles.os.remove(source_path)
    raise ValueError("unsupported_source")


def _safe_error_code(error: Exception) -> str:
    value = str(error).lower()
    if "private or local" in value:
        return "blocked_url"
    if "too large" in value or "exceeds" in value:
        return "input_too_large"
    if "format" in value or "media" in value:
        return "unsupported_media"
    if "gemini" in value or "api key" in value:
        return "provider_unavailable"
    if "ffmpeg" in value:
        return "missing_ffmpeg"
    if "time" in value or "timeout" in value:
        return "processing_timeout"
    if "404" in value or "403" in value:
        return "download_failed"
    return "processing_failed"


async def process_one_job(client: httpx.AsyncClient) -> bool:
    """Claim, process, and complete a single job. Returns False when the queue is empty."""
    claim = await client.post(f"{BACKEND_URL}/api/admin-ingestion/worker/claim")
    if claim.status_code == 204:
        logger.info("No queued ingestion job was available.")
        return False
    claim.raise_for_status()

    job = claim.json()
    completion: dict = {"leaseId": job["leaseId"]}
    try:
        completion["scenario"] = await _process_job(client, job)
        logger.info("Ingestion job %s produced a scenario draft.", job["jobId"])
    except Exception as error:
        logger.exception("Ingestion job %s failed.", job["jobId"])
        completion["errorCode"] = _safe_error_code(error)

    response = await client.post(
        f"{BACKEND_URL}/api/admin-ingestion/worker/jobs/{job['jobId']}/complete",
        json=completion,
    )
    response.raise_for_status()
    return True


async def run() -> None:
    _require_configuration()
    headers = {"X-Ingestion-Worker-Key": WORKER_KEY}
    async with httpx.AsyncClient(headers=headers, timeout=30.0) as client:
        if RUN_ONCE:
            # GitHub Actions invokes this mode. Drain the durable queue so one
            # workflow run is not artificially limited to a single job.
            while await process_one_job(client):
                pass
            return

        while True:
            try:
                processed = await process_one_job(client)
                if not processed:
                    await asyncio.sleep(POLL_SECONDS)
            except asyncio.CancelledError:
                raise
            except Exception:
                logger.exception("Ingestion worker polling failed.")
                await asyncio.sleep(POLL_SECONDS)


if __name__ == "__main__":
    asyncio.run(run())
