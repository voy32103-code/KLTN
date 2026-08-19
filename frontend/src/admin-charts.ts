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
  text: '#151515',
  secondaryText: '#3d3d3d',
  muted: '#5f5f5f',
  grid: '#d8d8d8',
  violet: '#5546FF',
  mint: '#9BE8B8',
  yellow: '#FFDE59',
  coral: '#FF8B73',
  rose: '#FF9CA7',
}

Chart.defaults.color = chartColors.text
Chart.defaults.font.family = 'Arial, sans-serif'

const neoTooltip = {
  backgroundColor: chartColors.text,
  titleColor: '#ffffff',
  bodyColor: '#ffffff',
  borderColor: chartColors.text,
  borderWidth: 2,
  cornerRadius: 0,
  padding: 10,
  titleFont: { weight: 'bold' as const },
}

const neoScale = {
  ticks: { color: chartColors.text, font: { weight: 'bold' as const } },
  grid: { color: chartColors.grid, lineWidth: 1 },
  border: { color: chartColors.text, width: 2 },
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
            chartColors.rose,
            chartColors.coral,
            chartColors.yellow,
            chartColors.mint,
            chartColors.violet,
          ],
          borderColor: chartColors.text,
          borderWidth: 2,
          borderRadius: 0
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: neoTooltip,
        title: {
          display: true,
          text: 'Phân bổ Coverage Score (%)',
          color: chartColors.text,
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: { x: neoScale, y: { ...neoScale, ticks: { ...neoScale.ticks, stepSize: 1 }, beginAtZero: true } }
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
          borderColor: chartColors.violet,
          backgroundColor: 'rgba(85, 70, 255, 0.16)',
          fill: true,
          tension: 0,
          borderWidth: 3,
          pointRadius: 5,
          pointHoverRadius: 7,
          pointBackgroundColor: chartColors.yellow,
          pointBorderColor: chartColors.text,
          pointBorderWidth: 2,
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { display: false },
        tooltip: neoTooltip,
        title: {
          display: true,
          text: 'Xu hướng phiên phỏng vấn theo ngày',
          color: chartColors.text,
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: {
        x: { ...neoScale, ticks: { ...neoScale.ticks, maxRotation: 45 } },
        y: { ...neoScale, ticks: { ...neoScale.ticks, stepSize: 1 }, beginAtZero: true },
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
          backgroundColor: chartColors.violet,
          borderColor: chartColors.text,
          borderWidth: 2,
          borderRadius: 0
        },
        {
          label: 'Số lượt hội thoại TB',
          data: safeScenarios.map(s => s.averageTurns),
          backgroundColor: chartColors.mint,
          borderColor: chartColors.text,
          borderWidth: 2,
          borderRadius: 0
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { labels: { color: chartColors.text, font: { weight: 'bold' } } },
        tooltip: neoTooltip,
        title: {
          display: true,
          text: 'So sánh hiệu suất theo Kịch bản',
          color: chartColors.text,
          font: { size: 14, weight: 'bold' }
        }
      },
      scales: { x: neoScale, y: { ...neoScale, beginAtZero: true } }
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
      labels: ['Khớp hoàn toàn', 'Khớp ngữ nghĩa', 'Khớp một phần', 'Chưa khớp'],
      datasets: [
        {
          data: [exact, semantic, partial, missed],
          backgroundColor: [chartColors.mint, chartColors.violet, chartColors.yellow, chartColors.rose],
          borderColor: chartColors.text,
          borderWidth: 3
        }
      ]
    },
    options: {
      responsive: true,
      maintainAspectRatio: false,
      plugins: {
        legend: { position: 'right', labels: { color: chartColors.text, font: { size: 12, weight: 'bold' } } },
        tooltip: neoTooltip,
        title: {
          display: true,
          text: 'Tỷ lệ Loại so khớp Requirement',
          color: chartColors.text,
          font: { size: 14, weight: 'bold' }
        }
      },
      cutout: '58%'
    }
  })
}
