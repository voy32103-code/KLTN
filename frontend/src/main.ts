import './style.css'
import { createApiClient } from './api'
import { API_BASE_URL, EXPIRED_SESSION_NOTICE, TOKEN_KEY } from './constants'
import type {
  AppState,
  ChatMessage,
  EvaluationResult,
  Notice,
  ReviewSessionDetail,
  ReviewSessionSummary,
  ScenarioDetail,
  ScenarioSummary,
  SessionState,
} from './types'
import { getErrorMessage, isPrivilegedRole, isTokenExpired, readUserFromToken } from './utils'
import { renderApp } from './views'



const root = document.querySelector<HTMLDivElement>('#app')
if (!root) throw new Error('Missing #app root')
const app: HTMLDivElement = root

const storedToken = localStorage.getItem(TOKEN_KEY)
const hasExpiredStoredToken = Boolean(storedToken && isTokenExpired(storedToken))
const initialToken = hasExpiredStoredToken ? null : storedToken

const state: AppState = {
  token: initialToken,
  user: null,
  authMode: 'login',
  view: 'auth',
  scenarios: [],
  selectedScenario: null,
  selectedPersonaId: null,
  session: null,
  messages: [],
  evaluation: null,
  reviewSessions: [],
  selectedReviewSessionId: null,
  reviewDetail: null,
  busy: false,
  notice: null,
}

const api = createApiClient({
  baseUrl: API_BASE_URL,
  getToken: () => state.token,
  onUnauthorized: () => logout(false),
})

state.user = state.token ? readUserFromToken(state.token) : null
state.view = state.token ? 'scenarios' : 'auth'
if (hasExpiredStoredToken) {
  localStorage.removeItem(TOKEN_KEY)
  state.notice = { type: 'info', text: EXPIRED_SESSION_NOTICE }
}

render()
if (state.token) {
  void loadScenarios()
}

function render() {
  app.innerHTML = renderApp(state)
  bindEvents()

  // Sync body view class
  document.body.className = ''
  document.body.classList.add(`view-${state.view}`)
  initAnimations()
}

function bindEvents() {
  document.querySelectorAll<HTMLButtonElement>('[data-auth-mode]').forEach((button) => {
    button.addEventListener('click', () => {
      state.authMode = button.dataset.authMode === 'register' ? 'register' : 'login'
      clearNotice()
      render()
    })
  })

  document.querySelector<HTMLFormElement>('#auth-form')?.addEventListener('submit', (event) => {
    event.preventDefault()
    void submitAuth(new FormData(event.currentTarget as HTMLFormElement))
  })

  document.querySelectorAll<HTMLButtonElement>('[data-scenario-id]').forEach((button) => {
    button.addEventListener('click', () => {
      const scenarioId = button.dataset.scenarioId
      if (scenarioId) void selectScenario(scenarioId)
    })
  })

  document.querySelectorAll<HTMLButtonElement>('[data-persona-id]').forEach((button) => {
    button.addEventListener('click', () => {
      state.selectedPersonaId = button.dataset.personaId ?? null
      clearNotice()
      render()
    })
  })

  document.querySelectorAll<HTMLButtonElement>('[data-review-session-id]').forEach((button) => {
    button.addEventListener('click', () => {
      const sessionId = button.dataset.reviewSessionId
      if (sessionId) void selectReviewSession(sessionId)
    })
  })

  document.querySelector<HTMLFormElement>('#message-form')?.addEventListener('submit', (event) => {
    event.preventDefault()
    void sendMessage(new FormData(event.currentTarget as HTMLFormElement))
  })

  document.querySelectorAll<HTMLButtonElement>('[data-action]').forEach((button) => {
    button.addEventListener('click', () => {
      void handleAction(button.dataset.action ?? '')
    })
  })

  document.querySelector('#messages')?.scrollTo({ top: 999999 })
}



async function handleAction(action: string) {
  if (state.busy) return
  switch (action) {
    case 'logout':
      logout()
      break
    case 'refresh-scenarios':
      await loadScenarios()
      break
    case 'start-session':
      await startSession()
      break
    case 'end-session':
      await endSession()
      break
    case 'back-to-scenarios':
      resetActiveSession()
      state.view = 'scenarios'
      clearNotice()
      render()
      break
    case 'open-review':
      await openReviewDashboard()
      break
    case 'open-student-lab':
      state.view = 'scenarios'
      clearNotice()
      render()
      if (state.scenarios.length === 0) await loadScenarios(false)
      break
    case 'refresh-review':
      await loadReviewSessions()
      break
    case 'export-review-json':
      exportReviewArtifact()
      break
    case 'export-review-csv':
      exportReviewCsv()
      break
  }
}

