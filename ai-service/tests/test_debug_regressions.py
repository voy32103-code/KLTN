import asyncio
import os
import tempfile
import time
import unittest
from pathlib import Path
from unittest.mock import AsyncMock, patch

from google.genai import types
from pydantic import ValidationError

from app.services.admin_crawler_service import (
    ScenarioConfigSchema,
    ScenarioRequirementRuleSchema,
)
from app.services.admin_service import CrawlScenarioRequest
from app.services.api_client_manager import ApiClientManager, GroqResponseShim
from app.services.video_processing_service import extract_audio_from_video, generate_scenario_from_video, validate_media_signature


def valid_scenario_data(scenario_key: str) -> dict:
    return {
        "scenario_key": scenario_key,
        "scenario_title": "Inventory System",
        "context": "Inventory context",
        "general_keywords": ["inventory"],
        "gate_keyword_groups": {"0": ["inventory"]},
        "question_type_gate_map": {"OpenEnded": [0]},
        "max_new_reveals_per_turn": 1,
        "requirements": [
            ScenarioRequirementRuleSchema(
                id="R1",
                text="Track stock",
                gate=0,
                keywords=["stock"],
                question_types=["OpenEnded"],
                reveal_condition="Ask about stock",
                reveal_difficulty="Easy",
            )
        ],
    }


class SecurityRegressionTests(unittest.TestCase):
    def test_crawler_rejects_loopback_url(self):
        with self.assertRaises(ValidationError):
            CrawlScenarioRequest(url="http://127.0.0.1:8000/internal")

    def test_scenario_key_rejects_path_traversal(self):
        with self.assertRaises(ValidationError):
            ScenarioConfigSchema(**valid_scenario_data("../../outside"))


class ProviderConfigRegressionTests(unittest.IsolatedAsyncioTestCase):
    async def test_groq_receives_structured_output_config(self):
        with patch.dict(os.environ, {}, clear=True):
            manager = ApiClientManager()

        manager._call_groq = AsyncMock(return_value=GroqResponseShim("{}"))
        config = types.GenerateContentConfig(
            system_instruction="Return a scenario.",
            response_mime_type="application/json",
            temperature=0.2,
            max_output_tokens=20000,
        )

        await manager.generate_content(
            model="llama-3.3-70b-versatile",
            contents="input",
            config=config,
        )

        kwargs = manager._call_groq.await_args.kwargs
        self.assertEqual(kwargs["system_instruction"], "Return a scenario.")
        self.assertEqual(kwargs["temperature"], 0.2)
        self.assertEqual(kwargs["max_output_tokens"], 20000)
        self.assertEqual(kwargs["response_format"], {"type": "json_object"})

    async def test_all_blocked_gemini_keys_skip_blocked_clients(self):
        class FailingModels:
            def generate_content(self, **_kwargs):
                raise AssertionError("blocked Gemini key must not be called")

        class FailingClient:
            models = FailingModels()

        with patch.dict(os.environ, {}, clear=True):
            manager = ApiClientManager()
        manager.gemini_clients = [FailingClient(), FailingClient()]
        manager.blocked_until = {
            0: time.time() + 60,
            1: time.time() + 60,
        }
        manager.groq_api_key = "configured-for-test"
        manager._call_groq = AsyncMock(return_value=GroqResponseShim("fallback"))

        response = await manager.generate_content(
            model="gemini-2.5-flash",
            contents="input",
        )

        self.assertEqual(response.text, "fallback")
        manager._call_groq.assert_awaited_once()


class VideoFileRegressionTests(unittest.IsolatedAsyncioTestCase):
    def test_media_signature_rejects_renamed_text_file(self):
        with tempfile.TemporaryDirectory() as directory:
            fake_video = Path(directory) / "meeting.mp4"
            fake_video.write_text("not media", encoding="utf-8")
            with self.assertRaises(ValueError):
                validate_media_signature(fake_video)

    async def test_audio_only_pipeline_rejects_video_extension_before_provider_call(self):
        with tempfile.TemporaryDirectory() as directory:
            video = Path(directory) / "meeting.mp4"
            video.write_bytes(b"\x00\x00\x00\x18ftypmp42")
            with self.assertRaises(ValueError):
                await generate_scenario_from_video(str(video))

    async def test_audio_extraction_never_overwrites_adjacent_user_file(self):
        class CompletedProcess:
            returncode = 0

            async def communicate(self):
                return b"", b""

        with tempfile.TemporaryDirectory() as directory:
            video = Path(directory) / "meeting.mp4"
            adjacent_audio = Path(directory) / "meeting.mp3"
            video.write_bytes(b"video")
            adjacent_audio.write_bytes(b"keep-me")

            with patch(
                "app.services.video_processing_service.asyncio.create_subprocess_exec",
                new=AsyncMock(return_value=CompletedProcess()),
            ):
                generated_audio = await extract_audio_from_video(video)

            try:
                self.assertNotEqual(generated_audio, adjacent_audio)
                self.assertEqual(adjacent_audio.read_bytes(), b"keep-me")
            finally:
                generated_audio.unlink(missing_ok=True)


if __name__ == "__main__":
    unittest.main()
