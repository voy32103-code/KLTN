import './style.css'
import { createApiClient } from './api'
import { API_BASE_URL, EXPIRED_SESSION_NOTICE, TOKEN_KEY } from './constants'
import { buildLecturerOverridePayload } from './contracts'
import type {
  AdminOverview,
  AdminState,
  AdminUserItem,
  AppState,
  ChatMessage,
  CoverageDistributionBin,
  EvaluationResult,
  GradingReviewReport,
  MatchTypeBreakdownData,
  Notice,
  ReviewSessionDetail,
  ReviewSessionSummary,
  ScenarioDetail,
  ScenarioLanguage,
  ScenarioDraft,
  IngestionJob,
  PersonaTemplate,
  ScenarioPreviewResponse,
  ScenarioStatItem,
  ScenarioSummary,
  SessionState,
  SessionsOverTimeData,
  StudentSessionDetail,
  StudentSessionSummary,
  TopStudentItem,
} from './types'
import { getErrorMessage, isPrivilegedRole, isTokenExpired, readUserFromToken } from './utils'
import { renderApp } from './views'
import {
  destroyAllAdminCharts,
  renderCoverageDistributionChart,
  renderMatchTypeBreakdownChart,
  renderScenarioStatsChart,
  renderSessionsOverTimeChart
} from './admin-charts'

const root = document.querySelector<HTMLDivElement>('#app')
if (!root) throw new Error('Missing #app root')
const app: HTMLDivElement = root

const SCENARIO_LANGUAGE_KEY = 'req_simulator_scenario_language'
const TUTORIAL_KEY_PREFIX = 'req_simulator_tutorial_seen_v1_'
const storedToken = localStorage.getItem(TOKEN_KEY)
const hasExpiredStoredToken = Boolean(storedToken && isTokenExpired(storedToken))
const initialToken = hasExpiredStoredToken ? null : storedToken
const initialScenarioLanguage: ScenarioLanguage =
  localStorage.getItem(SCENARIO_LANGUAGE_KEY) === 'en' ? 'en' : 'vi'

const state: AppState = {
  token: initialToken,
  user: null,
  authMode: 'login',
  view: 'auth',
  scenarios: [],
  selectedScenario: null,
  selectedPersonaId: null,
  session: null,
  selectedModel: 'automatic',
  scenarioLanguage: initialScenarioLanguage,
  messages: [],
  evaluation: null,
  reviewSessions: [],
  selectedReviewSessionId: null,
  reviewDetail: null,
  studentHistory: [],
  selectedStudentSessionId: null,
  studentHistoryDetail: null,
  adminState: null,
  busy: false,
  notice: null,
  tutorialOpen: false,
  tutorialStep: 0,
}

let modalReturnFocus: HTMLElement | null = null

const api = createApiClient({
  baseUrl: API_BASE_URL,
  getToken: () => state.token,
  onUnauthorized: () => logout(false),
})

state.user = state.token ? readUserFromToken(state.token) : null
state.view = state.token
  ? (state.user?.role === 'Admin' ? 'admin' : state.user?.role === 'Lecturer' ? 'review' : 'scenarios')
  : 'auth'

if (hasExpiredStoredToken) {
  localStorage.removeItem(TOKEN_KEY)
  state.notice = { type: 'info', text: EXPIRED_SESSION_NOTICE }
}

render()
if (state.token) {
  if (state.user?.role === 'Admin') {
    void openAdminDashboard()
  } else if (state.user?.role === 'Lecturer') {
    void openReviewDashboard()
  } else {
    void loadScenarios()
  }
}

function render() {
  destroyAllAdminCharts()
  app.innerHTML = renderApp(state)
  bindEvents()

  // Sync body view class
  document.body.className = ''
  document.body.classList.add(`view-${state.view}`)
  initAnimations()

  // Trigger Admin Charts rendering if in admin overview view
  if (state.view === 'admin' && state.adminState?.activeTab === 'overview') {
    requestAnimationFrame(() => {
      renderAdminCharts()
    })
  }

  // Auto-scroll chat messages container to bottom when in chat view
  if (state.view === 'chat') {
    requestAnimationFrame(() => {
      const messagesContainer = document.querySelector<HTMLDivElement>('#messages')
      if (messagesContainer) {
        messagesContainer.scrollTop = messagesContainer.scrollHeight
      }
    })
  }

  if (state.tutorialOpen) {
    requestAnimationFrame(() => document.querySelector<HTMLElement>('#tutorial-modal')?.focus())
  }
}

function renderAdminCharts() {
  const admin = state.adminState
  if (!admin) return

  const covCanvas = document.querySelector<HTMLCanvasElement>('#chart-coverage-dist')
  if (covCanvas && admin.coverageDistribution) {
    renderCoverageDistributionChart(covCanvas, admin.coverageDistribution)
  }

  const timeCanvas = document.querySelector<HTMLCanvasElement>('#chart-sessions-time')
  if (timeCanvas && admin.sessionsOverTime) {
    renderSessionsOverTimeChart(timeCanvas, admin.sessionsOverTime)
  }

  const scenCanvas = document.querySelector<HTMLCanvasElement>('#chart-scenario-stats')
  if (scenCanvas && admin.scenarioStats) {
    renderScenarioStatsChart(scenCanvas, admin.scenarioStats)
  }

  const matchCanvas = document.querySelector<HTMLCanvasElement>('#chart-match-breakdown')
  if (matchCanvas && admin.matchTypeBreakdown) {
    renderMatchTypeBreakdownChart(matchCanvas, admin.matchTypeBreakdown)
  }
}

