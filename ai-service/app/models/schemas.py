"""
Pydantic models — contract giữa ASP.NET Backend ↔ FastAPI AI Service.
Phải khớp 1:1 với các DTOs trong AiServiceClient.cs.
"""
from pydantic import BaseModel
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


class ChatResponse(BaseModel):
    stakeholderReply: str
    detectedQuestionType: str | None = None
    stateUpdate: PersonaStateUpdate | None = None


# ===== Extract =====
class ExtractRequest(BaseModel):
    sessionId: str
    history: list[ChatMessage]


class ExtractedReq(BaseModel):
    text: str
    confidence: float


class ExtractResponse(BaseModel):
    requirements: list[ExtractedReq]


# ===== Evaluate =====
class HiddenReq(BaseModel):
    id: str
    text: str
    category: str


class EvaluateRequest(BaseModel):
    extracted: list[ExtractedReq]
    hiddenRequirements: list[HiddenReq]


class ReqMatch(BaseModel):
    hiddenId: str
    hiddenText: str | None = None
    extractedText: str | None = None
    score: float
    matchType: str     # "exact" | "semantic" | "partial" | "missed"
    reason: str


class DesignSuggestionsData(BaseModel):
    useCaseMermaid: str
    erdMermaid: str
    mainActors: list[str]
    mainEntities: list[str]


class FeedbackData(BaseModel):
    strengths: list[str]
    weaknesses: list[str]
    suggestions: list[str]
    designSuggestions: DesignSuggestionsData | None = None



class ScoringPolicyData(BaseModel):
    preset: str
    exactThreshold: float
    semanticThreshold: float
    partialThreshold: float
    rubricPartialMatcher: bool
    embeddingModel: str


class EvaluateResponse(BaseModel):
    coverageScore: float
    matches: list[ReqMatch]
    feedback: FeedbackData
    scoringPolicy: ScoringPolicyData | None = None
