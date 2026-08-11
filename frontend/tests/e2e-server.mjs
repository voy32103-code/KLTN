import { createReadStream, existsSync } from 'node:fs'
import { createServer } from 'node:http'
import { extname, normalize, resolve } from 'node:path'

const root = resolve(import.meta.dirname, '../dist')
const contentTypes = {
  '.css': 'text/css; charset=utf-8',
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.svg': 'image/svg+xml',
}

const server = createServer((request, response) => {
  const requestPath = new URL(request.url ?? '/', 'http://127.0.0.1').pathname
  const relativePath = requestPath === '/' ? 'index.html' : requestPath.replace(/^\/+/, '')
  const requestedFile = resolve(root, normalize(relativePath))
  const file = requestedFile.startsWith(root) && existsSync(requestedFile)
    ? requestedFile
    : resolve(root, 'index.html')

  response.writeHead(200, { 'Content-Type': contentTypes[extname(file)] ?? 'application/octet-stream' })
  createReadStream(file).pipe(response)
})

const shutdown = () => server.close(() => process.exit(0))
process.on('SIGINT', shutdown)
process.on('SIGTERM', shutdown)
server.listen(4173, '127.0.0.1')
