import { i18n, t } from './i18n'
import { formatPersonaText } from './persona-display'
import type {
  AdminState,
  AdminUserItem,
  AppState,
  AppView,
  ChatMessage,
  EvaluationResult,
  Notice,
  Persona,
  ReviewExtractedRequirement,
  ReviewHiddenRequirement,
  ReviewSessionDetail,
  ReviewSessionSummary,
  RequirementMatchReport,
  ScenarioDetail,
  ScenarioSummary,
  ScoringPolicy,
} from './types'
import {
  escapeAttribute,
  escapeHtml,
  formatScore,
  formatScoreClass,
  formatThreshold,
  formatTime,
  isPrivilegedRole,
  shortId,
} from './utils'

function getFriendlyModelName(modelId?: string): string {
  if (!modelId) return 'Gemini 2.5 Flash'
  switch (modelId) {
    case 'gemini-2.5-flash': return 'Gemini 2.5 Flash'
    case 'gemini-2.5-pro': return 'Gemini 2.5 Pro'
    case 'gemini-2.5-flash-lite': return 'Gemini 2.5 Flash Lite'
    case 'gemini-3-flash-preview': return 'Gemini 3 Flash Preview'
    case 'gemini-3.1-flash-lite': return 'Gemini 3.1 Flash Lite'
    case 'gemini-3.5-flash': return 'Gemini 3.5 Flash'
    case 'gemini-3.5-flash-lite': return 'Gemini 3.5 Flash Lite'
    case 'gemini-3.6-flash': return 'Gemini 3.6 Flash'
    case 'gemini-3.7-flash': return 'Gemini 3.7 Flash'
    case 'llama-3.3-70b-versatile': return 'Llama 3.3 70B (Groq)'
    case 'llama-3.1-8b-instant': return 'Llama 3.1 8B (Groq)'
    case 'deepseek-chat': return 'DeepSeek Chat'
    case 'deepseek-v4pro': return 'DeepSeek v4 Pro'
    case 'deepseek-v4flash': return 'DeepSeek v4 Flash'
    case 'mimo-v2.5pro': return 'Mimo v2.5 Pro'
    case 'openrouter/meta-llama/llama-3.3-70b-instruct': return 'Llama 3.3 70B (OpenRouter)'
    case 'openrouter/deepseek/deepseek-chat': return 'DeepSeek Chat (OpenRouter)'
    case 'openrouter/google/gemini-2.5-flash': return 'Gemini 2.5 Flash (OpenRouter)'
    case 'omniroute/kmc/k3': return 'Kimi K3 (OmniRoute)'
    case 'omniroute/kmc/kimi-for-coding': return 'Kimi for Coding (OmniRoute)'
    case 'omniroute/kmc/kimi-for-coding-highspeed': return 'Kimi Coding Fast (OmniRoute)'
    case 'omniroute/cp/cline-pass/glm-5.2': return 'GLM-5.2 (OmniRoute)'
    case 'omniroute/cp/cline-pass/minimax-m3': return 'MiniMax-M3 (OmniRoute)'
    case 'omniroute/cp/cline-pass/deepseek-v4-pro': return 'DeepSeek V4 Pro (OmniRoute)'
    case 'omniroute/cp/cline-pass/deepseek-v4-flash': return 'DeepSeek V4 Flash (OmniRoute)'
    case 'omniroute/cp/cline-pass/kimi-k3': return 'Kimi K3 CP (OmniRoute)'
    case 'omniroute/cp/cline-pass/kimi-k2.7-code': return 'Kimi K2.7 Code (OmniRoute)'
    case 'omniroute/cp/cline-pass/mimo-v2.5-pro': return 'MiMo-V2.5-Pro (OmniRoute)'
    case 'omniroute/cp/cline-pass/mimo-v2.5': return 'MiMo-V2.5 (OmniRoute)'
    case 'omniroute/cp/cline-pass/qwen3.7-max': return 'Qwen3.7 Max (OmniRoute)'
    case 'omniroute/cp/cline-pass/qwen3.7-plus': return 'Qwen3.7 Plus (OmniRoute)'
    case 'omniroute/kr/claude-sonnet-5': return 'Claude Sonnet 5 (OmniRoute)'
    case 'omniroute/kr/claude-sonnet-4.5': return 'Claude Sonnet 4.5 (OmniRoute)'
    case 'omniroute/kr/claude-haiku-4.5': return 'Claude Haiku 4.5 (OmniRoute)'
    case 'omniroute/kr/deepseek-3.2': return 'DeepSeek V3.2 (OmniRoute)'
    case 'omniroute/kr/minimax-m2.5': return 'MiniMax M2.5 (OmniRoute)'
    case 'omniroute/kr/minimax-m2.1': return 'MiniMax M2.1 (OmniRoute)'
    case 'omniroute/kr/glm-5': return 'GLM-5 (OmniRoute)'
    case 'omniroute/kr/qwen3-coder-next': return 'Qwen3 Coder Next (OmniRoute)'
    case 'omniroute/kr/gpt-5.6-sol': return 'GPT-5.6 Sol (OmniRoute)'
    case 'omniroute/kr/gpt-5.6-terra': return 'GPT-5.6 Terra (OmniRoute)'
    case 'omniroute/kr/gpt-5.6-luna': return 'GPT-5.6 Luna (OmniRoute)'
    default: return modelId
  }
}

export function renderApp(state: AppState) {
  return `
    <div class="shell">
      ${renderTopbar(state)}
      <main class="workspace">
        ${state.notice ? renderNotice(state.notice) : ''}
        ${state.view === 'auth' ? renderAuth(state) : ''}
        ${state.view === 'scenarios' ? renderScenarioPicker(state) : ''}
        ${state.view === 'chat' ? renderChat(state) : ''}
        ${state.view === 'history' ? renderStudentHistory(state) : ''}
        ${state.view === 'review' ? renderReviewDashboard(state) : ''}
        ${state.view === 'admin' ? renderAdminDashboard(state) : ''}
      </main>
      ${state.token && state.tutorialOpen ? renderTutorialModal(state) : ''}
    </div>
  `
}

function renderTopbar(state: AppState) {
  const viewLabels: Record<AppView, string> = {
    auth: 'Cổng truy cập',
    scenarios: 'Lựa chọn kịch bản',
    chat: state.evaluation ? 'Báo cáo đánh giá' : 'Phỏng vấn trực tiếp',
    history: 'Lịch sử phỏng vấn',
    review: 'Bảng Review Giảng viên',
    admin: 'Quản trị hệ thống (Admin)'
  }
  const canReview = isPrivilegedRole(state.user?.role)
  const isAdmin = state.user?.role === 'Admin'

  return `
    <header class="topbar" style="background: var(--surface); border-bottom: 1px solid var(--line-subtle); padding: 14px 28px;">
      <div class="brand-block" style="display: flex; align-items: center; gap: 12px;">
        <span class="brand-mark font-heading" aria-hidden="true" style="background: var(--accent); color: #FFF; width: 36px; height: 36px; border-radius: 10px; display: flex; align-items: center; justify-content: center; font-weight: 800; font-size: 18px;">R</span>
        <div>
          <p class="eyebrow" style="letter-spacing: 0.08em; font-size: 10px; font-weight: 800; color: var(--accent-indigo); text-transform: uppercase; margin: 0;">ReqSimulator • PRO AI</p>
          <h1 style="font-size: 17px; font-family: var(--font-heading); font-weight: 700; color: var(--text-primary); margin: 0;">
            Phòng Thực Hành Khai Thác Yêu Cầu
          </h1>
        </div>
      </div>
      <div class="topbar-actions" style="display: flex; align-items: center; gap: 12px;">
        <span class="view-pill" style="background: var(--surface-raised); border: 1px solid var(--line-subtle); color: var(--accent-indigo); font-weight: 600; font-size: 12px; padding: 4px 12px; border-radius: 20px;">${escapeHtml(viewLabels[state.view])}</span>
        ${state.user?.email ? `<span class="user-pill" style="background: var(--surface-raised); border: 1px solid var(--line-subtle); color: var(--text-primary); font-size: 12px; font-weight: 500; padding: 4px 14px; border-radius: 20px;">${escapeHtml(state.user.email)} <span style="color: var(--accent-emerald); font-weight: 700; margin-left: 4px;">(${escapeHtml(t(state.user.role ?? 'Student', i18n.roles, 'Sinh viên'))})</span></span>` : ''}
        ${state.token ? `<button class="ghost-button" data-action="open-tutorial" type="button" ${state.busy ? 'disabled' : ''}>Hướng dẫn</button>` : ''}
        ${state.user?.role === 'Student' && state.view !== 'history' ? `<button class="ghost-button" data-action="open-student-history" type="button" ${state.busy ? 'disabled' : ''}>Lịch sử</button>` : ''}
        ${state.token && state.view !== 'scenarios' ? `<button class="ghost-button" data-action="open-student-lab" type="button" ${state.busy ? 'disabled' : ''}>Phòng thực hành</button>` : ''}
        ${canReview && state.view !== 'review' ? `<button class="ghost-button" data-action="open-review" type="button" ${state.busy ? 'disabled' : ''}>Review Giảng viên</button>` : ''}
        ${isAdmin && state.view !== 'admin' ? `<button class="ghost-button" data-action="open-admin" type="button" ${state.busy ? 'disabled' : ''} style="border-color: var(--accent-amber); color: var(--accent-amber);">Admin Console</button>` : ''}
        ${state.token ? `<button class="ghost-button" data-action="logout" type="button" ${state.busy ? 'disabled' : ''} style="color: var(--accent-rose); border-color: rgba(244,63,94,0.3);">Đăng xuất</button>` : ''}
      </div>
    </header>
  `
}

function renderNotice(notice: Notice) {
  const role = notice.type === 'error' ? 'alert' : 'status'
  const live = notice.type === 'error' ? 'assertive' : 'polite'
  return `<div class="notice ${notice.type}" role="${role}" aria-live="${live}" aria-atomic="true">${escapeHtml(notice.text)}</div>`
}

function renderAuth(state: AppState) {
  const isLogin = state.authMode === 'login'
  return `
    <section class="auth-layout">
      <div class="auth-copy">
        <div class="badge-pill-container" style="margin-bottom: 16px;">
          <span class="rounded-full-pill">Khai thác yêu cầu thực tế cùng Stakeholder AI</span>
        </div>
        <h2 class="hero-heading font-serif" style="margin-bottom: 24px;">
          ReqSimulator
        </h2>
        <p class="hero-subtext" style="margin-bottom: 32px;">
          Hệ thống mô phỏng phỏng vấn stakeholder hỗ trợ sinh viên luyện tập và cải thiện kỹ năng kỹ nghệ yêu cầu phần mềm thông qua các kịch bản giả lập có đánh giá tự động.
        </p>
        <div class="auth-proof-grid">
          <div class="proof-item">
            <span class="proof-num font-serif">01</span>
            <p class="proof-text"><strong>Hội thoại giả lập:</strong> Phỏng vấn Stakeholder AI có cá tính, cảm xúc và mức độ kiên nhẫn thay đổi liên tục.</p>
          </div>
          <div class="proof-item">
            <span class="proof-num font-serif">02</span>
            <p class="proof-text"><strong>Đánh giá tự động:</strong> Đối soát ngữ nghĩa các yêu cầu trích xuất được với danh sách yêu cầu ẩn (hidden requirements).</p>
          </div>
          <div class="proof-item">
            <span class="proof-num font-serif">03</span>
            <p class="proof-text"><strong>Báo cáo bao phủ:</strong> Cung cấp chi tiết điểm số, nhận xét điểm mạnh, điểm yếu và gợi ý cải thiện kỹ năng phỏng vấn.</p>
          </div>
        </div>
      </div>
      <div class="auth-form-wrapper">
        <form class="auth-panel" id="auth-form" role="tabpanel" aria-labelledby="auth-tab-${isLogin ? 'login' : 'register'}">
          <div class="form-heading">
            <p class="section-kicker">${isLogin ? 'Đăng nhập' : 'Đăng ký'}</p>
            <h2>${isLogin ? 'Vào phòng thực hành' : 'Tạo tài khoản sinh viên'}</h2>
          </div>
          <div class="tabs" role="tablist" aria-label="Đăng nhập hoặc tạo tài khoản">
            <button id="auth-tab-login" class="${isLogin ? 'active' : ''}" data-auth-mode="login" role="tab" aria-selected="${isLogin}" aria-controls="auth-form" type="button">Đăng nhập</button>
            <button id="auth-tab-register" class="${!isLogin ? 'active' : ''}" data-auth-mode="register" role="tab" aria-selected="${!isLogin}" aria-controls="auth-form" type="button">Tạo tài khoản</button>
          </div>
          ${isLogin ? '' : `
            <label for="auth-name">
              Họ tên
              <input id="auth-name" name="name" autocomplete="name" required maxlength="100" />
            </label>
          `}
          <label for="auth-email">
            Email
            <input id="auth-email" name="email" type="email" autocomplete="email" required maxlength="255" />
          </label>
          <label for="auth-password">
            Mật khẩu
            <input id="auth-password" name="password" type="password" autocomplete="${isLogin ? 'current-password' : 'new-password'}" required minlength="${isLogin ? 1 : 12}" maxlength="128" />
          </label>
          <button class="primary-button" type="submit" ${state.busy ? 'disabled' : ''}>
            ${state.busy ? 'Đang xử lý...' : isLogin ? 'Vào hệ thống' : 'Tạo tài khoản'}
          </button>
        </form>
      </div>
    </section>
  `
}

function renderScenarioPicker(state: AppState) {
  const scenario = state.selectedScenario
  const language = state.scenarioLanguage
  return `
    <section class="section-head" data-animate="fade-up" style="--index: 0">
      <div>
        <p class="section-kicker">Thiết lập kịch bản</p>
        <h2>Chọn tình huống và đối tác phỏng vấn</h2>
      </div>
      <div class="scenario-head-actions">
        <div class="scenario-language-switch" role="group" aria-label="Ngôn ngữ nội dung kịch bản">
          <button class="ghost-button ${language === 'vi' ? 'active' : ''}" data-action="set-scenario-language" data-language="vi" type="button" aria-pressed="${language === 'vi'}" ${state.busy ? 'disabled' : ''}>VI</button>
          <button class="ghost-button ${language === 'en' ? 'active' : ''}" data-action="set-scenario-language" data-language="en" type="button" aria-pressed="${language === 'en'}" ${state.busy ? 'disabled' : ''}>EN</button>
        </div>
        <button class="ghost-button" data-action="refresh-scenarios" type="button" ${state.busy ? 'disabled' : ''}>
          Tải lại kịch bản
        </button>
      </div>
    </section>
    <section class="picker-layout">
      <aside class="scenario-list" data-animate="fade-up" style="--index: 1">        <div class="panel-heading">
          <div>
            <p class="section-kicker">Danh sách</p>
            <h2>${state.scenarios.length} kịch bản khả dụng</h2>
          </div>
        </div>
        <div class="list-stack">
          ${state.scenarios.length === 0 ? renderEmpty('Chưa có kịch bản nào được kích hoạt.', 'Kiểm tra dữ liệu mẫu của máy chủ hoặc tải lại danh sách.') : state.scenarios.map((item, index) => renderScenarioItem(item, state, index)).join('')}
        </div>
      </aside>
      <section class="detail-panel" data-animate="fade-up" style="--index: 2">        ${scenario ? renderScenarioDetail(scenario, state) : renderScenarioPlaceholder()}
      </section>
    </section>
  `
}

