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

if __name__ == "__main__":
    unittest.main()