function bindEvents() {
  const legacyMediaInput = document.querySelector<HTMLInputElement>('#admin-video-path-input')
  if (legacyMediaInput) {
    legacyMediaInput.type = 'file'
    legacyMediaInput.accept = 'audio/mpeg,audio/wav,audio/x-wav,audio/mp4,audio/aac,audio/ogg,audio/webm,video/mp4,video/webm,video/quicktime'
    legacyMediaInput.removeAttribute('placeholder')
    const label = legacyMediaInput.closest('.form-group')?.querySelector('label')
    if (label) label.textContent = 'Tệp video/audio cuộc họp (tối đa 250 MB):'
  }
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
  document.querySelector<HTMLButtonElement>('#admin-add-requirement')?.addEventListener('click', () => {
    if (!state.adminState || state.busy) return
    try {
      const draft = readScenarioDraftForm()
      draft.requirements.push(createEmptyRequirement(draft.requirements.length + 1))
      state.adminState.scenarioDraft = draft
      clearNotice()
      render()
    } catch (error) {
      setNotice('error', getErrorMessage(error))
    }
  })

  document.querySelectorAll<HTMLButtonElement>('[data-draft-remove-index]').forEach((button) => {
    button.addEventListener('click', () => {
      if (!state.adminState || state.busy) return
      try {
        const draft = readScenarioDraftForm()
        if (draft.requirements.length <= 1) {
          throw new Error('Scenario phải có ít nhất một yêu cầu.')
        }
        const index = Number(button.dataset.draftRemoveIndex)
        if (!Number.isInteger(index) || index < 0 || index >= draft.requirements.length) return
        draft.requirements.splice(index, 1)
        state.adminState.scenarioDraft = draft
        clearNotice()
        render()
      } catch (error) {
        setNotice('error', getErrorMessage(error))
      }
    })
  })

  document.querySelectorAll<HTMLButtonElement>('[data-action]').forEach((button) => {
    button.addEventListener('click', (event) => {
      // Ngăn chặn sự kiện click lan truyền lên document khi click vào dropdown-trigger
      if (button.dataset.action === 'toggle-model-dropdown') {
        event.stopPropagation()
      }
      const action = button.dataset.action ?? ''
      const tab = button.dataset.tab ?? ''
      const userId = button.dataset.userId ?? ''
      const model = button.dataset.model ?? ''
      const jobId = button.dataset.jobId ?? ''
      const language = button.dataset.language ?? ''
      void handleAction(action, { tab, userId, model, jobId, language })
    })
  })

  document.querySelector('#end-session-modal-overlay')?.addEventListener('click', (event) => {
    if (event.target === event.currentTarget) {
      closeEndSessionModal()
    }
  })

  document.querySelectorAll<HTMLButtonElement>('[data-student-session-id]').forEach((button) => {
    button.addEventListener('click', () => {
      const sessionId = button.dataset.studentSessionId
      if (sessionId) void selectStudentHistorySession(sessionId)
    })
  })

  document.querySelector('#tutorial-modal-overlay')?.addEventListener('click', (event) => {
    if (event.target === event.currentTarget) {
      closeTutorial()
    }
  })

  document.querySelector('#messages')?.scrollTo({ top: 999999 })

  // Đóng dropdown khi bấm ra ngoài vùng dropdown container
  if (!(window as any).hasAccessibilityKeyboardListener) {
    (window as any).hasAccessibilityKeyboardListener = true
    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') {
        if (state.tutorialOpen) {
          event.preventDefault()
          closeTutorial()
          return
        }
        if (state.confirmEndSession) {
          event.preventDefault()
          closeEndSessionModal()
          return
        }
      }

      if (event.key !== 'Tab' || !state.confirmEndSession) return
      const dialog = document.querySelector<HTMLElement>('#end-session-modal')
      if (!dialog) return
      const focusable = Array.from(dialog.querySelectorAll<HTMLElement>('button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'))
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    })
  }
}

function closeEndSessionModal() {
  state.confirmEndSession = false
  render()
  requestAnimationFrame(() => modalReturnFocus?.focus())
}

function closeTutorial() {
  const role = state.user?.role ?? 'Student'
  localStorage.setItem(`${TUTORIAL_KEY_PREFIX}${role}`, '1')
  state.tutorialOpen = false
  state.tutorialStep = 0
  render()
}