function renderScenarioItem(scenario: ScenarioSummary, state: AppState, index: number) {
  const active = scenario.id === state.selectedScenario?.id
  return `
    <button class="scenario-item ${active ? 'active' : ''}" data-scenario-id="${escapeAttribute(scenario.id)}" type="button" data-animate="fade-up" style="--index: ${index}" ${state.busy ? 'disabled' : ''}>
      <span class="scenario-title-block">
        <strong>${escapeHtml(scenario.title)}</strong>
        <small style="font-family: var(--font-mono); text-transform: uppercase;">${escapeHtml(scenario.domain ?? 'Nghiệp vụ')} · ${escapeHtml(scenario.difficulty)}</small>
      </span>
      <span class="scenario-stats">
        <span>${scenario.personaCount} đối tác</span>
        <span>${scenario.requirementCount} yêu cầu</span>
      </span>
    </button>
  `
}

function renderScenarioPlaceholder() {
  return `
    <div class="placeholder">
      <p class="section-kicker">Bắt đầu</p>
      <h2>Chọn kịch bản để xem chi tiết</h2>
      <p>Thông tin kịch bản sẽ hiển thị lĩnh vực, độ khó, danh sách các yêu cầu ẩn và đối tác phỏng vấn.</p>
    </div>
  `
}

function renderScenarioDetail(scenario: ScenarioDetail, state: AppState) {
  const selectedPersona = scenario.personas.find((persona) => persona.id === state.selectedPersonaId)
  return `
    <div class="detail-header" data-animate="fade-up" style="--index: 0">
      <div>
        <p class="section-kicker">${escapeHtml(scenario.domain ?? 'Kịch bản')}</p>
        <h2>${escapeHtml(scenario.title)}</h2>
        <p>${escapeHtml(scenario.description)}</p>
      </div>
      <div class="metrics">
        <span>Đối tác phỏng vấn: <strong>${scenario.personaCount}</strong></span>
        <span>Yêu cầu ẩn: <strong>${scenario.requirementCount}</strong></span>
        <span>Độ khó: <strong style="font-family: var(--font-sans); text-transform: uppercase;">${escapeHtml(scenario.difficulty)}</strong></span>
      </div>
    </div>
    <div class="persona-section">
      <div class="subsection-heading" data-animate="fade-up" style="--index: 1">
        <h3>Nhân vật phỏng vấn</h3>
        ${selectedPersona ? `<span class="view-pill font-serif">${escapeHtml(formatPersonaText(selectedPersona.name))}</span>` : ''}
      </div>
      <div class="persona-grid">
        ${scenario.personas.length === 0 ? renderEmpty('Kịch bản này chưa có đối tác phỏng vấn nào.', 'Cần khởi tạo dữ liệu đối tác trước khi bắt đầu phỏng vấn.') : scenario.personas.map((persona, index) => renderPersonaCard(persona, state, index + 2)).join('')}
      </div>
      ${selectedPersona ? renderPersonaBrief(selectedPersona) : ''}
    </div>
    <div class="panel-footer" data-animate="fade-up" style="--index: 5">
      <div>
        <strong>${selectedPersona ? escapeHtml(formatPersonaText(selectedPersona.roleTitle ?? 'Đối tác')) : 'Chưa chọn đối tác'}</strong>
        <span>${selectedPersona ? 'Phiên mô phỏng phỏng vấn phác thảo sẽ bắt đầu với nhân vật này.' : 'Vui lòng lựa chọn một đối tác phỏng vấn để bắt đầu.'}</span>
      </div>
      <div style="display: flex; gap: var(--spacing-sm); align-items: center; flex-wrap: wrap; position: relative;">
        <span style="font-size: 13px; color: var(--text-secondary);">AI được hệ thống tự chọn theo quota khả dụng.</span>
        <button class="primary-button" data-action="start-session" type="button" ${!state.selectedPersonaId || state.busy ? 'disabled' : ''}>
          ${state.busy ? 'Đang khởi tạo...' : 'Bắt đầu phỏng vấn'}
        </button>
      </div>
    </div>
  `
}

function renderPersonaCard(persona: Persona, state: AppState, index: number) {
  const active = persona.id === state.selectedPersonaId
  return `
    <button class="persona-card ${active ? 'active' : ''}" data-persona-id="${escapeAttribute(persona.id)}" type="button" data-animate="fade-up" style="--index: ${index}">
      <span class="persona-topline">
        <strong class="font-serif">${escapeHtml(formatPersonaText(persona.name))}</strong>
        <span class="difficulty-badge">${escapeHtml(formatPersonaText(persona.difficulty))}</span>
      </span>
      <span>${escapeHtml(formatPersonaText(persona.roleTitle ?? 'Đối tác'))}</span>
      <small>Trao đổi: ${escapeHtml(formatPersonaText(persona.communicationStyle ?? 'trung lập'))} · Mức độ am hiểu: ${escapeHtml(formatPersonaText(persona.knowledgeLevel ?? 'tiêu chuẩn'))}</small>
    </button>
  `
}

function renderPersonaBrief(persona: Persona) {
  const knowledge = formatPersonaText(persona.knowledgeLevel ?? 'tiêu chuẩn')
  const style = formatPersonaText(persona.communicationStyle ?? 'trung lập')
  const role = formatPersonaText(persona.roleTitle ?? 'nhân vật phỏng vấn')
  const isEndUser = `${persona.name} ${persona.roleTitle ?? ''}`.toLowerCase().includes('người dùng')
  const scope = isEndUser
    ? 'Chỉ chia sẻ trải nghiệm sử dụng, thao tác hằng ngày và vấn đề gặp phải; không đại diện cho quyết định kỹ thuật hoặc kiến trúc hệ thống.'
    : (persona.knowledgeLevel ?? '').toLowerCase() === 'high'
    ? 'Nắm rõ nghiệp vụ thuộc vai trò của mình và có thể làm rõ quy trình, ngoại lệ khi được hỏi đúng trọng tâm.'
    : 'Có góc nhìn vận hành thực tế; hãy dùng câu hỏi mở và câu hỏi làm rõ để thu thập thông tin cần thiết.'
  return `
    <aside class="notice info" style="margin-top: 14px; margin-bottom: 0;">
      <strong>Brief trước phỏng vấn: ${escapeHtml(role)}</strong>
      <p style="margin: 6px 0 0;">Mục tiêu: khai thác nhu cầu, quy tắc và ngoại lệ từ góc nhìn của nhân vật. ${escapeHtml(scope)}</p>
      <small style="display:block; margin-top: 6px;">Cách trao đổi: ${escapeHtml(style)} · Mức độ am hiểu: ${escapeHtml(knowledge)}. Thông tin chi tiết sẽ được nhân vật tiết lộ dần qua câu hỏi phù hợp.</small>
    </aside>
  `
}

function renderStudentHistory(state: AppState) {
  const detail = state.studentHistoryDetail
  const progress = state.studentProgress
  return `
    <section class="section-head" data-animate="fade-up" style="--index: 0">
      <div>
        <p class="section-kicker">Theo dõi tiến bộ</p>
        <h2>Lịch sử phỏng vấn</h2>
        <p>Xem lại transcript, điểm AI ban đầu và điểm sau khi giảng viên review.</p>
      </div>
      <div class="scenario-head-actions">
        <button class="ghost-button" data-action="refresh-student-history" type="button" ${state.busy ? 'disabled' : ''}>Làm mới</button>
        <button class="primary-button" data-action="open-student-lab" type="button">Phỏng vấn mới</button>
      </div>
    </section>
    ${progress ? `<section class="top-students-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); margin-bottom: 16px;">
      <div class="panel-heading"><div><p class="section-kicker">Năng lực IT</p><h3>Tiến bộ qua các phiên</h3></div><span>${progress.completedSessions} phiên đã chấm</span></div>
      <div class="score-grid">
        <span>Điểm đầu: <strong>${formatScore(progress.firstScore ?? null)}</strong></span>
        <span>Điểm gần nhất: <strong>${formatScore(progress.latestScore ?? null)}</strong></span>
        <span>Thay đổi: <strong>${typeof progress.scoreChange === 'number' ? `${progress.scoreChange > 0 ? '+' : ''}${progress.scoreChange} điểm` : 'Chưa đủ dữ liệu'}</strong></span>
        <span>Chất lượng câu hỏi: <strong>${typeof progress.questionQuality === 'number' ? `${progress.questionQuality}%` : 'Chưa đủ dữ liệu'}</strong></span>
      </div>
      ${progress.competencies.length ? `<div class="score-grid" style="margin-top: 10px;">${progress.competencies.map(item => `<span>${escapeHtml(item.competency)}: <strong>${item.score}%</strong> <small>(${item.assessed} mục)</small></span>`).join('')}</div>` : '<small>Hoàn thành phiên đầu tiên để xem competency profile.</small>'}
    </section>` : ''}
    <section class="review-layout student-history-layout">
      <aside class="review-list" data-animate="fade-up" style="--index: 1">
        <div class="panel-heading"><div><p class="section-kicker">Các phiên đã lưu</p><h2>${state.studentHistory.length} phiên</h2></div></div>
        <div class="list-stack">
          ${state.studentHistory.length === 0
            ? renderEmpty('Chưa có phiên phỏng vấn nào.', 'Hãy thực hiện một phiên mới để kết quả xuất hiện ở đây.')
            : state.studentHistory.map((session) => {
              const score = session.evaluation?.finalScore ?? session.evaluation?.coverageScore
              return `<button class="review-session-item ${session.id === state.selectedStudentSessionId ? 'active' : ''}" data-student-session-id="${escapeAttribute(session.id)}" type="button" ${state.busy ? 'disabled' : ''}>
                <span><strong>${escapeHtml(session.scenario.title)}</strong><small>${escapeHtml(formatPersonaText(session.persona.name))} · ${formatTime(session.startedAt)}</small></span>
                <span class="history-score">${typeof score === 'number' ? formatScore(score) : 'Chưa chấm'}</span>
              </button>`
            }).join('')}
        </div>
      </aside>
      <section class="review-detail student-history-detail" data-animate="fade-up" style="--index: 2">
        ${detail ? `
          <div class="review-detail-header"><div><p class="section-kicker">${escapeHtml(detail.session.scenario.domain ?? 'Nghiệp vụ')}</p><h2>${escapeHtml(detail.session.scenario.title)}</h2><p>Nhân vật phỏng vấn: <strong>${escapeHtml(formatPersonaText(detail.session.persona.name))}</strong></p></div><span class="view-pill">${detail.session.isActive ? 'Đang tiến hành' : 'Đã kết thúc'}</span></div>
          ${detail.evaluation ? `<div class="history-score-compare"><div><small>Điểm AI ban đầu</small><strong>${formatScore(detail.evaluation.coverageScore ?? null)}</strong></div><div class="history-score-arrow">→</div><div><small>Điểm sau review</small><strong>${typeof detail.evaluation.overriddenCoverageScore === 'number' ? formatScore(detail.evaluation.overriddenCoverageScore) : 'Chưa review'}</strong></div></div>` : renderEmpty('Phiên chưa có điểm.', 'Kết thúc phiên để hệ thống đánh giá.')}
          <section class="review-block"><div class="panel-heading"><h3>Transcript</h3><span>${detail.messages.length} tin nhắn</span></div><div class="review-transcript">${detail.messages.map((message, index) => renderMessage(message, index)).join('')}</div></section>
          ${detail.evaluation ? renderEvaluation(detail.evaluation) : ''}
        ` : renderEmpty('Chọn một phiên để xem lại.', 'Transcript và điểm số sẽ hiển thị tại đây.')}
      </section>
    </section>
  `
}

function renderChat(state: AppState) {
  const scenario = state.selectedScenario
  const persona = scenario?.personas.find((item) => item.id === state.selectedPersonaId)
  const studentMessageCount = state.messages.filter((message) => message.sender === 'Student').length
  const lastQuestionType = [...state.messages].reverse().find((message) => message.detectedQuestionType)?.detectedQuestionType
  const sessionStatus = state.evaluation ? 'Đã hoàn thành' : 'Đang tiến hành'

  const isThinking = state.busy && !state.evaluation && state.messages.length > 0 && state.messages[state.messages.length - 1].sender === 'Student'
  const thinkingHtml = isThinking ? `
    <article class="message stakeholder thinking" data-animate="fade-up" style="--index: ${state.messages.length}">
      <div class="message-meta">
        <span class="font-serif">${escapeHtml(persona?.name ?? 'Đối tác')}</span>
        <small>Đang suy nghĩ...</small>
      </div>
      <div class="thinking-indicator">
        <span class="dot"></span>
        <span class="dot"></span>
        <span class="dot"></span>
      </div>
    </article>
  ` : ''

  return `
    <section class="chat-layout">
      <aside class="session-panel" data-animate="fade-up" style="--index: 0">        <button class="ghost-button back-button" data-action="back-to-scenarios" type="button" ${state.busy ? 'disabled' : ''}>
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" style="margin-right: 4px;"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>
          Quay lại
        </button>
        <div class="session-title">
          <p class="section-kicker">Phiên phỏng vấn</p>
          <h2>${escapeHtml(scenario?.title ?? 'Phiên')}</h2>
        </div>
        <div class="session-status ${state.evaluation ? 'completed' : 'active'}">
          <span>${sessionStatus}</span>
          <strong>${studentMessageCount}</strong>
          <small>Lượt hỏi</small>
        </div>
        <dl>
          <div><dt>Đối tác phỏng vấn</dt><dd class="font-serif" style="font-size: 16px;">${escapeHtml(persona?.name ?? 'Đối tác')}</dd></div>
          <div><dt>Vai trò</dt><dd>${escapeHtml(persona?.roleTitle ?? 'N/A')}</dd></div>
          <div><dt>Mô hình AI</dt><dd style="font-family: var(--font-mono); font-size: 12px; color: var(--pastel-blue-text); font-weight: bold;">${getFriendlyModelName(state.session?.selectedModel || state.selectedModel)}</dd></div>
          <div><dt>Loại câu hỏi gần nhất</dt><dd>${escapeHtml(lastQuestionType ?? 'Chưa phát hiện')}</dd></div>
          <div><dt>Mã phiên phỏng vấn</dt><dd style="font-family: var(--font-mono); font-size: 12px;">${escapeHtml(shortId(state.session?.id ?? ''))}</dd></div>
        </dl>
        <button class="danger-button" data-action="open-end-session-modal" type="button" ${state.busy || Boolean(state.evaluation) ? 'disabled' : ''}>
          ${state.busy ? 'Đang xử lý...' : state.evaluation ? 'Đã kết thúc phiên' : 'Kết thúc & Chấm điểm'}
        </button>
        ${state.evaluation ? renderEvaluation(state.evaluation, true) : ''}
      </aside>
      <section class="chat-panel" data-animate="fade-up" style="--index: 1">        <div class="chat-header">
          <div>
            <p class="section-kicker">Hội thoại Trực tiếp (${getFriendlyModelName(state.session?.selectedModel || state.selectedModel)})</p>
            <h2 class="font-serif">${escapeHtml(persona?.name ?? 'Đối tác')}</h2>
          </div>
          <span class="view-pill">${state.evaluation ? 'Chế độ xem lại' : state.busy ? 'Đang xử lý' : 'Sẵn sàng'}</span>
        </div>
        <div class="messages" id="messages">
          ${state.messages.length === 0 ? renderEmpty('Chưa có tin nhắn trong phiên này.', 'Bắt đầu bằng một câu hỏi khảo sát nghiệp vụ.') : state.messages.map((msg, index) => renderMessage(msg, index)).join('')}
          ${thinkingHtml}
        </div>
        <form class="composer" id="message-form">
          <div class="composer-input">
            <textarea name="content" rows="3" maxlength="4000" aria-label="Câu hỏi gửi cho đối tác phỏng vấn" placeholder="Nhập câu hỏi nghiệp vụ gửi cho đối tác phỏng vấn..." ${state.busy || Boolean(state.evaluation) ? 'disabled' : ''}></textarea>
            <small><kbd>Enter</kbd> gửi · <kbd>Shift + Enter</kbd> xuống dòng</small>
          </div>
          <button class="primary-button" type="submit" ${state.busy || Boolean(state.evaluation) ? 'disabled' : ''}>
            ${state.busy ? 'Đang gửi...' : 'Gửi'}
          </button>
        </form>
      </section>
    </section>
    ${state.confirmEndSession ? renderEndSessionModal(state) : ''}
  `
}

