import { spawn } from 'node:child_process'

async function waitForServer(url, timeoutMs = 10_000) {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    try {
      const response = await fetch(url)
      if (response.ok) return
    } catch {
      // The static server is still starting.
    }
    await new Promise(resolve => setTimeout(resolve, 100))
  }
  throw new Error(`E2E server did not start at ${url}`)
}

export default async function globalSetup() {
  const server = spawn(process.execPath, ['tests/e2e-server.mjs'], {
    stdio: 'ignore',
    windowsHide: true,
  })
  await waitForServer('http://127.0.0.1:4173/')

  return async () => {
    if (!server.killed) server.kill()
  }
}
