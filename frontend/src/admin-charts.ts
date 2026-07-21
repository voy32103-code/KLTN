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
  destroyChart('coverage-dist')
  activeCharts['coverage-dist'] = new Chart(canvas, {
    type: 'bar',
    data: {
      labels: bins.map(b => b.label),
      datasets: [
        {
          label: 'Số phiên phỏng vấn',
          data: bins.map(b => b.count),
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
          color: '#e2e8f0',
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: '#94a3b8' }, grid: { color: '#334155' } },
        y: { ticks: { color: '#94a3b8', stepSize: 1 }, grid: { color: '#334155' }, beginAtZero: true }
      }
    }
  })
}

export function renderSessionsOverTimeChart(
  canvas: HTMLCanvasElement,
  data: SessionsOverTimeData
) {
  destroyChart('sessions-time')
  activeCharts['sessions-time'] = new Chart(canvas, {
    type: 'line',
    data: {
      labels: data.labels,
      datasets: [
        {
          label: 'Số phiên',
          data: data.counts,
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
          color: '#e2e8f0',
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: '#94a3b8', maxRotation: 45 }, grid: { color: '#334155' } },
        y: { ticks: { color: '#94a3b8', stepSize: 1 }, grid: { color: '#334155' }, beginAtZero: true }
      }
    }
  })
}

export function renderScenarioStatsChart(
  canvas: HTMLCanvasElement,
  scenarios: ScenarioStatItem[]
) {
  destroyChart('scenario-stats')
  activeCharts['scenario-stats'] = new Chart(canvas, {
    type: 'bar',
    data: {
      labels: scenarios.map(s => s.scenarioTitle.length > 20 ? s.scenarioTitle.slice(0, 18) + '...' : s.scenarioTitle),
      datasets: [
        {
          label: 'Coverage TB (%)',
          data: scenarios.map(s => s.averageCoverage),
          backgroundColor: 'rgba(99, 102, 241, 0.8)',
          borderRadius: 4
        },
        {
          label: 'Số lượt hội thoại TB',
          data: scenarios.map(s => s.averageTurns),
          backgroundColor: 'rgba(236, 72, 153, 0.8)',
          borderRadius: 4
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { labels: { color: '#cbd5e1' } },
        title: {
          display: true,
          text: 'So sánh hiệu suất theo Kịch bản',
          color: '#e2e8f0',
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: '#94a3b8' }, grid: { color: '#334155' } },
        y: { ticks: { color: '#94a3b8' }, grid: { color: '#334155' }, beginAtZero: true }
      }
    }
  })
}

export function renderMatchTypeBreakdownChart(
  canvas: HTMLCanvasElement,
  data: MatchTypeBreakdownData
) {
  destroyChart('match-breakdown')
  activeCharts['match-breakdown'] = new Chart(canvas, {
    type: 'doughnut',
    data: {
      labels: ['Exact Match', 'Semantic Match', 'Partial Match', 'Missed'],
      datasets: [
        {
          data: [data.exact, data.semantic, data.partial, data.missed],
          backgroundColor: ['#10b981', '#3b82f6', '#f59e0b', '#ef4444'],
          borderColor: '#0f172a',
          borderWidth: 2
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'right', labels: { color: '#cbd5e1', font: { size: 12 } } },
        title: {
          display: true,
          text: 'Tỷ lệ Loại so khớp Requirement',
          color: '#e2e8f0',
          font: { size: 14, weight: 'bold' }
        }
      }
    }
  })
}
