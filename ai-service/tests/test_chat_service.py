import unittest
from app.models.schemas import ChatRequest, PersonaProfile
from types import SimpleNamespace
from typing import Any

from app.services.chat_service import apply_consistency_guard, build_fallback_reply, build_system_prompt
from app.services.scenario_config_service import get_scenario_config


class ChatServiceFallbackTests(unittest.TestCase):
    def test_system_prompt_requires_complete_professional_vietnamese_replies(self):
        request = ChatRequest(
            sessionId="session",
            scenarioTitle="University Course Registration System",
            studentMessage="Đăng nhập mà không cần đăng ký hả?",
            history=[],
            persona=PersonaProfile(
                name="Stakeholder",
                roleTitle="Registrar",
                traits='{"traits":["impatient"]}',
                style="formal-busy",
                mood="neutral_busy",
                patience=0.65,
            ),
            availableRequirements=["Students must be able to register for courses online."],
        )

        prompt = build_system_prompt(
            request,
            {"mood": "neutral_busy", "patience": 0.65, "turn_count": 1},
            "OpenEnded",
            ["Students must be able to register for courses online."],
            [],
            [],
            get_scenario_config("University Course Registration System", []),
            "on_topic",
            "R1",
        )

        self.assertIn("complete grammatical sentences", prompt)
        self.assertIn("without being rude", prompt)
        self.assertIn("Do not output internal labels", prompt)

    def test_end_user_prompt_blocks_technical_explanations(self):
        request = ChatRequest(
            sessionId="session",
            scenarioTitle="DevOps - CI/CD",
            studentMessage="CI/CD pipeline và secret được cấu hình như thế nào?",
            history=[],
            persona=PersonaProfile(
                name="Người dùng cuối - Hợp tác",
                roleTitle="Người dùng Trực tiếp",
                traits='{"jargon_level":"low","technical_scope":"none"}',
                style="collaborative",
                mood="neutral",
                patience=1,
                knowledgeLevel="low",
            ),
            availableRequirements=["Production deployment requires an approved change request."],
        )

        prompt = build_system_prompt(
            request,
            {"mood": "neutral", "patience": 1, "turn_count": 1},
            "Probing",
            ["Production deployment requires an approved change request."],
            [], [], None, "specific", "R1",
        )

        self.assertIn("You are an END USER", prompt)
        self.assertIn("Do NOT explain or speculate about APIs, databases", prompt)
        self.assertIn("Technical wording in the allowed knowledge is not permission", prompt)

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
