import unittest
from app.services.admin_crawler_service import clean_html, ScenarioConfigSchema

class AdminServiceTests(unittest.TestCase):
    def test_clean_html_removes_scripts_styles_and_tags(self):
        html = """
        <html>
            <head>
                <style>body { color: red; }</style>
                <script>console.log("hello");</script>
            </head>
            <body>
                <h1>Đặc tả yêu cầu</h1>
                <p>Hệ thống phải có <b>bảo mật</b> cao.</p>
            </body>
        </html>
        """
        clean_text = clean_html(html)
        self.assertNotIn("console.log", clean_text)
        self.assertNotIn("color: red", clean_text)
        self.assertIn("Đặc tả yêu cầu", clean_text)
        self.assertIn("Hệ thống phải có bảo mật cao.", clean_text)

    def test_scenario_config_schema_validation(self):
        json_data = {
            "scenario_key": "test_scenario",
            "scenario_title": "Test Scenario System",
            "context": "Mô tả bối cảnh",
            "general_keywords": ["hệ", "thống"],
            "gate_keyword_groups": {
                "1": ["yêu", "cầu"]
            },
            "question_type_gate_map": {
                "ConstraintOriented": [1]
            },
            "max_new_reveals_per_turn": 1,
            "requirements": [
                {
                    "id": "R1",
                    "text": "Yêu cầu 1",
                    "gate": 1,
                    "keywords": ["yêu", "cầu"],
                    "question_types": ["OpenEnded"],
                    "reveal_condition": "Hỏi về yêu cầu",
                    "reveal_difficulty": "Easy",
                    "requires": []
                }
            ]
        }
        
        schema = ScenarioConfigSchema(**json_data)
        self.assertEqual(schema.scenario_key, "test_scenario")
        self.assertEqual(schema.requirements[0].id, "R1")

    def test_convert_keys_to_snake(self):
        from app.services.scenario_config_service import convert_keys_to_snake
        camel_data = {
            "scenarioKey": "test_scenario",
            "gateKeywordGroups": {
                "1": ["yêu", "cầu"]
            },
            "requirements": [
                {
                    "id": "R1",
                    "revealCondition": "Hỏi về yêu cầu",
                    "questionTypes": ["OpenEnded"]
                }
            ]
        }
        snake_data = convert_keys_to_snake(camel_data)
        self.assertEqual(snake_data["scenario_key"], "test_scenario")
        self.assertEqual(snake_data["gate_keyword_groups"]["1"], ["yêu", "cầu"])
        self.assertEqual(snake_data["requirements"][0]["reveal_condition"], "Hỏi về yêu cầu")
        self.assertEqual(snake_data["requirements"][0]["question_types"], ["OpenEnded"])

    def test_parse_config_from_dict_success(self):
        from app.services.scenario_config_service import parse_config_from_dict
        camel_data = {
            "scenarioKey": "test_scenario",
            "scenarioTitle": "Test Scenario System",
            "context": "Mô tả bối cảnh",
            "generalKeywords": ["hệ", "thống"],
            "gateKeywordGroups": {
                "1": ["yêu", "cầu"]
            },
            "questionTypeGateMap": {
                "ConstraintOriented": [1]
            },
            "maxNewRevealsPerTurn": 1,
            "requirements": [
                {
                    "id": "R1",
                    "text": "Yêu cầu 1",
                    "gate": 1,
                    "keywords": ["yêu", "cầu"],
                    "questionTypes": ["OpenEnded"],
                    "revealCondition": "Hỏi về yêu cầu",
                    "revealDifficulty": "Easy",
                    "requires": []
                }
            ]
        }
        config = parse_config_from_dict(camel_data)
        self.assertEqual(config.scenario_key, "test_scenario")
        self.assertEqual(config.scenario_title, "Test Scenario System")
        self.assertEqual(config.requirements[0].requirement_id, "R1")
        self.assertEqual(config.requirements[0].text, "Yêu cầu 1")
        self.assertEqual(config.requirements[0].reveal_condition, "Hỏi về yêu cầu")

    def test_parse_config_from_dict_with_nulls(self):
        from app.services.scenario_config_service import parse_config_from_dict
        camel_data = {
            "scenarioKey": "test_scenario",
            "scenarioTitle": "Test Scenario System",
            "context": "Mô tả bối cảnh",
            "generalKeywords": ["hệ", "thống"],
            "gateKeywordGroups": {
                "1": ["yêu", "cầu"]
            },
            "questionTypeGateMap": {
                "ConstraintOriented": [1]
            },
            "maxNewRevealsPerTurn": 1,
            "requirements": [
                {
                    "id": "R1",
                    "text": "Yêu cầu 1",
                    "gate": 1,
                    "keywords": None,
                    "questionTypes": None,
                    "revealCondition": "Hỏi về yêu cầu",
                    "revealDifficulty": "Easy",
                    "requires": None
                }
            ]
        }
        config = parse_config_from_dict(camel_data)
        self.assertEqual(config.scenario_key, "test_scenario")
        self.assertEqual(config.requirements[0].keywords, ())
        self.assertEqual(config.requirements[0].question_types, ())
        self.assertEqual(config.requirements[0].requires, ())

    def test_parse_and_validate_scenario_config_dict_format(self):
        from app.services.admin_crawler_service import parse_and_validate_scenario_config
        json_str = """
        {
            "scenario_key": "dict_scenario",
            "scenario_title": "Dict Scenario",
            "context": "Context description",
            "general_keywords": ["general"],
            "gate_keyword_groups": {
                "1": ["keyword1"]
            },
            "question_type_gate_map": {
                "OpenEnded": [1]
            },
            "requirements": [
                {
                    "id": "R1",
                    "text": "Requirement 1",
                    "gate": 1,
                    "keywords": ["keyword1"],
                    "question_types": ["OpenEnded"],
                    "reveal_condition": "condition",
                    "reveal_difficulty": "Easy"
                }
            ]
        }
        """
        config = parse_and_validate_scenario_config(json_str)
        self.assertEqual(config.scenario_key, "dict_scenario")
        self.assertEqual(config.gate_keyword_groups["1"], ["keyword1"])
        self.assertEqual(config.question_type_gate_map["OpenEnded"], [1])

    def test_parse_and_validate_scenario_config_gemini_list_format(self):
        from app.services.admin_crawler_service import parse_and_validate_scenario_config
        json_str = """
        {
            "scenario_key": "list_scenario",
            "scenario_title": "List Scenario",
            "context": "Context description",
            "general_keywords": ["general"],
            "gate_keyword_groups": [
                {"gate": "1", "keywords": ["keyword1"]}
            ],
            "question_type_gate_map": [
                {"question_type": "OpenEnded", "gates": [1]}
            ],
            "requirements": [
                {
                    "id": "R1",
                    "text": "Requirement 1",
                    "gate": 1,
                    "keywords": ["keyword1"],
                    "question_types": ["OpenEnded"],
                    "reveal_condition": "condition",
                    "reveal_difficulty": "Easy"
                }
            ]
        }
        """
        config = parse_and_validate_scenario_config(json_str)
        self.assertEqual(config.scenario_key, "list_scenario")
        self.assertEqual(config.gate_keyword_groups["1"], ["keyword1"])
        self.assertEqual(config.question_type_gate_map["OpenEnded"], [1])

    def test_parse_and_validate_scenario_config_camel_case_and_missing_fields(self):
        from app.services.admin_crawler_service import parse_and_validate_scenario_config
        json_str = """
        {
            "scenarioKey": "camel_scenario",
            "scenarioTitle": "Camel Scenario",
            "context": "Context description",
            "gateKeywordGroups": {
                "1": ["keyword1"]
            },
            "questionTypeGateMap": {
                "OpenEnded": [1]
            },
            "requirements": [
                {
                    "id": "R1",
                    "text": "Requirement 1",
                    "gate": 1,
                    "keywords": ["keyword1"],
                    "questionTypes": ["OpenEnded"],
                    "revealCondition": "condition"
                }
            ]
        }
        """
        config = parse_and_validate_scenario_config(json_str)
        self.assertEqual(config.scenario_key, "camel_scenario")
        self.assertEqual(config.general_keywords, [])
        self.assertEqual(config.requirements[0].requires, [])
        self.assertEqual(config.requirements[0].reveal_difficulty, "Medium")

if __name__ == "__main__":
    unittest.main()
