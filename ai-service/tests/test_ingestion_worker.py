import os
import unittest
from unittest.mock import AsyncMock, patch

import aiofiles
import aiofiles.os
import httpx

from app import ingestion_worker
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


class RunOnceWorkerTests(unittest.IsolatedAsyncioTestCase):
    async def test_run_once_drains_all_queued_jobs(self):
        with (
            patch("app.ingestion_worker.RUN_ONCE", True),
            patch("app.ingestion_worker._require_configuration"),
            patch("app.ingestion_worker.process_one_job", new=AsyncMock(side_effect=[True, True, False])) as process,
        ):
            await ingestion_worker.run()

        self.assertEqual(process.await_count, 3)

    async def test_download_artifact_streams_to_a_temporary_file(self):
        payload = b"audio fixture"
        client = httpx.AsyncClient(
            transport=httpx.MockTransport(lambda request: httpx.Response(200, content=payload))
        )
        try:
            path = await ingestion_worker._download_artifact(
                client,
                {"originalFileName": "recording.mp3", "downloadUrl": "https://storage.example/recording"},
            )
            self.assertEqual(path.suffix, ".mp3")
            async with aiofiles.open(path, "rb") as artifact_file:
                self.assertEqual(await artifact_file.read(), payload)
        finally:
            if "path" in locals():
                await aiofiles.os.remove(path)
            await client.aclose()

    async def test_process_one_job_returns_false_when_queue_is_empty(self):
        class EmptyResponse:
            status_code = 204

        client = type("Client", (), {"post": AsyncMock(return_value=EmptyResponse())})()

        processed = await ingestion_worker.process_one_job(client)

        self.assertFalse(processed)
        client.post.assert_awaited_once()

    async def test_process_one_job_completes_claimed_job(self):
        class Response:
            def __init__(self, status_code: int, payload: dict | None = None):
                self.status_code = status_code
                self._payload = payload

            def raise_for_status(self):
                return None

            def json(self):
                return self._payload

        job = {"jobId": "job-1", "leaseId": "lease-1", "sourceKind": "Url", "urls": ["https://example.com"]}
        client = type("Client", (), {"post": AsyncMock(side_effect=[Response(200, job), Response(200)])})()
        with patch("app.ingestion_worker._process_job", new=AsyncMock(return_value={"scenarioKey": "demo"})):
            processed = await ingestion_worker.process_one_job(client)

        self.assertTrue(processed)
        self.assertEqual(client.post.await_count, 2)
        self.assertEqual(client.post.await_args_list[1].kwargs["json"]["leaseId"], "lease-1")