function renderEndSessionModal(state: AppState) {
  const scenario = state.selectedScenario
  const persona = scenario?.personas.find((item) => item.id === state.selectedPersonaId)
  const studentMessageCount = state.messages.filter((message) => message.sender === 'Student').length

  return `
    <div class="modal-overlay" id="end-session-modal-overlay">
      <div class="modal-card" id="end-session-modal" role="dialog" aria-modal="true" aria-labelledby="end-session-modal-title" aria-describedby="end-session-modal-description" tabindex="-1">
        <div class="modal-header">
          <div class="modal-icon">
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>
          </div>
          <div>
            <h2 id="end-session-modal-title" style="margin:0; font-size:18px; font-weight:700; color:var(--text-primary); font-family: var(--font-heading);">Xác nhận kết thúc phỏng vấn</h2>
            <span style="font-size:12px; color:var(--accent-indigo); font-weight: 600;">${escapeHtml(scenario?.title ?? '')}</span>
          </div>
        </div>
        <div class="modal-body">
          <p id="end-session-modal-description">Bạn có chắc chắn muốn kết thúc phiên phỏng vấn với đối tác <strong style="color: var(--text-primary);">${escapeHtml(persona?.name ?? 'Stakeholder')}</strong> không?</p>
          <p style="margin-top: 8px; font-size: 13px;">Hệ thống AI sẽ tự động trích xuất các yêu cầu phần mềm đã trao đổi và tiến hành chấm điểm bài làm của bạn. Sau khi nộp bài, bạn không thể tiếp tục gửi câu hỏi trong phiên này.</p>
        </div>
        <div class="modal-stats">
          <span class="modal-stat-label">Số câu hỏi đã trao đổi</span>
          <span class="modal-stat-value">${studentMessageCount} lượt hỏi</span>
        </div>
        <div class="modal-actions">
          <button class="ghost-button" data-action="cancel-end-session" type="button" ${state.busy ? 'disabled' : ''}>
            Tiếp tục phỏng vấn
          </button>
          <button class="danger-button" data-action="confirm-end-session" type="button" ${state.busy ? 'disabled' : ''}>
            ${state.busy ? 'Đang xử lý...' : 'Xác nhận & Nộp bài'}
          </button>
        </div>
      </div>
    </div>
  `
}

function renderTutorialModal(state: AppState) {
  const role = state.user?.role ?? 'Student'
  const tutorial = role === 'Admin'
    ? {
        eyebrow: 'Hướng dẫn quản trị viên',
        title: 'Tạo và kiểm duyệt tri thức nghiệp vụ',
        steps: [
          ['1. Chọn nguồn', 'Mở Admin Console, chọn URL công khai hoặc tải video/audio cuộc họp để bắt đầu nạp tri thức.'],
          ['2. Theo dõi xử lý', 'Kiểm tra trạng thái job trong Lịch sử nạp tri thức. Video được chuyển thành audio-only trước khi gửi AI.'],
          ['3. Kiểm tra bản nháp', 'Mở preview, rà scenario, stakeholder và các yêu cầu; chỉnh sửa nếu nội dung chưa chính xác.'],
          ['4. Publish', 'Ghi chú kiểm duyệt rồi publish phiên bản scenario để sinh viên có thể sử dụng.'],
        ],
      }
    : role === 'Lecturer'
      ? {
          eyebrow: 'Hướng dẫn giảng viên',
          title: 'Review kết quả luyện tập',
          steps: [
            ['1. Mở Review', 'Chọn Review Giảng viên trên thanh điều hướng để xem các phiên sinh viên đã hoàn thành.'],
            ['2. Đọc transcript', 'Mở một phiên để xem câu hỏi, câu trả lời stakeholder và các requirement đã phát hiện.'],
            ['3. Kiểm tra matching', 'Đối chiếu kết quả matching với Ground Truth; chọn loại match phù hợp nếu hệ thống đánh giá chưa đúng.'],
            ['4. Lưu và xuất', 'Nhập nhận xét, lưu override và xuất JSON/CSV khi cần làm minh chứng hoặc phản hồi cho sinh viên.'],
          ],
        }
      : {
          eyebrow: 'Hướng dẫn sinh viên',
          title: 'Bắt đầu một phiên phỏng vấn',
          steps: [
            ['1. Chọn scenario', 'Tại Phòng thực hành, chọn một kịch bản nghiệp vụ phù hợp với bài tập của bạn.'],
            ['2. Chọn stakeholder', 'Đọc vai trò, độ khó và phong cách giao tiếp rồi chọn stakeholder để phỏng vấn.'],
            ['3. Đặt câu hỏi', 'Bắt đầu bằng câu hỏi mở, sau đó hỏi làm rõ ngoại lệ, điều kiện và quy tắc nghiệp vụ.'],
            ['4. Kết thúc và xem điểm', 'Khi đủ thông tin, kết thúc phiên để xem requirement matching, coverage score và gợi ý cải thiện.'],
          ],
        }
  const step = Math.max(0, Math.min(state.tutorialStep ?? 0, tutorial.steps.length - 1))
  const current = tutorial.steps[step]
  return `
    <div class="modal-overlay tutorial-overlay" id="tutorial-modal-overlay">
      <section class="modal-card tutorial-card" id="tutorial-modal" role="dialog" aria-modal="true" aria-labelledby="tutorial-title" aria-describedby="tutorial-description" tabindex="-1">
        <div class="modal-header">
          <div class="modal-icon tutorial-icon" aria-hidden="true">?</div>
          <div style="min-width:0; flex:1;">
            <p class="section-kicker" style="margin:0 0 4px;">${escapeHtml(tutorial.eyebrow)}</p>
            <h2 id="tutorial-title" style="margin:0; font-size:20px; color:var(--text-primary);">${escapeHtml(tutorial.title)}</h2>
          </div>
          <button class="icon-button" data-action="tutorial-close" type="button" aria-label="Đóng hướng dẫn">×</button>
        </div>
        <div class="tutorial-progress" aria-label="Tiến trình hướng dẫn">
          ${tutorial.steps.map((_, index) => `<span class="tutorial-dot ${index === step ? 'active' : ''}" aria-hidden="true"></span>`).join('')}
          <small>Bước ${step + 1}/${tutorial.steps.length}</small>
        </div>
        <div class="modal-body tutorial-body">
          <h3>${escapeHtml(current[0])}</h3>
          <p id="tutorial-description">${escapeHtml(current[1])}</p>
        </div>
        <div class="modal-actions tutorial-actions">
          <button class="ghost-button" data-action="tutorial-close" type="button">Bỏ qua</button>
          <span style="flex:1"></span>
          ${step > 0 ? '<button class="ghost-button" data-action="tutorial-prev" type="button">Quay lại</button>' : ''}
          ${step < tutorial.steps.length - 1
            ? '<button class="primary-button" data-action="tutorial-next" type="button">Tiếp theo</button>'
            : '<button class="primary-button" data-action="tutorial-close" type="button">Bắt đầu</button>'}
        </div>
      </section>
    </div>
  `
}

function renderReviewDashboard(state: AppState) {
  const completed = state.reviewSessions.filter((session) => Boolean(session.evaluation)).length
  const active = state.reviewSessions.filter((session) => session.isActive).length
  const averageCoverage = calculateAverageCoverage(state.reviewSessions)

  return `
    <section class="section-head" data-animate="fade-up" style="--index: 0">
      <div>
        <p class="section-kicker">Đánh giá của Giảng viên</p>
        <h2>Bảng kết quả phỏng vấn của sinh viên</h2>
      </div>
      <button class="ghost-button" data-action="refresh-review" type="button" ${state.busy ? 'disabled' : ''}>
        Tải lại danh sách phiên
      </button>
    </section>
    <section class="review-metrics" data-animate="fade-up" style="--index: 1">
      <span>
        <strong>${state.reviewSessions.length}</strong>
        <small>phiên chạy</small>
      </span>
      <span>
        <strong>${completed}</strong>
        <small>đã đánh giá</small>
      </span>
      <span>
        <strong>${active}</strong>
        <small>đang hoạt động</small>
      </span>
      <span>
        <strong>${averageCoverage}</strong>
        <small>độ bao phủ trung bình</small>
      </span>
    </section>
    <section class="review-layout">
      <aside class="review-list" data-animate="fade-up" style="--index: 2">        <div class="panel-heading">
          <div>
            <p class="section-kicker">Bản ghi thử nghiệm</p>
            <h2>Các phiên gần nhất</h2>
          </div>
        </div>
        <div class="list-stack">
          ${state.reviewSessions.length === 0 ? renderEmpty('Chưa có phiên phỏng vấn nào để đánh giá.', 'Hãy thực hiện một phiên phỏng vấn rồi quay lại bảng điều khiển.') : state.reviewSessions.map((session, index) => renderReviewSessionItem(session, state, index)).join('')}
        </div>
      </aside>
      <section class="review-detail" data-animate="fade-up" style="--index: 3">        ${state.reviewDetail ? renderReviewSessionDetail(state.reviewDetail) : renderReviewPlaceholder()}
      </section>
    </section>
  `
}

function renderReviewSessionItem(session: ReviewSessionSummary, state: AppState, index: number) {
  const active = session.id === state.selectedReviewSessionId
  const score = session.evaluation?.coverageScore
  return `
    <button class="review-session-item ${active ? 'active' : ''}" data-review-session-id="${escapeAttribute(session.id)}" type="button" data-animate="fade-up" style="--index: ${index}" ${state.busy ? 'disabled' : ''}>
      <span class="scenario-title-block">
        <strong>${escapeHtml(session.student.name)}</strong>
        <small>${escapeHtml(session.scenario.title)}</small>
      </span>
      <span class="scenario-stats">
        <span>${session.studentTurnCount} lượt hỏi</span>
        <span>${session.finalizationStatus === 'completed' ? 'Đã hoàn thành' : 'Đang thực hiện'}</span>
        <span>${typeof score === 'number' ? formatScore(score) : 'Chưa điểm'}</span>
      </span>
    </button>
  `
}

function renderReviewPlaceholder() {
  return `
    <div class="placeholder">
      <p class="section-kicker">Review</p>
      <h2>Chọn phiên phỏng vấn để xem chi tiết lịch sử hội thoại</h2>
      <p>Bảng tổng quan hiển thị hội thoại phỏng vấn sinh viên, các yêu cầu trích xuất, và báo cáo đối soát chấm điểm tự động.</p>
    </div>
  `
}

function renderReviewSessionDetail(detail: ReviewSessionDetail) {
  const studentTurns = detail.messages.filter((message) => message.sender === 'Student').length
  const evalScore = detail.evaluation?.overriddenCoverageScore ?? detail.evaluation?.coverageScore ?? null
  const isOverridden = typeof detail.evaluation?.overriddenCoverageScore === 'number'

  return `
    <div class="review-detail-header" data-animate="fade-up" style="--index: 0">
      <div>
        <p class="section-kicker">${escapeHtml(detail.session.scenario.domain ?? 'Nghiệp vụ')}</p>
        <h2>${escapeHtml(detail.session.scenario.title)}</h2>
        <p>${escapeHtml(detail.session.scenario.description)}</p>
      </div>
      <div class="review-detail-actions">
        <div class="metrics">
          <span>Điểm bao phủ: <strong>${formatScore(evalScore)}</strong> ${isOverridden ? '<span class="override-badge">Đã duyệt bởi GV</span>' : ''}</span>
          <span>Lượt hỏi: <strong>${studentTurns}</strong></span>
          <span>Yêu cầu ẩn: <strong>${detail.hiddenRequirements.length}</strong></span>
        </div>
        <div class="export-actions">
          <button class="ghost-button" data-action="export-review-json" type="button">Xuất JSON</button>
          <button class="ghost-button" data-action="export-review-csv" type="button">Xuất CSV</button>
          ${detail.evaluation?.reviewFinalizedAt
            ? '<button class="ghost-button" data-action="reveal-review-identity" type="button">Mở danh tính</button>'
            : detail.evaluation
              ? '<button class="primary-button" data-action="finalize-review" type="button">Chốt review ẩn danh</button>'
              : ''}
        </div>
      </div>
    </div>
    <div class="review-identity-grid" data-animate="fade-up" style="--index: 1">
      <span><strong>${detail.session.student.email ? 'Danh tính sinh viên' : 'Mã bài ẩn danh'}</strong>${escapeHtml(detail.session.student.name)}${detail.session.student.email ? ` · ${escapeHtml(detail.session.student.email)}` : ''}</span>
      <span><strong>Đối tác</strong><span class="font-serif">${escapeHtml(formatPersonaText(detail.session.persona.name))}</span> · ${escapeHtml(formatPersonaText(detail.session.persona.roleTitle ?? 'Đối tác'))}</span>
      <span><strong>Trạng thái</strong>${detail.session.finalizationStatus === 'completed' ? 'Đã hoàn thành' : 'Đang thực hiện'} · ${detail.session.isActive ? 'Đang hoạt động' : 'Đã đóng'}</span>
    </div>
    ${isOverridden && detail.evaluation?.overriddenByLecturer ? `
      <div class="notice info" style="margin-top: 12px; margin-bottom: 0;">
        Điểm số đã được chỉnh sửa bởi giảng viên <strong>${escapeHtml(detail.evaluation.overriddenByLecturer)}</strong> vào ${formatTime(detail.evaluation.overriddenAt!)}. Điểm AI gốc: ${formatScore(detail.evaluation.coverageScore ?? null)}.
      </div>
    ` : ''}
    <div class="review-content-grid">
      <section class="review-block" data-animate="fade-up" style="--index: 2">
        <div class="subsection-heading">
          <h3>Hội thoại phỏng vấn</h3>
          <span>${detail.messages.length} tin nhắn</span>
        </div>
        <div class="review-transcript">
          ${detail.messages.length === 0 ? renderEmpty('Phiên phỏng vấn chưa có lịch sử hội thoại.') : detail.messages.map((msg, index) => renderMessage(msg, index)).join('')}
        </div>
      </section>
      <section class="review-block" data-animate="fade-up" style="--index: 3">
        <div class="subsection-heading">
          <h3>Báo cáo chấm điểm</h3>
          <span>${detail.evaluation ? 'Đã lưu' : 'Chưa đánh giá'}</span>
        </div>
        ${detail.evaluation ? renderEvaluation(detail.evaluation, false, true) : renderEmpty('Phiên phỏng vấn chưa được chấm điểm.', 'Kết thúc phiên phỏng vấn để tiến hành đánh giá.')}
      </section>
      <section class="review-block" data-animate="fade-up" style="--index: 4">
        <div class="subsection-heading">
          <h3>Yêu cầu trích xuất (Đã thu thập)</h3>
          <span>${detail.extractedRequirements.length} mục</span>
        </div>
        ${renderExtractedRequirements(detail.extractedRequirements)}
      </section>
      <section class="review-block" data-animate="fade-up" style="--index: 5">
        <div class="subsection-heading">
          <h3>Yêu cầu ẩn</h3>
          <span>${detail.hiddenRequirements.length} mục</span>
        </div>
        ${renderHiddenRequirements(detail.hiddenRequirements)}
      </section>
    </div>
  `
}