async function handleAction(action: string, options: { tab?: string; userId?: string; model?: string; jobId?: string; language?: string } = {}) {
  if (state.busy) return
  switch (action) {
    case 'open-tutorial':
      state.tutorialOpen = true
      state.tutorialStep = 0
      render()
      break
    case 'tutorial-next':
      state.tutorialStep = Math.min((state.tutorialStep ?? 0) + 1, 3)
      render()
      break
    case 'tutorial-prev':
      state.tutorialStep = Math.max((state.tutorialStep ?? 0) - 1, 0)
      render()
      break
    case 'tutorial-close':
      closeTutorial()
      break
    case 'logout':
      logout()
      break
    case 'refresh-scenarios':
      await loadScenarios()
      break
    case 'set-scenario-language':
      if (options.language === 'vi' || options.language === 'en') {
        await switchScenarioLanguage(options.language)
      }
      break
    case 'start-session':
      await startSession()
      break
    case 'open-end-session-modal':
      modalReturnFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null
      state.confirmEndSession = true
      render()
      requestAnimationFrame(() => document.querySelector<HTMLElement>('#end-session-modal')?.focus())
      break
    case 'cancel-end-session':
      closeEndSessionModal()
      break
    case 'confirm-end-session':
    case 'end-session':
      state.confirmEndSession = false
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
    case 'open-student-history':
      await openStudentHistory()
      break
    case 'refresh-student-history':
      await loadStudentHistory()
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
    case 'open-admin':
      await openAdminDashboard()
      break
    case 'set-admin-tab':
      if (state.adminState && (options.tab === 'overview' || options.tab === 'users' || options.tab === 'scenarios')) {
        state.adminState.activeTab = options.tab as any
        render()
      }
      break
    case 'admin-crawl':
      await runQueuedAdminCrawl()
      break
    case 'admin-video':
      await runQueuedAdminVideo()
      break
    case 'refresh-ingestion-history':
      await refreshIngestionHistory(true)
      break
    case 'review-ingestion-job':
      if (options.jobId) await openIngestionJob(options.jobId)
      break
    case 'admin-publish-scenario':
      await publishAdminScenario()
      break
    case 'admin-edit-published-scenario':
      await openPublishedScenarioDraft()
      break
    case 'admin-cancel-preview':
      if (state.adminState) {
        state.adminState.scenarioDraft = null
        state.adminState.scenarioDraftSource = null
        clearNotice()
        render()
      }
      break
    case 'submit-override':
      await submitLecturerOverride()
      break
    case 'submit-feedback-survey':
      await submitFeedbackSurvey()
      break
    case 'open-create-user-modal':
      if (state.adminState) {
        state.adminState.isCreatingUser = true
        state.adminState.editingUser = null
        render()
      }
      break
    case 'cancel-user-form':
      if (state.adminState) {
        state.adminState.isCreatingUser = false
        state.adminState.editingUser = null
        render()
      }
      break
    case 'submit-create-user':
      await submitCreateUser()
      break
    case 'edit-user':
      if (options.userId && state.adminState) {
        const user = state.adminState.users.find(u => u.id === options.userId)
        if (user) {
          state.adminState.editingUser = user
          state.adminState.isCreatingUser = false
          render()
        }
      }
      break
    case 'submit-edit-user':
      await submitEditUser()
      break
    case 'delete-user':
      if (options.userId) {
        await deleteUser(options.userId)
      }
      break
    case 'filter-users':
      await filterAdminUsers()
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
    const tutorialRole = state.user?.role ?? 'Student'
    state.tutorialOpen = !localStorage.getItem(`${TUTORIAL_KEY_PREFIX}${tutorialRole}`)
    state.tutorialStep = 0
    setNotice('success', 'Đăng nhập thành công.')
    if (state.user?.role === 'Admin') {
      await openAdminDashboard()
    } else if (state.user?.role === 'Lecturer') {
      await openReviewDashboard()
    } else {
      state.view = 'scenarios'
      await loadScenarios(false)
    }
  })
}

async function loadScenarios(showLoading = true) {
  await withBusy(async () => {
    const scenarios = await api.request<ScenarioSummary[]>(`/api/Scenarios?lang=${state.scenarioLanguage}`)
    state.scenarios = scenarios
    if (state.selectedScenario && !scenarios.some((item) => item.id === state.selectedScenario?.id)) {
      state.selectedScenario = null
      state.selectedPersonaId = null
    }
    if (showLoading) setNotice('success', 'Đã tải danh sách kịch bản.')
  })
}

async function selectScenario(scenarioId: string) {
  if (state.busy) return
  await withBusy(async () => {
    const scenario = await api.request<ScenarioDetail>(
      `/api/Scenarios/${scenarioId}?lang=${state.scenarioLanguage}`
    )
    state.selectedScenario = scenario
    state.selectedPersonaId = scenario.personas[0]?.id ?? null
    state.evaluation = null
    clearNotice()
  })
}

async function switchScenarioLanguage(language: ScenarioLanguage) {
  if (language === state.scenarioLanguage) return
  const selectedScenarioId = state.selectedScenario?.id
  state.scenarioLanguage = language
  localStorage.setItem(SCENARIO_LANGUAGE_KEY, language)
  await loadScenarios(false)
  if (selectedScenarioId) {
    await selectScenario(selectedScenarioId)
  }
  setNotice(
    'success',
    language === 'vi'
      ? 'Đã chuyển nội dung kịch bản sang tiếng Việt.'
      : 'Scenario content switched to English.'
  )
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
    setNotice('success', 'Phiên phỏng vấn đã được tạo.')
  })
}

async function openReviewDashboard() {
  if (!isPrivilegedRole(state.user?.role)) {
    setNotice('error', 'Bạn cần quyền Giảng viên hoặc Quản trị viên để mở bảng điều khiển.')
    return
  }

  state.view = 'review'
  clearNotice()
  render()
  await loadReviewSessions(false)
}

async function openStudentHistory() {
  if (!state.token || state.user?.role !== 'Student') {
    setNotice('error', 'Lịch sử phỏng vấn chỉ dành cho tài khoản sinh viên.')
    return
  }
  state.view = 'history'
  state.selectedStudentSessionId = null
  state.studentHistoryDetail = null
  clearNotice()
  render()
  await loadStudentHistory(false)
}

