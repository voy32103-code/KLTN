import type { UserInfo } from './types'

export function tryParseJson(text: string) {
  try {
    return JSON.parse(text) as unknown
  } catch {
    return text
  }
}

export function extractApiError(data: unknown): string | null {
  if (typeof data === 'string') return data
  if (!data || typeof data !== 'object') return null
  const value = data as Record<string, unknown>
  if (typeof value.error === 'string') return value.error
  if (typeof value.detail === 'string') return value.detail
  if (typeof value.message === 'string') return value.message
  if (Array.isArray(value.errors)) return value.errors.join(', ')
  if (value.errors && typeof value.errors === 'object') {
    return Object.values(value.errors as Record<string, unknown>).flat().join(', ')
  }
  return null
}

export function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Đã xảy ra lỗi không xác định.'
}

export function readUserFromToken(token: string): UserInfo {
  const payload = decodeJwtPayload(token)
  if (!payload) return {}
  return {
    email: readClaim(payload, [
      'email',
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress',
    ]),
    role: readClaim(payload, [
      'role',
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role',
    ]),
  }
}

export function decodeJwtPayload(token: string): Record<string, unknown> | null {
  const [, payload] = token.split('.')
  if (!payload) return null
  try {
    const normalized = payload.replace(/-/g, '+').replace(/_/g, '/')
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=')
    return JSON.parse(atob(padded)) as Record<string, unknown>
  } catch {
    return null
  }
}

export function isTokenExpired(token: string, nowMs = Date.now()) {
  const payload = decodeJwtPayload(token)
  const exp = payload?.exp
  if (typeof exp !== 'number') return false
  return exp * 1000 <= nowMs
}

export function readClaim(payload: Record<string, unknown>, keys: string[]) {
  for (const key of keys) {
    const value = payload[key]
    if (typeof value === 'string') return value
  }
  return undefined
}

export function isPrivilegedRole(role?: string) {
  return role === 'Lecturer' || role === 'Admin'
}

export function formatScore(score: number | null) {
  return typeof score === 'number' ? `${Math.round(score)}%` : 'N/A'
}

export function formatScoreClass(score: number | null) {
  if (typeof score !== 'number') return 'unknown'
  if (score >= 70) return 'strong'
  if (score >= 45) return 'medium'
  return 'low'
}

export function formatThreshold(value: number) {
  return `${Math.round(value * 100)}%`
}

export function formatTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return ''
  return date.toLocaleTimeString('vi-VN', { hour: '2-digit', minute: '2-digit' })
}

export function shortId(value: string) {
  return value ? `${value.slice(0, 8)}...` : 'N/A'
}

export function escapeHtml(value: unknown) {
  if (value === null || value === undefined) return ''
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;')
}

export function escapeAttribute(value: unknown) {
  return escapeHtml(value)
}