function renderExtractedRequirements(requirements: ReviewExtractedRequirement[]) {
  if (requirements.length === 0) {
    return renderEmpty('Chưa có yêu cầu trích xuất nào.')
  }

  return `
    <div class="artifact-list">
      ${requirements.map((requirement) => `
        <article>
          <strong>${escapeHtml(localizeExtractedRequirement(requirement.requirementText))}</strong>
          <small>Độ tin cậy: ${formatNullablePercent(requirement.confidenceScore)}</small>
        </article>
      `).join('')}
    </div>
  `
}

function renderHiddenRequirements(requirements: ReviewHiddenRequirement[]) {
  if (requirements.length === 0) {
    return renderEmpty('Kịch bản này chưa có yêu cầu ẩn nào.')
  }

  return `
    <div class="artifact-list">
      ${requirements.map((requirement) => `
        <article>
          <strong>${escapeHtml(localizeRequirementText(requirement.requirementText))}</strong>
          <small>Cổng mở khóa: ${requirement.gateOrder} · Nhóm: ${escapeHtml(localizeRequirementCategory(requirement.category))} · Độ khó: ${escapeHtml(localizePersonaDifficulty(requirement.revealDifficulty))}</small>
          ${requirement.revealCondition ? `<small>Điều kiện: ${escapeHtml(localizeRevealCondition(requirement.revealCondition))}</small>` : ''}
        </article>
      `).join('')}
    </div>
  `
}

function renderMessage(message: ChatMessage, index?: number) {
  const own = message.sender === 'Student'
  const idx = typeof index === 'number' ? index : 0
  return `
    <article class="message ${own ? 'student' : 'stakeholder'} ${message.pending ? 'pending' : ''}" data-animate="fade-up" style="--index: ${idx}">
      <div class="message-meta">
        <span class="${own ? '' : 'font-serif'}">${own ? 'Sinh viên' : escapeHtml(message.sender)}</span>
        <small>${message.pending ? 'Đang gửi' : formatTime(message.timestamp)}</small>
      </div>
      ${message.detectedQuestionType ? `<div class="question-chip">${escapeHtml(message.detectedQuestionType)}</div>` : ''}
      ${message.detectedTopic ? `<div class="question-chip">Topic: ${escapeHtml(message.detectedTopic)}</div>` : ''}
      ${message.questionQuality ? `<div class="question-chip">Quality: ${escapeHtml(message.questionQuality)}</div>` : ''}
      <p class="${own ? '' : 'font-serif'}">${escapeHtml(message.content)}</p>
    </article>
  `
}

function calculateAverageCoverage(sessions: ReviewSessionSummary[]) {
  const scores = sessions
    .map((session) => session.evaluation?.coverageScore)
    .filter((score): score is number => typeof score === 'number')

  if (scores.length === 0) return 'N/A'

  return formatScore(scores.reduce((sum, score) => sum + score, 0) / scores.length)
}

// Format score helper
function formatNullablePercent(value?: number | null) {
  return typeof value === 'number' ? formatScore(value * 100) : 'N/A'
}

function renderEvaluation(evaluation: EvaluationResult, showSurvey = false, canOverride = false) {
  const feedback = evaluation.feedback
  const total = evaluation.matchedCount + evaluation.partialCount + evaluation.missedCount
  const design = feedback?.designSuggestions
  const extractionReviewHtml = feedback?.extractionsToReview?.length ? `
    <div class="feedback-list" style="margin-top: 16px;">
      <strong>Requirements to review (${evaluation.extraExtractedCount ?? feedback.extractionsToReview.length})</strong>
      <ul>${feedback.extractionsToReview.map(item => `<li>${escapeHtml(item)}</li>`).join('')}</ul>
    </div>
  ` : ''
  
  const designTabHtml = design ? `
    <div class="design-suggestions-card" style="display: flex; flex-direction: column; gap: 16px; margin-top: 8px;">
      <div class="subsection-heading">
        <h3>Gợi ý Mô hình thiết kế sơ bộ (Gợi ý từ AI)</h3>
        <span style="font-size: 11px; opacity: 0.7;">Được sinh tự động dựa trên các yêu cầu thu thập được</span>
        <span class="view-pill">Mermaid: ${escapeHtml(design.validationStatus ?? 'valid')}</span>
      </div>
      
      <div class="design-meta-grid" style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 8px;">
        <div class="meta-section" style="background: rgba(255, 255, 255, 0.01); border: 1px solid var(--color-border); border-radius: 6px; padding: 12px;">
          <strong style="display: block; margin-bottom: 8px; font-size: 13px; color: var(--color-text-secondary);">Tác nhân chính (Actors)</strong>
          <div class="badge-list" style="display: flex; flex-wrap: wrap; gap: 6px;">
            ${design.mainActors.map(actor => `<span class="actor-badge" style="background: var(--pastel-blue-bg); color: var(--pastel-blue-text); padding: 4px 8px; border-radius: 4px; border: 1px solid var(--line); font-size: 11px; font-family: var(--font-mono); font-weight: bold;">${escapeHtml(actor)}</span>`).join('')}
          </div>
        </div>
        <div class="meta-section" style="background: rgba(255, 255, 255, 0.01); border: 1px solid var(--color-border); border-radius: 6px; padding: 12px;">
          <strong style="display: block; margin-bottom: 8px; font-size: 13px; color: var(--color-text-secondary);">Thực thể chính (Entities)</strong>
          <div class="badge-list" style="display: flex; flex-wrap: wrap; gap: 6px;">
            ${design.mainEntities.map(entity => `<span class="entity-badge" style="background: var(--pastel-green-bg); color: var(--pastel-green-text); padding: 4px 8px; border-radius: 4px; border: 1px solid var(--line); font-size: 11px; font-family: var(--font-mono); font-weight: bold;">${escapeHtml(entity)}</span>`).join('')}
          </div>
        </div>
      </div>
 
      <div class="diagram-section" style="background: rgba(255, 255, 255, 0.01); border: 1px solid var(--color-border); border-radius: 6px; padding: 16px;">
        <h4 style="margin: 0 0 12px 0; font-size: 13px; color: var(--color-text-secondary);">Sơ đồ Use Case sơ bộ</h4>
        <div class="mermaid-container" style="background: rgba(0,0,0,0.15); border-radius: 4px; padding: 12px; overflow-x: auto; display: flex; justify-content: center;">
          <pre class="mermaid" style="margin: 0; background: transparent; padding: 0; font-family: var(--font-mono); font-size: 12px; line-height: 1.4; color: var(--color-text);">${escapeHtml(design.useCaseMermaid)}</pre>
        </div>
      </div>
 
      <div class="diagram-section" style="background: rgba(255, 255, 255, 0.01); border: 1px solid var(--color-border); border-radius: 6px; padding: 16px;">
        <h4 style="margin: 0 0 12px 0; font-size: 13px; color: var(--color-text-secondary);">Sơ đồ lớp thực thể (ERD) sơ bộ</h4>
        <div class="mermaid-container" style="background: rgba(0,0,0,0.15); border-radius: 4px; padding: 12px; overflow-x: auto; display: flex; justify-content: center;">
          <pre class="mermaid" style="margin: 0; background: transparent; padding: 0; font-family: var(--font-mono); font-size: 12px; line-height: 1.4; color: var(--color-text);">${escapeHtml(design.erdMermaid)}</pre>
        </div>
      </div>
    </div>
  ` : `
    <div class="empty" style="padding: 24px;">
      <strong>Không có gợi ý thiết kế</strong>
      <span>Vui lòng thu thập nhiều yêu cầu hơn để AI có thể phân tích sơ đồ.</span>
    </div>
  `
 
  const isOverridden = typeof evaluation.overriddenCoverageScore === 'number'
  const displayScore = isOverridden ? evaluation.overriddenCoverageScore : evaluation.coverageScore
  const scoreFormatted = formatScore(displayScore ?? null)
  const scoreClass = formatScoreClass(displayScore ?? null)

  return `
    <section class="evaluation" data-animate="fade-up" style="--index: 0">
      <div class="score-card ${scoreClass}">
        <span>${scoreFormatted}</span>
        <small>${isOverridden ? 'Điểm GV đã duyệt' : 'Mức độ bao phủ'}</small>
        ${isOverridden ? `<small style="display:block; font-size: 10px; opacity:0.8;">(Gốc AI: ${formatScore(evaluation.coverageScore ?? null)})</small>` : ''}
      </div>
      <div class="score-grid">
        <span>Trùng khớp: <strong>${evaluation.matchedCount}</strong></span>
        <span>Một phần: <strong>${evaluation.partialCount}</strong></span>
        <span>Bỏ lỡ: <strong>${evaluation.missedCount}</strong></span>
        <span>Trích xuất: <strong>${evaluation.extractedCount}</strong></span>
        <span>Tổng yêu cầu: <strong>${total}</strong></span>
      </div>
 
      <div class="eval-tabs" style="margin-top: 24px; border-bottom: 1px solid var(--color-border); display: flex; gap: 20px; margin-bottom: 16px;">
        <button class="eval-tab-btn active" data-tab="feedback" style="background: none; border: none; border-bottom: 2px solid var(--color-primary); padding: 8px 4px; color: var(--color-text); font-weight: bold; cursor: pointer; transition: all 0.2s ease;">Đánh giá & Feedback</button>
        <button class="eval-tab-btn" data-tab="design" style="background: none; border: none; border-bottom: 2px solid transparent; padding: 8px 4px; color: var(--color-text-secondary); cursor: pointer; transition: all 0.2s ease;">Mô hình Thiết kế sơ bộ</button>
        <button class="eval-tab-btn" data-tab="matching" style="background: none; border: none; border-bottom: 2px solid transparent; padding: 8px 4px; color: var(--color-text-secondary); cursor: pointer; transition: all 0.2s ease;">So khớp Chi tiết</button>
      </div>
 
      <div class="eval-tab-content active" id="tab-feedback">
        ${evaluation.scoringPolicy ? renderScoringPolicy(evaluation.scoringPolicy) : ''}
        ${renderItCompetencyRubric()}
        ${evaluation.aiProvenance ? renderAiEvaluationProvenance(evaluation.aiProvenance) : ''}
        ${extractionReviewHtml}
        ${feedback ? `
          <div class="feedback-block" style="margin-top: 16px;">
            <small>Feedback experiment: variant ${escapeHtml(feedback.experimentVariant ?? 'A')}</small>
            ${renderFeedbackList('Điểm mạnh', feedback.strengths)}
            ${renderFeedbackList('Cần cải thiện', feedback.weaknesses)}
            ${renderFeedbackList('Gợi ý tiếp theo', feedback.suggestions)}
          </div>
          ${showSurvey ? `
            <form id="feedback-survey-form" class="feedback-block" style="margin-top:16px;">
              <strong>Đánh giá feedback này</strong>
              <label>Hữu ích (1–5)<select name="helpfulness">${[1,2,3,4,5].map(value => `<option value="${value}" ${value === 4 ? 'selected' : ''}>${value}</option>`).join('')}</select></label>
              <label>Dễ hành động (1–5)<select name="actionability">${[1,2,3,4,5].map(value => `<option value="${value}" ${value === 4 ? 'selected' : ''}>${value}</option>`).join('')}</select></label>
              <label>Không làm lộ đáp án (1–5)<select name="noAnswerLeak">${[1,2,3,4,5].map(value => `<option value="${value}" ${value === 5 ? 'selected' : ''}>${value}</option>`).join('')}</select></label>
              <textarea name="comment" maxlength="1000" rows="2" placeholder="Nhận xét thêm (không bắt buộc)"></textarea>
              <button class="ghost-button" data-action="submit-feedback-survey" type="button">Gửi đánh giá feedback</button>
            </form>
          ` : ''}
        ` : ''}
      </div>
 
      <div class="eval-tab-content" id="tab-design" style="display: none;">
        ${designTabHtml}
      </div>
 
      <div class="eval-tab-content" id="tab-matching" style="display: none;">
        ${evaluation.matches?.length ? renderRequirementReport(evaluation.matches, canOverride) : ''}
      </div>
    </section>
  `
}
 
function renderScoringPolicy(policy: ScoringPolicy) {
  return `
    <div class="policy-card">
      <div class="subsection-heading">
        <h3>Chính sách tính điểm</h3>
        <span style="font-family: var(--font-mono); font-size: 11px;">Thiết lập: ${escapeHtml(policy.preset)}</span>
      </div>
      <div class="policy-grid">
        <span>Khớp hoàn toàn: <strong>${formatThreshold(policy.exactThreshold)}</strong></span>
        <span>Khớp ngữ nghĩa: <strong>${formatThreshold(policy.semanticThreshold)}</strong></span>
        <span>Khớp một phần: <strong>${formatThreshold(policy.partialThreshold)}</strong></span>
        <span>Rubric: <strong>${policy.rubricPartialMatcher ? 'bật' : 'tắt'}</strong></span>
        <span>Mô hình nhúng: <strong>${escapeHtml(policy.embeddingModel)}</strong></span>
      </div>
    </div>
  `
}

