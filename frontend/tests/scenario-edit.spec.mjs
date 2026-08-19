import { test, expect } from '@playwright/test'
import { installAdminApiMock } from './helpers/admin-mock.mjs'

test('admin can load an active scenario into an editable versioned draft', async ({ page }) => {
  await installAdminApiMock(page)
  await page.goto('/')
  await expect(page.locator('#admin-tab-overview')).toBeEnabled()
  await page.locator('#admin-tab-scenarios').click()

  await page.locator('#admin-published-scenario-select').selectOption('scenario-1')
  await page.getByRole('button', { name: 'Tải để chỉnh sửa' }).click()

  await expect(page.locator('#admin-scenario-preview-form')).toBeVisible()
  await expect(page.locator('[data-draft-field="scenario_key"]')).toHaveValue('library_booking')
  await expect(page.locator('[data-draft-field="text"]')).toHaveValue('Students can reserve an available room.')
  await expect(page.getByText('Publish sẽ tạo phiên bản mới', { exact: false })).toBeVisible()
})
