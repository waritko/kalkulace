import { useEffect, useState } from 'react'
import { authApi } from './auth'
import type { UserInfo, UserRequest } from './types'

const blank = (): UserRequest => ({ username: '', password: '', isAdmin: false })

export function UserManagement({ currentUser, onDirty }: { currentUser: UserInfo; onDirty: (dirty: boolean) => void }) {
  const [users, setUsers] = useState<UserInfo[]>([])
  const [editing, setEditing] = useState<number | null>(null)
  const [draft, setDraft] = useState<UserRequest>(blank)
  const [busy, setBusy] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const refresh = async () => setUsers(await authApi<UserInfo[]>('/users'))
  useEffect(() => { refresh().catch(e => setError(e.message)).finally(() => setLoading(false)) }, [])
  const update = (patch: Partial<UserRequest>) => { setDraft(prev => ({ ...prev, ...patch })); onDirty(true); setNotice('') }
  const reset = () => { setDraft(blank()); setEditing(null); onDirty(false) }
  return <><div className="page-heading"><div><div className="eyebrow">ADMINISTRACE</div><h1>Uživatelé<span className="heading-period">.</span></h1><p>Správa účtů, hesel a oprávnění.</p></div></div>
    {error && <div className="alert error" role="alert">{error}</div>}{notice && <div className="alert" role="status">{notice}</div>}
    <section className="panel form-panel"><h2>{editing === null ? 'Nový uživatel' : 'Upravit uživatele'}</h2>
      <form onSubmit={async event => {
        event.preventDefault(); setBusy(true); setError(''); setNotice('')
        try {
          await authApi<UserInfo>(editing === null ? '/users' : `/users/${editing}`, { method: editing === null ? 'POST' : 'PUT', body: JSON.stringify(draft) })
          const self = editing === currentUser.id
          reset()
          if (self) window.dispatchEvent(new Event('session-expired'))
          else { await refresh(); setNotice('Uživatel byl uložen.') }
        } catch (e) { setError((e as Error).message) } finally { setBusy(false) }
      }}>
        <div className="form-grid">
          <label className="field"><span>Uživatelské jméno</span><input required maxLength={100} autoComplete="off" value={draft.username} disabled={busy} onChange={e => update({ username: e.target.value })}/></label>
          <label className="field"><span>{editing === null ? 'Heslo' : 'Nové heslo (prázdné zachová současné)'}</span><input type="password" autoComplete="new-password" required={editing === null} minLength={8} maxLength={128} value={draft.password} disabled={busy} onChange={e => update({ password: e.target.value })}/></label>
          <label className="user-role"><input type="checkbox" checked={draft.isAdmin} disabled={busy} onChange={e => update({ isAdmin: e.target.checked })}/> Administrátor</label>
        </div>
        <p className="field-help">Administrátor spravuje uživatele, ceník a výchozí nastavení. Změna účtu ukončí jeho dosavadní přihlášení.</p>
        <div className="user-actions"><button className="primary" disabled={busy} type="submit">{busy ? 'Ukládám…' : 'Uložit uživatele'}</button><button className="secondary" type="button" disabled={busy} onClick={reset}>Zrušit / nový uživatel</button></div>
      </form>
    </section>
    <section className="panel list-panel user-list"><div className="panel-head"><h2>Seznam uživatelů</h2></div>
      {loading ? <p role="status">Načítám uživatele…</p> : <div className="table-scroll"><table><thead><tr><th>Jméno</th><th>Oprávnění</th><th>Akce</th></tr></thead><tbody>{users.map(user => <tr key={user.id}><td>{user.username}{user.id === currentUser.id ? ' (vy)' : ''}</td><td>{user.isAdmin ? 'Administrátor' : 'Uživatel'}</td><td><div className="user-actions">
        <button className="secondary" disabled={busy} onClick={() => { if (draft.username && !window.confirm('Zahodit rozepsané změny uživatele?')) return; setEditing(user.id); setDraft({ username: user.username, isAdmin: user.isAdmin, password: '' }); onDirty(false); setError(''); setNotice('') }}>Upravit</button>
        <button className="secondary" disabled={busy} onClick={async () => {
          if (!window.confirm(`Odstranit uživatele ${user.username}?`)) return
          setBusy(true); setError('')
          try { await authApi(`/users/${user.id}`, { method: 'DELETE' }); if (editing === user.id) reset(); if (user.id === currentUser.id) window.dispatchEvent(new Event('session-expired')); else await refresh() }
          catch (e) { setError((e as Error).message) } finally { setBusy(false) }
        }}>Odstranit</button>
      </div></td></tr>)}</tbody></table></div>}
    </section>
  </>
}
