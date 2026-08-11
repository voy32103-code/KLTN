import re
import unittest
from pathlib import Path


class SecurityConfigurationTests(unittest.TestCase):
    def test_provider_client_source_contains_no_embedded_api_key(self):
        source_path = Path(__file__).resolve().parents[1] / "app" / "services" / "api_client_manager.py"
        source = source_path.read_text(encoding="utf-8")

        self.assertIsNone(re.search(r"sk-[0-9A-Za-z_-]{20,}", source))
        self.assertNotIn("dev-internal-key", source)

    def test_legacy_admin_service_is_not_registered(self):
        app_root = Path(__file__).resolve().parents[1] / "app"
        main_source = (app_root / "main.py").read_text(encoding="utf-8")

        self.assertNotIn("admin_service", main_source)
        self.assertFalse((app_root / "services" / "admin_service.py").exists())


if __name__ == "__main__":
    unittest.main()
