"""
Evaluate Service — So sánh extracted requirements vs hidden requirements.
Multi-level matching: Exact → Semantic → Partial → Missed.
Dùng sentence-transformers cho semantic similarity (RQ3).
"""
import os
import difflib
import logging
import numpy as np
from fastapi import APIRouter, HTTPException

logger = logging.getLogger(__name__)
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
from app.services.design_service import generate_design_models


import threading

router = APIRouter()

# Thread-safe lazy loading setup for sentence-transformers
_embedder_lock = threading.Lock()
_embedder_initialized = False
has_transformers = False
embedder = None
EMBEDDING_MODEL = os.getenv("EMBEDDING_MODEL", "all-MiniLM-L6-v2")

def _init_embedder():
    global embedder, has_transformers, _embedder_initialized
    if not _embedder_initialized:
        with _embedder_lock:
            if not _embedder_initialized:
                try:
                    from sentence_transformers import SentenceTransformer
                    embedder = SentenceTransformer(EMBEDDING_MODEL)
                    has_transformers = True
                except Exception:
                    has_transformers = False
                    embedder = None
                _embedder_initialized = True

def compute_similarity_matrix(
    extracted_texts: list[str],
    hidden_texts: list[str]
) -> np.ndarray:
    """
    Tính similarity matrix giữa mọi cặp (extracted, hidden).
    Returns: matrix shape (len(extracted), len(hidden))
    """
    if not extracted_texts or not hidden_texts:
        return np.array([])

    _init_embedder()

    if has_transformers and embedder is not None:
        try:
            ext_embeddings = embedder.encode(extracted_texts, normalize_embeddings=True)
            hid_embeddings = embedder.encode(hidden_texts, normalize_embeddings=True)
            # Cosine similarity = dot product khi vectors đã normalize
            return np.dot(ext_embeddings, hid_embeddings.T)
        except Exception:
            pass

    # Fallback to difflib text similarity ratio (no PyTorch/sentence-transformers dependency)
    matrix = np.zeros((len(extracted_texts), len(hidden_texts)))
    for i, ext in enumerate(extracted_texts):
        for j, hid in enumerate(hidden_texts):
            matrix[i, j] = difflib.SequenceMatcher(None, ext.lower(), hid.lower()).ratio()
    return matrix

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
        design_suggestions = generate_design_models(extracted_texts)
        feedback = FeedbackData(
            strengths=strengths,
            weaknesses=weaknesses,
            suggestions=suggestions,
            designSuggestions=design_suggestions,
        )



        return EvaluateResponse(
            coverageScore=round(coverage, 2),
            matches=matches,
            feedback=feedback,
            scoringPolicy=ScoringPolicyData(**build_scoring_policy_metadata(EMBEDDING_MODEL)),
        )

    except Exception as e:
        logger.exception("Evaluation error occurred.")
        raise HTTPException(status_code=500, detail="An error occurred during evaluation processing.")