async function loadStudentHistory(showLoading = true) {
  await withBusy(async () => {
    const sessions = await api.request<StudentSessionSummary[]>('/api/Sessions/mine')
    state.studentHistory = sessions
    if (state.selectedStudentSessionId && !sessions.some(item => item.id === state.selectedStudentSessionId)) {
      state.selectedStudentSessionId = null
      state.studentHistoryDetail = null
    }
    if (!state.selectedStudentSessionId && sessions[0]) {
      state.selectedStudentSessionId = sessions[0].id
      state.studentHistoryDetail = await api.request<StudentSessionDetail>(`/api/Sessions/mine/${sessions[0].id}`)
    }
    if (showLoading) setNotice('success', 'Đã tải lịch sử phỏng vấn.')
  })
}

async function selectStudentHistorySession(sessionId: string) {
  if (state.busy) return
  await withBusy(async () => {
    state.selectedStudentSessionId = sessionId
    state.studentHistoryDetail = await api.request<StudentSessionDetail>(`/api/Sessions/mine/${sessionId}`)
    clearNotice()
  })
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

    if (showLoading) setNotice('success', 'Đã tải bảng điều khiển giảng viên.')
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
      const response = await api.request<{
        reply: string
        questionType?: string | null
        topic?: string | null
        questionQuality?: ChatMessage['questionQuality']
      }>(
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
          detectedTopic: response.topic,
          questionQuality: response.questionQuality,
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
    setNotice('success', 'Đã kết thúc phiên phỏng vấn và nhận kết quả đánh giá.')
  })
}

async function submitFeedbackSurvey() {
  if (!state.session) return
  const form = document.querySelector<HTMLFormElement>('#feedback-survey-form')
  if (!form) return
  const data = new FormData(form)
  await withBusy(async () => {
    await api.request(`/api/Sessions/${state.session?.id}/feedback-survey`, {
      method: 'POST',
      body: {
        helpfulness: Number(data.get('helpfulness')),
        actionability: Number(data.get('actionability')),
        noAnswerLeak: Number(data.get('noAnswerLeak')),
        comment: String(data.get('comment') ?? '').trim() || null,
      },
    })
    setNotice('success', 'Đã lưu đánh giá feedback cho thử nghiệm A/B.')
  })
}

async function submitLecturerOverride() {
  if (!state.reviewDetail) return

  const form = document.querySelector<HTMLFormElement>('#override-form')
  if (!form) return

  const matchSelects = form.querySelectorAll<HTMLSelectElement>('.override-type-select')
  const overrides: { matchId: string; matchType: string }[] = []
  matchSelects.forEach((select) => {
    const matchId = select.dataset.matchId
    if (matchId && matchId !== '00000000-0000-0000-0000-000000000000') {
      overrides.push({ matchId, matchType: select.value })
    }
  })

  const commentInput = document.querySelector<HTMLTextAreaElement>('#override-comment')
  const comment = commentInput?.value?.trim() ?? ''

  await withBusy(async () => {
    const updatedEval = await api.request<EvaluationResult>(`/api/Sessions/review/${state.reviewDetail?.session.id}/override`, {
      method: 'PUT',
      body: buildLecturerOverridePayload(overrides, comment),
    })

    if (state.reviewDetail) {
      state.reviewDetail.evaluation = updatedEval
    }
    // Update summary item score in list
    const summaryItem = state.reviewSessions.find(s => s.id === state.reviewDetail?.session.id)
    if (summaryItem && summaryItem.evaluation) {
      summaryItem.evaluation.coverageScore = updatedEval.overriddenCoverageScore ?? updatedEval.coverageScore
    }
    setNotice('success', 'Đã cập nhật đánh giá lại của Giảng viên và tính lại Coverage Score.')
  })
}

async function openAdminDashboard() {
  await withBusy(async () => {
    state.view = 'admin'
    state.adminState = {
      activeTab: 'overview',
      overview: null,
      coverageDistribution: null,
      sessionsOverTime: null,
      scenarioStats: [],
      topStudents: [],
      matchTypeBreakdown: null,
      gradingReview: null,
      users: [],
      userSearch: '',
      userRoleFilter: '',
      editingUser: null,
      isCreatingUser: false,
      scenarioDraft: null,
      scenarioDraftSource: null,
      ingestionJob: null,
      ingestionJobs: [],
      personaTemplates: [],
      feedbackExperiment: null,
    }
    clearNotice()

    // Fetch Overview metrics & charts in parallel
    const [ov, distResp, time, scen, top, breakdown, gradingReview, userList, feedbackExperiment, ingestionJobs, personaTemplates] = await Promise.all([
      api.request<AdminOverview>('/api/Admin/stats/overview'),
      api.request<{ bins: CoverageDistributionBin[] } | CoverageDistributionBin[]>('/api/Admin/stats/coverage-distribution'),
      api.request<SessionsOverTimeData>('/api/Admin/stats/sessions-over-time'),
      api.request<ScenarioStatItem[]>('/api/Admin/stats/by-scenario'),
      api.request<TopStudentItem[]>('/api/Admin/stats/top-students'),
      api.request<MatchTypeBreakdownData>('/api/Admin/stats/match-type-breakdown'),
      api.request<GradingReviewReport>('/api/Admin/stats/grading-review'),
      api.request<AdminUserItem[]>('/api/Admin/users'),
      api.request<NonNullable<AdminState['feedbackExperiment']>>('/api/Admin/stats/feedback-experiment'),
      api.request<IngestionJob[]>('/api/admin-ingestion/jobs').catch(() => []),
      api.request<PersonaTemplate[]>('/api/admin-persona-templates').catch(() => []),
    ])

    state.adminState.overview = ov
    state.adminState.coverageDistribution = Array.isArray(distResp) ? distResp : distResp?.bins ?? []
    state.adminState.sessionsOverTime = time
    state.adminState.scenarioStats = Array.isArray(scen) ? scen : []
    state.adminState.topStudents = Array.isArray(top) ? top : []
    state.adminState.feedbackExperiment = feedbackExperiment
    state.adminState.matchTypeBreakdown = breakdown
    state.adminState.gradingReview = gradingReview
    state.adminState.users = Array.isArray(userList) ? userList : []
    state.adminState.ingestionJobs = ingestionJobs
    state.adminState.personaTemplates = personaTemplates.filter(template => template.isActive)
  })
}

