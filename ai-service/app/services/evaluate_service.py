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

# Thread-safe lazy loading setup for Gemini Client
_embedder_lock = threading.Lock()
_embedder_initialized = False
has_gemini = False
gemini_client = None

EMBEDDING_MODEL = os.getenv("EMBEDDING_MODEL", "models/text-embedding-004")
ACTUAL_EMBEDDING_MODEL = EMBEDDING_MODEL

def _init_embedder():
    global gemini_client, has_gemini, _embedder_initialized
    if not _embedder_initialized:
        with _embedder_lock:
            if not _embedder_initialized:
                try:
                    from google import genai
                    api_key = os.getenv("GEMINI_API_KEY")
                    if not api_key:
                        raise RuntimeError("GEMINI_API_KEY is not configured.")
                    gemini_client = genai.Client(api_key=api_key)
                    has_gemini = True
                except Exception as e:
                    logger.warning("Failed to initialize Gemini embedder client: %s", str(e))
                    has_gemini = False
                    gemini_client = None
                _embedder_initialized = True

def compute_similarity_matrix(
    extracted_texts: list[str],
    hidden_texts: list[str]
) -> np.ndarray:
    """
    Tính similarity matrix giữa mọi cặp (extracted, hidden) sử dụng Gemini API Embeddings.
    Returns: matrix shape (len(extracted), len(hidden))
    """
    if not extracted_texts or not hidden_texts:
        return np.array([])

    _init_embedder()

    if has_gemini and gemini_client is not None:
        try:
            # Cố gắng sử dụng model được cấu hình, nếu không được tự động thử các ứng cử viên khả dụng
            models_to_try = [EMBEDDING_MODEL, "models/gemini-embedding-2", "models/gemini-embedding-001"]
            res_ext = None
            res_hid = None
            used_model = EMBEDDING_MODEL

            for model_name in models_to_try:
                try:
                    res_ext = gemini_client.models.embed_content(
                        model=model_name,
                        contents=extracted_texts
                    )
                    res_hid = gemini_client.models.embed_content(
                        model=model_name,
                        contents=hidden_texts
                    )
                    used_model = model_name
                    break
                except Exception as api_err:
                    logger.warning("Embeddings call failed with model %s: %s. Trying next candidate...", model_name, str(api_err))
                    continue

            if res_ext is not None and res_hid is not None:
                ext_vectors = np.array([e.values for e in res_ext.embeddings])
                hid_vectors = np.array([e.values for e in res_hid.embeddings])

                # Chuẩn hóa L2 norm để nhân dot product tương đương cosine similarity
                ext_norm = ext_vectors / np.linalg.norm(ext_vectors, axis=1, keepdims=True)
                hid_norm = hid_vectors / np.linalg.norm(hid_vectors, axis=1, keepdims=True)

                sim_matrix = np.dot(ext_norm, hid_norm.T)

                # Giải phóng bộ nhớ tối đa
                del ext_vectors
                del hid_vectors
                del ext_norm
                del hid_norm
                import gc
                gc.collect()

                global ACTUAL_EMBEDDING_MODEL
                ACTUAL_EMBEDDING_MODEL = used_model
                return sim_matrix
        except Exception as e:
            logger.warning("Gemini embeddings calculation failed, falling back to difflib. Error: %s", str(e))
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
            scoringPolicy=ScoringPolicyData(**build_scoring_policy_metadata(ACTUAL_EMBEDDING_MODEL)),
        )

    except Exception as e:
        logger.exception("Evaluation error occurred.")
        raise HTTPException(status_code=500, detail="An error occurred during evaluation processing.")