async function submitAuth(form: FormData) {
  const email = String(form.get('email') ?? '').trim()
  const password = String(form.get('password') ?? '')
  const name = String(form.get('name') ?? '').trim()

  if (!email || !password || (state.authMode === 'register' && !name)) {
    setNotice('error', 'Vui lòng nhập đầy đủ thông tin.')
    return
  }

  await withBusy(async () => {
    if (state.authMode === 'register') {
      await api.request('/api/Auth/register', {
        method: 'POST',
        body: { name, email, password },
        auth: false,
      })
      state.authMode = 'login'
      setNotice('success', 'Tạo tài khoản thành công. Bạn có thể đăng nhập.')
      return
    }

    const result = await api.request<{ token: string }>('/api/Auth/login', {
      method: 'POST',
      body: { email, password },
      auth: false,
    })
    state.token = result.token
    state.user = readUserFromToken(result.token)
    localStorage.setItem(TOKEN_KEY, result.token)
    state.view = 'scenarios'
    setNotice('success', 'Đăng nhập thành công.')
    await loadScenarios(false)
  })
}

async function loadScenarios(showLoading = true) {
  await withBusy(async () => {
    const scenarios = await api.request<ScenarioSummary[]>('/api/Scenarios')
    state.scenarios = scenarios
    if (state.selectedScenario && !scenarios.some((item) => item.id === state.selectedScenario?.id)) {
      state.selectedScenario = null
      state.selectedPersonaId = null
    }
    if (showLoading) setNotice('success', 'Đã tải danh sách scenario.')
  })
}

async function selectScenario(scenarioId: string) {
  if (state.busy) return
  await withBusy(async () => {
    const scenario = await api.request<ScenarioDetail>(`/api/Scenarios/${scenarioId}`)
    state.selectedScenario = scenario
    state.selectedPersonaId = scenario.personas[0]?.id ?? null
    state.evaluation = null
    clearNotice()
  })
}

async function startSession() {
  if (!state.selectedScenario || !state.selectedPersonaId) return

  await withBusy(async () => {
    const session = await api.request<SessionState>('/api/Sessions', {
      method: 'POST',
      body: {
        scenarioId: state.selectedScenario?.id,
        personaId: state.selectedPersonaId,
      },
    })
    state.session = session
    state.messages = []
    state.evaluation = null
    state.view = 'chat'
    setNotice('success', 'Session đã được tạo.')
  })
}

async function openReviewDashboard() {
  if (!isPrivilegedRole(state.user?.role)) {
    setNotice('error', 'Bạn cần quyền Lecturer hoặc Admin để mở review dashboard.')
    return
  }

  state.view = 'review'
  clearNotice()
  render()
  await loadReviewSessions(false)
}

async function loadReviewSessions(showLoading = true) {
  await withBusy(async () => {
    const sessions = await api.request<ReviewSessionSummary[]>('/api/Sessions/review')
    state.reviewSessions = sessions

    if (state.selectedReviewSessionId && !sessions.some((item) => item.id === state.selectedReviewSessionId)) {
      state.selectedReviewSessionId = null
      state.reviewDetail = null
    }

    if (!state.selectedReviewSessionId && sessions[0]) {
      state.selectedReviewSessionId = sessions[0].id
      state.reviewDetail = await api.request<ReviewSessionDetail>(`/api/Sessions/review/${sessions[0].id}`)
    }

    if (showLoading) setNotice('success', 'Đã tải lecturer review dashboard.')
  })
}

async function selectReviewSession(sessionId: string) {
  if (state.busy) return
  await withBusy(async () => {
    state.selectedReviewSessionId = sessionId
    state.reviewDetail = await api.request<ReviewSessionDetail>(`/api/Sessions/review/${sessionId}`)
    clearNotice()
  })
}

async function sendMessage(form: FormData) {
  if (!state.session) return
  const content = String(form.get('content') ?? '').trim()
  if (!content) return

  const previousMessages = state.messages
  const optimisticMessage: ChatMessage = {
    sender: 'Student',
    content,
    timestamp: new Date().toISOString(),
    pending: true,
  }

  state.messages = [...previousMessages, optimisticMessage]
  render()

  await withBusy(async () => {
    try {
      const response = await api.request<{ reply: string; questionType?: string | null }>(
        `/api/Sessions/${state.session?.id}/messages`,
        {
          method: 'POST',
          body: { content },
        },
      )
      state.messages = [
        ...previousMessages,
        {
          ...optimisticMessage,
          detectedQuestionType: response.questionType,
          pending: false,
        },
        {
          sender: 'Stakeholder',
          content: response.reply,
          detectedQuestionType: response.questionType,
          timestamp: new Date().toISOString(),
        },
      ]
      clearNotice()
    } catch (error) {
      state.messages = previousMessages
      throw error
    }
  })
}

async function endSession() {
  if (!state.session) return

  await withBusy(async () => {
    state.evaluation = await api.request<EvaluationResult>(`/api/Sessions/${state.session?.id}/end`, {
      method: 'POST',
    })
    setNotice('success', 'Đã kết thúc session và nhận kết quả đánh giá.')
  })
}