async function filterAdminUsers() {
  if (!state.adminState) return
  const searchInput = document.querySelector<HTMLInputElement>('#user-search-input')
  const roleSelect = document.querySelector<HTMLSelectElement>('#user-role-filter')

  const search = searchInput?.value?.trim() ?? ''
  const role = roleSelect?.value ?? ''

  state.adminState.userSearch = search
  state.adminState.userRoleFilter = role

  await withBusy(async () => {
    const query = new URLSearchParams()
    if (search) query.set('search', search)
    if (role) query.set('role', role)

    const userList = await api.request<AdminUserItem[]>(`/api/Admin/users?${query.toString()}`)
    if (state.adminState) {
      state.adminState.users = userList
    }
  })
}

async function submitCreateUser() {
  const form = document.querySelector<HTMLFormElement>('#create-user-form')
  if (!form) return
  const formData = new FormData(form)
  const name = String(formData.get('name') ?? '').trim()
  const email = String(formData.get('email') ?? '').trim()
  const password = String(formData.get('password') ?? '')
  const role = String(formData.get('role') ?? 'Student')

  if (!name || !email || !password) {
    setNotice('error', 'Vui lòng điền đầy đủ các trường bắt buộc.')
    return
  }

  await withBusy(async () => {
    await api.request('/api/Admin/users', {
      method: 'POST',
      body: { name, email, password, role }
    })
    setNotice('success', `Đã tạo người dùng mới: ${email}`)
    if (state.adminState) state.adminState.isCreatingUser = false
    await filterAdminUsers()
  })
}

async function submitEditUser() {
  if (!state.adminState?.editingUser) return
  const form = document.querySelector<HTMLFormElement>('#edit-user-form')
  if (!form) return
  const formData = new FormData(form)
  const name = String(formData.get('name') ?? '').trim()
  const email = String(formData.get('email') ?? '').trim()
  const newPassword = String(formData.get('newPassword') ?? '')
  const role = String(formData.get('role') ?? 'Student')

  await withBusy(async () => {
    await api.request(`/api/Admin/users/${state.adminState?.editingUser?.id}`, {
      method: 'PUT',
      body: {
        name,
        email,
        newPassword: newPassword || null,
        role
      }
    })
    setNotice('success', `Đã cập nhật thông tin người dùng: ${email}`)
    if (state.adminState) state.adminState.editingUser = null
    await filterAdminUsers()
  })
}

async function deleteUser(userId: string) {
  if (!confirm('Bạn có chắc chắn muốn xóa người dùng này?')) return

  await withBusy(async () => {
    await api.request(`/api/Admin/users/${userId}`, {
      method: 'DELETE'
    })
    setNotice('success', 'Đã xóa người dùng thành công.')
    await filterAdminUsers()
  })
}

