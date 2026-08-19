"""
Pydantic models — contract giữa ASP.NET Backend ↔ FastAPI AI Service.
Phải khớp 1:1 với các DTOs trong AiServiceClient.cs.
"""
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field
from datetime import datetime


class ChatMessage(BaseModel):
    role: str          # "Student" hoặc "Stakeholder"
    content: str
    timestamp: datetime


class PersonaProfile(BaseModel):
    name: str
    roleTitle: str
    traits: str        # JSON string chứa personality traits
    style: str         # communication style
    mood: str
    patience: float


class PersonaStateUpdate(BaseModel):
    mood: str
    patience: float
    turnCount: int
    newlyRevealed: list[str]


# ===== Chat =====
class ChatRequest(BaseModel):
    sessionId: str
    scenarioTitle: str | None = None
    studentMessage: str
    history: list[ChatMessage]
    persona: PersonaProfile
    personaStateJson: str | None = None
    availableRequirements: list[str]
    selectedModel: str | None = None
    scenarioConfig: dict | None = None


class ChatResponse(BaseModel):
    stakeholderReply: str
    detectedQuestionType: str | None = None
    detectedTopic: str | None = None
    questionQuality: Literal["vague", "on_topic", "specific", "conditional"] | None = None
    stateUpdate: PersonaStateUpdate | None = None
    isFallback: bool = False


# ===== Extract =====
class ExtractRequest(BaseModel):
    sessionId: str
    history: list[ChatMessage]
    selectedModel: str | None = None
    normalizationGlossary: dict[str, dict[str, str]] | None = None


class ExtractedReq(BaseModel):
    """Legacy simple extraction format - kept for backward compatibility"""
    text: str
    confidence: float
    actor: str | None = None
    action: str | None = None
    object: str | None = None
    condition: str | None = None
    type: Literal["FR", "NFR", "BR"] | None = None
    priority: Literal["high", "medium", "low"] | None = None


# ===== NEW: Structured Requirement Extraction =====
class StructuredRequirement(BaseModel):
    """
    Structured requirement với phân loại đầy đủ theo Actor-Action-Object-Condition.
    Hỗ trợ phân loại FR/NFR/BR và confidence scoring.
    """
    model_config = ConfigDict(
        extra="forbid",
        str_strip_whitespace=True,
        json_schema_extra={
            "examples": [
                {
                    "id": "REQ001",
                    "actor": "Khách hàng",
                    "action": "Đặt",
                    "object": "Phòng",
                    "condition": "Còn phòng trống",
                    "type": "FR",
                    "priority": "high",
                    "confidence": 0.95,
                    "raw_text": "Khách hàng có thể đặt phòng nếu còn phòng trống"
                },
                {
                    "id": "REQ002",
                    "actor": "Hệ thống",
                    "action": "Phản hồi",
                    "object": "Yêu cầu đặt phòng",
                    "condition": "Trong 2 giây",
                    "type": "NFR",
                    "priority": "medium",
                    "confidence": 0.88,
                    "raw_text": "Hệ thống cần phản hồi yêu cầu đặt phòng trong vòng 2 giây"
                }
            ]
        },
    )

    id: str = Field(min_length=1, max_length=64, pattern=r"^[A-Za-z0-9_-]+$")
    actor: str = Field(min_length=1, max_length=160)
    action: str = Field(min_length=1, max_length=160)
    object: str = Field(min_length=1, max_length=240)
    condition: str | None = Field(default=None, max_length=500)
    type: Literal["FR", "NFR", "BR"]
    priority: Literal["high", "medium", "low"] = "medium"
    confidence: float = Field(default=1.0, ge=0.0, le=1.0)
    raw_text: str | None = Field(default=None, max_length=2000)

class NormalizedRequirement(BaseModel):
    """
    Requirement sau khi chuẩn hóa - dùng cho matching với Ground Truth.
    """
    model_config = ConfigDict(extra="forbid")

    id: str
    actorNormalized: str
    actionNormalized: str
    objectNormalized: str
    conditionNormalized: str | None = None
    type: Literal["FR", "NFR", "BR"]
    priority: Literal["high", "medium", "low"] = "medium"
    confidence: float = Field(ge=0.0, le=1.0)
    canonicalKey: str
    canonicalText: str
    original: StructuredRequirement


class ExtractResponse(BaseModel):
    requirements: list[ExtractedReq]
    isFallback: bool = False
    structuredRequirements: list[StructuredRequirement] = Field(default_factory=list)
    normalizedRequirements: list[NormalizedRequirement] = Field(default_factory=list)
    requestedModel: str | None = None
    effectiveModel: str | None = None
    promptVersion: str | None = None
    fallbackReason: str | None = None


# ===== Evaluate =====
class HiddenReq(BaseModel):
    id: str
    text: str
    category: str
    actor: str | None = None
    action: str | None = None
    object: str | None = None
    condition: str | None = None
    type: Literal["FR", "NFR", "BR"] | None = None
    priority: Literal["high", "medium", "low"] | None = None


class EvaluateRequest(BaseModel):
    extracted: list[ExtractedReq]
    hiddenRequirements: list[HiddenReq]
    selectedModel: str | None = None
    scenarioDescription: str | None = None
    feedbackVariant: Literal["A", "B"] = "A"
    normalizationGlossary: dict[str, dict[str, str]] | None = None


class ReqMatch(BaseModel):
    hiddenId: str
    hiddenText: str | None = None
    extractedText: str | None = None
    score: float
    matchType: str     # "exact" | "semantic" | "partial" | "missed"
    reason: str
    componentScores: dict[str, float] | None = None


class DesignSuggestionsData(BaseModel):
    useCaseMermaid: str
    erdMermaid: str
    mainActors: list[str]
    mainEntities: list[str]
    validationStatus: Literal["valid", "repaired", "fallback"] = "valid"
    validationErrors: list[str] = Field(default_factory=list)


class FeedbackData(BaseModel):
    strengths: list[str]
    weaknesses: list[str]
    suggestions: list[str]
    designSuggestions: DesignSuggestionsData | None = None
    extractionsToReview: list[str] = Field(default_factory=list)
    experimentVariant: Literal["A", "B"] = "A"



class ScoringPolicyData(BaseModel):
    preset: str
    exactThreshold: float
    semanticThreshold: float
    partialThreshold: float
    rubricPartialMatcher: bool
    embeddingModel: str
    matchingMethod: str = "semantic_similarity"
    actorWeight: float = 0.20
    actionWeight: float = 0.30
    objectWeight: float = 0.30
    conditionWeight: float = 0.20
    matchThreshold: float = 0.80


class EvaluateResponse(BaseModel):
    coverageScore: float
    matches: list[ReqMatch]
    feedback: FeedbackData
    scoringPolicy: ScoringPolicyData | None = None
    extraExtractedCount: int = Field(default=0, ge=0)
