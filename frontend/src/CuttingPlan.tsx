import { apiFetch } from './auth'
import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Printer, Save, Scissors } from 'lucide-react'
import { CuttingPlanPrint } from './CuttingPlanPrint'
import type { CuttingPlanResult, ProjectInput, SavedCuttingPlan, SaveCuttingPlanRequest } from './types'

const num = (value: number) => new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 2 }).format(value)
const newSaveId = () => {
  if (crypto.randomUUID) return crypto.randomUUID()
  // getRandomValues also works when the app is served over HTTP on a local network.
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6] & 0x0f) | 0x40
  bytes[8] = (bytes[8] & 0x3f) | 0x80
  const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

export function CuttingPlan({ projectId, input, update, onProduced }: {
  onProduced: (parts: ProjectInput['woodParts']) => void
  projectId: number | null
  input: ProjectInput
  update: <K extends keyof ProjectInput>(key: K, value: ProjectInput[K]) => void
}) {
  const [plan, setPlan] = useState<CuttingPlanResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [printing, setPrinting] = useState(false)
  const [savedPlans, setSavedPlans] = useState<SavedCuttingPlan[]>([])
  const [selected, setSelected] = useState<SavedCuttingPlan | null>(null)
  const [saving, setSaving] = useState(false)
  const saveLock = useRef(false)
  const saveId = useRef('')
  const generation = useRef(0)
  // Updating production flags after saving must preserve the saved plan for viewing and printing.
  const planInputKey = JSON.stringify({ ...input, woodParts: input.woodParts.map(part => ({ ...part, isProduced: false })) })
  useEffect(() => { generation.current++; setPlan(null); setSelected(null); setError(''); setBusy(false) }, [planInputKey])
  useEffect(() => {
    if (projectId === null) return
    let cancelled = false
    apiFetch(`/api/projects/${projectId}/cutting-plans`).then(async response => {
      if (!response.ok) throw new Error('Uložené plány se nepodařilo načíst.')
      return response.json() as Promise<SavedCuttingPlan[]>
    }).then(plans => { if (!cancelled) setSavedPlans(current => [...current, ...plans.filter(p => !current.some(c => c.id === p.id))]) }).catch(e => { if (!cancelled) setError(e.message) })
    return () => { cancelled = true }
  }, [projectId])
  useEffect(() => () => { generation.current++ }, [])
  useEffect(() => {
    if (!printing) return
    const finishPrint = () => setPrinting(false)
    window.addEventListener('afterprint', finishPrint)
    const frame = window.requestAnimationFrame(() => {
      try { window.print() } finally { finishPrint() }
    })
    return () => { window.cancelAnimationFrame(frame); window.removeEventListener('afterprint', finishPrint) }
  }, [printing])
  const create = async () => {
    const request = ++generation.current
    setBusy(true); setError(''); setPlan(null); setSelected(null)
    try {
      const response = await apiFetch('/api/cutting-plan', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) })
      const data = await response.json()
      if (!response.ok) throw new Error(data.error || Object.values(data.errors || {}).flat().join(' ') || 'Nářezový plán se nepodařilo vytvořit.')
      if (request === generation.current) { setPlan(data); saveId.current = newSaveId() }
    } catch (e) { if (request === generation.current) setError((e as Error).message) }
    finally { if (request === generation.current) setBusy(false) }
  }
  const save = async () => {
    if (saveLock.current || !plan || selected || projectId === null) return
    saveLock.current = true
    const request = generation.current
    setSaving(true); setError('')
    try {
      const body: SaveCuttingPlanRequest = { id: saveId.current, projectId, input, plan }
      const response = await apiFetch('/api/cutting-plans', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
      const data = await response.json()
      if (!response.ok) throw new Error(data.error || Object.values(data.errors || {}).flat().join(' ') || 'Plán se nepodařilo uložit.')
      const saved = data as SavedCuttingPlan
      setSavedPlans(plans => [saved, ...plans.filter(p => p.id !== saved.id)])
      const indices = new Set(saved.plan.panels.flatMap(panel => panel.parts.map(part => part.partIndex)))
      onProduced(saved.input.woodParts.map((part, index) => indices.has(index) ? { ...part, isProduced: true } : part))
      if (request === generation.current) { setSelected(saved); setPlan(saved.plan) }
    } catch (e) { setError((e as Error).message) }
    finally { saveLock.current = false; setSaving(false) }
  }
  return <section className="panel form-panel">
    <div className="section-title cutting-plan-actions"><div><h2>Nářezový plán ze skladu</h2><p>Lamely z dostupných fošen a zbytků podle dřeviny a tloušťky</p></div><div className="heading-actions"><button type="button" className="secondary" disabled={busy || saving || !input.woodParts.some(part => !part.isProduced)} onClick={create}><Scissors size={17}/>{busy ? 'Připravuji…' : 'Vytvořit nářezový plán'}</button><button type="button" disabled={busy || saving || !plan?.complete || !plan.lamellas.length || !!selected || projectId === null} onClick={save}><Save size={17}/>{saving ? 'Ukládám…' : 'Uložit plán a odečíst sklad'}</button><button type="button" className="secondary" disabled={!plan || busy || printing || saving} onClick={() => setPrinting(true)}><Printer size={17}/> Tisk nářezového plánu</button></div></div>
    {!input.woodParts.some(part => !part.isProduced) && input.woodParts.length > 0 && <p className="info-strip">Všechny dílce jsou vyrobené. Pro další plán přidejte nové dílce.</p>}
    {projectId === null && <p className="info-strip">Před uložením nářezového plánu nejprve uložte zakázku.</p>}
    {!!savedPlans.length && <label className="field"><span>Uložené nářezové plány</span><select disabled={busy || saving} value={selected?.id ?? ''} onChange={e => {
      const saved = savedPlans.find(p => p.id === e.target.value) ?? null
      generation.current++; setSelected(saved); setPlan(saved?.plan ?? null); setError('')
    }}><option value="">Vyberte uložený plán</option>{savedPlans.map(p => <option key={p.id} value={p.id}>{new Date(p.savedAt).toLocaleString('cs-CZ')} – {p.input.name} ({p.plan.lamellas.length} lamel)</option>)}</select></label>}
    {selected && <div className="alert success" role="status">Plán uložen {new Date(selected.savedAt).toLocaleString('cs-CZ')}. Dílce byly označeny jako vyrobené a uzamčeny. Použité fošny byly odečteny a použitelné zbytky přijaty do skladu. Zobrazení a tisk používají uložené zadání.</div>}
    <div className="pricing-grid">
      {([
        ['glueBoardMaxWidthMm', 'Největší šířka spárovky pro spojení dílců (mm)'],
        ['lamellaMinLengthMm', 'Nejmenší délka lamely (mm)'],
        ['lamellaMinWidthMm', 'Nejmenší šířka lamely (mm)'],
        ['lamellaMaxWidthMm', 'Největší šířka lamely (mm)'],
        ['lamellaWidthLossMm', 'Úběr šířky při srovnávání (mm)'],
        ['offcutMinWidthMm', 'Nejmenší šířka zbytku pro sklad (mm)'],
        ['offcutMinLengthMm', 'Nejmenší délka zbytku pro sklad (mm)'],
      ] as const).map(([key, label]) => <label className="field" key={key}><span>{label}</span><input type="number" disabled={saving || !!selected} min={key === 'lamellaWidthLossMm' ? 0 : 1} step="1" value={(selected?.input ?? input)[key]} onChange={e => update(key, Number(e.target.value))}/></label>)}
    </div>
    <div className="info-strip">Plán vychází ze seznamu dílců s přídavkem na délku. Krátké dílce řadí za sebe do minimální délky, dílce podobné délky vedle sebe do největší šířky spárovky. Procentní prořez ignoruje. Prořez pily je 3 mm na řez. Meze šířky platí před srovnáním; úběr se odečítá jednou z každé lamely a požadavek pokrývá až šířka po srovnání. Vytvoření plánu sklad nemění ani nerezervuje materiál. Uložení kompletního plánu odečte použité fošny a přidá použitelné zbytky do skladu.</div>
    {error && <div className="alert error" role="alert">{error}</div>}
    {plan && <>
      <div className={`alert ${plan.complete ? 'success' : 'error'}`} role="status">{plan.complete ? `Nářezový plán je kompletní: ${num(plan.lamellas.length)} lamel z ${new Set(plan.lamellas.map(l => l.boardId)).size} skladových kusů.` : 'Ze současného skladu se nepodařilo sestavit úplný nářezový plán. Níže je částečný plán a chybějící materiál.'}</div>
      {plan.warnings.map(warning => <p key={warning} className="info-strip">{warning}</p>)}
      {!!plan.offcuts.length && <><h3>Odpad a zbytky pro sklad</h3><p>Meze pro ponechání platí pro rozměry po odečtení prořezu. Použitelné zbytky se přijmou do skladu při uložení plánu.</p><div className="table-scroll"><table><thead><tr><th>Fošna / kus</th><th>Délka × šířka</th><th>Poloha (délka / šířka)</th><th>Určení</th></tr></thead><tbody>{plan.offcuts.map((p, i) => <tr key={i}><td>#{p.boardId}</td><td>{num(p.lengthMm)} × {num(p.widthMm)} mm</td><td>{num(p.offsetLengthMm)} / {num(p.offsetWidthMm)} mm</td><td>{p.isKerf ? 'Prořez pily' : p.keepInStock ? 'Ponechat na skladě' : 'Odpad'}</td></tr>)}</tbody></table></div></>}
      {!!plan.panels.length && <div className="table-scroll"><table><thead><tr><th>Deska</th><th>Dřevina / tloušťka</th><th>Délka × šířka</th><th>Dílce a poloha (délka / šířka)</th></tr></thead><tbody>{plan.panels.map(p => <tr key={p.id}><td>#{p.id}</td><td>{p.woodType} / {num(p.boardThicknessMm)} mm</td><td>{num(p.lengthMm)} × {num(p.widthMm)} mm</td><td>{p.parts.map((part, i) => <div key={i}>{part.name} (řádek {part.partIndex + 1}): {num(part.lengthMm)} × {num(part.widthMm)} mm; {num(part.offsetLengthMm)} / {num(part.offsetWidthMm)} mm</div>)}</td></tr>)}</tbody></table></div>}
      {!!plan.shortages.length && <div className="table-scroll"><table><thead><tr><th>Chybějící dřevina</th><th>Tloušťka fošny</th><th>Délka lamely</th><th>Chybějící součet šířek</th></tr></thead><tbody>{plan.shortages.map((s, i) => <tr key={i}><td>{s.woodType}</td><td>{num(s.boardThicknessMm)} mm</td><td>{num(s.lengthMm)} mm</td><td>{num(s.missingWidthMm)} mm</td></tr>)}</tbody></table></div>}
      {!!plan.lamellas.length && <><p>Každý řádek představuje jednu lamelu. Poloha je vzdálenost od začátku skladového kusu. Nejprve řežte na délku, potom na šířku; již podélně nařezané kusy zachovávají svou délku.</p><div className="table-scroll"><table><thead><tr><th>Deska</th><th>Fošna / kus</th><th>Dřevina</th><th>Tloušťka</th><th>Délka lamely</th><th>Šířka pro řezání</th><th>Šířka po srovnání</th><th>Poloha na délku</th><th>Poloha na šířku</th></tr></thead><tbody>{plan.lamellas.map((l, i) => <tr key={i}><td>#{l.panelId}</td><td><strong>#{l.boardId}</strong></td><td>{l.woodType}</td><td>{num(l.boardThicknessMm)} mm</td><td>{num(l.lengthMm)} mm</td><td>{num(l.widthMm)} mm</td><td>{num(l.finishedWidthMm)} mm</td><td>{num(l.offsetLengthMm)} mm</td><td>{num(l.offsetWidthMm)} mm</td></tr>)}</tbody></table></div></>}
    </>}
    {printing && plan && createPortal(<CuttingPlanPrint input={selected?.input ?? input} plan={plan}/>, document.body)}
  </section>
}