function renderItCompetencyRubric() {
  return `
    <div class="policy-card" style="margin-top: 12px;">
      <div class="subsection-heading"><h3>Rubric năng lực IT</h3><span>Khung tham chiếu</span></div>
      <div class="policy-grid">
        <span>Yêu cầu chức năng: <strong>35%</strong></span>
        <span>Phi chức năng: <strong>20%</strong></span>
        <span>Ngoại lệ và rủi ro: <strong>15%</strong></span>
        <span>Chất lượng câu hỏi: <strong>15%</strong></span>
        <span>AAOC và ưu tiên: <strong>15%</strong></span>
      </div>
      <small>Coverage Score hiện tại đo mức độ khai thác Ground Truth. Rubric này dùng để đọc competency profile và định hướng review theo vai trò Tester, DevOps hoặc Backend Developer.</small>
    </div>
  `
}

function renderAiEvaluationProvenance(provenance: NonNullable<EvaluationResult['aiProvenance']>) {
  const extraction = provenance.extraction
  const scoring = provenance.scoring
  return `
    <div class="policy-card" style="margin-top: 12px;">
      <div class="subsection-heading">
        <h3>Dấu vết chấm AI</h3>
        <span style="font-family: var(--font-mono); font-size: 11px;">${escapeHtml(provenance.schemaVersion)}</span>
      </div>
      <div class="policy-grid">
        <span>Model yêu cầu: <strong>${escapeHtml(extraction?.requestedModel ?? 'Không có dữ liệu')}</strong></span>
        <span>Model thực tế: <strong>${escapeHtml(extraction?.effectiveModel ?? 'Không có dữ liệu')}</strong></span>
        <span>Phiên bản prompt: <strong>${escapeHtml(extraction?.promptVersion ?? 'Không có dữ liệu')}</strong></span>
        <span>Model nhúng: <strong>${escapeHtml(scoring?.embeddingModel ?? 'Không có dữ liệu')}</strong></span>
        <span>Cách so khớp: <strong>${escapeHtml(scoring?.matchingMethod ?? 'Không có dữ liệu')}</strong></span>
        <span>Thời điểm chấm: <strong>${provenance.evaluatedAt ? escapeHtml(formatTime(provenance.evaluatedAt)) : 'Không có dữ liệu'}</strong></span>
      </div>
    </div>
  `
}

function renderRequirementReport(matches: RequirementMatchReport[], canOverride = false) {
  return `
    <div class="requirement-report">
      <div class="subsection-heading">
        <h3>${canOverride ? 'Báo cáo so khớp chi tiết & Điều chỉnh điểm' : 'Báo cáo so khớp chi tiết'}</h3>
        <span>${matches.length} mục</span>
      </div>
      <form id="override-form" class="report-table">
        ${matches.map((match) => {
          const activeType = (match.overriddenMatchType || match.matchType).toLowerCase()
          return `
            <article class="requirement-row ${escapeAttribute(activeType)}" data-match-id="${escapeAttribute(match.matchId)}">
              <div class="requirement-row-header" style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                <strong style="font-family: var(--font-mono); font-size: 12px;">${escapeHtml(match.hiddenId)}</strong>
                <div style="display: flex; align-items: center; gap: 8px;">
                  ${match.overriddenMatchType ? '<span class="override-badge">Đã chỉnh</span>' : ''}
                  ${renderMatchBadge(match.overriddenMatchType || match.matchType, match.score)}
                  ${canOverride ? `<select class="override-type-select" data-match-id="${escapeAttribute(match.matchId)}" style="background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 4px; padding: 2px 6px; font-size: 12px;">
                    <option value="exact" ${activeType === 'exact' ? 'selected' : ''}>Khớp hoàn toàn</option>
                    <option value="semantic" ${activeType === 'semantic' ? 'selected' : ''}>Khớp ngữ nghĩa</option>
                    <option value="partial" ${activeType === 'partial' ? 'selected' : ''}>Khớp một phần</option>
                    <option value="missed" ${activeType === 'missed' ? 'selected' : ''}>Chưa khớp</option>
                  </select>` : ''}
                </div>
              </div>
              <p>${escapeHtml(localizeRequirementText(match.hiddenText ?? 'Yêu cầu ẩn'))}</p>
              <div class="evidence-line">
                <span>Bằng chứng từ hội thoại</span>
                <small>${escapeHtml(match.extractedText ? localizeExtractedRequirement(match.extractedText) : 'Không tìm thấy thông tin trùng khớp')}</small>
              </div>
              <div class="evidence-line">
                <span>Lý do đối soát</span>
                <small>${escapeHtml(match.reason)}</small>
              </div>
            </article>
          `
        }).join('')}
        ${canOverride ? `<div class="override-action-panel" style="margin-top: 16px; padding: 16px; background: var(--surface-raised); border-radius: 8px; border: 1px solid var(--line);">
          <h4 style="margin-bottom: 8px; color: var(--accent);">Lưu thay đổi đánh giá của Giảng viên</h4>
          <p style="font-size: 13px; color: var(--text-secondary); margin-bottom: 12px;">Hệ thống sẽ tự động tính lại <strong>Coverage Score</strong> dựa trên loại so khớp (MatchType) mới được chọn ở trên.</p>
          <div style="margin-bottom: 12px;">
            <label style="display: block; font-size: 12px; color: var(--muted-strong); margin-bottom: 4px;" for="override-comment">Lý do điều chỉnh điểm <span aria-hidden="true">*</span></label>
            <textarea id="override-comment" rows="2" required maxlength="1000" style="width: 100%; background: var(--surface); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-family: var(--font-sans); font-size: 13px;" placeholder="Nêu căn cứ điều chỉnh theo transcript hoặc rubric..."></textarea>
          </div>
          <button class="primary-button" data-action="submit-override" type="button">Lưu & Tính lại điểm số</button>
        </div>` : ''}
      </form>
    </div>
  `
}

function renderMatchBadge(matchType: string, score: number) {
  return `<span class="match-badge ${escapeAttribute(matchType.toLowerCase())}">${escapeHtml(localizeMatchType(matchType))} · ${Math.round(score * 100)}%</span>`
}

function localizeMatchType(matchType: string) {
  switch (matchType.trim().toLowerCase()) {
    case 'exact': return 'Khớp hoàn toàn'
    case 'semantic': return 'Khớp ngữ nghĩa'
    case 'partial': return 'Khớp một phần'
    case 'missed': return 'Chưa khớp'
    default: return matchType
  }
}

const IT_REQUIREMENT_VI: Record<string, string> = {
  'Authenticated customers must create an order with validated items, delivery address and payment method.': 'Khách hàng đã xác thực phải tạo được đơn hàng với sản phẩm, địa chỉ giao hàng và phương thức thanh toán hợp lệ.',
  'The API must validate stock and reserve inventory before confirming an order.': 'API phải kiểm tra tồn kho và giữ hàng trước khi xác nhận đơn hàng.',
  'Create-order requests must support idempotency to prevent duplicate orders after client retries.': 'Yêu cầu tạo đơn hàng phải hỗ trợ idempotency để tránh tạo trùng khi phía khách gửi lại yêu cầu.',
  'Only authorized users can view or update their own orders, while staff permissions are role based.': 'Chỉ người dùng được phân quyền mới được xem hoặc cập nhật đơn hàng của mình; quyền của nhân viên phải theo vai trò.',
  'The API must return consistent error codes for validation, stock conflict, payment failure and unauthorized access.': 'API phải trả mã lỗi nhất quán cho lỗi xác thực dữ liệu, xung đột tồn kho, thanh toán thất bại và truy cập trái phép.',
  'Order state changes must be audited and the read API should meet the agreed latency target.': 'Thay đổi trạng thái đơn hàng phải được lưu vết; API đọc phải đáp ứng mục tiêu độ trễ đã thỏa thuận.',
  'Every merge to the release branch must trigger build, unit tests, security checks and a versioned deployment artifact.': 'Mỗi lần gộp mã vào nhánh phát hành phải kích hoạt build, unit test, kiểm tra bảo mật và tạo artifact triển khai có phiên bản.',
  'Production deployment requires an approved change request and secrets must be read from a managed secret store.': 'Triển khai production phải có yêu cầu thay đổi được phê duyệt; secret phải được đọc từ kho secret được quản lý.',
  'The platform must support rollback to the previous stable release when health checks fail.': 'Nền tảng phải hỗ trợ rollback về bản phát hành ổn định trước đó khi health check thất bại.',
  'Database migrations must be backward compatible and have a documented recovery procedure.': 'Migration cơ sở dữ liệu phải tương thích ngược và có quy trình khôi phục được tài liệu hóa.',
  'Production services must expose monitoring, error alerts and SLO dashboards for each release.': 'Dịch vụ production phải có monitoring, cảnh báo lỗi và dashboard SLO cho mỗi lần phát hành.',
  'Deployment and rollback actions must be logged with actor, time, version and approval reference.': 'Thao tác triển khai và rollback phải được ghi log gồm người thực hiện, thời gian, phiên bản và tham chiếu phê duyệt.',
  'Tester must create a defect with title, steps to reproduce, expected result, actual result and affected build.': 'Tester phải tạo lỗi với tiêu đề, bước tái hiện, kết quả mong đợi, kết quả thực tế và bản build bị ảnh hưởng.',
  'The system must record browser, device, operating system and test environment for each defect.': 'Hệ thống phải ghi nhận trình duyệt, thiết bị, hệ điều hành và môi trường kiểm thử cho mỗi lỗi.',
  'Defects must have separate severity and priority values with a documented escalation rule for critical production issues.': 'Lỗi phải có mức độ nghiêm trọng và độ ưu tiên riêng, kèm quy tắc escalation được tài liệu hóa cho lỗi production nghiêm trọng.',
  'Assigned developers must receive notifications and the reporter must be notified when a defect changes status.': 'Developer được giao phải nhận thông báo; người báo lỗi cũng phải được thông báo khi trạng thái lỗi thay đổi.',
  'Resolved defects require retesting evidence before closure and can be reopened when the issue persists.': 'Lỗi đã xử lý cần có bằng chứng kiểm thử lại trước khi đóng và có thể mở lại nếu vấn đề còn tồn tại.',
  'All defect changes must be auditable and access must follow project roles.': 'Mọi thay đổi của lỗi phải có thể kiểm tra/audit và quyền truy cập phải theo vai trò trong dự án.',
}

function localizeRequirementText(text: string) {
  return IT_REQUIREMENT_VI[text] ?? text
}

const EXTRACTED_REQUIREMENT_VI: Record<string, string> = {
  'system must block tao phieu loi when thieu thong tin bat buoc tieu de cac buoc tai hien ket qua mong doi ket qua thuc te thong tin build.': 'Hệ thống phải chặn tạo phiếu lỗi khi thiếu thông tin bắt buộc: tiêu đề, các bước tái hiện, kết quả mong đợi, kết quả thực tế hoặc thông tin bản build.',
  'system must kiem soat du lieu dau vao when khi tao phieu loi.': 'Hệ thống phải kiểm soát dữ liệu đầu vào khi tạo phiếu lỗi.',
  'user must gan muc do uu tien when khi loi duoc xac nhan.': 'Người dùng phải gán mức độ ưu tiên khi lỗi được xác nhận.',
  'system must chuyen trang thai phieu loi when loi duoc tao.': 'Hệ thống phải chuyển trạng thái phiếu lỗi khi lỗi được tạo.',
  'system must ghi lai nhat ky thay doi trang thai loi when moi thay doi trang thai.': 'Hệ thống phải ghi lại nhật ký thay đổi trạng thái lỗi sau mỗi lần cập nhật.',
  'system must gui thong bao when doi ngu phat trien cap nhat trang thai loi la da sua xong.': 'Hệ thống phải gửi thông báo khi đội ngũ phát triển cập nhật trạng thái lỗi là đã sửa xong.',
  'tester must tao phieu ghi nhan loi when phat hien loi.': 'Người kiểm thử phải tạo phiếu ghi nhận lỗi khi phát hiện lỗi.',
}

function localizeExtractedRequirement(text: string) {
  const source = text.trim()
  const exact = EXTRACTED_REQUIREMENT_VI[source.toLowerCase()]
  if (exact) return exact

  const normalized = source
    .replace(/\bsystem must\b/gi, 'Hệ thống phải')
    .replace(/\btester must\b/gi, 'Người kiểm thử phải')
    .replace(/\buser must\b/gi, 'Người dùng phải')
    .replace(/\bwhen khi\b/gi, 'khi')
    .replace(/\bwhen\b/gi, 'khi')
    .replace(/\bblock\b/gi, 'chặn')
    .replace(/\btao phieu loi\b/gi, 'tạo phiếu lỗi')
    .replace(/\bphieu ghi nhan loi\b/gi, 'phiếu ghi nhận lỗi')
    .replace(/\bkiem soat du lieu dau vao\b/gi, 'kiểm soát dữ liệu đầu vào')
    .replace(/\bchuyen trang thai\b/gi, 'chuyển trạng thái')
    .replace(/\bghi lai nhat ky\b/gi, 'ghi lại nhật ký')
    .replace(/\bgui thong bao\b/gi, 'gửi thông báo')
  return normalized.replace(/^./, character => character.toUpperCase())
}

function localizeRequirementCategory(category: string) {
  const labels: Record<string, string> = {
    Functional: 'Chức năng',
    NonFunctional: 'Phi chức năng',
    BusinessRule: 'Quy tắc nghiệp vụ',
    Constraint: 'Ràng buộc',
  }
  return labels[category] ?? category
}

function localizePersonaDifficulty(difficulty: string) {
  const labels: Record<string, string> = { Easy: 'Dễ', Medium: 'Trung bình', Hard: 'Khó' }
  return labels[difficulty] ?? difficulty
}

const IT_REVEAL_CONDITION_VI: Record<string, string> = {
  'Ask how a customer creates an order.': 'Hỏi cách khách hàng tạo đơn hàng.',
  'Ask what happens when stock changes during checkout.': 'Hỏi điều gì xảy ra khi tồn kho thay đổi trong lúc thanh toán.',
  'Ask how the API handles network retries.': 'Hỏi API xử lý việc gửi lại yêu cầu do lỗi mạng như thế nào.',
  'Ask who can access an order.': 'Hỏi ai được phép truy cập đơn hàng.',
  'Ask how API errors are returned to clients.': 'Hỏi API trả lỗi cho phía khách như thế nào.',
  'Ask about non-functional requirements and traceability.': 'Hỏi về yêu cầu phi chức năng và khả năng truy vết.',
  'Ask what happens when code is merged for release.': 'Hỏi điều gì xảy ra khi mã được gộp để phát hành.',
  'Ask about production authorization and credentials.': 'Hỏi về quyền phê duyệt production và thông tin xác thực.',
  'Ask what happens when a deployment is unhealthy.': 'Hỏi điều gì xảy ra khi một bản triển khai không đạt trạng thái khỏe mạnh.',
  'Ask how schema changes are handled during rollback.': 'Hỏi cách xử lý thay đổi schema khi rollback.',
  'Ask how the team detects and measures production problems.': 'Hỏi đội ngũ phát hiện và đo lường vấn đề production như thế nào.',
  'Ask about release accountability.': 'Hỏi về trách nhiệm giải trình khi phát hành.',
  'Ask what information is required when reporting a defect.': 'Hỏi cần những thông tin gì khi báo cáo lỗi.',
  'Ask how the team reproduces a reported defect.': 'Hỏi đội ngũ tái hiện lỗi đã báo cáo như thế nào.',
  'Ask how critical defects are classified and escalated.': 'Hỏi cách phân loại và escalation các lỗi production nghiêm trọng.',
  'Ask how defect ownership and status changes are communicated.': 'Hỏi việc phân công xử lý lỗi và thay đổi trạng thái được thông báo như thế nào.',
  'Ask what happens after a developer marks a defect resolved.': 'Hỏi điều gì xảy ra sau khi developer đánh dấu lỗi đã xử lý.',
  'Ask about accountability and access control.': 'Hỏi về khả năng giải trình và kiểm soát quyền truy cập.',
}

