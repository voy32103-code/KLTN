export type AuthMode = 'login' | 'register'
export type AppView = 'auth' | 'scenarios' | 'chat' | 'review' | 'admin'
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
  label?: string | null
  stakeholder?: {
    id: string
    name: string
    roleTitle: string
    department?: string | null
  } | null
}

export type ScenarioDetail = ScenarioSummary & {
  personas: Persona[]
}

export type ChatMessage = {
  sender: Sender
  content: string
  detectedQuestionType?: string | null
  detectedTopic?: string | null
  questionQuality?: 'vague' | 'on_topic' | 'specific' | 'conditional' | null
  timestamp: string
  pending?: boolean
}

export type DesignSuggestions = {
  useCaseMermaid: string
  erdMermaid: string
  mainActors: string[]
  mainEntities: string[]
  validationStatus?: 'valid' | 'repaired' | 'fallback'
  validationErrors?: string[]
}

export type EvaluationFeedback = {
  strengths: string[]
  weaknesses: string[]
  suggestions: string[]
  designSuggestions?: DesignSuggestions | null
  extractionsToReview?: string[]
  experimentVariant?: 'A' | 'B'
}

export type EvaluationResult = {
  coverageScore: number | null
  overriddenCoverageScore?: number | null
  overriddenByLecturer?: string | null
  overriddenAt?: string | null
  matchedCount: number
  partialCount: number
  missedCount: number
  extractedCount: number
  extraExtractedCount?: number
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
  matchId: string
  hiddenId: string
  hiddenText?: string | null
  extractedText?: string | null
  score: number
  matchType: string
  reason: string
  overriddenMatchType?: string | null
}

export type SessionState = {
  id: string
  startedAt: string
  selectedModel?: string
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
  id: string
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
  rawRequirementData?: string | null
  normalizedRequirementData?: string | null
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

// === Admin & User Management Types ===
export type AdminOverview = {
  totalSessions: number
  totalStudents: number
  totalScenarios: number
  averageCoverage: number
  completedSessions: number
  activeSessions: number
}

export type CoverageDistributionBin = {
  label: string
  count: number
}

export type SessionsOverTimeData = {
  labels: string[]
  counts: number[]
}

export type ScenarioStatItem = {
  scenarioId: string
  scenarioTitle: string
  sessionCount: number
  averageCoverage: number
  averageTurns: number
}

export type TopStudentItem = {
  studentId: string
  studentName: string
  studentEmail: string
  sessionCount: number
  completedCount: number
  bestCoverage: number
  averageCoverage: number
}

export type MatchTypeBreakdownData = {
  exact: number
  semantic: number
  partial: number
  missed: number
}

export type AdminUserItem = {
  id: string
  name: string
  email: string
  role: string
  createdAt: string
}

export type ScenarioRequirementDraft = {
  id: string
  text: string
  gate: number
  keywords: string[]
  question_types: string[]
  reveal_condition: string
  reveal_difficulty: 'Easy' | 'Medium' | 'Hard'
  requires: string[]
  actor: string
  action: string
  object: string
  condition?: string | null
  type: 'FR' | 'NFR' | 'BR'
  priority: 'high' | 'medium' | 'low'
}

export type ScenarioDraft = {
  scenario_key: string
  scenario_title: string
  context: string
  general_keywords: string[]
  gate_keyword_groups: Record<string, string[]>
  question_type_gate_map: Record<string, number[]>
  max_new_reveals_per_turn: number
  requirements: ScenarioRequirementDraft[]
  source_urls?: string[]
}

export type ScenarioPreviewResponse = {
  message: string
  scenario: ScenarioDraft
}
export type AdminState = {
  activeTab: 'overview' | 'users' | 'scenarios'
  overview: AdminOverview | null
  coverageDistribution: CoverageDistributionBin[] | null
  sessionsOverTime: SessionsOverTimeData | null
  scenarioStats: ScenarioStatItem[]
  topStudents: TopStudentItem[]
  matchTypeBreakdown: MatchTypeBreakdownData | null
  users: AdminUserItem[]
  userSearch: string
  userRoleFilter: string
  editingUser: AdminUserItem | null
  isCreatingUser: boolean
  scenarioDraft: ScenarioDraft | null
  scenarioDraftSource: string | null
  feedbackExperiment: {
    variants: Array<{
      variant: string
      sampleSize: number
      target: number
      remaining: number
      quotaMet: boolean
      helpfulness: number
      actionability: number
      noAnswerLeak: number
    }>
    warning?: string | null
    targetPerVariant?: number
    readyForAnalysis?: boolean
    totalRemaining?: number
  } | null
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
  selectedModel: string
  messages: ChatMessage[]
  evaluation: EvaluationResult | null
  reviewSessions: ReviewSessionSummary[]
  selectedReviewSessionId: string | null
  reviewDetail: ReviewSessionDetail | null
  adminState: AdminState | null
  busy: boolean
  notice: Notice | null
  confirmEndSession?: boolean
  modelDropdownOpen?: boolean
}
