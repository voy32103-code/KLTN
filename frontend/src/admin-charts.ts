import { Chart, registerables } from 'chart.js'
import type {
  CoverageDistributionBin,
  SessionsOverTimeData,
  ScenarioStatItem,
  MatchTypeBreakdownData
} from './types'

Chart.register(...registerables)

const activeCharts: Record<string, Chart> = {}

function destroyChart(id: string) {
  if (activeCharts[id]) {
    activeCharts[id].destroy()
    delete activeCharts[id]
  }
}

export function destroyAllAdminCharts() {
  Object.keys(activeCharts).forEach(destroyChart)
}

export function renderCoverageDistributionChart(
  canvas: HTMLCanvasElement,
  bins: CoverageDistributionBin[]
) {
  const safeBins: CoverageDistributionBin[] = Array.isArray(bins) ? bins : (Array.isArray((bins as any)?.bins) ? (bins as any).bins : [])
  destroyChart('coverage-dist')
  activeCharts['coverage-dist'] = new Chart(canvas, {
    type: 'bar',
    data: {
      labels: safeBins.map(b => b.label),
      datasets: [
        {
          label: 'Số phiên phỏng vấn',
          data: safeBins.map(b => b.count),
          backgroundColor: [
            'rgba(239, 68, 68, 0.7)',
            'rgba(245, 158, 11, 0.7)',
            'rgba(59, 130, 246, 0.7)',
            'rgba(16, 185, 129, 0.7)',
            'rgba(139, 92, 246, 0.7)'
          ],
          borderColor: [
            '#ef4444',
            '#f59e0b',
            '#3b82f6',
            '#10b981',
            '#8b5cf6'
          ],
          borderWidth: 1,
          borderRadius: 6
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        title: {
          display: true,
          text: 'Phân bổ Coverage Score (%)',
          color: '#1e293b',
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: '#64748b' }, grid: { color: 'rgba(99, 102, 241, 0.06)' } },
        y: { ticks: { color: '#64748b', stepSize: 1 }, grid: { color: 'rgba(99, 102, 241, 0.06)' }, beginAtZero: true }
      }
    }
  })
}

export function renderSessionsOverTimeChart(
  canvas: HTMLCanvasElement,
  data: SessionsOverTimeData
) {
  const labels = data?.labels ?? []
  const counts = data?.counts ?? []
  destroyChart('sessions-time')
  activeCharts['sessions-time'] = new Chart(canvas, {
    type: 'line',
    data: {
      labels,
      datasets: [
        {
          label: 'Số phiên',
          data: counts,
          borderColor: '#38bdf8',
          backgroundColor: 'rgba(56, 189, 248, 0.15)',
          fill: true,
          tension: 0.35,
          pointRadius: 3,
          pointHoverRadius: 6
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        title: {
          display: true,
          text: 'Xu hướng phiên phỏng vấn theo ngày',
          color: '#1e293b',
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: '#64748b', maxRotation: 45 }, grid: { color: 'rgba(99, 102, 241, 0.06)' } },
        y: { ticks: { color: '#64748b', stepSize: 1 }, grid: { color: 'rgba(99, 102, 241, 0.06)' }, beginAtZero: true }
      }
    }
  })
}

export function renderScenarioStatsChart(
  canvas: HTMLCanvasElement,
  scenarios: ScenarioStatItem[]
) {
  const safeScenarios = Array.isArray(scenarios) ? scenarios : []
  destroyChart('scenario-stats')
  activeCharts['scenario-stats'] = new Chart(canvas, {
    type: 'bar',
    data: {
      labels: safeScenarios.map(s => s.scenarioTitle.length > 20 ? s.scenarioTitle.slice(0, 18) + '...' : s.scenarioTitle),
      datasets: [
        {
          label: 'Coverage TB (%)',
          data: safeScenarios.map(s => s.averageCoverage),
          backgroundColor: 'rgba(99, 102, 241, 0.8)',
          borderRadius: 4
        },
        {
          label: 'Số lượt hội thoại TB',
          data: safeScenarios.map(s => s.averageTurns),
          backgroundColor: 'rgba(236, 72, 153, 0.8)',
          borderRadius: 4
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { labels: { color: '#334155' } },
        title: {
          display: true,
          text: 'So sánh hiệu suất theo Kịch bản',
          color: '#1e293b',
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: '#64748b' }, grid: { color: 'rgba(99, 102, 241, 0.06)' } },
        y: { ticks: { color: '#64748b' }, grid: { color: 'rgba(99, 102, 241, 0.06)' }, beginAtZero: true }
      }
    }
  })
}

export function renderMatchTypeBreakdownChart(
  canvas: HTMLCanvasElement,
  data: MatchTypeBreakdownData
) {
  const exact = data?.exact ?? 0
  const semantic = data?.semantic ?? 0
  const partial = data?.partial ?? 0
  const missed = data?.missed ?? 0
  destroyChart('match-breakdown')
  activeCharts['match-breakdown'] = new Chart(canvas, {
    type: 'doughnut',
    data: {
      labels: ['Exact Match', 'Semantic Match', 'Partial Match', 'Missed'],
      datasets: [
        {
          data: [exact, semantic, partial, missed],
          backgroundColor: ['#10b981', '#3b82f6', '#f59e0b', '#ef4444'],
          borderColor: '#ffffff',
          borderWidth: 2
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'right', labels: { color: '#334155', font: { size: 12 } } },
        title: {
          display: true,
          text: 'Tỷ lệ Loại so khớp Requirement',
          color: '#1e293b',
          font: { size: 14, weight: 'bold' }
        }
      }
    }
  })
}
