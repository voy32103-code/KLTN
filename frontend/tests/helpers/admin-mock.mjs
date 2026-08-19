const API_BASE_URL = 'http://localhost:5206'

export const adminToken = `header.${Buffer.from(JSON.stringify({
  email: 'admin@example.test',
  role: 'Admin',
  exp: Math.floor(Date.now() / 1000) + 3600,
})).toString('base64url')}.signature`

export async function installAdminApiMock(page, jobs = []) {
  await page.addInitScript((token) => localStorage.setItem('reqsimulator.token', token), adminToken)

  await page.route(`${API_BASE_URL}/**`, async (route) => {
    const request = route.request()
    const url = new URL(request.url())
    const path = url.pathname
    const json = (body, status = 200) => route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) })

    if (path === '/api/Admin/stats/overview') return json({ totalSessions: 0, totalStudents: 0, totalScenarios: 0, averageCoverage: 0, completedSessions: 0, activeSessions: 0 })
    if (path === '/api/Admin/stats/coverage-distribution') return json([])
    if (path === '/api/Admin/stats/sessions-over-time') return json({ labels: [], counts: [] })
    if (path === '/api/Admin/stats/by-scenario') {
      return json([{ scenarioId: 'scenario-1', scenarioTitle: 'Library Booking', sessionCount: 3, averageCoverage: 75, averageTurns: 6 }])
    }
    if (path === '/api/Admin/stats/top-students' || path === '/api/Admin/users') return json([])
    if (path === '/api/Admin/stats/match-type-breakdown') return json({ exact: 0, semantic: 0, partial: 0, missed: 0 })
    if (path === '/api/Admin/stats/grading-review') {
      return json({
        methodology: {
          minimumReviews: 5,
          meanAdjustmentThreshold: 15,
          highAdjustmentThreshold: 25,
          disclaimer: 'Statistical review only.',
        },
        reviewers: [],
      })
    }
    if (path === '/api/Admin/stats/feedback-experiment') return json({ variants: [] })
    if (path === '/api/admin-ingestion/jobs' && request.method() === 'GET') return json(jobs)
    if (path === '/api/admin-ingestion/upload-intents' && request.method() === 'POST') {
      return json({ jobId: 'job-upload-1', artifactId: 'artifact-upload-1', uploadUrl: 'https://r2.mock/upload/job-upload-1' })
    }
    if (path === '/api/admin-ingestion/artifacts/artifact-upload-1/complete' && request.method() === 'POST') return json({ jobId: 'job-upload-1', status: 'Queued' }, 202)
    if (path === '/api/admin-ingestion/jobs/job-upload-1' && request.method() === 'GET') {
      return json({ jobId: 'job-upload-1', status: 'Queued', attempts: 0, sourceLabel: 'meeting.mp3', hasDraft: false })
    }
    if (path === '/api/AdminScenarios/scenario-1/draft' && request.method() === 'GET') {
      return json({
        scenario_key: 'library_booking',
        scenario_title: 'Library Booking',
        context: 'Students reserve study rooms.',
        general_keywords: ['library', 'room'],
        gate_keyword_groups: { '0': ['reserve'] },
        question_type_gate_map: { OpenEnded: [0] },
        max_new_reveals_per_turn: 1,
        requirements: [{
          id: 'LB1', text: 'Students can reserve an available room.', gate: 0,
          keywords: null, question_types: null,
          reveal_condition: 'Ask about room reservations.', reveal_difficulty: 'Easy',
          requires: null, actor: 'Student', action: 'reserve', object: 'study room',
          condition: 'when available', type: 'FR', priority: 'high',
        }],
      })
    }
    return json({ message: `Unexpected mock request: ${request.method()} ${path}` }, 404)
  })

  await page.route('https://r2.mock/**', async (route) => route.fulfill({ status: 200, body: '' }))
}
