import { Chart, registerables } from 'chart.js'
import type {
  CoverageDistributionBin,
  SessionsOverTimeData,
  ScenarioStatItem,
  MatchTypeBreakdownData
} from './types'

Chart.register(...registerables)

const activeCharts: Record<string, Chart> = {}

const chartColors = {
  text: '#1C1B1A',
  secondaryText: '#5F5B54',
  muted: '#77736B',
  grid: '#E9E6E0',
  accent: '#C96442',
  green: '#3F6B5C',
  amber: '#9A5B31',
  rose: '#A6454C',
}

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
            'rgba(166, 69, 76, 0.72)',
            'rgba(154, 91, 49, 0.72)',
            'rgba(201, 100, 66, 0.72)',
            'rgba(63, 107, 92, 0.72)',
            'rgba(119, 115, 107, 0.58)'
          ],
          borderColor: [
            chartColors.rose,
            chartColors.amber,
            chartColors.accent,
            chartColors.green,
            chartColors.muted,
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
          color: chartColors.text,
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: chartColors.muted }, grid: { color: chartColors.grid } },
        y: { ticks: { color: chartColors.muted, stepSize: 1 }, grid: { color: chartColors.grid }, beginAtZero: true }
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
          borderColor: chartColors.accent,
          backgroundColor: 'rgba(201, 100, 66, 0.16)',
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
          color: chartColors.text,
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: chartColors.muted, maxRotation: 45 }, grid: { color: chartColors.grid } },
        y: { ticks: { color: chartColors.muted, stepSize: 1 }, grid: { color: chartColors.grid }, beginAtZero: true }
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
          backgroundColor: 'rgba(201, 100, 66, 0.8)',
          borderRadius: 4
        },
        {
          label: 'Số lượt hội thoại TB',
          data: safeScenarios.map(s => s.averageTurns),
          backgroundColor: 'rgba(119, 115, 107, 0.8)',
          borderRadius: 4
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { labels: { color: chartColors.secondaryText } },
        title: {
          display: true,
          text: 'So sánh hiệu suất theo Kịch bản',
          color: chartColors.text,
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ticks: { color: chartColors.muted }, grid: { color: chartColors.grid } },
        y: { ticks: { color: chartColors.muted }, grid: { color: chartColors.grid }, beginAtZero: true }
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
          backgroundColor: [chartColors.green, chartColors.accent, chartColors.amber, chartColors.rose],
          borderColor: '#ffffff',
          borderWidth: 2
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'right', labels: { color: chartColors.secondaryText, font: { size: 12 } } },
        title: {
          display: true,
          text: 'Tỷ lệ Loại so khớp Requirement',
          color: chartColors.text,
          font: { size: 14, weight: 'bold' }
        }
      }
    }
  })
}
