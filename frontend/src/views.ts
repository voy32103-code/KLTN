import type {
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

export function renderApp(state: AppState) {
  return `
    <div class="shell">
      ${renderTopbar(state)}
      <main class="workspace">
        ${state.notice ? renderNotice(state.notice) : ''}
        ${state.view === 'auth' ? renderAuth(state) : ''}
        ${state.view === 'scenarios' ? renderScenarioPicker(state) : ''}
        ${state.view === 'chat' ? renderChat(state) : ''}
        ${state.view === 'review' ? renderReviewDashboard(state) : ''}
      </main>
    </div>
  `
}

function renderTopbar(state: AppState) {
  const viewLabels: Record<AppView, string> = {
    auth: 'Cổng truy cập',
    scenarios: 'Lựa chọn kịch bản',
    chat: state.evaluation ? 'Báo cáo đánh giá' : 'Phỏng vấn trực tiếp',
    review: 'Bảng điều khiển review',
  }
  const canReview = isPrivilegedRole(state.user?.role)

  return `
    <header class="topbar">
      <div class="brand-block">
        <span class="brand-mark" aria-hidden="true">R</span>
        <div>
          <p class="eyebrow">ReqSimulator</p>
          <h1 style="font-size: 20px; font-family: var(--font-sans); font-weight: 700;">
            Phòng thực hành Khai thác yêu cầu
          </h1>
        </div>
      </div>
      <div class="topbar-actions">
        <span class="view-pill">${escapeHtml(viewLabels[state.view])}</span>
        ${state.user?.email ? `<span class="user-pill">${escapeHtml(state.user.email)}</span>` : ''}
        ${canReview && state.view !== 'review' ? `<button class="ghost-button" data-action="open-review" type="button" ${state.busy ? 'disabled' : ''}>Bảng điều khiển</button>` : ''}
        ${canReview && state.view === 'review' ? `<button class="ghost-button" data-action="open-student-lab" type="button" ${state.busy ? 'disabled' : ''}>Phòng thực hành</button>` : ''}
        ${state.token ? `<button class="ghost-button" data-action="logout" type="button" ${state.busy ? 'disabled' : ''}>Đăng xuất</button>` : ''}
      </div>
    </header>
  `
}

function renderNotice(notice: Notice) {
  return `<div class="notice ${notice.type}" role="status">${escapeHtml(notice.text)}</div>`
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
        <form class="auth-panel" id="auth-form">
          <div class="form-heading">
            <p class="section-kicker">${isLogin ? 'Đăng nhập' : 'Đăng ký'}</p>
            <h2>${isLogin ? 'Vào phòng thực hành' : 'Tạo tài khoản sinh viên'}</h2>
          </div>
          <div class="tabs" role="tablist">
            <button class="${isLogin ? 'active' : ''}" data-auth-mode="login" type="button">Đăng nhập</button>
            <button class="${!isLogin ? 'active' : ''}" data-auth-mode="register" type="button">Tạo tài khoản</button>
          </div>
          ${isLogin ? '' : `
            <label>
              Họ tên
              <input name="name" autocomplete="name" required maxlength="100" />
            </label>
          `}
          <label>
            Email
            <input name="email" type="email" autocomplete="email" required maxlength="255" />
          </label>
          <label>
            Mật khẩu
            <input name="password" type="password" autocomplete="${isLogin ? 'current-password' : 'new-password'}" required minlength="6" />
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
  return `
    <section class="section-head" data-animate="fade-up" style="--index: 0">
      <div>
        <p class="section-kicker">Thiết lập kịch bản</p>
        <h2>Chọn tình huống và đối tác phỏng vấn</h2>
      </div>
      <button class="ghost-button" data-action="refresh-scenarios" type="button" ${state.busy ? 'disabled' : ''}>
        Tải lại kịch bản
      </button>
    </section>
    <section class="picker-layout">
      <aside class="faux-chrome scenario-list" data-animate="fade-up" style="--index: 1">
        <div class="chrome-bar">
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
        </div>
        <div class="panel-heading">
          <div>
            <p class="section-kicker">Danh sách</p>
            <h2>${state.scenarios.length} kịch bản khả dụng</h2>
          </div>
        </div>
        <div class="list-stack">
          ${state.scenarios.length === 0 ? renderEmpty('Chưa có scenario active.', 'Kiểm tra backend seed data hoặc tải lại danh sách.') : state.scenarios.map((item, index) => renderScenarioItem(item, state, index)).join('')}
        </div>
      </aside>
      <section class="faux-chrome detail-panel" data-animate="fade-up" style="--index: 2">
        <div class="chrome-bar">
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
        </div>
        ${scenario ? renderScenarioDetail(scenario, state) : renderScenarioPlaceholder()}
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
        <span>${scenario.personaCount} stakeholder</span>
        <span>${scenario.requirementCount} req</span>
      </span>
    </button>
  `
}

function renderScenarioPlaceholder() {
  return `
    <div class="placeholder">
      <p class="section-kicker">Bắt đầu</p>
      <h2>Chọn kịch bản để xem chi tiết</h2>
      <p>Thông tin kịch bản sẽ hiển thị lĩnh vực, độ khó, danh sách các hidden requirements và stakeholder đối tác.</p>
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
        <span>Stakeholder: <strong>${scenario.personaCount}</strong></span>
        <span>Yêu cầu ẩn: <strong>${scenario.requirementCount}</strong></span>
        <span>Độ khó: <strong style="font-family: var(--font-sans); text-transform: uppercase;">${escapeHtml(scenario.difficulty)}</strong></span>
      </div>
    </div>
    <div class="persona-section">
      <div class="subsection-heading" data-animate="fade-up" style="--index: 1">
        <h3>Đối tác Stakeholder</h3>
        ${selectedPersona ? `<span class="view-pill liquid-glass font-serif">${escapeHtml(selectedPersona.name)}</span>` : ''}
      </div>
      <div class="persona-grid">
        ${scenario.personas.length === 0 ? renderEmpty('Scenario này chưa có persona.', 'Cần seed persona trước khi bắt đầu phỏng vấn.') : scenario.personas.map((persona, index) => renderPersonaCard(persona, state, index + 2)).join('')}
      </div>
    </div>
    <div class="panel-footer" data-animate="fade-up" style="--index: 5">
      <div>
        <strong>${selectedPersona ? escapeHtml(selectedPersona.roleTitle ?? 'Stakeholder') : 'Chưa chọn đối tác'}</strong>
        <span>${selectedPersona ? 'Phiên mô phỏng phỏng vấn phác thảo sẽ bắt đầu với nhân vật này.' : 'Vui lòng lựa chọn một stakeholder để bắt đầu.'}</span>
      </div>
      <button class="primary-button" data-action="start-session" type="button" ${!state.selectedPersonaId || state.busy ? 'disabled' : ''}>
        ${state.busy ? 'Đang khởi tạo...' : 'Bắt đầu phỏng vấn'}
      </button>
    </div>
  `
}

function renderPersonaCard(persona: Persona, state: AppState, index: number) {
  const active = persona.id === state.selectedPersonaId
  return `
    <button class="persona-card ${active ? 'active' : ''}" data-persona-id="${escapeAttribute(persona.id)}" type="button" data-animate="fade-up" style="--index: ${index}">
      <span class="persona-topline">
        <strong class="font-serif">${escapeHtml(persona.name)}</strong>
        <span class="difficulty-badge liquid-glass">${escapeHtml(persona.difficulty)}</span>
      </span>
      <span>${escapeHtml(persona.roleTitle ?? 'Stakeholder')}</span>
      <small>${escapeHtml(persona.communicationStyle ?? 'trung lập')} · ${escapeHtml(persona.knowledgeLevel ?? 'tiêu chuẩn')}</small>
    </button>
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
        <span class="font-serif">${escapeHtml(persona?.name ?? 'Stakeholder')}</span>
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
      <aside class="faux-chrome session-panel" data-animate="fade-up" style="--index: 0">
        <div class="chrome-bar">
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
        </div>
        <button class="ghost-button back-button" data-action="back-to-scenarios" type="button" ${state.busy ? 'disabled' : ''}>
          <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" style="margin-right: 4px;"><path d="M19 12H5M12 19l-7-7 7-7"/></svg>
          Quay lại
        </button>
        <div class="session-title">
          <p class="section-kicker">Phiên phỏng vấn</p>
          <h2>${escapeHtml(scenario?.title ?? 'Session')}</h2>
        </div>
        <div class="session-status ${state.evaluation ? 'completed' : 'active'}">
          <span>${sessionStatus}</span>
          <strong>${studentMessageCount}</strong>
          <small>Lượt hỏi</small>
        </div>
        <dl>
          <div><dt>Stakeholder</dt><dd class="font-serif" style="font-size: 16px;">${escapeHtml(persona?.name ?? 'Stakeholder')}</dd></div>
          <div><dt>Vai trò</dt><dd>${escapeHtml(persona?.roleTitle ?? 'N/A')}</dd></div>
          <div><dt>Mô hình AI</dt><dd style="font-family: var(--font-mono); font-size: 12px; color: var(--pastel-blue-text); font-weight: bold;">Gemini 2.5 Flash</dd></div>
          <div><dt>Loại câu hỏi gần nhất</dt><dd>${escapeHtml(lastQuestionType ?? 'Chưa phát hiện')}</dd></div>
          <div><dt>Session ID</dt><dd style="font-family: var(--font-mono); font-size: 12px;">${escapeHtml(shortId(state.session?.id ?? ''))}</dd></div>
        </dl>
        <button class="danger-button" data-action="end-session" type="button" ${state.busy || Boolean(state.evaluation) ? 'disabled' : ''}>
          ${state.busy ? 'Đang xử lý...' : state.evaluation ? 'Đã kết thúc phiên' : 'Kết thúc & Chấm điểm'}
        </button>
        ${state.evaluation ? renderEvaluation(state.evaluation) : ''}
      </aside>
      <section class="faux-chrome chat-panel" data-animate="fade-up" style="--index: 1">
        <div class="chrome-bar">
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
        </div>
        <div class="chat-header">
          <div>
            <p class="section-kicker">Hội thoại Trực tiếp (Gemini 2.5 Flash)</p>
            <h2 class="font-serif">${escapeHtml(persona?.name ?? 'Stakeholder')}</h2>
          </div>
          <span class="view-pill liquid-glass">${state.evaluation ? 'Chế độ xem lại' : state.busy ? 'Đang xử lý' : 'Sẵn sàng'}</span>
        </div>
        <div class="messages" id="messages">
          ${state.messages.length === 0 ? renderEmpty('Chưa có tin nhắn trong phiên này.', 'Bắt đầu bằng một câu hỏi khảo sát nghiệp vụ.') : state.messages.map((msg, index) => renderMessage(msg, index)).join('')}
          ${thinkingHtml}
        </div>
        <form class="composer" id="message-form">
          <textarea name="content" rows="3" maxlength="4000" placeholder="Nhập câu hỏi nghiệp vụ gửi cho stakeholder..." ${state.busy || Boolean(state.evaluation) ? 'disabled' : ''}></textarea>
          <button class="primary-button" type="submit" ${state.busy || Boolean(state.evaluation) ? 'disabled' : ''}>
            ${state.busy ? 'Đang gửi...' : 'Gửi'}
          </button>
        </form>
      </section>
    </section>
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
        Tải lại sessions
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
      <aside class="faux-chrome review-list" data-animate="fade-up" style="--index: 2">
        <div class="chrome-bar">
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
        </div>
        <div class="panel-heading">
          <div>
            <p class="section-kicker">Bản ghi thử nghiệm</p>
            <h2>Các phiên gần nhất</h2>
          </div>
        </div>
        <div class="list-stack">
          ${state.reviewSessions.length === 0 ? renderEmpty('Chưa có session để review.', 'Chạy thử một session rồi quay lại dashboard.') : state.reviewSessions.map((session, index) => renderReviewSessionItem(session, state, index)).join('')}
        </div>
      </aside>
      <section class="faux-chrome review-detail" data-animate="fade-up" style="--index: 3">
        <div class="chrome-bar">
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
          <div class="chrome-dot"></div>
        </div>
        ${state.reviewDetail ? renderReviewSessionDetail(state.reviewDetail) : renderReviewPlaceholder()}
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
        <strong>${escapeHtml(session.student.name || session.student.email)}</strong>
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
      <h2>Chọn session để xem transcript chi tiết</h2>
      <p>Bảng tổng quan hiển thị hội thoại phỏng vấn sinh viên, các yêu cầu trích xuất, và báo cáo đối soát chấm điểm tự động.</p>
    </div>
  `
}

function renderReviewSessionDetail(detail: ReviewSessionDetail) {
  const studentTurns = detail.messages.filter((message) => message.sender === 'Student').length
  return `
    <div class="review-detail-header" data-animate="fade-up" style="--index: 0">
      <div>
        <p class="section-kicker">${escapeHtml(detail.session.scenario.domain ?? 'Nghiệp vụ')}</p>
        <h2>${escapeHtml(detail.session.scenario.title)}</h2>
        <p>${escapeHtml(detail.session.scenario.description)}</p>
      </div>
      <div class="review-detail-actions">
        <div class="metrics">
          <span>Điểm bao phủ: <strong>${formatScore(detail.evaluation?.coverageScore ?? null)}</strong></span>
          <span>Lượt hỏi: <strong>${studentTurns}</strong></span>
          <span>Yêu cầu ẩn: <strong>${detail.hiddenRequirements.length}</strong></span>
        </div>
        <div class="export-actions">
          <button class="ghost-button" data-action="export-review-json" type="button">Xuất JSON</button>
          <button class="ghost-button" data-action="export-review-csv" type="button">Xuất CSV</button>
        </div>
      </div>
    </div>
    <div class="review-identity-grid" data-animate="fade-up" style="--index: 1">
      <span><strong>Sinh viên</strong>${escapeHtml(detail.session.student.name)} · ${escapeHtml(detail.session.student.email)}</span>
      <span><strong>Stakeholder</strong><span class="font-serif">${escapeHtml(detail.session.persona.name)}</span> · ${escapeHtml(detail.session.persona.roleTitle ?? 'Stakeholder')}</span>
      <span><strong>Trạng thái</strong>${detail.session.finalizationStatus === 'completed' ? 'Đã hoàn thành' : 'Đang thực hiện'} · ${detail.session.isActive ? 'Đang hoạt động' : 'Đã đóng'}</span>
    </div>
    <div class="review-content-grid">
      <section class="review-block" data-animate="fade-up" style="--index: 2">
        <div class="subsection-heading">
          <h3>Hội thoại phỏng vấn</h3>
          <span>${detail.messages.length} tin nhắn</span>
        </div>
        <div class="review-transcript">
          ${detail.messages.length === 0 ? renderEmpty('Session chưa có transcript.') : detail.messages.map((msg, index) => renderMessage(msg, index)).join('')}
        </div>
      </section>
      <section class="review-block" data-animate="fade-up" style="--index: 3">
        <div class="subsection-heading">
          <h3>Báo cáo chấm điểm</h3>
          <span>${detail.evaluation ? 'Đã lưu' : 'Chưa đánh giá'}</span>
        </div>
        ${detail.evaluation ? renderEvaluation(detail.evaluation) : renderEmpty('Session chưa được chấm điểm.', 'Kết thúc phiên phỏng vấn để tiến hành đánh giá.')}
      </section>
      <section class="review-block" data-animate="fade-up" style="--index: 4">
        <div class="subsection-heading">
          <h3>Yêu cầu trích xuất (Extracted)</h3>
          <span>${detail.extractedRequirements.length} mục</span>
        </div>
        ${renderExtractedRequirements(detail.extractedRequirements)}
      </section>
      <section class="review-block" data-animate="fade-up" style="--index: 5">
        <div class="subsection-heading">
          <h3>Yêu cầu ẩn (Hidden Requirements)</h3>
          <span>${detail.hiddenRequirements.length} mục</span>
        </div>
        ${renderHiddenRequirements(detail.hiddenRequirements)}
      </section>
    </div>
  `
}

function renderExtractedRequirements(requirements: ReviewExtractedRequirement[]) {
  if (requirements.length === 0) {
    return renderEmpty('Chưa có extracted requirement.')
  }

  return `
    <div class="artifact-list">
      ${requirements.map((requirement) => `
        <article>
          <strong>${escapeHtml(requirement.requirementText)}</strong>
          <small>Độ tin cậy: ${formatNullablePercent(requirement.confidenceScore)}</small>
        </article>
      `).join('')}
    </div>
  `
}

function renderHiddenRequirements(requirements: ReviewHiddenRequirement[]) {
  if (requirements.length === 0) {
    return renderEmpty('Scenario này chưa có hidden requirement.')
  }

  return `
    <div class="artifact-list">
      ${requirements.map((requirement) => `
        <article>
          <strong>${escapeHtml(requirement.requirementText)}</strong>
          <small>Cấp độ: ${requirement.gateOrder} · Nhóm: ${escapeHtml(requirement.category)} · Độ khó: ${escapeHtml(requirement.revealDifficulty)}</small>
          ${requirement.revealCondition ? `<small>Điều kiện: ${escapeHtml(requirement.revealCondition)}</small>` : ''}
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

function renderEvaluation(evaluation: EvaluationResult) {
  const feedback = evaluation.feedback
  const total = evaluation.matchedCount + evaluation.partialCount + evaluation.missedCount
  const design = feedback?.designSuggestions
  
  const designTabHtml = design ? `
    <div class="design-suggestions-card" style="display: flex; flex-direction: column; gap: 16px; margin-top: 8px;">
      <div class="subsection-heading">
        <h3>Gợi ý Mô hình thiết kế sơ bộ (AI Suggestion)</h3>
        <span style="font-size: 11px; opacity: 0.7;">Được sinh tự động dựa trên các yêu cầu thu thập được</span>
      </div>
      
      <div class="design-meta-grid" style="display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 8px;">
        <div class="meta-section" style="background: rgba(255, 255, 255, 0.01); border: 1px solid var(--color-border); border-radius: 6px; padding: 12px;">
          <strong style="display: block; margin-bottom: 8px; font-size: 13px; color: var(--color-text-secondary);">Tác nhân chính (Actors)</strong>
          <div class="badge-list" style="display: flex; flex-wrap: wrap; gap: 6px;">
            ${design.mainActors.map(actor => `<span class="actor-badge" style="background: rgba(147, 197, 253, 0.1); color: #93c5fd; padding: 4px 8px; border-radius: 4px; border: 1px solid rgba(147, 197, 253, 0.2); font-size: 11px; font-family: var(--font-mono); font-weight: bold;">${escapeHtml(actor)}</span>`).join('')}
          </div>
        </div>
        <div class="meta-section" style="background: rgba(255, 255, 255, 0.01); border: 1px solid var(--color-border); border-radius: 6px; padding: 12px;">
          <strong style="display: block; margin-bottom: 8px; font-size: 13px; color: var(--color-text-secondary);">Thực thể chính (Entities)</strong>
          <div class="badge-list" style="display: flex; flex-wrap: wrap; gap: 6px;">
            ${design.mainEntities.map(entity => `<span class="entity-badge" style="background: rgba(167, 243, 208, 0.1); color: #a7f3d0; padding: 4px 8px; border-radius: 4px; border: 1px solid rgba(167, 243, 208, 0.2); font-size: 11px; font-family: var(--font-mono); font-weight: bold;">${escapeHtml(entity)}</span>`).join('')}
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
 
  return `
    <section class="evaluation" data-animate="fade-up" style="--index: 0">
      <div class="score-card ${formatScoreClass(evaluation.coverageScore)}">
        <span>${formatScore(evaluation.coverageScore)}</span>
        <small>Mức độ bao phủ</small>
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
        ${feedback ? `
          <div class="feedback-block" style="margin-top: 16px;">
            ${renderFeedbackList('Điểm mạnh', feedback.strengths)}
            ${renderFeedbackList('Cần cải thiện', feedback.weaknesses)}
            ${renderFeedbackList('Gợi ý tiếp theo', feedback.suggestions)}
          </div>
        ` : ''}
      </div>
 
      <div class="eval-tab-content" id="tab-design" style="display: none;">
        ${designTabHtml}
      </div>
 
      <div class="eval-tab-content" id="tab-matching" style="display: none;">
        ${evaluation.matches?.length ? renderRequirementReport(evaluation.matches) : ''}
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

function renderRequirementReport(matches: RequirementMatchReport[]) {
  return `
    <div class="requirement-report">
      <div class="subsection-heading">
        <h3>Báo cáo so khớp chi tiết</h3>
        <span>${matches.length} mục</span>
      </div>
      <div class="report-table">
        ${matches.map((match) => `
          <article class="requirement-row ${escapeAttribute(match.matchType.toLowerCase())}">
            <div class="requirement-row-header" style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
              <strong style="font-family: var(--font-mono); font-size: 12px;">${escapeHtml(match.hiddenId)}</strong>
              ${renderMatchBadge(match.matchType, match.score)}
            </div>
            <p>${escapeHtml(match.hiddenText ?? 'Hidden requirement')}</p>
            <div class="evidence-line">
              <span>Bằng chứng hội thoại (Extracted)</span>
              <small>${escapeHtml(match.extractedText ?? 'Không tìm thấy thông tin trùng khớp')}</small>
            </div>
            <div class="evidence-line">
              <span>Lý do đối soát</span>
              <small>${escapeHtml(match.reason)}</small>
            </div>
          </article>
        `).join('')}
      </div>
    </div>
  `
}

function renderMatchBadge(matchType: string, score: number) {
  return `<span class="match-badge ${escapeAttribute(matchType.toLowerCase())}">${escapeHtml(matchType)} · ${Math.round(score * 100)}%</span>`
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
    try {
      (window as any).mermaid.run({
        nodes: targetContent.querySelectorAll('.mermaid')
      });
    } catch (err) {
      console.error('Mermaid render error:', err);
    }
  }
})

