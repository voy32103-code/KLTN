import unittest
from types import SimpleNamespace

from app.services.gating_service import (
    detect_question_type,
    is_repeated_question,
    load_persona_state,
    select_gated_requirements,
)
from app.services.consistency_checker import check_response_consistency
from app.services.scenario_config_service import get_scenario_config, load_scenario_configs


def make_request(message: str, state_json: str | None = None):
    config = get_scenario_config("University Course Registration System", [])
    assert config is not None
    return SimpleNamespace(
        sessionId="test-session",
        scenarioTitle="University Course Registration System",
        studentMessage=message,
        history=[],
        persona=SimpleNamespace(
            name="Ms. Nguyen",
            roleTitle="University Registrar",
            traits='{"traits":["organized","impatient","detail_oriented"]}',
            style="formal-busy",
            mood="neutral_busy",
            patience=0.65,
        ),
        personaStateJson=state_json,
        availableRequirements=[rule.text for rule in config.requirements],
    )


class ScenarioConfigTests(unittest.TestCase):
    def test_all_baseline_configs_load(self):
        configs = load_scenario_configs()
        titles = {config.scenario_title for config in configs}

        self.assertIn("University Course Registration System", titles)
        self.assertIn("Hospital Appointment System", titles)
        self.assertIn("Small Business Inventory Management", titles)
        self.assertGreaterEqual(len(configs), 3)

    def test_university_registration_config_loads_with_required_rules(self):
        config = get_scenario_config("University Course Registration System", [])

        self.assertIsNotNone(config)
        self.assertEqual(config.scenario_key, "university_course_registration")
        self.assertEqual(len(config.requirements), 10)
        self.assertEqual(config.max_new_reveals_per_turn, 1)

    def test_new_scenarios_have_reviewable_requirement_sets(self):
        hospital = get_scenario_config("Hospital Appointment System", [])
        inventory = get_scenario_config("Small Business Inventory Management", [])

        self.assertIsNotNone(hospital)
        self.assertIsNotNone(inventory)
        self.assertEqual(len(hospital.requirements), 12)
        self.assertEqual(len(inventory.requirements), 9)
        self.assertTrue(all(rule.reveal_condition for rule in hospital.requirements))
        self.assertTrue(all(rule.reveal_condition for rule in inventory.requirements))


class VietnameseQuestionClassificationTests(unittest.TestCase):
    def test_vietnamese_question_types_are_classified(self):
        self.assertEqual(
            detect_question_type("Nếu lớp đã đầy thì hệ thống xử lý thế nào?"),
            "ExceptionOriented",
        )
        self.assertEqual(
            detect_question_type("Bạn có thể giải thích cụ thể quy trình đăng ký không?"),
            "Clarifying",
        )
        self.assertEqual(
            detect_question_type("Hệ thống có bắt buộc kiểm tra môn tiên quyết không?"),
            "ConstraintOriented",
        )

    def test_near_duplicate_question_is_treated_as_repeated(self):
        history = [SimpleNamespace(
            role="Student",
            content="What is the complete online registration process for students?",
        )]

        self.assertTrue(is_repeated_question(
            "What is the complete online registration process for student?",
            history,
        ))


class GatingSelectionTests(unittest.TestCase):
    def test_opening_question_reveals_only_gate_zero_requirement(self):
        req = make_request("What is the main purpose of this registration system?")
        config = get_scenario_config(req.scenarioTitle, req.availableRequirements)
        state = load_persona_state(req)

        allowed, previous, newly = select_gated_requirements(
            req,
            state,
            detect_question_type(req.studentMessage),
            config,
        )

        self.assertEqual(previous, [])
        self.assertEqual(newly, ["Sinh viên phải có khả năng đăng ký các học phần trực tuyến."])
        self.assertEqual(allowed, newly)

    def test_one_turn_reveals_at_most_one_new_requirement(self):
        req = make_request("What rules, prerequisites, deadlines, and waitlist behavior should registration handle?")
        config = get_scenario_config(req.scenarioTitle, req.availableRequirements)
        state = load_persona_state(req)

        _, _, newly = select_gated_requirements(
            req,
            state,
            detect_question_type(req.studentMessage),
            config,
        )

        self.assertLessEqual(len(newly), 1)

    def test_missing_scenario_config_fails_closed(self):
        req = make_request("What is the main purpose of this registration system?")
        req.scenarioTitle = "Unknown Scenario"
        state = load_persona_state(req)

        allowed, previous, newly = select_gated_requirements(
            req,
            state,
            detect_question_type(req.studentMessage),
            config=None,
        )

        self.assertEqual(allowed, [])
        self.assertEqual(previous, [])
        self.assertEqual(newly, [])

    def test_unmapped_requirements_fail_closed(self):
        req = make_request("What is the main purpose of this registration system?")
        req.availableRequirements = ["This requirement does not exist in the scenario config."]
        config = get_scenario_config(req.scenarioTitle, [])
        state = load_persona_state(req)

        allowed, previous, newly = select_gated_requirements(
            req,
            state,
            detect_question_type(req.studentMessage),
            config,
        )

        self.assertEqual(allowed, [])
        self.assertEqual(previous, [])
        self.assertEqual(newly, [])


