import './style.css'
import { createApiClient } from './api'
import { API_BASE_URL, EXPIRED_SESSION_NOTICE, TOKEN_KEY } from './constants'
import type {
  AdminOverview,
  AdminUserItem,
  AppState,
  ChatMessage,
  CoverageDistributionBin,
  EvaluationResult,
  MatchTypeBreakdownData,
  Notice,
  ReviewSessionDetail,
  ReviewSessionSummary,
  ScenarioDetail,
  ScenarioStatItem,
  ScenarioSummary,
  SessionState,
  SessionsOverTimeData,
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

const MODEL_KEY = 'req_simulator_selected_model'
const storedToken = localStorage.getItem(TOKEN_KEY)
const hasExpiredStoredToken = Boolean(storedToken && isTokenExpired(storedToken))
const initialToken = hasExpiredStoredToken ? null : storedToken
const initialModel = localStorage.getItem(MODEL_KEY) || 'gemini-2.5-flash'

const state: AppState = {
  token: initialToken,
  user: null,
  authMode: 'login',
  view: 'auth',
  scenarios: [],
  selectedScenario: null,
  selectedPersonaId: null,
  session: null,
  selectedModel: initialModel,
  messages: [],
  evaluation: null,
  reviewSessions: [],
  selectedReviewSessionId: null,
  reviewDetail: null,
  adminState: null,
  busy: false,
  notice: null,
}

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
      const action = button.dataset.action ?? ''
      const tab = button.dataset.tab ?? ''
      const userId = button.dataset.userId ?? ''
      void handleAction(action, { tab, userId })
    })
  })

  document.querySelector('#messages')?.scrollTo({ top: 999999 })

  document.querySelector('#ai-model-select')?.addEventListener('change', (event) => {
    const select = event.currentTarget as HTMLSelectElement
    state.selectedModel = select.value
    localStorage.setItem(MODEL_KEY, select.value)
    render()
  })
}



async function handleAction(action: string, options: { tab?: string; userId?: string } = {}) {
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
    case 'open-admin':
      await openAdminDashboard()
      break
    case 'set-admin-tab':
      if (state.adminState && (options.tab === 'overview' || options.tab === 'users')) {
        state.adminState.activeTab = options.tab
        render()
      }
      break
    case 'submit-override':
      await submitLecturerOverride()
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
    const scenarios = await api.request<ScenarioSummary[]>('/api/Scenarios')
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
        selectedModel: state.selectedModel,
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
    setNotice('success', 'Đã kết thúc phiên phỏng vấn và nhận kết quả đánh giá.')
  })
}

async function submitLecturerOverride() {
  if (!state.reviewDetail) return

  const form = document.querySelector<HTMLFormElement>('#override-form')
  if (!form) return

  const matchSelects = form.querySelectorAll<HTMLSelectElement>('.override-type-select')
  const overrides: { matchId: string; overriddenType: string }[] = []
  matchSelects.forEach((select) => {
    const matchId = select.dataset.matchId
    if (matchId && matchId !== '00000000-0000-0000-0000-000000000000') {
      overrides.push({ matchId, overriddenType: select.value })
    }
  })

  const commentInput = document.querySelector<HTMLTextAreaElement>('#override-comment')
  const comment = commentInput?.value?.trim() ?? ''

  await withBusy(async () => {
    const updatedEval = await api.request<EvaluationResult>(`/api/Sessions/review/${state.reviewDetail?.session.id}/override`, {
      method: 'PUT',
      body: { overrides, comment },
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
      users: [],
      userSearch: '',
      userRoleFilter: '',
      editingUser: null,
      isCreatingUser: false,
    }
    clearNotice()

    // Fetch Overview metrics & charts in parallel
    const [ov, distResp, time, scen, top, breakdown, userList] = await Promise.all([
      api.request<AdminOverview>('/api/Admin/stats/overview'),
      api.request<{ bins: CoverageDistributionBin[] } | CoverageDistributionBin[]>('/api/Admin/stats/coverage-distribution'),
      api.request<SessionsOverTimeData>('/api/Admin/stats/sessions-over-time'),
      api.request<ScenarioStatItem[]>('/api/Admin/stats/by-scenario'),
      api.request<TopStudentItem[]>('/api/Admin/stats/top-students'),
      api.request<MatchTypeBreakdownData>('/api/Admin/stats/match-type-breakdown'),
      api.request<AdminUserItem[]>('/api/Admin/users'),
    ])

    state.adminState.overview = ov
    state.adminState.coverageDistribution = Array.isArray(distResp) ? distResp : distResp?.bins ?? []
    state.adminState.sessionsOverTime = time
    state.adminState.scenarioStats = Array.isArray(scen) ? scen : []
    state.adminState.topStudents = Array.isArray(top) ? top : []
    state.adminState.matchTypeBreakdown = breakdown
    state.adminState.users = Array.isArray(userList) ? userList : []
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

