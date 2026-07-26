"""
Scenario configuration loading and lookup for prompt gating.
"""
import json
import re
from typing import Any
from dataclasses import dataclass
from functools import lru_cache
from pathlib import Path


SCENARIO_DIR = Path(__file__).resolve().parent.parent / "scenarios"


def normalize_text(text: str) -> str:
    return " ".join(text.lower().split())


@dataclass(frozen=True)
class ScenarioRequirementRule:
    requirement_id: str
    text: str
    gate: int
    keywords: tuple[str, ...]
    question_types: tuple[str, ...]
    reveal_condition: str
    reveal_difficulty: str
    requires: tuple[str, ...] = ()

    @property
    def normalized_text(self) -> str:
        return normalize_text(self.text)


@dataclass(frozen=True)
class ScenarioConfig:
    scenario_key: str
    scenario_title: str
    context: str
    general_keywords: tuple[str, ...]
    gate_keyword_groups: dict[int, tuple[str, ...]]
    question_type_gate_map: dict[str, tuple[int, ...]]
    max_new_reveals_per_turn: int
    requirements: tuple[ScenarioRequirementRule, ...]

    @property
    def normalized_title(self) -> str:
        return normalize_text(self.scenario_title)

    @property
    def requirement_map(self) -> dict[str, ScenarioRequirementRule]:
        return {rule.normalized_text: rule for rule in self.requirements}


def _as_tuple(values) -> tuple[str, ...]:
    return tuple(str(item).strip() for item in values if str(item).strip())


def _validate_top_level(raw: dict, file_path: Path) -> None:
    required_fields = (
        "scenario_key",
        "scenario_title",
        "context",
        "general_keywords",
        "gate_keyword_groups",
        "question_type_gate_map",
        "requirements",
    )
    missing = [field for field in required_fields if field not in raw]
    if missing:
        raise ValueError(f"Scenario config {file_path.name} missing fields: {', '.join(missing)}")


def _parse_requirement(raw: dict, file_path: Path) -> ScenarioRequirementRule:
    required_fields = (
        "id",
        "text",
        "gate",
        "keywords",
        "question_types",
        "reveal_condition",
        "reveal_difficulty",
    )
    missing = [field for field in required_fields if field not in raw]
    if missing:
        raise ValueError(
            f"Scenario config {file_path.name} requirement missing fields: {', '.join(missing)}"
        )

    requires = raw.get("requires")
    if requires is None:
        requires = []
    elif isinstance(requires, str):
        requires = [requires]

    keywords = raw.get("keywords") or []
    question_types = raw.get("question_types") or []

    return ScenarioRequirementRule(
        requirement_id=str(raw["id"]),
        text=str(raw["text"]),
        gate=int(raw["gate"]),
        keywords=_as_tuple(keywords),
        question_types=_as_tuple(question_types),
        reveal_condition=str(raw["reveal_condition"]),
        reveal_difficulty=str(raw["reveal_difficulty"]),
        requires=_as_tuple(requires),
    )


def _parse_config(file_path: Path) -> ScenarioConfig:
    raw = json.loads(file_path.read_text(encoding="utf-8"))
    _validate_top_level(raw, file_path)

    gate_keyword_groups = {
        int(gate): _as_tuple(keywords)
        for gate, keywords in raw["gate_keyword_groups"].items()
    }
    question_type_gate_map = {
        str(question_type): tuple(int(gate) for gate in gates)
        for question_type, gates in raw["question_type_gate_map"].items()
    }
    requirements = tuple(_parse_requirement(item, file_path) for item in raw["requirements"])

    return ScenarioConfig(
        scenario_key=str(raw["scenario_key"]),
        scenario_title=str(raw["scenario_title"]),
        context=str(raw["context"]),
        general_keywords=_as_tuple(raw["general_keywords"]),
        gate_keyword_groups=gate_keyword_groups,
        question_type_gate_map=question_type_gate_map,
        max_new_reveals_per_turn=int(raw.get("max_new_reveals_per_turn", 1)),
        requirements=requirements,
    )


@lru_cache(maxsize=1)
def load_scenario_configs() -> tuple[ScenarioConfig, ...]:
    if not SCENARIO_DIR.exists():
        return ()

    configs = []
    for file_path in sorted(SCENARIO_DIR.glob("*.json")):
        configs.append(_parse_config(file_path))

    return tuple(configs)


def get_scenario_config(scenario_title: str | None, available_requirements: list[str]) -> ScenarioConfig | None:
    configs = load_scenario_configs()
    if not configs:
        return None

    normalized_title = normalize_text(scenario_title or "")
    if normalized_title:
        for config in configs:
            if config.normalized_title == normalized_title:
                return config

    available_norm = {normalize_text(item) for item in available_requirements}
    best_match = None
    best_score = 0

    for config in configs:
        overlap = sum(1 for rule in config.requirements if rule.normalized_text in available_norm)
        if overlap > best_score:
            best_score = overlap
            best_match = config

    return best_match if best_score > 0 else None


def camel_to_snake(name: str) -> str:
    s1 = re.sub('(.)([A-Z][a-z]+)', r'\1_\2', name)
    return re.sub('([a-z0-9])([A-Z])', r'\1_\2', s1).lower()


def convert_keys_to_snake(data: Any) -> Any:
    if isinstance(data, dict):
        new_dict = {}
        for k, v in data.items():
            new_key = camel_to_snake(k)
            new_dict[new_key] = convert_keys_to_snake(v)
        return new_dict
    elif isinstance(data, list):
        return [convert_keys_to_snake(item) for item in data]
    else:
        return data


def parse_config_from_dict(raw: dict) -> ScenarioConfig:
    raw_snake = convert_keys_to_snake(raw)
    
    # Use dummy file_path for validation
    dummy_path = Path("dict_input")
    _validate_top_level(raw_snake, dummy_path)

    gate_keyword_groups = {
        int(gate): _as_tuple(keywords)
        for gate, keywords in raw_snake["gate_keyword_groups"].items()
    }
    question_type_gate_map = {
        str(question_type): tuple(int(gate) for gate in gates)
        for question_type, gates in raw_snake["question_type_gate_map"].items()
    }
    requirements = tuple(_parse_requirement(item, dummy_path) for item in raw_snake["requirements"])

    return ScenarioConfig(
        scenario_key=str(raw_snake["scenario_key"]),
        scenario_title=str(raw_snake["scenario_title"]),
        context=str(raw_snake["context"]),
        general_keywords=_as_tuple(raw_snake["general_keywords"]),
        gate_keyword_groups=gate_keyword_groups,
        question_type_gate_map=question_type_gate_map,
        max_new_reveals_per_turn=int(raw_snake.get("max_new_reveals_per_turn", 1)),
        requirements=requirements,
    )
