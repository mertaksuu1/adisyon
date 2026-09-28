// API'ye istek atan tek yer. Her isteğe cihaz anahtarını ve (varsa) giriş token'ını ekler,
// hataları okunabilir bir ApiError'a çevirir.

import { storage } from '../auth/storage'

/**
 * Sunucu hataları "ProblemDetails" biçiminde döner: { status, title, ... }.
 * title alanı kullanıcıya gösterilebilecek Türkçe mesajdır.
 */
export class ApiError extends Error {
  readonly status: number
  readonly extensions: Record<string, unknown>

  constructor(status: number, message: string, extensions: Record<string, unknown> = {}) {
    super(message)
    this.status = status
    this.extensions = extensions
  }
}

/** Oturum geçersiz olunca (401) çağrılır; AuthProvider bunu dinleyip PIN ekranına döner. */
let onUnauthorized: (() => void) | null = null
export function setUnauthorizedHandler(handler: (() => void) | null) {
  onUnauthorized = handler
}

export async function api<T>(method: string, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const device = storage.getDevice()
  if (device) headers['X-Device-Token'] = device.deviceToken
  const session = storage.getSession()
  if (session) headers.Authorization = `Bearer ${session.token}`

  let response: Response
  try {
    response = await fetch(`/api${path}`, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, 'Sunucuya ulaşılamıyor. Bağlantıyı kontrol edin.')
  }

  if (response.ok) {
    return (response.status === 204 ? undefined : await response.json()) as T
  }

  const problem = await response.json().catch(() => ({}))
  const { title, errors, status: _status, type: _type, traceId: _traceId, ...extensions } = problem as Record<string, unknown>

  // Oturumlu bir istek 401 aldıysa token geçersiz/süresi dolmuş demektir.
  if (response.status === 401 && session) onUnauthorized?.()

  throw new ApiError(response.status, errorMessage(response.status, title, errors), extensions)
}

/** Doğrulama hatalarında (400) sunucu alan bazında mesajlar döner; ilkini gösteriyoruz. */
function errorMessage(status: number, title: unknown, errors: unknown): string {
  if (errors && typeof errors === 'object') {
    const first = Object.values(errors as Record<string, string[]>)[0]?.[0]
    if (first) return first
  }
  if (typeof title === 'string' && title) return title
  if (status === 403) return 'Bu işlem için yetkiniz yok.'
  return `Beklenmeyen bir hata oluştu (${status}).`
}
