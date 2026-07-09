"""
Evaluate Service — So sánh extracted requirements vs hidden requirements.
Multi-level matching: Exact → Semantic → Partial → Missed.
Dùng sentence-transformers cho semantic similarity (RQ3).
"""
import os
from fastapi import APIRouter, HTTPException
from sentence_transformers import SentenceTransformer
import numpy as np
from app.models.schemas import (
    EvaluateRequest, EvaluateResponse,
    ReqMatch, FeedbackData, ScoringPolicyData
)
from app.services.evaluation_policy import (
    build_scoring_policy_metadata,
    calculate_coverage,
    classify_match,
    explain_match,
    generate_feedback,
)

router = APIRouter()

# Load embedding model 1 lần khi startup (all-MiniLM-L6-v2 ≈ 80MB)
EMBEDDING_MODEL = os.getenv("EMBEDDING_MODEL", "all-MiniLM-L6-v2")
embedder = SentenceTransformer(EMBEDDING_MODEL)

def compute_similarity_matrix(
    extracted_texts: list[str],
    hidden_texts: list[str]
) -> np.ndarray:
    """
    Tính cosine similarity matrix giữa mọi cặp (extracted, hidden).
    Returns: matrix shape (len(extracted), len(hidden))
    """
    if not extracted_texts or not hidden_texts:
        return np.array([])

    ext_embeddings = embedder.encode(extracted_texts, normalize_embeddings=True)
    hid_embeddings = embedder.encode(hidden_texts, normalize_embeddings=True)

    # Cosine similarity = dot product khi vectors đã normalize
    return np.dot(ext_embeddings, hid_embeddings.T)

@router.post("/evaluate", response_model=EvaluateResponse)
async def evaluate(req: EvaluateRequest):
    """
    Pipeline đánh giá 3 bước:
    1. Tính similarity matrix
    2. Matching: mỗi hidden req → best match từ extracted
    3. Tính coverage score + generate feedback
    """
    try:
        extracted_texts = [r.text for r in req.extracted]
        hidden_texts = [r.text for r in req.hiddenRequirements]

        matches: list[ReqMatch] = []

        if not extracted_texts:
            # Student không extract được gì
            for hr in req.hiddenRequirements:
                matches.append(ReqMatch(
                    hiddenId=hr.id,
                    hiddenText=hr.text,
                    extractedText=None,
                    score=0.0,
                    matchType="missed",
                    reason=explain_match(hr.text, None, 0.0, "missed"),
                ))
        else:
            sim_matrix = compute_similarity_matrix(extracted_texts, hidden_texts)

            for j, hr in enumerate(req.hiddenRequirements):
                # Tìm extracted requirement giống nhất
                best_idx = int(np.argmax(sim_matrix[:, j]))
                best_score = float(sim_matrix[best_idx, j])
                match_type = classify_match(best_score, hr.text, extracted_texts[best_idx])

                matches.append(ReqMatch(
                    hiddenId=hr.id,
                    hiddenText=hr.text,
                    extractedText=extracted_texts[best_idx] if match_type != "missed" else None,
                    score=round(best_score, 3),
                    matchType=match_type,
                    reason=explain_match(
                        hr.text,
                        extracted_texts[best_idx],
                        best_score,
                        match_type,
                    ),
                ))

        coverage, _, _, _ = calculate_coverage(matches)
        strengths, weaknesses, suggestions = generate_feedback(matches, req.hiddenRequirements)
        feedback = FeedbackData(
            strengths=strengths,
            weaknesses=weaknesses,
            suggestions=suggestions,
        )

        return EvaluateResponse(
            coverageScore=round(coverage, 2),
            matches=matches,
            feedback=feedback,
            scoringPolicy=ScoringPolicyData(**build_scoring_policy_metadata(EMBEDDING_MODEL)),
        )

    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Evaluation error: {str(e)}")
