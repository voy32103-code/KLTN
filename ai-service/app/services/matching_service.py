"""Pure one-to-one assignment helpers for requirement evaluation."""
from __future__ import annotations

from collections.abc import Callable

import numpy as np


def assign_one_to_one(
    similarity_matrix: np.ndarray,
    is_candidate: Callable[[int, int, float], bool],
) -> dict[int, int]:
    """Greedily choose the highest valid score without reusing either requirement.

    The matrix has extracted requirements on rows and hidden requirements on
    columns.  A requirement is assigned only if the caller says its score is a
    genuine candidate; weak lexical neighbours stay unmatched rather than being
    counted as a second requirement.
    """
    if similarity_matrix.size == 0:
        return {}

    candidates = [
        (float(similarity_matrix[extracted_index, hidden_index]), extracted_index, hidden_index)
        for extracted_index in range(similarity_matrix.shape[0])
        for hidden_index in range(similarity_matrix.shape[1])
    ]
    candidates.sort(key=lambda item: (-item[0], item[1], item[2]))

    used_extracted: set[int] = set()
    used_hidden: set[int] = set()
    assignments: dict[int, int] = {}
    for score, extracted_index, hidden_index in candidates:
        if extracted_index in used_extracted or hidden_index in used_hidden:
            continue
        if not is_candidate(extracted_index, hidden_index, score):
            continue
        assignments[hidden_index] = extracted_index
        used_extracted.add(extracted_index)
        used_hidden.add(hidden_index)
    return assignments