function exportReviewArtifact() {
  if (!state.reviewDetail) {
    setNotice('error', 'Chọn một session trước khi export artifact.')
    return
  }

  const artifact = {
    artifactType: 'ReqSimulatorSessionReview',
    exportedAt: new Date().toISOString(),
    app: {
      name: 'ReqSimulator',
      source: 'lecturer-dashboard',
    },
    ...state.reviewDetail,
  }
  const json = JSON.stringify(artifact, null, 2)
  const blob = new Blob([json], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = buildReviewArtifactFileName(state.reviewDetail)
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
  setNotice('success', 'Đã export session artifact JSON.')
}

function exportReviewCsv() {
  if (!state.reviewDetail) {
    setNotice('error', 'Chọn một session trước khi export artifact.')
    return
  }

  const csv = buildReviewCsv(state.reviewDetail)
  const blob = new Blob([`\uFEFF${csv}`], { type: 'text/csv;charset=utf-8' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = buildReviewArtifactFileName(state.reviewDetail).replace(/\.json$/, '.csv')
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
  setNotice('success', 'Đã export session artifact CSV.')
}

async function withBusy(task: () => Promise<void>) {
  state.busy = true
  render()
  try {
    await task()
  } catch (error) {
    setNotice('error', getErrorMessage(error))
  } finally {
    state.busy = false
    render()
  }
}

function logout(renderAfter = true) {
  localStorage.removeItem(TOKEN_KEY)
  state.token = null
  state.user = null
  state.view = 'auth'
  state.selectedScenario = null
  state.selectedPersonaId = null
  state.scenarios = []
  resetReviewDashboard()
  resetActiveSession()
  setNotice('info', 'Bạn đã đăng xuất.', false)
  if (renderAfter) render()
}

function resetActiveSession() {
  state.session = null
  state.messages = []
  state.evaluation = null
}

function resetReviewDashboard() {
  state.reviewSessions = []
  state.selectedReviewSessionId = null
  state.reviewDetail = null
}

function buildReviewArtifactFileName(detail: ReviewSessionDetail) {
  const scenario = slugify(detail.session.scenario.title)
  const student = slugify(detail.session.student.email || detail.session.student.name)
  const sessionId = detail.session.id.slice(0, 8)
  return `reqsimulator-${scenario}-${student}-${sessionId}.json`
}

function buildReviewCsv(detail: ReviewSessionDetail) {
  const columns = [
    'recordType',
    'sessionId',
    'scenarioTitle',
    'studentEmail',
    'index',
    'sender',
    'content',
    'questionType',
    'timestamp',
    'hiddenId',
    'hiddenText',
    'extractedText',
    'matchType',
    'score',
    'reason',
    'confidenceScore',
    'category',
    'revealDifficulty',
    'gateOrder',
    'value',
  ]
  const base = {
    sessionId: detail.session.id,
    scenarioTitle: detail.session.scenario.title,
    studentEmail: detail.session.student.email,
  }
  const rows: Record<string, string | number | null | undefined>[] = [
    {
      recordType: 'summary',
      ...base,
      value: JSON.stringify({
        startedAt: detail.session.startedAt,
        endedAt: detail.session.endedAt,
        finalizationStatus: detail.session.finalizationStatus,
        coverageScore: detail.evaluation?.coverageScore ?? null,
        matchedCount: detail.evaluation?.matchedCount ?? 0,
        partialCount: detail.evaluation?.partialCount ?? 0,
        missedCount: detail.evaluation?.missedCount ?? 0,
      }),
    },
    ...detail.messages.map((message, index) => ({
      recordType: 'transcript',
      ...base,
      index: index + 1,
      sender: message.sender,
      content: message.content,
      questionType: message.detectedQuestionType,
      timestamp: message.timestamp,
    })),
    ...(detail.evaluation?.matches ?? []).map((match, index) => ({
      recordType: 'match',
      ...base,
      index: index + 1,
      hiddenId: match.hiddenId,
      hiddenText: match.hiddenText,
      extractedText: match.extractedText,
      matchType: match.matchType,
      score: match.score,
      reason: match.reason,
    })),
    ...detail.extractedRequirements.map((requirement, index) => ({
      recordType: 'extracted',
      ...base,
      index: index + 1,
      extractedText: requirement.requirementText,
      confidenceScore: requirement.confidenceScore,
      timestamp: requirement.extractedAt,
    })),
    ...detail.hiddenRequirements.map((requirement, index) => ({
      recordType: 'hidden',
      ...base,
      index: index + 1,
      hiddenId: requirement.id,
      hiddenText: requirement.requirementText,
      category: requirement.category,
      revealDifficulty: requirement.revealDifficulty,
      gateOrder: requirement.gateOrder,
      reason: requirement.revealCondition,
    })),
  ]

  return [
    columns.join(','),
    ...rows.map((row) => columns.map((column) => csvCell(row[column])).join(',')),
  ].join('\r\n')
}

function csvCell(value: string | number | null | undefined) {
  if (value === null || value === undefined) return ''
  return `"${String(value).replaceAll('"', '""')}"`
}

function slugify(value: string) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80) || 'artifact'
}

function setNotice(type: Notice['type'], text: string, shouldRender = true) {
  state.notice = { type, text }
  if (shouldRender) render()
}

function clearNotice() {
  state.notice = null
}

function initAnimations() {
  const elements = document.querySelectorAll('[data-animate="fade-up"]')
  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        entry.target.classList.add('is-visible')
        observer.unobserve(entry.target)
      }
    })
  }, { threshold: 0.05 })
  elements.forEach((el) => observer.observe(el))
}

