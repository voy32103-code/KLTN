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
from app.services.learning_feedback_service import generate_learning_feedback
from app.services.matching_service import assign_one_to_one
from app.services.aaoc_matching_service import (
    assign_weighted_one_to_one,
    classify_aaoc,
    explain_aaoc,
    has_aaoc,
)


import threading

router = APIRouter()

from app.services.api_client_manager import client_manager

EMBEDDING_MODEL = os.getenv("EMBEDDING_MODEL", "models/text-embedding-004")
ACTUAL_EMBEDDING_MODEL = EMBEDDING_MODEL

async def compute_similarity_matrix(
    extracted_texts: list[str],
    hidden_texts: list[str]
) -> np.ndarray:
    """
    Tính similarity matrix giữa mọi cặp (extracted, hidden) sử dụng Gemini API Embeddings.
    Returns: matrix shape (len(extracted), len(hidden))
    """
    if not extracted_texts or not hidden_texts:
        return np.array([])

    try:
        # Cố gắng sử dụng model được cấu hình, nếu không được tự động thử các ứng cử viên khả dụng
        models_to_try = [EMBEDDING_MODEL, "models/gemini-embedding-2", "models/gemini-embedding-001"]
        res_ext = None
        res_hid = None
        used_model = EMBEDDING_MODEL

        for model_name in models_to_try:
            try:
                res_ext = await client_manager.embed_content(
                    model=model_name,
                    contents=extracted_texts
                )
                res_hid = await client_manager.embed_content(
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
        assigned_extracted_indexes: set[int] = set()
        use_aaoc = bool(extracted_texts and req.hiddenRequirements) and all(
            has_aaoc(item) for item in [*req.extracted, *req.hiddenRequirements]
        )

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
        elif use_aaoc:
            # Start with high-confidence structured AAOC matches.  If the
            # extractor uses a different but valid wording, fall back to
            # semantic matching for the remaining one-to-one pairs instead of
            # declaring them missed solely because Action/Object are not
            # identical after normalization.
            assignments = assign_weighted_one_to_one(
                req.extracted,
                req.hiddenRequirements,
                req.normalizationGlossary,
            )
            assigned_extracted_indexes = {
                assignment.extracted_index for assignment in assignments.values()
            }
            unassigned_hidden_indexes = [
                index for index in range(len(req.hiddenRequirements)) if index not in assignments
            ]
            unassigned_extracted_indexes = [
                index for index in range(len(req.extracted)) if index not in assigned_extracted_indexes
            ]
            semantic_assignments: dict[int, int] = {}
            semantic_matrix = None
            if unassigned_hidden_indexes and unassigned_extracted_indexes:
                full_matrix = await compute_similarity_matrix(extracted_texts, hidden_texts)
                semantic_matrix = full_matrix[np.ix_(unassigned_extracted_indexes, unassigned_hidden_indexes)]
                reduced_assignments = assign_one_to_one(
                    semantic_matrix,
                    lambda extracted_index, hidden_index, score: classify_match(
                        score,
                        req.hiddenRequirements[unassigned_hidden_indexes[hidden_index]].text,
                        extracted_texts[unassigned_extracted_indexes[extracted_index]],
                    ) != "missed",
                )
                semantic_assignments = {
                    unassigned_hidden_indexes[hidden_index]: unassigned_extracted_indexes[extracted_index]
                    for hidden_index, extracted_index in reduced_assignments.items()
                }
                assigned_extracted_indexes.update(semantic_assignments.values())
            for hidden_index, hidden in enumerate(req.hiddenRequirements):
                assignment = assignments.get(hidden_index)
                if assignment is not None:
                    extracted = req.extracted[assignment.extracted_index]
                    match_type = classify_aaoc(assignment.score, assignment.component_scores)
                    matches.append(ReqMatch(
                        hiddenId=hidden.id,
                        hiddenText=hidden.text,
                        extractedText=extracted.text,
                        score=assignment.score,
                        matchType=match_type,
                        reason=explain_aaoc(assignment.component_scores, assignment.score, match_type),
                        componentScores=assignment.component_scores,
                    ))
                    continue

                semantic_index = semantic_assignments.get(hidden_index)
                if semantic_index is None:
                    nearest_score = 0.0
                    if semantic_matrix is not None:
                        local_hidden_index = unassigned_hidden_indexes.index(hidden_index)
                        nearest_score = float(np.max(semantic_matrix[:, local_hidden_index]))
                    matches.append(ReqMatch(
                        hiddenId=hidden.id,
                        hiddenText=hidden.text,
                        extractedText=None,
                        score=round(nearest_score, 3),
                        matchType="missed",
                        reason=explain_match(hidden.text, None, nearest_score, "missed"),
                    ))
                    continue

                extracted = req.extracted[semantic_index]
                semantic_score = float(
                    semantic_matrix[
                        unassigned_extracted_indexes.index(semantic_index),
                        unassigned_hidden_indexes.index(hidden_index),
                    ]
                )
                match_type = classify_match(semantic_score, hidden.text, extracted.text)
                matches.append(ReqMatch(
                    hiddenId=hidden.id,
                    hiddenText=hidden.text,
                    extractedText=extracted.text,
                    score=round(semantic_score, 3),
                    matchType=match_type,
                    reason=explain_match(hidden.text, extracted.text, semantic_score, match_type),
                ))
        else:
            sim_matrix = await compute_similarity_matrix(extracted_texts, hidden_texts)

            assignments = assign_one_to_one(
                sim_matrix,
                lambda extracted_index, hidden_index, score: classify_match(
                    score,
                    req.hiddenRequirements[hidden_index].text,
                    extracted_texts[extracted_index],
                ) != "missed",
            )

            for j, hr in enumerate(req.hiddenRequirements):
                best_idx = assignments.get(j)
                if best_idx is None:
                    nearest_idx = int(np.argmax(sim_matrix[:, j]))
                    nearest_score = float(sim_matrix[nearest_idx, j])
                    matches.append(ReqMatch(
                        hiddenId=hr.id,
                        hiddenText=hr.text,
                        extractedText=None,
                        score=round(nearest_score, 3),
                        matchType="missed",
                        reason=explain_match(hr.text, None, nearest_score, "missed"),
                    ))
                    continue

                best_score = float(sim_matrix[best_idx, j])
                match_type = classify_match(best_score, hr.text, extracted_texts[best_idx])
                assigned_extracted_indexes.add(best_idx)
                matches.append(ReqMatch(
                    hiddenId=hr.id,
                    hiddenText=hr.text,
                    extractedText=extracted_texts[best_idx],
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
        extractions_to_review = [
            text
            for index, text in enumerate(extracted_texts)
            if index not in assigned_extracted_indexes
        ]
        strengths, weaknesses, suggestions = generate_feedback(matches, req.hiddenRequirements)
        strengths, weaknesses, suggestions = await generate_learning_feedback(
            matches,
            req.hiddenRequirements,
            req.selectedModel,
            req.feedbackVariant,
            (strengths, weaknesses, suggestions),
        )
        if extractions_to_review:
            suggestions.append(
                "Có yêu cầu được trích xuất nhưng chưa đối chiếu được với ground truth; "
                "hãy để giảng viên xem lại trước khi dùng làm kết luận."
            )
        design_suggestions = await generate_design_models(
            req.extracted,
            req.scenarioDescription,
            req.selectedModel,
        )
        feedback = FeedbackData(
            strengths=strengths,
            weaknesses=weaknesses,
            suggestions=suggestions,
            designSuggestions=design_suggestions,
            extractionsToReview=extractions_to_review,
            experimentVariant=req.feedbackVariant,
        )

        meta = build_scoring_policy_metadata(ACTUAL_EMBEDDING_MODEL)
        scoring_policy = ScoringPolicyData(
            preset=str(meta["preset"]),
            exactThreshold=float(meta["exactThreshold"]),
            semanticThreshold=float(meta["semanticThreshold"]),
            partialThreshold=float(meta["partialThreshold"]),
            rubricPartialMatcher=bool(meta["rubricPartialMatcher"]),
            embeddingModel=str(meta["embeddingModel"]),
            matchingMethod="aaoc_weighted_hybrid_one_to_one" if use_aaoc else "semantic_similarity_one_to_one",
        )

        return EvaluateResponse(
            coverageScore=round(coverage, 2),
            matches=matches,
            feedback=feedback,
            scoringPolicy=scoring_policy,
            extraExtractedCount=len(extractions_to_review),
        )

    except Exception as e:
        logger.exception("Evaluation error occurred.")
        raise HTTPException(status_code=500, detail="An error occurred during evaluation processing.")