function localizeRevealCondition(condition: string) {
  return IT_REVEAL_CONDITION_VI[condition] ?? condition
}

function renderFeedbackList(title: string, items: string[]) {
  if (items.length === 0) return ''
  return `
    <div>
      <h3 style="margin-bottom: 6px;">${escapeHtml(title)}</h3>
      <ul>${items.map((item) => `<li>${escapeHtml(item)}</li>`).join('')}</ul>
    </div>
  `
}

function renderEmpty(title: string, detail?: string) {
  return `
    <div class="empty">
      <strong>${escapeHtml(title)}</strong>
      ${detail ? `<span>${escapeHtml(detail)}</span>` : ''}
    </div>
  `
}

export function renderAdminDashboard(state: AppState) {
  const admin = state.adminState
  if (!admin) {
    return `
      <section class="admin-dashboard">
        <div class="placeholder">
          <h2>Đang tải dữ liệu Quản trị hệ thống...</h2>
        </div>
      </section>
    `
  }

  const isOverviewTab = admin.activeTab === 'overview'
  const isUsersTab = admin.activeTab === 'users'
  const isScenariosTab = admin.activeTab === 'scenarios'
  const activeTab = isOverviewTab ? 'overview' : isUsersTab ? 'users' : 'scenarios'

  return `
    <section class="admin-dashboard" data-animate="fade-up">
      <div class="admin-header" style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px;">
        <div>
          <p class="section-kicker">Bảng điều khiển Admin</p>
          <h2 style="font-size: 24px; font-weight: 700; color: var(--text-primary);">Quản trị Hệ thống & Nạp Tri thức AI</h2>
        </div>
        <div class="admin-nav-tabs" role="tablist" aria-label="Các chức năng quản trị" style="display: flex; gap: 8px; background: var(--surface); padding: 4px; border-radius: 8px; border: 1px solid var(--line);">
          <button id="admin-tab-overview" role="tab" aria-selected="${isOverviewTab}" aria-controls="admin-tabpanel-overview" class="ghost-button ${isOverviewTab ? 'active' : ''}" data-action="set-admin-tab" data-tab="overview" type="button" style="${isOverviewTab ? 'background: var(--accent-indigo); color: #fff;' : ''}">Thống kê Analytics</button>
          <button id="admin-tab-users" role="tab" aria-selected="${isUsersTab}" aria-controls="admin-tabpanel-users" class="ghost-button ${isUsersTab ? 'active' : ''}" data-action="set-admin-tab" data-tab="users" type="button" style="${isUsersTab ? 'background: var(--accent-indigo); color: #fff;' : ''}">Quản lý Người dùng (CRUD)</button>
          <button id="admin-tab-scenarios" role="tab" aria-selected="${isScenariosTab}" aria-controls="admin-tabpanel-scenarios" class="ghost-button ${isScenariosTab ? 'active' : ''}" data-action="set-admin-tab" data-tab="scenarios" type="button" style="${isScenariosTab ? 'background: var(--accent-indigo); color: #fff;' : ''}">Cào/Nạp Kịch Bản (AI)</button>
        </div>
      </div>

      <div id="admin-tabpanel-${activeTab}" role="tabpanel" aria-labelledby="admin-tab-${activeTab}" tabindex="0">
        ${isOverviewTab ? renderAdminOverviewSection(admin) :
          isUsersTab ? renderAdminUserManagementSection(admin) :
          renderAdminScenarioSection(state)}
      </div>
    </section>
  `
}

function renderIngestionHistory(admin: AdminState | null) {
  const jobs = admin?.ingestionJobs ?? []
  return `
    <section class="ingestion-history panel" aria-labelledby="ingestion-history-title">
      <div class="ingestion-history-heading">
        <div>
          <p class="section-kicker">Theo dõi tiến trình</p>
          <h3 id="ingestion-history-title">Lịch sử nạp tri thức</h3>
        </div>
        <button class="ghost-button" data-action="refresh-ingestion-history" type="button" aria-label="Làm mới lịch sử nạp tri thức">Làm mới</button>
      </div>
      ${jobs.length === 0 ? `
        <p class="ingestion-history-empty">Chưa có job nào. Sau khi nạp URL hoặc video/audio, job sẽ hiển thị ở đây kể cả khi bạn tải lại trang.</p>
      ` : `
        <ul class="ingestion-job-list">
          ${jobs.map(job => `
            <li class="ingestion-job-item">
              <div class="ingestion-job-main">
                <strong>${escapeHtml(job.sourceLabel ?? 'Nguồn nạp tri thức')}</strong>
                <span class="ingestion-job-meta"><span class="ingestion-job-status">${escapeHtml(job.status)}</span>${job.attempts} lượt chạy${job.updatedAt ? ` · ${escapeHtml(formatTime(job.updatedAt))}` : ''}</span>
                ${job.errorCode ? `<small>Mã lỗi: ${escapeHtml(job.errorCode)}</small>` : ''}
              </div>
              ${job.status === 'AwaitingReview' && job.hasDraft ? `<button class="primary-button" data-action="review-ingestion-job" data-job-id="${escapeAttribute(job.jobId)}" type="button">Mở bản nháp</button>` : ''}
            </li>
          `).join('')}
        </ul>
      `}
    </section>
  `
}

function renderAdminScenarioSection(state: AppState) {
  const activeScenarios = state.adminState?.scenarioStats ?? []
  return `
    <section class="panel" aria-labelledby="published-scenario-edit-title" style="padding: 20px; border: 1px solid var(--line); background: var(--surface); border-radius: var(--radius-lg); box-shadow: var(--shadow-subtle); margin-bottom: 24px;">
      <div style="display: grid; gap: 8px;">
        <div>
          <p class="section-kicker">Quản lý phiên bản</p>
          <h3 id="published-scenario-edit-title" style="margin: 0;">Sửa scenario hiện tại</h3>
          <p style="margin: 6px 0 0; color: var(--text-secondary); font-size: 13px;">Tải scenario đang hoạt động thành bản nháp để chỉnh sửa câu hỏi, nội dung tiết lộ và yêu cầu. Khi publish, hệ thống tạo phiên bản mới và giữ nguyên lịch sử các phiên cũ.</p>
        </div>
        <div style="display: flex; gap: 10px; align-items: end; flex-wrap: wrap;">
          <label style="display: grid; gap: 6px; min-width: min(100%, 360px); flex: 1;">
            <span style="font-size: 13px; color: var(--text-secondary);">Scenario đang hoạt động</span>
            <select id="admin-published-scenario-select" ${state.busy || activeScenarios.length === 0 ? 'disabled' : ''}>
              <option value="">Chọn scenario để sửa</option>
              ${activeScenarios.map(scenario => `<option value="${escapeAttribute(scenario.scenarioId)}">${escapeHtml(scenario.scenarioTitle)} · ${scenario.sessionCount} phiên</option>`).join('')}
            </select>
          </label>
          <button class="primary-button" data-action="admin-edit-published-scenario" type="button" ${state.busy || activeScenarios.length === 0 ? 'disabled' : ''}>Tải để chỉnh sửa</button>
        </div>
        ${activeScenarios.length === 0 ? '<small style="color: var(--muted);">Chưa có scenario hoạt động để chỉnh sửa.</small>' : ''}
      </div>
    </section>
    ${renderIngestionHistory(state.adminState)}
    ${renderAdminScenarioPreview(state)}
    <div class="admin-scenarios-grid" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 24px;">
      
      <!-- Card 1: Crawl BA Document -->
      <div class="card glass-panel" style="padding: 24px; display: flex; flex-direction: column; gap: 16px; border: 1px solid var(--line); background: var(--surface); border-radius: var(--radius-lg); box-shadow: var(--shadow-subtle);">
        <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 8px;">
          <div style="background: rgba(217, 119, 87, 0.1); padding: 8px; border-radius: 8px; color: var(--accent-indigo);">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"></path><polyline points="3.27 6.96 12 12.01 20.73 6.96"></polyline><line x1="12" y1="22.08" x2="12" y2="12"></line></svg>
          </div>
          <div>
            <h3 style="font-size: 18px; font-weight: 600; color: var(--text-primary); margin: 0;">Cào & Trích xuất Tài liệu BA</h3>
            <p style="font-size: 13px; color: var(--text-secondary); margin: 4px 0 0 0;">Tự động phân tích PRD/SRS từ URL thành kịch bản phỏng vấn</p>
          </div>
        </div>
        
        <div class="form-group" style="display: flex; flex-direction: column; gap: 8px;">
          <label for="admin-crawl-url-input" style="font-size: 13px; color: var(--text-secondary); font-weight: 500;">Đường dẫn URL chứa tài liệu:</label>
          <textarea id="admin-crawl-url-input" rows="4" placeholder="Mỗi dòng một URL (tối đa 10 nguồn)..." style="background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 10px 12px; font-size: 13px; outline: none; width: 100%; resize: vertical;"></textarea>
        </div>

        <div class="form-group admin-ingestion-field" style="display: flex; flex-direction: column; gap: 8px;">
          <label for="admin-crawl-model-select" style="font-size: 13px; color: var(--text-secondary); font-weight: 500;">Mô hình AI xử lý:</label>
          <select id="admin-crawl-model-select" class="admin-ingestion-select">
            <option value="gemini-2.5-flash">Gemini 2.5 Flash (Khuyên dùng - Structured Output)</option>
            <option value="gemini-2.5-flash-lite">Gemini 2.5 Flash Lite</option>
            <option value="gemini-3-flash-preview">Gemini 3 Flash Preview</option>
            <option value="gemini-3.1-flash-lite">Gemini 3.1 Flash Lite</option>
            <option value="gemini-3.5-flash">Gemini 3.5 Flash</option>
            <option value="gemini-3.5-flash-lite">Gemini 3.5 Flash Lite</option>
            <option value="gemini-3.6-flash">Gemini 3.6 Flash</option>
            <option value="gemini-3.7-flash">Gemini 3.7 Flash</option>
            <option value="llama-3.3-70b-versatile">Llama 3.3 70B (Groq Fallback - JSON mode)</option>
            <option value="llama-3.1-8b-instant">Llama 3.1 8B (Groq Fallback - Nhanh)</option>
            <option value="deepseek-chat">DeepSeek Chat</option>
            <option value="deepseek-v4flash">DeepSeek v4 Flash</option>
            <option value="deepseek-v4pro">DeepSeek v4 Pro</option>
            <option value="mimo-v2.5pro">Mimo v2.5 Pro</option>
            <option value="openrouter/meta-llama/llama-3.3-70b-instruct">Llama 3.3 70B (OpenRouter)</option>
            <option value="openrouter/deepseek/deepseek-chat">DeepSeek Chat (OpenRouter)</option>
            <option value="openrouter/google/gemini-2.5-flash">Gemini 2.5 Flash (OpenRouter)</option>
          </select>
        </div>

        <button class="primary-button" data-action="admin-crawl" type="button" ${state.busy ? 'disabled' : ''} style="margin-top: 12px; display: flex; align-items: center; justify-content: center; gap: 8px;">
          ${state.busy ? '<span class="spinner-mini"></span> Đang xử lý...' : 'Tạo bản preview từ tài liệu'}
        </button>
      </div>

      <!-- Card 2: Video Knowledge Upload -->
      <div class="card glass-panel" style="padding: 24px; display: flex; flex-direction: column; gap: 16px; border: 1px solid var(--line); background: var(--surface); border-radius: var(--radius-lg); box-shadow: var(--shadow-subtle);">
        <div style="display: flex; align-items: center; gap: 12px; margin-bottom: 8px;">
          <div style="background: var(--pastel-yellow-bg); padding: 8px; border-radius: 8px; color: var(--pastel-yellow-text);">
            <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polygon points="23 7 16 12 23 17 23 7"></polygon><rect x="1" y="5" width="15" height="14" rx="2" ry="2"></rect></svg>
          </div>
          <div>
            <h3 style="font-size: 18px; font-weight: 600; color: var(--text-primary); margin: 0;">Nạp Tri thức Nghiệp vụ từ Video</h3>
            <p style="font-size: 13px; color: var(--text-secondary); margin: 4px 0 0 0;">Tải video/audio cuộc họp trực tiếp lên kho riêng để tạo bản nháp scenario</p>
          </div>
        </div>

        <div class="form-group" style="display: flex; flex-direction: column; gap: 8px;">
          <label for="admin-video-path-input" style="font-size: 13px; color: var(--text-secondary); font-weight: 500;">Tệp video/audio cuộc họp (MP4, MOV, WebM, MP3, WAV, M4A, AAC hoặc OGG; tối đa 250 MB):</label>
          <input id="admin-video-path-input" type="file" accept="audio/mpeg,audio/wav,audio/x-wav,audio/mp4,audio/aac,audio/ogg,audio/webm,video/mp4,video/webm,video/quicktime" style="background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 10px 12px; font-size: 13px; outline: none; width: 100%;" />
        </div>

        <div class="form-group admin-ingestion-field" style="display: flex; flex-direction: column; gap: 8px;">
          <label for="admin-video-model-select" style="font-size: 13px; color: var(--text-secondary); font-weight: 500;">Mô hình AI xử lý:</label>
          <select id="admin-video-model-select" class="admin-ingestion-select">
            <option value="gemini-2.5-flash">Gemini 2.5 Flash (Khuyên dùng - Multimodal)</option>
            <option value="gemini-3.1-flash-lite">Gemini 3.1 Flash Lite (Hạn mức cao)</option>
            <option value="gemini-3.5-flash-lite">Gemini 3.5 Flash Lite (Hạn mức cao)</option>
            <option value="gemini-2.5-flash-lite">Gemini 2.5 Flash Lite</option>
            <option value="gemini-3-flash-preview">Gemini 3 Flash Preview</option>
            <option value="gemini-3.5-flash">Gemini 3.5 Flash</option>
            <option value="gemini-3.6-flash">Gemini 3.6 Flash</option>
            <option value="gemini-3.7-flash">Gemini 3.7 Flash</option>
          </select>
        </div>

        <div style="background: rgba(217, 119, 87, 0.08); border: 1px solid rgba(217, 119, 87, 0.2); border-radius: 6px; padding: 10px 12px; display: flex; gap: 8px; align-items: flex-start;">
          <span style="color: var(--accent-indigo); font-weight: bold; font-size: 14px;">⚠️</span>
          <span style="font-size: 11px; color: var(--text-secondary); line-height: 1.4;">Chỉ quản trị viên có thể nạp nguồn. Worker trích xuất audio từ video rồi gửi dữ liệu đã chọn đến Gemini.</span>
        </div>

        <button class="primary-button" data-action="admin-video" type="button" ${state.busy ? 'disabled' : ''} style="margin-top: 4px; border: none; display: flex; align-items: center; justify-content: center; gap: 8px;">
          ${state.busy ? '<span class="spinner-mini"></span> Đang xử lý...' : 'Tạo bản preview từ video/audio'}
        </button>
      </div>

    </div>
  `
}