function exportReviewArtifact() {
  if (!state.reviewDetail) {
    setNotice('error', 'Chọn một phiên phỏng vấn trước khi xuất dữ liệu.')
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
  setNotice('success', 'Đã xuất dữ liệu phiên phỏng vấn dưới dạng JSON.')
}

function exportReviewCsv() {
  if (!state.reviewDetail) {
    setNotice('error', 'Chọn một phiên phỏng vấn trước khi xuất dữ liệu.')
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
  setNotice('success', 'Đã xuất dữ liệu phiên phỏng vấn dưới dạng CSV.')
}

function splitDraftList(value: string): string[] {
  return value
    .split(',')
    .map(item => item.trim())
    .filter(Boolean)
}

function parseDraftMap<T extends string | number>(
  raw: string,
  label: string,
  itemType: 'string' | 'number'
): Record<string, T[]> {
  let parsed: unknown
  try {
    parsed = JSON.parse(raw.trim() || '{}')
  } catch {
    throw new Error(`${label} phải là JSON hợp lệ.`)
  }

  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error(`${label} phải là một JSON object.`)
  }

  const result: Record<string, T[]> = {}
  for (const [key, value] of Object.entries(parsed)) {
    if (!Array.isArray(value)) {
      throw new Error(`${label}.${key} phải là một mảng.`)
    }
    if (itemType === 'string' && value.some(item => typeof item !== 'string')) {
      throw new Error(`${label}.${key} chỉ được chứa chuỗi.`)
    }
    if (itemType === 'number' && value.some(item => typeof item !== 'number' || !Number.isInteger(item))) {
      throw new Error(`${label}.${key} chỉ được chứa số nguyên.`)
    }
    result[key] = value as T[]
  }
  return result
}

function parseNormalizationGlossary(raw: string): Record<string, Record<string, string>> {
  let parsed: unknown
  try {
    parsed = JSON.parse(raw.trim() || '{}')
  } catch {
    throw new Error('Glossary chuẩn hóa phải là JSON hợp lệ.')
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('Glossary chuẩn hóa phải là một JSON object.')
  }
  const allowedComponents = new Set(['actor', 'action', 'object', 'condition'])
  const result: Record<string, Record<string, string>> = {}
  for (const [component, aliases] of Object.entries(parsed)) {
    if (!allowedComponents.has(component)) {
      throw new Error(`Glossary chỉ hỗ trợ actor, action, object và condition (nhận được ${component}).`)
    }
    if (!aliases || typeof aliases !== 'object' || Array.isArray(aliases)) {
      throw new Error(`Glossary.${component} phải là object dạng "từ đồng nghĩa": "thuật ngữ chuẩn".`)
    }
    const normalizedAliases: Record<string, string> = {}
    for (const [source, target] of Object.entries(aliases)) {
      if (typeof target !== 'string' || !source.trim() || !target.trim()) {
        throw new Error(`Glossary.${component} chỉ nhận các cặp chuỗi không rỗng.`)
      }
      if (source.length > 160 || target.length > 240) {
        throw new Error(`Glossary.${component} có thuật ngữ vượt quá giới hạn độ dài.`)
      }
      normalizedAliases[source.trim()] = target.trim()
    }
    result[component] = normalizedAliases
  }
  return result
}

function getDraftField(root: ParentNode, field: string): string {
  const element = root.querySelector<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>(
    `[data-draft-field="${field}"]`
  )
  if (!element) throw new Error(`Không tìm thấy trường ${field} trong bản preview.`)
  return element.value.trim()
}

function createEmptyRequirement(index: number): ScenarioDraft['requirements'][number] {
  return {
    id: `R${index}`,
    text: '',
    gate: 0,
    keywords: [],
    question_types: ['OpenEnded'],
    reveal_condition: '',
    reveal_difficulty: 'Medium',
    requires: [],
    actor: '',
    action: '',
    object: '',
    condition: '',
    type: 'FR',
    priority: 'medium',
  }
}

function readScenarioDraftForm(): ScenarioDraft {
  if (!state.adminState?.scenarioDraft) {
    throw new Error('Không có bản scenario preview để chỉnh sửa.')
  }

  const form = document.querySelector<HTMLFormElement>('#admin-scenario-preview-form')
  if (!form) return state.adminState.scenarioDraft

  const requirements = Array.from(form.querySelectorAll<HTMLElement>('[data-requirement-row]')).map((row) => {
    const gate = Number(getDraftField(row, 'gate'))
    const difficulty = getDraftField(row, 'reveal_difficulty') as 'Easy' | 'Medium' | 'Hard'
    return {
      id: getDraftField(row, 'id'),
      text: getDraftField(row, 'text'),
      gate,
      keywords: splitDraftList(getDraftField(row, 'keywords')),
      question_types: splitDraftList(getDraftField(row, 'question_types')),
      reveal_condition: getDraftField(row, 'reveal_condition'),
      reveal_difficulty: difficulty,
      requires: splitDraftList(getDraftField(row, 'requires')),
      actor: getDraftField(row, 'actor'),
      action: getDraftField(row, 'action'),
      object: getDraftField(row, 'object'),
      condition: getDraftField(row, 'condition'),
      type: getDraftField(row, 'type') as 'FR' | 'NFR' | 'BR',
      priority: getDraftField(row, 'priority') as 'high' | 'medium' | 'low',
    }
  })

  const personaTemplateKeys = Array.from(
    form.querySelectorAll<HTMLInputElement>('[data-persona-template-key]:checked')
  ).map(input => input.value)

  const draft: ScenarioDraft = {
    scenario_key: getDraftField(form, 'scenario_key'),
    scenario_title: getDraftField(form, 'scenario_title'),
    context: getDraftField(form, 'context'),
    general_keywords: splitDraftList(getDraftField(form, 'general_keywords')),
    gate_keyword_groups: parseDraftMap<string>(
      getDraftField(form, 'gate_keyword_groups'),
      'Nhóm từ khóa Gate',
      'string'
    ),
    question_type_gate_map: parseDraftMap<number>(
      getDraftField(form, 'question_type_gate_map'),
      'Ánh xạ loại câu hỏi',
      'number'
    ),
    max_new_reveals_per_turn: Number(getDraftField(form, 'max_new_reveals_per_turn')),
    requirements,
    source_urls: state.adminState.scenarioDraft.source_urls ?? [],
    persona_template_keys: personaTemplateKeys,
    normalization_glossary: parseNormalizationGlossary(getDraftField(form, 'normalization_glossary')),
    review_notes: getDraftField(form, 'review_notes') || null,
  }

  validateScenarioDraft(draft)
  return draft
}

function validateScenarioDraft(draft: ScenarioDraft) {
  if (!/^[a-z0-9]+(?:_[a-z0-9]+)*$/.test(draft.scenario_key) || draft.scenario_key.length > 100) {
    throw new Error('Mã scenario chỉ gồm chữ thường, số và dấu gạch dưới; tối đa 100 ký tự.')
  }
  if (!draft.scenario_title) throw new Error('Tên scenario không được để trống.')
  if (!draft.context) throw new Error('Bối cảnh scenario không được để trống.')
  if (!Number.isInteger(draft.max_new_reveals_per_turn) ||
      draft.max_new_reveals_per_turn < 1 ||
      draft.max_new_reveals_per_turn > 12) {
    throw new Error('Số yêu cầu tiết lộ mỗi lượt phải là số nguyên từ 1 đến 12.')
  }
  if (draft.requirements.length === 0) throw new Error('Scenario phải có ít nhất một yêu cầu.')

  if (draft.persona_template_keys && draft.persona_template_keys.length > 0 &&
      (draft.persona_template_keys.length < 2 || draft.persona_template_keys.length > 3)) {
    throw new Error('Hãy chọn từ 2 đến 3 persona template cho mỗi stakeholder.')
  }
  if ((draft.review_notes?.length ?? 0) > 1000) {
    throw new Error('Ghi chú review không được vượt quá 1.000 ký tự.')
  }

  const ids = new Set<string>()
  for (const [index, requirement] of draft.requirements.entries()) {
    if (!requirement.id) throw new Error(`Yêu cầu ${index + 1} chưa có mã.`)
    const normalizedId = requirement.id.toLowerCase()
    if (ids.has(normalizedId)) throw new Error(`Mã yêu cầu "${requirement.id}" bị trùng.`)
    ids.add(normalizedId)
    if (!requirement.text) throw new Error(`Nội dung yêu cầu ${requirement.id} không được để trống.`)
    if (!requirement.actor || !requirement.action || !requirement.object) {
      throw new Error(`Yêu cầu ${requirement.id} phải có đủ Actor, Action và Object.`)
    }
    if (!['FR', 'NFR', 'BR'].includes(requirement.type)) {
      throw new Error(`Type của yêu cầu ${requirement.id} không hợp lệ.`)
    }
    if (!['high', 'medium', 'low'].includes(requirement.priority)) {
      throw new Error(`Priority của yêu cầu ${requirement.id} không hợp lệ.`)
    }
    if (!Number.isInteger(requirement.gate) || requirement.gate < 0 || requirement.gate > 4) {
      throw new Error(`Gate của yêu cầu ${requirement.id} phải từ 0 đến 4.`)
    }
    if (!['Easy', 'Medium', 'Hard'].includes(requirement.reveal_difficulty)) {
      throw new Error(`Độ khó của yêu cầu ${requirement.id} không hợp lệ.`)
    }
  }

  for (const requirement of draft.requirements) {
    for (const dependency of requirement.requires) {
      if (!ids.has(dependency.toLowerCase())) {
        throw new Error(`Yêu cầu ${requirement.id} phụ thuộc mã không tồn tại: ${dependency}.`)
      }
      if (dependency.toLowerCase() === requirement.id.toLowerCase()) {
        throw new Error(`Yêu cầu ${requirement.id} không thể phụ thuộc chính nó.`)
      }
    }
  }
}

async function publishAdminScenario() {
  if (!state.adminState?.scenarioDraft) return

  let scenario: ScenarioDraft
  try {
    scenario = readScenarioDraftForm()
    state.adminState.scenarioDraft = scenario
  } catch (error) {
    setNotice('error', getErrorMessage(error))
    return
  }

  await withBusy(async () => {
    clearNotice()
    const result = await api.request<{
      message: string
      scenarioId: string
      scenarioKey: string
      version: number
      title: string
      requirementsCount: number
    }>('/api/AdminScenarios/publish', {
      method: 'POST',
      body: scenario,
    })
    if (!state.adminState) return
    state.adminState.scenarioDraft = null
    state.adminState.scenarioDraftSource = null
    setNotice(
      'success',
      `Đã publish "${result.title}" phiên bản ${result.version} với ${result.requirementsCount} yêu cầu.`
    )
  })
}

async function openPublishedScenarioDraft() {
  if (!state.adminState) return

  const select = document.querySelector<HTMLSelectElement>('#admin-published-scenario-select')
  const scenarioId = select?.value
  if (!scenarioId) {
    setNotice('error', 'Vui lòng chọn scenario đang hoạt động để chỉnh sửa.')
    return
  }

  const scenarioTitle = select?.selectedOptions[0]?.textContent?.trim() ?? 'Scenario đang hoạt động'
  await withBusy(async () => {
    clearNotice()
    const draft = await api.request<ScenarioDraft>(`/api/AdminScenarios/${scenarioId}/draft`)
    if (!state.adminState) return
    state.adminState.scenarioDraft = draft
    state.adminState.scenarioDraftSource = `${scenarioTitle} — bản nháp từ phiên bản đang publish`
    setNotice('success', 'Đã tải scenario thành bản nháp. Publish sẽ tạo phiên bản mới, không ghi đè lịch sử cũ.')
  })
}

async function runQueuedAdminCrawl() {
  const urlInput = document.querySelector('#admin-crawl-url-input') as HTMLTextAreaElement | null
  const modelSelect = document.querySelector('#admin-crawl-model-select') as HTMLSelectElement | null
  const urls = urlInput?.value.split(/\r?\n/).map(value => value.trim()).filter(Boolean) ?? []
  if (urls.length === 0) {
    setNotice('error', 'Vui lòng nhập ít nhất một URL công khai.')
    return
  }
  await withBusy(async () => {
    const created = await api.request<{ jobId: string }>('/api/admin-ingestion/crawl-jobs', {
      method: 'POST', body: { urls, selectedModel: modelSelect?.value || 'gemini-2.5-flash' },
    })
    await waitForIngestion(created.jobId, urls.join(', '))
  })
}

async function runQueuedAdminVideo() {
  const input = document.querySelector('#admin-video-path-input') as HTMLInputElement | null
  const modelSelect = document.querySelector('#admin-video-model-select') as HTMLSelectElement | null
  const file = input?.files?.[0]
  if (!file) {
    setNotice('error', 'Vui lòng chọn tệp video hoặc audio cuộc họp.')
    return
  }
  if (file.size > 250 * 1024 * 1024) {
    setNotice('error', 'Tệp vượt quá giới hạn 250 MB.')
    return
  }
  await withBusy(async () => {
    const contentType = file.type || 'audio/mpeg'
    const intent = await api.request<{ jobId: string; artifactId: string; uploadUrl: string }>('/api/admin-ingestion/upload-intents', {
      method: 'POST', body: { fileName: file.name, contentType, size: file.size, selectedModel: modelSelect?.value || 'gemini-2.5-flash' },
    })
    await uploadToPresignedUrl(intent.uploadUrl, file, contentType)
    await api.request(`/api/admin-ingestion/artifacts/${intent.artifactId}/complete`, { method: 'POST' })
    await waitForIngestion(intent.jobId, file.name)
  })
}

function uploadToPresignedUrl(uploadUrl: string, file: File, contentType: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest()
    request.open('PUT', uploadUrl)
    request.setRequestHeader('Content-Type', contentType)
    request.upload.onprogress = event => {
      if (event.lengthComputable) setNotice('info', `Đang tải tệp: ${Math.round((event.loaded / event.total) * 100)}%.`)
    }
    request.onerror = () => reject(new Error('Không thể tải tệp lên kho riêng.'))
    request.onload = () => request.status >= 200 && request.status < 300 ? resolve() : reject(new Error(`Tải tệp thất bại (${request.status}).`))
    request.send(file)
  })
}

