export type AuthMode = 'login' | 'register'
export type AppView = 'auth' | 'scenarios' | 'chat' | 'review'
export type Sender = 'Student' | 'Stakeholder'

export type ScenarioSummary = {
  id: string
  title: string
  description: string
  domain?: string | null
  difficulty: string
  personaCount: number
  requirementCount: number
}

export type Persona = {
  id: string
  name: string
  roleTitle?: string | null
  difficulty: string
  communicationStyle?: string | null
  knowledgeLevel?: string | null
}

export type ScenarioDetail = ScenarioSummary & {
  personas: Persona[]
}

export type ChatMessage = {
  sender: Sender
  content: string
  detectedQuestionType?: string | null
  timestamp: string
  pending?: boolean
}

export type EvaluationFeedback = {
  strengths: string[]
  weaknesses: string[]
  suggestions: string[]
}

export type EvaluationResult = {
  coverageScore: number | null
  matchedCount: number
  partialCount: number
  missedCount: number
  extractedCount: number
  feedback?: EvaluationFeedback | null
  matches?: RequirementMatchReport[]
  scoringPolicy?: ScoringPolicy | null
}

export type ScoringPolicy = {
  preset: string
  exactThreshold: number
  semanticThreshold: number
  partialThreshold: number
  rubricPartialMatcher: boolean
  embeddingModel: string
}

export type RequirementMatchReport = {
  hiddenId: string
  hiddenText?: string | null
  extractedText?: string | null
  score: number
  matchType: string
  reason: string
}

export type SessionState = {
  id: string
  startedAt: string
}

export type ReviewSessionSummary = {
  id: string
  startedAt: string
  endedAt?: string | null
  isActive: boolean
  finalizationStatus: string
  student: {
    id: string
    name: string
    email: string
  }
  scenario: {
    id: string
    title: string
    domain?: string | null
    difficulty: string
  }
  persona: {
    id: string
    name: string
    roleTitle?: string | null
  }
  messageCount: number
  studentTurnCount: number
  evaluation?: ReviewEvaluationSummary | null
}

export type ReviewEvaluationSummary = {
  coverageScore?: number | null
  matchedCount: number
  partialCount: number
  missedCount: number
  totalRequirements: number
  evaluatedAt: string
}

export type ReviewExtractedRequirement = {
  id: string
  requirementText: string
  confidenceScore?: number | null
  extractedAt: string
}

export type ReviewHiddenRequirement = {
  id: string
  requirementText: string
  category: string
  revealDifficulty: string
  revealCondition?: string | null
  gateOrder: number
}

export type ReviewSessionDetail = {
  session: {
    id: string
    startedAt: string
    endedAt?: string | null
    isActive: boolean
    finalizationStatus: string
    student: ReviewSessionSummary['student']
    scenario: ReviewSessionSummary['scenario'] & {
      description: string
    }
    persona: ReviewSessionSummary['persona'] & {
      communicationStyle?: string | null
      knowledgeLevel?: string | null
    }
  }
  messages: ChatMessage[]
  extractedRequirements: ReviewExtractedRequirement[]
  hiddenRequirements: ReviewHiddenRequirement[]
  evaluation?: EvaluationResult | null
}

export type Notice = {
  type: 'info' | 'error' | 'success'
  text: string
}

export type UserInfo = {
  email?: string
  role?: string
}

export type AppState = {
  token: string | null
  user: UserInfo | null
  authMode: AuthMode
  view: AppView
  scenarios: ScenarioSummary[]
  selectedScenario: ScenarioDetail | null
  selectedPersonaId: string | null
  session: SessionState | null
  messages: ChatMessage[]
  evaluation: EvaluationResult | null
  reviewSessions: ReviewSessionSummary[]
  selectedReviewSessionId: string | null
  reviewDetail: ReviewSessionDetail | null
  busy: boolean
  notice: Notice | null
}
