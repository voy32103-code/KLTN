import asyncio
import io
import os
import tempfile
import time
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import AsyncMock, patch, MagicMock

from google.genai import types
from pydantic import ValidationError

from app.services.admin_crawler_service import (
    ScenarioConfigSchema,
    ScenarioRequirementRuleSchema,
)
from app.services.admin_crawler_service import validate_public_http_url
from app.services.api_client_manager import ApiClientManager, GroqResponseShim
from app.services.video_processing_service import (
    extract_audio_from_video,
    generate_scenario_from_video,
    validate_media_signature,
    wait_for_gemini_file_active,
)


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
        with self.assertRaises(ValueError):
            asyncio.run(validate_public_http_url("http://127.0.0.1:8000/internal"))

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

    async def test_transient_gemini_failure_uses_next_available_key(self):
        class FailingModels:
            def generate_content(self, **_kwargs):
                raise RuntimeError("connection timed out")

        class SuccessfulModels:
            def generate_content(self, **_kwargs):
                return GroqResponseShim("second key worked")

        with patch.dict(os.environ, {}, clear=True):
            manager = ApiClientManager()
        manager.gemini_clients = [
            SimpleNamespace(models=FailingModels()),
            SimpleNamespace(models=SuccessfulModels()),
        ]

        response = await manager.generate_content(model="gemini-2.5-flash", contents="input")

        self.assertEqual(response.text, "second key worked")
        self.assertIn(0, manager.blocked_until)


class VideoFileRegressionTests(unittest.IsolatedAsyncioTestCase):
    def test_media_signature_rejects_renamed_text_file(self):
        with tempfile.TemporaryDirectory() as directory:
            fake_video = Path(directory) / "meeting.mp4"
            fake_video.write_text("not media", encoding="utf-8")
            with self.assertRaises(ValueError):
                validate_media_signature(fake_video)

    def test_media_signature_reads_only_the_header(self):
        class HeaderFile(io.BytesIO):
            def __init__(self):
                super().__init__(b"\x00\x00\x00\x18ftypmp42")
                self.read_sizes = []

            def read(self, size=-1):
                self.read_sizes.append(size)
                return super().read(size)

        media_file = HeaderFile()
        with patch.object(Path, "open", return_value=media_file):
            validate_media_signature(Path("large-upload.mp4"))

        self.assertEqual(media_file.read_sizes, [16])

    async def test_media_signature_accepts_iso_video_container(self):
        with tempfile.TemporaryDirectory() as directory:
            video = Path(directory) / "meeting.mp4"
            video.write_bytes(b"\x00\x00\x00\x18ftypmp42")
            validate_media_signature(video)

    async def test_audio_extraction_never_overwrites_adjacent_user_file(self):
        class CompletedProcess:
            returncode = 0

            async def communicate(self):
                return b"", b""

        async def create_audio_file(*command, **_kwargs):
            Path(command[-1]).write_bytes(b"audio")
            return CompletedProcess()

        with tempfile.TemporaryDirectory() as directory:
            video = Path(directory) / "meeting.mp4"
            adjacent_audio = Path(directory) / "meeting.mp3"
            video.write_bytes(b"video")
            adjacent_audio.write_bytes(b"keep-me")

            with patch(
                "app.services.video_processing_service.asyncio.create_subprocess_exec",
                new=AsyncMock(side_effect=create_audio_file),
            ):
                generated_audio = await extract_audio_from_video(video)

            try:
                self.assertNotEqual(generated_audio, adjacent_audio)
                self.assertEqual(adjacent_audio.read_bytes(), b"keep-me")
            finally:
                generated_audio.unlink(missing_ok=True)

    async def test_audio_conversion_timeout_kills_the_subprocess(self):
        class HangingProcess:
            returncode = None

            def __init__(self):
                self.killed = False

            def kill(self):
                self.killed = True
                self.returncode = -9

            async def communicate(self):
                if self.killed:
                    return b"", b""
                await asyncio.Future()

        process = HangingProcess()
        with tempfile.TemporaryDirectory() as directory:
            video = Path(directory) / "meeting.mp4"
            video.write_bytes(b"video")
            with (
                patch(
                    "app.services.video_processing_service.asyncio.create_subprocess_exec",
                    new=AsyncMock(return_value=process),
                ),
                patch("app.services.video_processing_service.FFMPEG_TIMEOUT_SECONDS", 0.001),
            ):
                with self.assertRaisesRegex(RuntimeError, "timed out"):
                    await extract_audio_from_video(video)
        self.assertTrue(process.killed)

    @patch("app.services.video_processing_service.client_manager")
    async def test_audio_only_ingestion_falls_back_to_direct_upload(self, mock_client_manager):
        # Setup mock client manager to return a mock client
        mock_client = MagicMock()
        mock_client.files.upload = MagicMock(return_value=SimpleNamespace(name="mock_file", state=types.FileState.ACTIVE))
        mock_client.models.generate_content = MagicMock(return_value=SimpleNamespace(text='{"title": "Test"}'))
        
        mock_client_manager._get_active_gemini_client_index.return_value = 0
        mock_client_manager.gemini_clients = [mock_client]
        
        with tempfile.TemporaryDirectory() as directory:
            video = Path(directory) / "meeting.mp4"
            video.write_bytes(b"\x00\x00\x00\x18ftypmp42")
            with patch("app.services.video_processing_service.is_ffmpeg_available", return_value=False):
                with patch("app.services.video_processing_service.wait_for_gemini_file_active", return_value=SimpleNamespace(name="mock_file")):
                    with patch("app.services.video_processing_service.parse_and_validate_scenario_config", return_value={}):
                        await generate_scenario_from_video(str(video))
                        # Assert that upload was called with the original video file path
                        mock_client.files.upload.assert_called_once_with(file=str(video))

    async def test_gemini_file_polling_waits_for_active_state(self):
        uploaded = SimpleNamespace(name="files/audio")
        client = SimpleNamespace(
            files=SimpleNamespace(
                get=lambda **_kwargs: next(states),
            )
        )
        states = iter([
            SimpleNamespace(state=SimpleNamespace(name="PROCESSING")),
            SimpleNamespace(state=SimpleNamespace(name="ACTIVE")),
        ])
        with patch("app.services.video_processing_service.asyncio.sleep", new=AsyncMock()) as sleep:
            ready = await wait_for_gemini_file_active(client, uploaded)

        self.assertEqual(ready.state.name, "ACTIVE")
        sleep.assert_awaited_once()


if __name__ == "__main__":
    unittest.main()
