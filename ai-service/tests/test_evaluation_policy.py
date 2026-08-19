import unittest
from types import SimpleNamespace

from app.services.evaluation_policy import (
    MATCH_THRESHOLD_PRESET,
    PARTIAL_THRESHOLD,
    SEMANTIC_THRESHOLD,
    build_scoring_policy_metadata,
    calculate_coverage,
    classify_match,
    explain_match,
    generate_feedback,
    resolve_thresholds,
    rubric_partial_match,
)


class EvaluationPolicyTests(unittest.TestCase):
    def test_default_threshold_preset_is_current(self):
        self.assertEqual(MATCH_THRESHOLD_PRESET, "current")
        self.assertEqual(PARTIAL_THRESHOLD, 0.55)

    def test_resolve_thresholds_supports_strict_preset(self):
        thresholds = resolve_thresholds({"MATCH_THRESHOLD_PRESET": "strict"})

        self.assertEqual(thresholds["preset"], "strict")
        self.assertEqual(thresholds["exact"], 0.95)
        self.assertEqual(thresholds["semantic"], 0.82)
        self.assertEqual(thresholds["partial"], 0.65)

    def test_individual_threshold_env_overrides_preset_default(self):
        thresholds = resolve_thresholds({
            "MATCH_THRESHOLD_PRESET": "strict",
            "MATCH_PARTIAL_THRESHOLD": "0.6",
        })

        self.assertEqual(thresholds["preset"], "strict")
        self.assertEqual(thresholds["exact"], 0.95)
        self.assertEqual(thresholds["semantic"], 0.82)
        self.assertEqual(thresholds["partial"], 0.6)

    def test_scoring_policy_metadata_uses_runtime_defaults(self):
        metadata = build_scoring_policy_metadata("all-MiniLM-L6-v2")

        self.assertEqual(metadata["preset"], "current")
        self.assertEqual(metadata["exactThreshold"], 0.92)
        self.assertEqual(metadata["semanticThreshold"], 0.75)
        self.assertEqual(metadata["partialThreshold"], 0.55)
        self.assertEqual(metadata["rubricPartialMatcher"], False)
        self.assertEqual(metadata["embeddingModel"], "all-MiniLM-L6-v2")

    def test_scoring_policy_metadata_supports_strict_rubric_candidate(self):
        metadata = build_scoring_policy_metadata(
            "all-MiniLM-L6-v2",
            {
                "MATCH_THRESHOLD_PRESET": "strict",
                "ENABLE_RUBRIC_PARTIAL_MATCHER": "true",
            },
        )

        self.assertEqual(metadata["preset"], "strict")
        self.assertEqual(metadata["exactThreshold"], 0.95)
        self.assertEqual(metadata["semanticThreshold"], 0.82)
        self.assertEqual(metadata["partialThreshold"], 0.65)
        self.assertEqual(metadata["rubricPartialMatcher"], True)
        self.assertEqual(metadata["embeddingModel"], "all-MiniLM-L6-v2")

    def test_rubric_partial_match_catches_near_threshold_concept_overlap(self):
        self.assertTrue(rubric_partial_match(
            "The system must enforce prerequisite checking before allowing registration.",
            "The system has eligibility conditions for registration.",
            0.48,
            partial_threshold=0.55,
        ))

    def test_rubric_partial_match_rejects_low_similarity_unrelated_text(self):
        self.assertFalse(rubric_partial_match(
            "The system must enforce prerequisite checking before allowing registration.",
            "The system should generate inventory stock reports.",
            0.20,
            partial_threshold=0.55,
        ))

    def test_classify_match_uses_expected_threshold_bands(self):
        self.assertEqual(classify_match(0.95), "exact")
        self.assertEqual(classify_match(SEMANTIC_THRESHOLD), "semantic")
        self.assertEqual(classify_match(PARTIAL_THRESHOLD), "partial")
        self.assertEqual(classify_match(PARTIAL_THRESHOLD - 0.01), "missed")

    def test_calculate_coverage_counts_full_and_partial_matches(self):
        matches = [
            SimpleNamespace(matchType="exact"),
            SimpleNamespace(matchType="semantic"),
            SimpleNamespace(matchType="partial"),
            SimpleNamespace(matchType="missed"),
        ]

        coverage, matched, partial, missed = calculate_coverage(matches)

        self.assertEqual(coverage, 62.5)
        self.assertEqual(matched, 2)
        self.assertEqual(partial, 1)
        self.assertEqual(missed, 1)

    def test_explain_match_includes_partial_context(self):
        reason = explain_match(
            "The system must enforce prerequisite checking before allowing registration.",
            "The system has eligibility rules before registration.",
            0.61,
            "partial",
        )

        self.assertIn("thiếu một tác nhân", reason)
        self.assertIn("61%", reason)

    def test_generate_feedback_returns_strengths_and_suggestions(self):
        hidden_reqs = [
            SimpleNamespace(id="R1", text="Students must be able to register for courses online.", category="Functional"),
            SimpleNamespace(id="R2", text="The system must enforce prerequisite checking before allowing registration.", category="Functional"),
        ]
        matches = [
            SimpleNamespace(hiddenId="R1", matchType="semantic", score=0.82),
            SimpleNamespace(hiddenId="R2", matchType="missed", score=0.12),
        ]

        strengths, weaknesses, suggestions = generate_feedback(matches, hidden_reqs)

        self.assertEqual(len(strengths), 1)
        self.assertEqual(len(weaknesses), 1)
        self.assertEqual(len(suggestions), 1)
        self.assertNotIn("R1", " ".join(strengths + weaknesses + suggestions))
        self.assertIn("chức năng", " ".join(strengths + weaknesses + suggestions).lower())
        self.assertIn("Bạn đã khai thác rõ", strengths[0])
        self.assertIn("Chưa có đủ bằng chứng", weaknesses[0])
        self.assertIn("câu hỏi tình huống", suggestions[0])


if __name__ == "__main__":
    unittest.main()