class ConsistencyCheckerTests(unittest.TestCase):
    def test_checker_flags_out_of_gate_disclosure(self):
        config = get_scenario_config("University Course Registration System", [])
        check = check_response_consistency(
            "The system should check prerequisites and enforce prerequisite rules before registration.",
            allowed_requirements=["Sinh viên phải có khả năng đăng ký các học phần trực tuyến."],
            config=config,
        )

        self.assertFalse(check.passed)
        self.assertEqual(check.violations[0].code, "out_of_gate_disclosure")

    def test_checker_allows_allowed_requirement(self):
        allowed = ["Hệ thống phải bắt buộc kiểm tra điều kiện tiên quyết trước khi cho phép đăng ký."]
        config = get_scenario_config("University Course Registration System", allowed)
        check = check_response_consistency(
            "Yes, prerequisite checking is needed before registration.",
            allowed_requirements=allowed,
            config=config,
        )

        self.assertTrue(check.passed)

    def test_financial_hold_is_not_revealed_before_financial_integration(self):
        req = make_request("Can unpaid fees block a student from registration?")
        config = get_scenario_config(req.scenarioTitle, req.availableRequirements)
        state = load_persona_state(req)

        _, _, newly = select_gated_requirements(
            req,
            state,
            detect_question_type(req.studentMessage),
            config,
        )

        self.assertNotIn(
            "Sinh viên chưa hoàn tất học phí phải bị chặn đăng ký cho đến khi thanh toán xong dư nợ.",
            newly,
        )
        self.assertEqual(
            newly,
            ["Hệ thống phải tích hợp với hệ thống tài chính hiện có để tính toán các khoản phí liên quan đến đăng ký."],
        )

    def test_financial_hold_can_reveal_after_dependency_is_revealed(self):
        state_json = """
        {
          "mood": "neutral_busy",
          "patience": 0.62,
          "turn_count": 2,
          "revealed_requirements": [
            "Hệ thống phải tích hợp với hệ thống tài chính hiện có để tính toán các khoản phí liên quan đến đăng ký."
          ]
        }
        """
        req = make_request("Can unpaid fees block a student from registration?", state_json)
        config = get_scenario_config(req.scenarioTitle, req.availableRequirements)
        state = load_persona_state(req)

        _, previous, newly = select_gated_requirements(
            req,
            state,
            detect_question_type(req.studentMessage),
            config,
        )

        self.assertEqual(
            previous,
            ["Hệ thống phải tích hợp với hệ thống tài chính hiện có để tính toán các khoản phí liên quan đến đăng ký."],
        )
        self.assertEqual(
            newly,
            ["Sinh viên chưa hoàn tất học phí phải bị chặn đăng ký cho đến khi thanh toán xong dư nợ."],
        )

    def test_low_patience_blocks_new_gate_four_quality_requirement(self):
        state_json = """
        {
          "mood": "rushed",
          "patience": 0.35,
          "turn_count": 4,
          "revealed_requirements": []
        }
        """
        req = make_request("What performance load should it support during peak periods?", state_json)
        config = get_scenario_config(req.scenarioTitle, req.availableRequirements)
        state = load_persona_state(req)

        allowed, _, newly = select_gated_requirements(
            req,
            state,
            detect_question_type(req.studentMessage),
            config,
        )

        self.assertEqual(allowed, [])
        self.assertEqual(newly, [])

    def test_revealed_requirement_can_be_referenced_again_without_new_reveal(self):
        state_json = """
        {
          "mood": "neutral_busy",
          "patience": 0.60,
          "turn_count": 1,
          "revealed_requirements": [
            "Sinh viên phải có khả năng đăng ký các học phần trực tuyến."
          ]
        }
        """
        req = make_request("Can you explain more about the online registration process?", state_json)
        config = get_scenario_config(req.scenarioTitle, req.availableRequirements)
        state = load_persona_state(req)

        allowed, previous, newly = select_gated_requirements(
            req,
            state,
            detect_question_type(req.studentMessage),
            config,
        )

        self.assertEqual(previous, ["Sinh viên phải có khả năng đăng ký các học phần trực tuyến."])
        self.assertIn("Sinh viên phải có khả năng đăng ký các học phần trực tuyến.", allowed)
        self.assertLessEqual(len(newly), 1)


if __name__ == "__main__":
    unittest.main()