async function waitForIngestion(jobId: string, source: string) {
  for (let attempt = 0; attempt < 120; attempt += 1) {
    const job = await api.request<IngestionJob>(`/api/admin-ingestion/jobs/${jobId}`)
    if (state.adminState) {
      state.adminState.ingestionJob = job
      const historyJob = { ...job, sourceLabel: source }
      state.adminState.ingestionJobs = [historyJob, ...state.adminState.ingestionJobs.filter(item => item.jobId !== jobId)]
    }
    if (job.status === 'Queued') {
      setNotice('info', 'Job queued — chạy GitHub Action.')
      return
    }
    if (job.status === 'AwaitingReview' && job.draft) {
      if (state.adminState) {
        state.adminState.scenarioDraft = job.draft
        state.adminState.scenarioDraftSource = source
      }
      setNotice('success', 'Bản nháp đã sẵn sàng. Hãy kiểm tra trước khi publish.')
      return
    }
    if (job.status === 'Failed') throw new Error(`Nạp tri thức thất bại (${job.errorCode ?? 'processing_failed'}).`)
    setNotice('info', 'Đang tạo bản nháp scenario…')
    await new Promise(resolve => window.setTimeout(resolve, 3000))
  }
  throw new Error('Job đang mất nhiều thời gian. Hãy mở lịch sử nạp tri thức trong Admin để kiểm tra lại.')
}

