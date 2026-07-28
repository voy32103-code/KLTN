import asyncio
import re
from pathlib import Path
from unittest.mock import AsyncMock, patch

from fastapi import HTTPException

from app.services.admin_service import CrawlScenarioRequest, crawl_scenario


def test_provider_client_source_contains_no_embedded_api_key() -> None:
    source_path = (
        Path(__file__).resolve().parents[1]
        / "app"
        / "services"
        / "api_client_manager.py"
    )
    source = source_path.read_text(encoding="utf-8")

    assert re.search(r"sk-[0-9A-Za-z_-]{20,}", source) is None
    assert "dev-internal-key" not in source


def test_admin_crawl_error_is_generic() -> None:
    request = CrawlScenarioRequest(
        url="https://example.com/specification",
        selectedModel="gemini-2.5-flash",
    )

    with patch(
        "app.services.admin_service.fetch_url_content",
        new=AsyncMock(side_effect=RuntimeError("sensitive upstream detail")),
    ):
        try:
            asyncio.run(crawl_scenario(request))
        except HTTPException as exc:
            assert exc.status_code == 500
            assert exc.detail == "Scenario processing failed."
            assert "sensitive" not in exc.detail
        else:
            raise AssertionError("crawl_scenario should return a generic HTTP error")
