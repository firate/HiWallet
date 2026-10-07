import { vi } from 'vitest'
import type { StaffPermission } from '../types'

export interface Call {
  method: string
  path: string
  headers: Record<string, string>
  body: unknown
}

type Reply = { status: number; body?: unknown }

/**
 * BFF'in yerine: her istek kaydediliyor, cevap yol ve yönteme göre sıradaki kayıttan.
 * Aynı uca birden fazla cevap verilirse sırayla dönüyor, sonuncusu tekrarlanıyor.
 */
export function fakeBff(routes: Record<string, Reply | Reply[]>): Call[] {
  const calls: Call[] = []
  const served: Record<string, number> = {}

  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: string, init: RequestInit = {}) => {
      const method = init.method ?? 'GET'
      const key = `${method} ${input}`
      const headers = { ...(init.headers as Record<string, string>) }

      calls.push({
        method,
        path: input,
        headers,
        body: typeof init.body === 'string' ? JSON.parse(init.body) : undefined,
      })

      const configured = routes[key]
      if (configured === undefined) {
        return new Response(null, { status: 404 })
      }

      const replies = Array.isArray(configured) ? configured : [configured]
      const index = Math.min(served[key] ?? 0, replies.length - 1)
      served[key] = index + 1

      const { status, body } = replies[index]
      return new Response(body === undefined ? null : JSON.stringify(body), {
        status,
        headers: { 'Content-Type': 'application/json' },
      })
    }),
  )

  return calls
}

/**
 * Oturumdaki çalışan: kimliği BFF'in oturumundan, rolleri ve izinleri personel
 * yönetiminden (/v1/me). Panel izni token'dan değil buradan okuyor.
 */
export function staffSession(
  permissions: StaffPermission[],
  who: { subject?: string; name?: string; roles?: string[] } = {},
): Record<string, Reply> {
  const { subject = 's1', name = 'Çalışan', roles = [] } = who

  return {
    'GET /bff/user': { status: 200, body: { subject, name, email: null } },
    'GET /v1/me': { status: 200, body: { subject, roles, permissions } },
  }
}
