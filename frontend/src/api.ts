import { EXPIRED_SESSION_NOTICE } from './constants'
import { extractApiError, isTokenExpired, tryParseJson } from './utils'

type RequestOptions = {
  method?: string
  body?: unknown
  auth?: boolean
}

type ApiClientOptions = {
  baseUrl: string
  getToken: () => string | null
  onUnauthorized: () => void
}

export function createApiClient(options: ApiClientOptions) {
  async function request<T = unknown>(
    path: string,
    requestOptions: RequestOptions = {},
  ): Promise<T> {
    const isProtectedRequest = requestOptions.auth !== false
    const token = options.getToken()
    const headers: Record<string, string> = {
      Accept: 'application/json',
    }

    if (requestOptions.body !== undefined) {
      headers['Content-Type'] = 'application/json'
    }

    if (isProtectedRequest && token) {
      headers.Authorization = `Bearer ${token}`
    }

    if (isProtectedRequest && token && isTokenExpired(token)) {
      options.onUnauthorized()
      throw new Error(EXPIRED_SESSION_NOTICE)
    }

    const response = await fetch(`${options.baseUrl}${path}`, {
      method: requestOptions.method ?? 'GET',
      headers,
      body: requestOptions.body === undefined ? undefined : JSON.stringify(requestOptions.body),
    })

    const text = await response.text()
    const data = text ? tryParseJson(text) : null

    if (response.status === 401 && !isProtectedRequest) {
      const message = extractApiError(data) ?? 'Đăng nhập không thành công.'
      throw new Error(message)
    }

    if (response.status === 401) {
      options.onUnauthorized()
      throw new Error('Phiên đăng nhập đã hết hạn.')
    }

    if (!response.ok) {
      const message = extractApiError(data) ?? `Request thất bại (${response.status}).`
      throw new Error(message)
    }

    return data as T
  }

  return { request }
}