function renderAdminScenarioPreview(state: AppState) {
  const draft = state.adminState?.scenarioDraft
  if (!draft) return ''

  const source = state.adminState?.scenarioDraftSource
  const personaTemplates = state.adminState?.personaTemplates ?? []
  const selectedPersonaTemplateKeys = new Set(draft.persona_template_keys ?? [])
  const glossary = draft.normalization_glossary ?? {
    actor: {}, action: {}, object: {}, condition: {},
  }
  const requirementCards = draft.requirements.map((requirement, index) => `
    <article class="scenario-draft-requirement" data-requirement-row="${index}">
      <div class="scenario-draft-requirement-header">
        <div>
          <span class="scenario-draft-index">Yêu cầu ${index + 1}</span>
          <strong>${escapeHtml(requirement.id || `R${index + 1}`)}</strong>
        </div>
        <button class="ghost-button danger-button" data-draft-remove-index="${index}" type="button"
          ${draft.requirements.length <= 1 || state.busy ? 'disabled' : ''}>Xóa yêu cầu</button>
      </div>

      <div class="scenario-draft-grid compact">
        <label>
          <span>Mã yêu cầu *</span>
          <input data-draft-field="id" value="${escapeAttribute(requirement.id)}" maxlength="50" />
        </label>
        <label>
          <span>Gate (0–4) *</span>
          <input data-draft-field="gate" type="number" min="0" max="4" step="1"
            value="${requirement.gate}" />
        </label>
        <label class="span-2">
          <span>Nội dung yêu cầu *</span>
          <textarea data-draft-field="text" rows="2">${escapeHtml(requirement.text)}</textarea>
        </label>
        <label>
          <span>Actor *</span>
          <input data-draft-field="actor" value="${escapeAttribute(requirement.actor ?? '')}" />
        </label>
        <label>
          <span>Action *</span>
          <input data-draft-field="action" value="${escapeAttribute(requirement.action ?? '')}" />
        </label>
        <label>
          <span>Object *</span>
          <input data-draft-field="object" value="${escapeAttribute(requirement.object ?? '')}" />
        </label>
        <label>
          <span>Condition</span>
          <input data-draft-field="condition" value="${escapeAttribute(requirement.condition ?? '')}" />
        </label>
        <label>
          <span>Type *</span>
          <select data-draft-field="type">
            ${['FR', 'NFR', 'BR'].map(value => `<option value="${value}" ${requirement.type === value ? 'selected' : ''}>${value}</option>`).join('')}
          </select>
        </label>
        <label>
          <span>Priority *</span>
          <select data-draft-field="priority">
            ${['high', 'medium', 'low'].map(value => `<option value="${value}" ${requirement.priority === value ? 'selected' : ''}>${value}</option>`).join('')}
          </select>
        </label>
        <label>
          <span>Từ khóa (phân cách bằng dấu phẩy)</span>
          <input data-draft-field="keywords"
            value="${escapeAttribute((requirement.keywords ?? []).join(', '))}" />
        </label>
        <label>
          <span>Loại câu hỏi</span>
          <input data-draft-field="question_types"
            value="${escapeAttribute((requirement.question_types ?? []).join(', '))}" />
        </label>
        <label class="span-2">
          <span>Điều kiện tiết lộ</span>
          <input data-draft-field="reveal_condition"
            value="${escapeAttribute(requirement.reveal_condition)}" />
        </label>
        <label>
          <span>Độ khó</span>
          <select data-draft-field="reveal_difficulty">
            <option value="Easy" ${requirement.reveal_difficulty === 'Easy' ? 'selected' : ''}>Easy</option>
            <option value="Medium" ${requirement.reveal_difficulty === 'Medium' ? 'selected' : ''}>Medium</option>
            <option value="Hard" ${requirement.reveal_difficulty === 'Hard' ? 'selected' : ''}>Hard</option>
          </select>
        </label>
        <label>
          <span>Phụ thuộc các mã</span>
          <input data-draft-field="requires"
            value="${escapeAttribute((requirement.requires ?? []).join(', '))}"
            placeholder="Ví dụ: R1, R2" />
        </label>
      </div>
    </article>
  `).join('')

  return `
    <form id="admin-scenario-preview-form" class="scenario-draft-panel" novalidate>
      <div class="scenario-draft-heading">
        <div>
          <p class="section-kicker">Bản nháp chưa publish</p>
          <h3>Kiểm tra và chỉnh sửa scenario</h3>
          <p>AI chỉ tạo bản nháp. Dữ liệu dưới đây chưa được đưa vào danh sách kịch bản.</p>
          ${source ? `<small>Nguồn: ${escapeHtml(source)}</small>` : ''}
        </div>
        <span class="scenario-draft-count">${draft.requirements.length} yêu cầu</span>
      </div>

      <div class="scenario-draft-grid">
        <label>
          <span>Mã scenario *</span>
          <input data-draft-field="scenario_key" value="${escapeAttribute(draft.scenario_key)}"
            maxlength="100" pattern="[a-z0-9]+(?:_[a-z0-9]+)*" />
          <small>Chữ thường, số và dấu gạch dưới.</small>
        </label>
        <label>
          <span>Tên scenario *</span>
          <input data-draft-field="scenario_title"
            value="${escapeAttribute(draft.scenario_title)}" maxlength="200" />
        </label>
        <label class="span-2">
          <span>Bối cảnh stakeholder *</span>
          <textarea data-draft-field="context" rows="4">${escapeHtml(draft.context)}</textarea>
        </label>
        <label>
          <span>Từ khóa chung</span>
          <input data-draft-field="general_keywords"
            value="${escapeAttribute((draft.general_keywords ?? []).join(', '))}" />
        </label>
        <label>
          <span>Số yêu cầu mới tối đa mỗi lượt</span>
          <input data-draft-field="max_new_reveals_per_turn" type="number" min="1" max="12"
            step="1" value="${draft.max_new_reveals_per_turn}" />
        </label>
      </div>

      <section class="scenario-draft-advanced" aria-labelledby="persona-template-title">
        <h4 id="persona-template-title">Persona templates tái sử dụng</h4>
        <p>Chọn 2–3 template để tạo snapshot cho mỗi stakeholder. Thay đổi template sau này không ảnh hưởng phiên phỏng vấn đã phát hành.</p>
        ${personaTemplates.length === 0 ? `
          <p><small>Chưa tải được thư viện persona; hệ thống sẽ dùng bộ mặc định Collaborative và Challenging.</small></p>
        ` : `
          <div class="scenario-draft-grid compact">
            ${personaTemplates.map(template => `
              <label>
                <span><input type="checkbox" data-persona-template-key value="${escapeAttribute(template.templateKey)}"
                  ${selectedPersonaTemplateKeys.size === 0
                    ? template.isSystemDefault ? 'checked' : ''
                    : selectedPersonaTemplateKeys.has(template.templateKey) ? 'checked' : ''} />
                  ${escapeHtml(formatPersonaText(template.label))}</span>
                <small>Trao đổi: ${escapeHtml(formatPersonaText(template.communicationStyle))} · Am hiểu: ${escapeHtml(formatPersonaText(template.knowledgeLevel))} · Độ khó: ${escapeHtml(formatPersonaText(template.difficulty))}</small>
              </label>
            `).join('')}
          </div>
        `}
      </section>

      <section class="scenario-draft-advanced" aria-labelledby="ground-truth-review-title">
        <h4 id="ground-truth-review-title">Review Ground Truth</h4>
        <p>Ghi lại kiểm tra của admin trước khi publish. Thông tin này được lưu vào audit trail của phiên bản scenario.</p>
        <label>
          <span>Ghi chú review (tùy chọn)</span>
          <textarea data-draft-field="review_notes" rows="3" maxlength="1000" placeholder="Ví dụ: Đã kiểm tra nguồn, AAOC, type/priority, gate và điều kiện tiết lộ.">${escapeHtml(draft.review_notes ?? '')}</textarea>
        </label>
      </section>

      <details class="scenario-draft-advanced">
        <summary>Cấu hình Gate nâng cao</summary>
        <div class="scenario-draft-grid">
          <label>
            <span>Nhóm từ khóa theo Gate (JSON)</span>
            <textarea data-draft-field="gate_keyword_groups" rows="7">${escapeHtml(JSON.stringify(draft.gate_keyword_groups, null, 2))}</textarea>
          </label>
          <label>
            <span>Ánh xạ loại câu hỏi → Gate (JSON)</span>
            <textarea data-draft-field="question_type_gate_map" rows="7">${escapeHtml(JSON.stringify(draft.question_type_gate_map, null, 2))}</textarea>
          </label>
          <label>
            <span>Glossary chuẩn hóa theo scenario (JSON)</span>
            <textarea data-draft-field="normalization_glossary" rows="7" spellcheck="false">${escapeHtml(JSON.stringify(glossary, null, 2))}</textarea>
            <small>Ví dụ: { "action": { "reserve": "book" }, "object": { "desk pass": "study desk" } }.</small>
          </label>
        </div>
      </details>

      <div class="scenario-draft-list-heading">
        <div>
          <h4>Danh sách yêu cầu ẩn</h4>
          <p>Kiểm tra AAOC, Type/Priority, Gate, điều kiện tiết lộ và quan hệ phụ thuộc.</p>
        </div>
        <button id="admin-add-requirement" class="ghost-button" type="button"
          ${state.busy ? 'disabled' : ''}>+ Thêm yêu cầu</button>
      </div>

      <div class="scenario-draft-requirements">${requirementCards}</div>

      <div class="scenario-draft-actions">
        <button class="ghost-button" data-action="admin-cancel-preview" type="button"
          ${state.busy ? 'disabled' : ''}>Hủy bản nháp</button>
        <button class="primary-button" data-action="admin-publish-scenario" type="button"
          ${state.busy ? 'disabled' : ''}>
          ${state.busy ? '<span class="spinner-mini"></span> Đang publish...' : 'Xác nhận & Publish'}
        </button>
      </div>
    </form>
  `
}
function renderAdminOverviewSection(admin: AdminState) {
  const overview = admin.overview
  return `
    <div class="admin-overview-stack" style="display: grid; gap: 24px;">
      <!-- Metrics Overview Cards -->
      <div class="admin-metrics-grid" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 16px;">
        <div class="admin-metric-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); box-shadow: var(--shadow-subtle);">
          <span style="font-size: 12px; color: var(--text-secondary); display: block;">Tổng số phiên</span>
          <strong style="font-size: 28px; color: var(--accent-indigo); display: block; margin-top: 4px;">${overview?.totalSessions ?? 0}</strong>
          <small style="font-size: 11px; color: var(--muted);">${overview?.completedSessions ?? 0} hoàn thành · ${overview?.activeSessions ?? 0} đang mở</small>
        </div>
        <div class="admin-metric-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); box-shadow: var(--shadow-subtle);">
          <span style="font-size: 12px; color: var(--text-secondary); display: block;">Sinh viên</span>
          <strong style="font-size: 28px; color: var(--pastel-blue-text); display: block; margin-top: 4px;">${overview?.totalStudents ?? 0}</strong>
          <small style="font-size: 11px; color: var(--muted);">Tài khoản sinh viên</small>
        </div>
        <div class="admin-metric-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); box-shadow: var(--shadow-subtle);">
          <span style="font-size: 12px; color: var(--text-secondary); display: block;">Kịch bản nghiệp vụ</span>
          <strong style="font-size: 28px; color: var(--pastel-green-text); display: block; margin-top: 4px;">${overview?.totalScenarios ?? 0}</strong>
          <small style="font-size: 11px; color: var(--muted);">Kịch bản sẵn sàng</small>
        </div>
        <div class="admin-metric-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); box-shadow: var(--shadow-subtle);">
          <span style="font-size: 12px; color: var(--text-secondary); display: block;">Coverage Score TB</span>
          <strong style="font-size: 28px; color: var(--pastel-yellow-text); display: block; margin-top: 4px;">${overview?.averageCoverage ?? 0}%</strong>
          <small style="font-size: 11px; color: var(--muted);">Trung bình toàn hệ thống</small>
        </div>
      </div>

      <div class="top-students-card" style="background: var(--surface); padding: 20px; border-radius: 10px; border: 1px solid var(--line);">
        <h3>Thử nghiệm A/B Learning Feedback</h3>
        <div class="score-grid">
          ${(admin.feedbackExperiment?.variants ?? []).map(item => `
            <span>Variant ${escapeHtml(item.variant)} · n=${item.sampleSize}/${item.target} · còn ${item.remaining}<br/>
              <strong>Hữu ích ${item.helpfulness.toFixed(2)} · Hành động ${item.actionability.toFixed(2)} · Không rò ${item.noAnswerLeak.toFixed(2)}</strong>
            </span>
          `).join('') || '<span>Chưa có phản hồi khảo sát.</span>'}
        </div>
        ${admin.feedbackExperiment?.warning ? `<small>${escapeHtml(admin.feedbackExperiment.warning)}</small>` : ''}
      </div>

      <div class="top-students-card" style="background: var(--surface); padding: 20px; border-radius: 10px; border: 1px solid var(--line); box-shadow: var(--shadow-subtle);">
        <h3 style="margin-bottom: 6px; font-size: 16px; color: var(--text-primary);">Rà soát nhất quán điều chỉnh điểm</h3>
        <p style="margin: 0 0 12px; color: var(--text-secondary); font-size: 13px;">So sánh điểm AI gốc với điểm sau review. ${escapeHtml(admin.gradingReview?.methodology.disclaimer ?? 'Chỉ báo thống kê, không phải kết luận thiên vị.')}</p>
        <div style="overflow-x: auto;">
          <table style="width: 100%; border-collapse: collapse; font-size: 13px; text-align: left;">
            <thead><tr style="border-bottom: 1px solid var(--line); color: var(--text-secondary);"><th style="padding: 10px;">Người review</th><th style="padding: 10px; text-align: center;">Số lần</th><th style="padding: 10px; text-align: center;">AI TB</th><th style="padding: 10px; text-align: center;">Sau review TB</th><th style="padding: 10px; text-align: center;">Chỉnh TB</th><th style="padding: 10px; text-align: center;">Trạng thái</th></tr></thead>
            <tbody>
              ${(admin.gradingReview?.reviewers ?? []).length === 0 ? '<tr><td colspan="6" style="padding: 20px; text-align: center; color: var(--muted);">Chưa có dữ liệu điều chỉnh điểm để phân tích.</td></tr>' : (admin.gradingReview?.reviewers ?? []).map(item => `
                <tr style="border-bottom: 1px solid var(--line-subtle);"><td style="padding: 10px; font-weight: 600; color: var(--text-primary);">${escapeHtml(item.lecturerName)}</td><td style="padding: 10px; text-align: center;">${item.reviewCount}</td><td style="padding: 10px; text-align: center;">${item.averageAiScore}%</td><td style="padding: 10px; text-align: center;">${item.averageFinalScore}%</td><td style="padding: 10px; text-align: center; font-weight: 600; color: ${item.averageAdjustment > 0 ? 'var(--pastel-green-text)' : item.averageAdjustment < 0 ? 'var(--accent-rose)' : 'var(--text-secondary)'};">${item.averageAdjustment > 0 ? '+' : ''}${item.averageAdjustment} điểm</td><td style="padding: 10px; text-align: center;">${item.requiresReview ? '<span style="color: var(--accent-rose); font-weight: 700;">Cần rà soát</span>' : item.hasSufficientData ? '<span style="color: var(--pastel-green-text);">Trong ngưỡng</span>' : '<span style="color: var(--text-secondary);">Chưa đủ dữ liệu</span>'}</td></tr>`).join('')}
            </tbody>
          </table>
        </div>
        <small style="display:block; margin-top: 10px; color: var(--muted);">Ngưỡng: tối thiểu ${admin.gradingReview?.methodology.minimumReviews ?? 5} lần review; cờ khi chỉnh trung bình từ ±${admin.gradingReview?.methodology.meanAdjustmentThreshold ?? 15} điểm hoặc ít nhất một nửa số lần lệch từ ${admin.gradingReview?.methodology.highAdjustmentThreshold ?? 25} điểm.</small>
        ${(admin.gradingReview?.cohorts ?? []).length > 0 ? `<small style="display:block; margin-top: 6px; color: var(--muted);">Đã phân tách theo cohort scenario/độ khó: ${(admin.gradingReview?.cohorts ?? []).filter(item => item.hasSufficientData).length}/${(admin.gradingReview?.cohorts ?? []).length} cohort đủ dữ liệu; ${(admin.gradingReview?.cohorts ?? []).filter(item => item.requiresReview).length} cohort cần rà soát.</small>` : ''}
      </div>
      <!-- Charts 2x2 Grid -->
      <div class="admin-charts-grid" style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">
        <div class="chart-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); height: 320px; box-shadow: var(--shadow-subtle);">
          <canvas id="chart-coverage-dist"></canvas>
        </div>
        <div class="chart-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); height: 320px; box-shadow: var(--shadow-subtle);">
          <canvas id="chart-sessions-time"></canvas>
        </div>
        <div class="chart-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); height: 320px; box-shadow: var(--shadow-subtle);">
          <canvas id="chart-scenario-stats"></canvas>
        </div>
        <div class="chart-card" style="background: var(--surface); padding: 16px; border-radius: 10px; border: 1px solid var(--line); height: 320px; box-shadow: var(--shadow-subtle);">
          <canvas id="chart-match-breakdown"></canvas>
        </div>
      </div>

      <!-- Top Students Table -->
      <div class="top-students-card" style="background: var(--surface); padding: 20px; border-radius: 10px; border: 1px solid var(--line); box-shadow: var(--shadow-subtle);">
        <h3 style="margin-bottom: 12px; font-size: 16px; color: var(--text-primary);">Bảng xếp hạng Top Sinh viên</h3>
        <div style="overflow-x: auto;">
          <table style="width: 100%; border-collapse: collapse; font-size: 13px; text-align: left;">
            <thead>
              <tr style="border-bottom: 1px solid var(--line); color: var(--text-secondary);">
                <th style="padding: 10px;">Họ tên</th>
                <th style="padding: 10px;">Email</th>
                <th style="padding: 10px; text-align: center;">Phiên phỏng vấn</th>
                <th style="padding: 10px; text-align: center;">Điểm cao nhất</th>
                <th style="padding: 10px; text-align: center;">Coverage TB</th>
              </tr>
            </thead>
            <tbody>
              ${admin.topStudents.length === 0 ? '<tr><td colspan="5" style="padding: 20px; text-align: center; color: var(--muted);">Chưa có dữ liệu sinh viên hoàn thành phiên.</td></tr>' : admin.topStudents.map((s) => `
                <tr style="border-bottom: 1px solid var(--line-subtle);">
                  <td style="padding: 10px; font-weight: 600; color: var(--text-primary);">${escapeHtml(s.studentName)}</td>
                  <td style="padding: 10px; color: var(--text-secondary); font-family: var(--font-mono); font-size: 12px;">${escapeHtml(s.studentEmail)}</td>
                  <td style="padding: 10px; text-align: center; color: var(--text-secondary);">${s.completedCount} / ${s.sessionCount}</td>
                  <td style="padding: 10px; text-align: center; font-weight: bold; color: var(--pastel-green-text);">${s.bestCoverage}%</td>
                  <td style="padding: 10px; text-align: center; font-weight: bold; color: var(--pastel-blue-text);">${s.averageCoverage}%</td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `
}

function renderAdminUserManagementSection(admin: AdminState) {
  const users = admin.users
  return `
    <div class="admin-users-stack" style="display: grid; gap: 20px;">
      <!-- Controls Bar -->
      <div style="display: flex; gap: 12px; justify-content: space-between; align-items: center; background: var(--surface); padding: 14px; border-radius: 10px; border: 1px solid var(--line); box-shadow: var(--shadow-subtle);">
        <div style="display: flex; gap: 12px; align-items: center; flex: 1;">
          <input id="user-search-input" type="text" value="${escapeAttribute(admin.userSearch)}" placeholder="Tìm kiếm theo tên hoặc email..." style="background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px 12px; font-size: 13px; min-width: 250px;" />
          <select id="user-role-filter" style="background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px 12px; font-size: 13px;">
            <option value="" ${admin.userRoleFilter === '' ? 'selected' : ''}>Tất cả vai trò</option>
            <option value="Student" ${admin.userRoleFilter === 'Student' ? 'selected' : ''}>Student</option>
            <option value="Lecturer" ${admin.userRoleFilter === 'Lecturer' ? 'selected' : ''}>Lecturer</option>
            <option value="Admin" ${admin.userRoleFilter === 'Admin' ? 'selected' : ''}>Admin</option>
          </select>
          <button class="ghost-button" data-action="filter-users" type="button">Lọc</button>
        </div>
        <button class="primary-button" data-action="open-create-user-modal" type="button">+ Thêm người dùng mới</button>
      </div>

      <!-- Create / Edit User Modal / Panel if open -->
      ${admin.isCreatingUser ? renderCreateUserForm() : ''}
      ${admin.editingUser ? renderEditUserForm(admin.editingUser) : ''}

      <!-- Users Table -->
      <div style="background: var(--surface); padding: 20px; border-radius: 10px; border: 1px solid var(--line); box-shadow: var(--shadow-subtle);">
        <h3 style="margin-bottom: 12px; font-size: 16px; color: var(--text-primary);">Danh sách Người dùng (${users.length})</h3>
        <div style="overflow-x: auto;">
          <table style="width: 100%; border-collapse: collapse; font-size: 13px; text-align: left;">
            <thead>
              <tr style="border-bottom: 1px solid var(--line); color: var(--text-secondary);">
                <th style="padding: 10px;">Họ tên</th>
                <th style="padding: 10px;">Email</th>
                <th style="padding: 10px;">Vai trò</th>
                <th style="padding: 10px;">Ngày tạo</th>
                <th style="padding: 10px; text-align: right;">Thao tác</th>
              </tr>
            </thead>
            <tbody>
              ${users.length === 0 ? '<tr><td colspan="5" style="padding: 20px; text-align: center; color: var(--muted);">Không tìm thấy người dùng nào.</td></tr>' : users.map((u) => `
                <tr style="border-bottom: 1px solid var(--line-subtle);">
                  <td style="padding: 10px; font-weight: 600; color: var(--text-primary);">${escapeHtml(u.name)}</td>
                  <td style="padding: 10px; color: var(--text-secondary); font-family: var(--font-mono); font-size: 12px;">${escapeHtml(u.email)}</td>
                  <td style="padding: 10px;"><span class="user-role-badge ${u.role.toLowerCase()}" style="padding: 2px 8px; border-radius: 4px; font-size: 11px; font-weight: bold; background: ${u.role === 'Admin' ? 'var(--pastel-red-bg)' : u.role === 'Lecturer' ? 'var(--pastel-yellow-bg)' : 'var(--pastel-blue-bg)'}; color: ${u.role === 'Admin' ? 'var(--pastel-red-text)' : u.role === 'Lecturer' ? 'var(--pastel-yellow-text)' : 'var(--pastel-blue-text)'}; border: 1px solid var(--line);">${escapeHtml(u.role)}</span></td>
                  <td style="padding: 10px; color: var(--text-secondary); font-size: 12px;">${formatTime(u.createdAt)}</td>
                  <td style="padding: 10px; text-align: right;">
                    <button class="ghost-button" data-action="edit-user" data-user-id="${escapeAttribute(u.id)}" type="button" style="padding: 2px 8px; font-size: 12px;">Sửa</button>
                    <button class="ghost-button" data-action="delete-user" data-user-id="${escapeAttribute(u.id)}" type="button" style="padding: 2px 8px; font-size: 12px; color: var(--accent-rose); border-color: rgba(201, 90, 100, 0.3);">Xóa</button>
                  </td>
                </tr>
              `).join('')}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  `
}

function renderCreateUserForm() {
  return `
    <div style="background: var(--surface); padding: 20px; border-radius: 10px; border: 1px solid var(--accent-indigo); margin-bottom: 16px; box-shadow: var(--shadow-subtle);">
      <h4 style="margin-bottom: 12px; color: var(--accent-indigo); font-weight: bold;">Tạo mới Người dùng</h4>
      <form id="create-user-form" style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
        <div>
          <label style="display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px;">Họ và tên *</label>
          <input name="name" type="text" required placeholder="Nguyễn Văn A" style="width: 100%; background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-size: 13px;" />
        </div>
        <div>
          <label style="display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px;">Email *</label>
          <input name="email" type="email" required placeholder="user@example.com" style="width: 100%; background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-size: 13px;" />
        </div>
        <div>
          <label style="display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px;">Mật khẩu (tối thiểu 12 ký tự) *</label>
          <input name="password" type="password" required minlength="12" maxlength="128" placeholder="******" style="width: 100%; background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-size: 13px;" />
        </div>
        <div>
          <label style="display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px;">Vai trò *</label>
          <select name="role" required style="width: 100%; background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-size: 13px; cursor: pointer;">
            <option value="Student">Student (Sinh viên)</option>
            <option value="Lecturer">Lecturer (Giảng viên)</option>
            <option value="Admin">Admin (Quản trị viên)</option>
          </select>
        </div>
        <div style="grid-column: 1 / -1; display: flex; gap: 8px; justify-content: flex-end; margin-top: 8px;">
          <button class="ghost-button" data-action="cancel-user-form" type="button">Hủy</button>
          <button class="primary-button" data-action="submit-create-user" type="button">Tạo người dùng</button>
        </div>
      </form>
    </div>
  `
}

function renderEditUserForm(user: AdminUserItem) {
  return `
    <div style="background: var(--surface); padding: 20px; border-radius: 10px; border: 1px solid var(--accent-amber); margin-bottom: 16px; box-shadow: var(--shadow-subtle);">
      <h4 style="margin-bottom: 12px; color: var(--accent-amber); font-weight: bold;">Cập nhật Người dùng: ${escapeHtml(user.name)}</h4>
      <form id="edit-user-form" data-user-id="${escapeAttribute(user.id)}" style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
        <div>
          <label style="display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px;">Họ và tên *</label>
          <input name="name" type="text" value="${escapeAttribute(user.name)}" required style="width: 100%; background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-size: 13px;" />
        </div>
        <div>
          <label style="display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px;">Email *</label>
          <input name="email" type="email" value="${escapeAttribute(user.email)}" required style="width: 100%; background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-size: 13px;" />
        </div>
        <div>
          <label style="display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px;">Mật khẩu mới (bỏ trống nếu không đổi)</label>
          <input name="newPassword" type="password" minlength="12" maxlength="128" placeholder="Để trống nếu giữ nguyên" style="width: 100%; background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-size: 13px;" />
        </div>
        <div>
          <label style="display: block; font-size: 12px; color: var(--text-secondary); margin-bottom: 4px;">Vai trò *</label>
          <select name="role" required style="width: 100%; background: var(--surface-raised); color: var(--text-primary); border: 1px solid var(--line); border-radius: 6px; padding: 8px; font-size: 13px; cursor: pointer;">
            <option value="Student" ${user.role === 'Student' ? 'selected' : ''}>Student (Sinh viên)</option>
            <option value="Lecturer" ${user.role === 'Lecturer' ? 'selected' : ''}>Lecturer (Giảng viên)</option>
            <option value="Admin" ${user.role === 'Admin' ? 'selected' : ''}>Admin (Quản trị viên)</option>
          </select>
        </div>
        <div style="grid-column: 1 / -1; display: flex; gap: 8px; justify-content: flex-end; margin-top: 8px;">
          <button class="ghost-button" data-action="cancel-user-form" type="button">Hủy</button>
          <button class="primary-button" data-action="submit-edit-user" type="button" style="background: var(--accent-amber); color: var(--text-primary); font-weight: bold; border: none;">Lưu thay đổi</button>
        </div>
      </form>
    </div>
  `
}

// Tab switcher event delegation và tích hợp render Mermaid
document.addEventListener('click', (e) => {
  const btn = (e.target as HTMLElement).closest('.eval-tab-btn')
  if (!btn) return
  
  const container = btn.closest('.evaluation')
  if (!container) return
  
  const tabName = btn.getAttribute('data-tab')
  if (!tabName) return

  // Cập nhật trạng thái active cho các nút tab
  container.querySelectorAll('.eval-tab-btn').forEach(b => {
    b.classList.remove('active')
  })
  btn.classList.add('active')

  // Ẩn tất cả các nội dung tab
  container.querySelectorAll('.eval-tab-content').forEach(c => {
    (c as HTMLElement).style.display = 'none'
  })
  
  // Hiển thị nội dung tab mục tiêu
  const targetContent = container.querySelector(`#tab-${tabName}`) as HTMLElement
  if (targetContent) {
    targetContent.style.display = 'block'
  }

  // Tự động kích hoạt Mermaid render khi chuyển sang tab design
  if (tabName === 'design' && (window as any).mermaid) {
    void validateAndRenderMermaid(targetContent)
  }
})

async function validateAndRenderMermaid(container: HTMLElement) {
  const mermaid = (window as any).mermaid
  const nodes = Array.from(container.querySelectorAll<HTMLElement>('.mermaid'))
  for (const node of nodes) {
    try {
      await mermaid.parse(node.textContent ?? '')
    } catch (error) {
      node.classList.remove('mermaid')
      node.textContent = 'Diagram không vượt qua Mermaid parser. Vui lòng dùng bản requirement để kiểm tra.'
      node.dataset.validationStatus = 'invalid'
      console.error('Mermaid validation error:', error)
    }
  }
  const validNodes = nodes.filter(node => node.dataset.validationStatus !== 'invalid')
  if (validNodes.length > 0) {
    await mermaid.run({ nodes: validNodes })
  }
}

