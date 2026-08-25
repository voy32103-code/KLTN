import { test, expect } from '@playwright/test'
import { installAdminApiMock } from './helpers/admin-mock.mjs'

test('admin can queue a mock video/audio upload and sees it in history', async ({ page }) => {
  await installAdminApiMock(page)
  await page.goto('/')
  await page.locator('#admin-tab-scenarios').click()

  await page.locator('#admin-video-path-input').setInputFiles({
    name: 'meeting.mp3',
    mimeType: 'audio/mpeg',
    buffer: Buffer.from('mock-audio'),
  })
  await page.locator('[data-action="admin-video"]').click()

  await expect(page.getByRole('status')).toContainText('Đã xếp hàng và kích hoạt worker')
  await expect(page.getByText('meeting.mp3', { exact: true })).toBeVisible()
  await expect(page.locator('.ingestion-job-item')).toContainText('Queued')
})
