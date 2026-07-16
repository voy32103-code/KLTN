import unittest
from types import SimpleNamespace
from typing import Any

from app.services.chat_service import apply_consistency_guard, build_fallback_reply
from app.services.scenario_config_service import get_scenario_config


class ChatServiceFallbackTests(unittest.TestCase):
    def test_fallback_reply_uses_only_newly_revealed_requirement(self):
        req: Any = SimpleNamespace(studentMessage="What is the main purpose?")

        reply = build_fallback_reply(
            req,
            "OpenEnded",
            allowed_requirements=[
                "Students must be able to register for courses online.",
                "The system must enforce prerequisite checking before allowing registration.",
            ],
            newly_revealed=["Students must be able to register for courses online."],
        )

        self.assertIn("Students must be able to register for courses online.", reply)
        self.assertNotIn("prerequisite", reply.lower())

    def test_fallback_reply_redirects_technical_questions(self):
        req: Any = SimpleNamespace(studentMessage="Which database schema should we use?")

        reply = build_fallback_reply(req, "OpenEnded", [], [])

        self.assertIn("chi tiết triển khai", reply)

    def test_consistency_guard_replaces_out_of_gate_reply_with_contextual_fallback(self):
        config = get_scenario_config("University Course Registration System", [])
        self.assertIsNotNone(config)
        req: Any = SimpleNamespace(studentMessage="What is the main purpose of this registration system?")

        reply = apply_consistency_guard(
            (
                "Students can register online, and there are prerequisites and eligibility "
                "rules before registration."
            ),
            req,
            "OpenEnded",
            allowed_requirements=["Students must be able to register for courses online."],
            newly_revealed=["Students must be able to register for courses online."],
            config=config,
        )

        self.assertIn("Students must be able to register for courses online.", reply)
        self.assertNotIn("prerequisites", reply.lower())
        self.assertNotIn("eligibility", reply.lower())

    def test_consistency_guard_redirects_implementation_leakage_with_technical_question(self):
        req: Any = SimpleNamespace(studentMessage="Which database API and backend endpoint should we use?")

        reply = apply_consistency_guard(
            "We should use a database API and backend endpoint for this workflow.",
            req,
            "OpenEnded",
            allowed_requirements=[],
            newly_revealed=[],
            config=None,
        )

        self.assertIn("chi tiết triển khai", reply)


if __name__ == "__main__":
    unittest.main()
