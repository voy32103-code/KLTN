import os
import unittest
from unittest.mock import AsyncMock, patch

from app.services.admin_crawler_service import fetch_url_content_with_spa_fallback


class SpaFallbackTests(unittest.IsolatedAsyncioTestCase):
    async def test_short_static_shell_uses_rendered_spa_text(self):
        with patch(
            "app.services.admin_crawler_service.fetch_url_content",
            new=AsyncMock(return_value="Loading…"),
        ), patch(
            "app.services.admin_crawler_service.render_spa_content",
            new=AsyncMock(return_value="The system must keep an audit log for every booking."),
        ) as render:
            result = await fetch_url_content_with_spa_fallback("https://example.com/app")
        self.assertIn("audit log", result)
        render.assert_awaited_once()

    async def test_substantial_static_page_does_not_start_browser(self):
        with patch.dict(os.environ, {"CRAWLER_RENDER_ALL": "false"}), patch(
            "app.services.admin_crawler_service.fetch_url_content",
            new=AsyncMock(return_value="requirement " * 100),
        ), patch(
            "app.services.admin_crawler_service.render_spa_content",
            new=AsyncMock(),
        ) as render:
            await fetch_url_content_with_spa_fallback("https://example.com/spec")
        render.assert_not_awaited()
