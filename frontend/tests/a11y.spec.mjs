import { test, expect } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'
import { installAdminApiMock } from './helpers/admin-mock.mjs'

test('admin dashboard has no WCAG A/AA violations in the tested screen', async ({ page }) => {
  // Prevent a transient fade-in from lowering the effective contrast during the audit.
  await page.emulateMedia({ reducedMotion: 'reduce' })
  await installAdminApiMock(page)
  await page.goto('/')
  await expect(page.locator('.admin-header h2')).toBeVisible()
  // The dashboard initially renders a disabled loading state while its data is
  // requested. Audit the stable, usable screen rather than that transient state.
  await expect(page.locator('#admin-tab-overview')).toBeEnabled()
  await expect(page.getByRole('heading', { name: 'Kiểm tra tính nhất quán khi chấm' })).toBeVisible()

  const results = await new AxeBuilder({ page })
    .withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'])
    .analyze()

  expect(results.violations).toEqual([])
})

test('admin ingestion screen reflows without horizontal scrolling at 320px', async ({ page }) => {
  await page.setViewportSize({ width: 320, height: 800 })
  await installAdminApiMock(page)
  await page.goto('/')
  await page.locator('#admin-tab-scenarios').click()

  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }))
  expect(dimensions.scrollWidth).toBeLessThanOrEqual(dimensions.clientWidth)
})
