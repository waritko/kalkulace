import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import type { UserInfo } from './types'

export async function apiFetch(path: string, init?: RequestInit) {
  const headers = new Headers(init?.headers)
  headers.set('X-Kalkulace-Request', '1')
  const response = await fetch(path, { ...init, headers, credentials: 'same-origin' })
  if (response.status === 401 && !path.startsWith('/api/auth/')) window.dispatchEvent(new Event('session-expired'))
  return response
}

export async function authApi<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await apiFetch(`/api${path}`, { ...init, headers: { 'Content-Type': 'application/json', ...init?.headers } })
  if (!response.ok) {
    const body = await response.json().catch(() => ({}))
    throw new Error(body.error || (response.status === 429 ? 'Příliš mnoho pokusů. Zkuste to za minutu.' : `Chyba serveru (${response.status})`))
  }
  return response.status === 204 ? undefined as T : response.json()
}

export function AuthGate({ children }: { children: (user: UserInfo, logout: () => Promise<void>) => ReactNode }) {
  const [user, setUser] = useState<UserInfo | null>(null)
  const [loading, setLoading] = useState(true)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  useEffect(() => {
    let active = true
    apiFetch('/api/auth/me').then(async response => {
      if (response.ok) { const current = await response.json(); if (active) setUser(current) }
      else if (response.status !== 401) throw new Error('Nepodařilo se ověřit přihlášení.')
    }).catch(e => { if (active) setError(e.message) }).finally(() => { if (active) setLoading(false) })
    const expired = () => { setUser(null); setPassword(''); setError('Přihlášení vypršelo nebo byl účet změněn. Přihlaste se znovu.') }
    window.addEventListener('session-expired', expired)
    return () => { active = false; window.removeEventListener('session-expired', expired) }
  }, [])
  const logout = async () => {
    await authApi('/auth/logout', { method: 'POST' })
    setUser(null); setPassword(''); setError('')
  }
  if (loading) return <div className="login-screen"><p role="status">Ověřuji přihlášení…</p></div>
  if (user) return children(user, logout)
  return <div className="login-screen"><section className="panel login-panel">
    <div className="brand"><div className="brand-mark"><span>K</span></div><div><strong>Kalkulace</strong><small>TRUHLÁŘSKÁ DÍLNA</small></div></div>
    <h1>Přihlášení</h1><p>Přihlaste se pro práci se zakázkami a skladem.</p>
    <form onSubmit={async event => {
      event.preventDefault(); setBusy(true); setError('')
      try { const current = await authApi<UserInfo>('/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }); setPassword(''); setUser(current) }
      catch (e) { setError((e as Error).message) } finally { setBusy(false) }
    }}>
      {error && <div className="alert error" role="alert">{error}</div>}
      <label className="field"><span>Uživatelské jméno</span><input autoFocus autoComplete="username" value={username} maxLength={100} required onChange={e => setUsername(e.target.value)}/></label>
      <label className="field"><span>Heslo</span><input type="password" autoComplete="current-password" value={password} maxLength={128} required onChange={e => setPassword(e.target.value)}/></label>
      <button className="primary" type="submit" disabled={busy}>{busy ? 'Přihlašuji…' : 'Přihlásit se'}</button>
    </form>
  </section></div>
}