async function refreshIngestionHistory(showNotice = false) {
  if (!state.adminState) return
  await withBusy(async () => {
    const jobs = await api.request<IngestionJob[]>('/api/admin-ingestion/jobs')
    if (!state.adminState) return
    state.adminState.ingestionJobs = jobs
    if (showNotice) setNotice('success', 'Đã làm mới lịch sử nạp tri thức.')
  })
}

async function openIngestionJob(jobId: string) {
  await withBusy(async () => {
    const job = await api.request<IngestionJob>(`/api/admin-ingestion/jobs/${jobId}`)
    if (!state.adminState) return
    state.adminState.ingestionJob = job
    state.adminState.ingestionJobs = state.adminState.ingestionJobs.map(item => item.jobId === job.jobId ? { ...item, ...job } : item)
    if (job.status !== 'AwaitingReview' || !job.draft) {
      setNotice('info', `Job hiện ở trạng thái ${job.status}.`)
      return
    }
    state.adminState.scenarioDraft = job.draft
    state.adminState.scenarioDraftSource = job.sourceLabel ?? 'Ingestion job'
    setNotice('success', 'Đã mở bản nháp scenario để kiểm tra trước khi publish.')
  })
}

async function runAdminCrawl() {
  const urlInput = document.querySelector('#admin-crawl-url-input') as HTMLTextAreaElement | null
  const modelSelect = document.querySelector('#admin-crawl-model-select') as HTMLSelectElement | null
  if (!urlInput || !urlInput.value.trim()) {
    setNotice('error', 'Vui lòng nhập đường dẫn URL tài liệu!')
    return
  }
  const urls = urlInput.value.split(/\r?\n/).map(value => value.trim()).filter(Boolean)
  const url = urls[0]
  const selectedModel = modelSelect?.value || 'gemini-2.5-flash'

  await withBusy(async () => {
    clearNotice()
    const endpoint = urls.length > 1
      ? '/api/AdminScenarios/crawl/preview-multiple'
      : '/api/AdminScenarios/crawl/preview'
    const result = await api.request<ScenarioPreviewResponse>(endpoint, {
      method: 'POST',
      body: urls.length > 1 ? { urls, selectedModel } : { url, selectedModel }
    })
    if (!state.adminState) return
    state.adminState.scenarioDraft = result.scenario
    state.adminState.scenarioDraftSource = urls.join(', ')
    setNotice('success', 'Đã tạo bản preview. Hãy kiểm tra, chỉnh sửa và xác nhận publish.')
  })
}

// Kept temporarily for compatibility with older UI integrations while queued ingestion rolls out.
void runAdminCrawl

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
