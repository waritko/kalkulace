import { useEffect, useState } from 'react'
import { Plus, RefreshCw, Scissors, X } from 'lucide-react'
import type { Catalog } from './types'

type Board = {
  id: number; woodType: string; widthMm: number; lengthMm: number; thicknessMm: number | null; parentId: number | null
  stage: 'original' | 'length' | 'width'; isCut: boolean
  cutAxis: 'length' | 'width' | null; cutSizeMm: number | null; kerfMm: number | null
}
const num = (value: number) => new Intl.NumberFormat('cs-CZ').format(value)
const stageLabels = { original: 'Celá fošna', length: 'Zkrácený kus', width: 'Nařezaný kus' }

async function stockApi<T>(path = '', body?: unknown): Promise<T> {
  const response = await fetch(`/api/stock/boards${path}`, body === undefined ? undefined : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  })
  if (!response.ok) {
    const data = await response.json().catch(() => ({}))
    throw new Error(data.error || `Chyba serveru (${response.status})`)
  }
  return response.json()
}

export function BoardStock({ catalog, onDirty }: { catalog: Catalog; onDirty: (dirty: boolean) => void }) {
  const [boards, setBoards] = useState<Board[]>([])
  const [woodType, setWoodType] = useState('')
  const [width, setWidth] = useState('')
  const [length, setLength] = useState(3000)
  const [thickness, setThickness] = useState(32)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [search, setSearch] = useState('')
  const [history, setHistory] = useState(false)
  const [cutBoard, setCutBoard] = useState<Board | null>(null)
  const [axis, setAxis] = useState<'length' | 'width'>('length')
  const [size, setSize] = useState('')
  const [kerf, setKerf] = useState('3')
  const [cutError, setCutError] = useState('')

  useEffect(() => { onDirty(!!woodType || !!width || length !== 3000 || thickness !== 32 || !!cutBoard) }, [woodType, width, length, thickness, cutBoard, onDirty])
  useEffect(() => {
    let active = true
    stockApi<Board[]>().then(data => { if (active) setBoards(data) }).catch(e => { if (active) setError(e.message) }).finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [])
  useEffect(() => {
    if (!cutBoard) return
    const close = (event: KeyboardEvent) => { if (event.key === 'Escape' && !busy) setCutBoard(null) }
    window.addEventListener('keydown', close)
    return () => window.removeEventListener('keydown', close)
  }, [cutBoard, busy])

  const refresh = async () => {
    setLoading(true); setError('')
    try { setBoards(await stockApi<Board[]>()) } catch (e) { setError((e as Error).message) } finally { setLoading(false) }
  }
  const add = async (event: React.FormEvent) => {
    event.preventDefault(); setBusy(true); setError(''); setNotice('')
    try {
      const board = await stockApi<Board>('', { woodType, widthMm: Number(width), lengthMm: length, thicknessMm: thickness })
      setBoards(prev => [board, ...prev]); setWoodType(''); setWidth(''); setLength(3000); setThickness(32)
      setNotice(`Fošna #${board.id} byla přidána do skladu.`)
    } catch (e) { setError((e as Error).message) } finally { setBusy(false) }
  }
  const saveThickness = async (board: Board, thicknessMm: number) => {
    setBusy(true); setError(''); setNotice('')
    try {
      const saved = await stockApi<Board>(`/${board.id}/thickness`, { thicknessMm })
      setBoards(prev => prev.map(b => b.id === saved.id ? saved : b))
      setNotice(`Tloušťka kusu #${board.id} byla uložena.`)
    } catch (e) { setError((e as Error).message) } finally { setBusy(false) }
  }
  const editThickness = (board: Board, field: HTMLInputElement) => {
    const value = Number(field.value)
    field.value = board.thicknessMm?.toString() ?? ''
    if (!Number.isInteger(value) || value <= 0 || value > 2147483647) {
      setError('Tloušťka musí být kladná, v celých milimetrech.'); return
    }
    if (value !== board.thicknessMm) void saveThickness(board, value)
  }
  const openCut = (board: Board) => {
    setCutBoard(board); setAxis(board.stage === 'width' ? 'width' : 'length'); setSize(''); setKerf('3'); setCutError('')
  }
  const cut = async (event: React.FormEvent) => {
    event.preventDefault()
    if (!cutBoard || !validCut) return
    setBusy(true); setCutError(''); setNotice('')
    try {
      const pieces = await stockApi<Board[]>(`/${cutBoard.id}/cut`, { axis, sizeMm: Number(size), kerfMm: Number(kerf) })
      setBoards(prev => [...pieces, ...prev.map(b => b.id === cutBoard.id ? { ...b, isCut: true, cutAxis: axis, cutSizeMm: Number(size), kerfMm: Number(kerf) } : b)].sort((a, b) => b.id - a.id))
      setCutBoard(null)
      setNotice(`Kus #${cutBoard.id} byl rozřezán. Ve skladu jsou nové kusy #${pieces[0].id} a #${pieces[1].id}.`)
    } catch (e) { setCutError((e as Error).message) } finally { setBusy(false) }
  }
  const dimension = cutBoard ? (axis === 'length' ? cutBoard.lengthMm : cutBoard.widthMm) : 0
  const remainder = dimension - Number(size) - Number(kerf)
  const validCut = size !== '' && kerf !== '' && Number.isInteger(Number(size)) && Number.isInteger(Number(kerf)) && Number(size) > 0 && Number(kerf) >= 0 && remainder > 0
  const available = boards.filter(b => !b.isCut)
  const visible = boards.filter(b => (history || !b.isCut) && `${b.woodType} #${b.id}`.toLocaleLowerCase('cs').includes(search.toLocaleLowerCase('cs')))

  return <>
    <div className="page-heading"><div><div className="eyebrow">MATERIÁL V DÍLNĚ</div><h1>Sklad fošen<span className="heading-period">.</span></h1><p>Evidujte celé fošny a jejich rozřezání nejprve na délku, poté na šířku.</p></div><button className="secondary" onClick={refresh} disabled={busy || loading}><RefreshCw size={16}/> Obnovit sklad</button></div>
    {error && <div className="alert error" role="alert">{error}</div>}
    {notice && <div className="alert success" role="status">{notice}</div>}
    <section className="panel form-panel">
      <div className="section-title"><h2>Přidat fošnu</h2><span>Každá fošna má vlastní číslo.</span></div>
      <form onSubmit={add} className="stock-add">
        <label className="field"><span>Dřevina</span><input required maxLength={100} list="stock-wood-types" value={woodType} onChange={e => setWoodType(e.target.value)} placeholder="Např. Dub" disabled={busy}/><datalist id="stock-wood-types">{catalog.wood.map(w => <option key={w.name} value={w.name}/>)}</datalist></label>
        <label className="field"><span>Šířka (mm)</span><input required type="number" min="1" max="2147483647" step="1" value={width} onChange={e => setWidth(e.target.value)} placeholder="Např. 300" disabled={busy}/></label>
        <label className="field"><span>Délka</span><select value={length} onChange={e => setLength(Number(e.target.value))} disabled={busy}>{[3000, 4000, 5000].map(l => <option key={l} value={l}>{l / 1000} m</option>)}</select></label>
        <label className="field"><span>Tloušťka (mm)</span><input required type="number" min="1" step="1" value={thickness} onChange={e => setThickness(Number(e.target.value))} disabled={busy}/></label>
        <button className="primary" disabled={busy || loading || !woodType.trim() || !width}><Plus size={17}/>{busy && !cutBoard ? 'Ukládám…' : 'Přidat do skladu'}</button>
      </form>
    </section>
    <section className="panel stock-list">
      <div className="panel-head"><div><h2>Kusy ve skladu</h2><p>{available.length} ks · {num(available.reduce((sum, b) => sum + b.widthMm * b.lengthMm / 1_000_000, 0))} m²</p></div><div className="stock-filters"><label><input type="checkbox" checked={history} onChange={e => setHistory(e.target.checked)}/> Zobrazit i rozřezané</label><input aria-label="Hledat ve skladu" placeholder="Dřevina nebo #číslo" value={search} onChange={e => setSearch(e.target.value)}/></div></div>
      <div className="table-scroll"><table><thead><tr><th>Kus</th><th>Dřevina</th><th>Šířka</th><th>Délka</th><th>Tloušťka</th><th>Původ</th><th>Stav</th><th>Akce</th></tr></thead><tbody>
        {visible.map(board => <tr key={board.id}><td><strong>#{board.id}</strong></td><td>{board.woodType}</td><td>{num(board.widthMm)} mm</td><td>{num(board.lengthMm)} mm</td><td>{board.isCut ? board.thicknessMm ? `${num(board.thicknessMm)} mm` : 'Nezadána' : <input className="table-input small" aria-label={`Tloušťka kusu #${board.id} (mm)`} type="number" min="1" step="1" key={board.thicknessMm} defaultValue={board.thicknessMm ?? ''} placeholder="Doplnit" disabled={busy || loading} onBlur={e => editThickness(board, e.currentTarget)}/>}</td><td>{board.parentId ? `Z kusu #${board.parentId}` : 'Příjem fošny'}</td><td>{board.isCut ? <><strong>Rozřezáno na {board.cutAxis === 'length' ? 'délku' : 'šířku'}</strong><small className="stock-cut-note">Řez {num(board.cutSizeMm!)} mm, prořez {num(board.kerfMm!)} mm<br/>Kusy {boards.filter(b => b.parentId === board.id).map(b => `#${b.id}`).join(', ')}</small></> : stageLabels[board.stage]}</td><td>{!board.isCut && <button className="secondary" disabled={busy || loading} onClick={() => openCut(board)}><Scissors size={15}/> Rozřezat</button>}</td></tr>)}
        {!visible.length && <tr><td colSpan={8} className="stock-empty">{loading ? 'Načítám sklad…' : error ? 'Sklad se nepodařilo načíst. Zkuste jej obnovit.' : boards.length ? 'Žádné kusy neodpovídají filtru.' : 'Sklad je prázdný. Přidejte první fošnu.'}</td></tr>}
      </tbody></table></div>
    </section>
    {cutBoard && <div className="modal-backdrop" onMouseDown={e => { if (e.target === e.currentTarget && !busy) setCutBoard(null) }}><div className="modal" role="dialog" aria-modal="true" aria-labelledby="stock-cut-title"><form onSubmit={cut}>
      <div className="modal-head"><div><div className="eyebrow">{cutBoard.woodType} · #{cutBoard.id}</div><h2 id="stock-cut-title">Rozřezat kus</h2></div><button type="button" className="icon-button" aria-label="Zavřít" disabled={busy} onClick={() => setCutBoard(null)}><X size={21}/></button></div>
      <div className="modal-body">
        <p>Rozměry: {num(cutBoard.widthMm)} × {num(cutBoard.lengthMm)} mm (šířka × délka).</p>
        {cutError && <div className="alert error" role="alert">{cutError}</div>}
        <div className="form-grid">
          <label className="field wide"><span>Směr řezu</span><select autoFocus value={axis} onChange={e => { setAxis(e.target.value as typeof axis); setSize('') }} disabled={busy}>{cutBoard.stage !== 'width' && <option value="length">Na délku — zkrácení fošny</option>}{cutBoard.stage !== 'original' && <option value="width">Na šířku — podélné rozřezání</option>}</select><small className="field-help">{cutBoard.stage === 'original' ? 'Celou fošnu nejprve zkraťte na délku.' : cutBoard.stage === 'width' ? 'Nařezaný kus můžete dále rozřezat na šířku.' : 'Zkrácený kus můžete dále zkrátit nebo rozřezat na šířku.'}</small></label>
          <label className="field"><span>{axis === 'length' ? 'Délka' : 'Šířka'} prvního kusu (mm)</span><input required type="number" min="1" max={dimension - Number(kerf) - 1} step="1" value={size} onChange={e => setSize(e.target.value)} disabled={busy}/></label>
          <label className="field"><span>Prořez pily (mm)</span><input required type="number" min="0" max={dimension - 2} step="1" value={kerf} onChange={e => setKerf(e.target.value)} disabled={busy}/></label>
        </div>
        {validCut ? <div className="stock-preview" role="status"><strong>Vzniknou dva kusy:</strong><p>{num(axis === 'width' ? Number(size) : cutBoard.widthMm)} × {num(axis === 'length' ? Number(size) : cutBoard.lengthMm)} mm</p><p>{num(axis === 'width' ? remainder : cutBoard.widthMm)} × {num(axis === 'length' ? remainder : cutBoard.lengthMm)} mm</p><small>Původní kus zůstane v historii. Oba nové kusy budou ve skladu.</small></div> : <p className="stock-cut-note">Zadejte rozměr řezu tak, aby po odečtení prořezu zbyly dva kladné kusy.</p>}
      </div>
      <div className="modal-foot"><button type="button" className="secondary" disabled={busy} onClick={() => setCutBoard(null)}>Zrušit</button><button className="primary" disabled={busy || !validCut}><Scissors size={17}/>{busy ? 'Ukládám…' : 'Potvrdit rozřezání'}</button></div>
    </form></div></div>}
  </>
}
