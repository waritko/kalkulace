import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { Printer, Scissors } from 'lucide-react'
import { CuttingPlanPrint } from './CuttingPlanPrint'
import type { CuttingPlanResult, ProjectInput } from './types'

const num = (value: number) => new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 2 }).format(value)

export function CuttingPlan({ input, update }: {
  input: ProjectInput
  update: <K extends keyof ProjectInput>(key: K, value: ProjectInput[K]) => void
}) {
  const [plan, setPlan] = useState<CuttingPlanResult | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [printing, setPrinting] = useState(false)
  const generation = useRef(0)
  useEffect(() => { generation.current++; setPlan(null); setError(''); setBusy(false) }, [input])
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
    setBusy(true); setError(''); setPlan(null)
    try {
      const response = await fetch('/api/cutting-plan', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input) })
      const data = await response.json()
      if (!response.ok) throw new Error(data.error || Object.values(data.errors || {}).flat().join(' ') || 'Nářezový plán se nepodařilo vytvořit.')
      if (request === generation.current) setPlan(data)
    } catch (e) { if (request === generation.current) setError((e as Error).message) }
    finally { if (request === generation.current) setBusy(false) }
  }
  return <section className="panel form-panel">
    <div className="section-title cutting-plan-actions"><div><h2>Nářezový plán ze skladu</h2><p>Lamely z dostupných fošen a zbytků podle dřeviny a tloušťky</p></div><div className="heading-actions"><button type="button" className="secondary" disabled={busy || !input.woodParts.length} onClick={create}><Scissors size={17}/>{busy ? 'Připravuji…' : 'Vytvořit nářezový plán'}</button><button type="button" className="secondary" disabled={!plan || busy || printing} onClick={() => setPrinting(true)}><Printer size={17}/> Tisk nářezového plánu</button></div></div>
    <div className="pricing-grid">
      {([
        ['glueBoardMaxWidthMm', 'Největší šířka spárovky pro spojení dílců (mm)'],
        ['lamellaMinLengthMm', 'Nejmenší délka lamely (mm)'],
        ['lamellaMinWidthMm', 'Nejmenší šířka lamely (mm)'],
        ['lamellaMaxWidthMm', 'Největší šířka lamely (mm)'],
        ['lamellaWidthLossMm', 'Úběr šířky při srovnávání (mm)'],
      ] as const).map(([key, label]) => <label className="field" key={key}><span>{label}</span><input type="number" min={key === 'lamellaWidthLossMm' ? 0 : 1} step="1" value={input[key]} onChange={e => update(key, Number(e.target.value))}/></label>)}
    </div>
    <div className="info-strip">Plán vychází ze seznamu dílců s přídavkem na délku. Krátké dílce řadí za sebe do minimální délky, dílce podobné délky vedle sebe do největší šířky spárovky. Procentní prořez ignoruje. Prořez pily je 3 mm na řez. Meze šířky platí před srovnáním; úběr se odečítá jednou z každé lamely a požadavek pokrývá až šířka po srovnání. Vytvoření plánu sklad nemění ani nerezervuje materiál. Před řezáním jej obnovte podle aktuálního skladu.</div>
    {error && <div className="alert error" role="alert">{error}</div>}
    {plan && <>
      <div className={`alert ${plan.complete ? 'success' : 'error'}`} role="status">{plan.complete ? `Nářezový plán je kompletní: ${num(plan.lamellas.length)} lamel z ${new Set(plan.lamellas.map(l => l.boardId)).size} skladových kusů.` : 'Ze současného skladu se nepodařilo sestavit úplný nářezový plán. Níže je částečný plán a chybějící materiál.'}</div>
      {plan.warnings.map(warning => <p key={warning} className="info-strip">{warning}</p>)}
      {!!plan.panels.length && <div className="table-scroll"><table><thead><tr><th>Deska</th><th>Dřevina / tloušťka</th><th>Délka × šířka</th><th>Dílce a poloha (délka / šířka)</th></tr></thead><tbody>{plan.panels.map(p => <tr key={p.id}><td>#{p.id}</td><td>{p.woodType} / {num(p.boardThicknessMm)} mm</td><td>{num(p.lengthMm)} × {num(p.widthMm)} mm</td><td>{p.parts.map((part, i) => <div key={i}>{part.name} (řádek {part.partIndex + 1}): {num(part.lengthMm)} × {num(part.widthMm)} mm; {num(part.offsetLengthMm)} / {num(part.offsetWidthMm)} mm</div>)}</td></tr>)}</tbody></table></div>}
      {!!plan.shortages.length && <div className="table-scroll"><table><thead><tr><th>Chybějící dřevina</th><th>Tloušťka fošny</th><th>Délka lamely</th><th>Chybějící součet šířek</th></tr></thead><tbody>{plan.shortages.map((s, i) => <tr key={i}><td>{s.woodType}</td><td>{num(s.boardThicknessMm)} mm</td><td>{num(s.lengthMm)} mm</td><td>{num(s.missingWidthMm)} mm</td></tr>)}</tbody></table></div>}
      {!!plan.lamellas.length && <><p>Každý řádek představuje jednu lamelu. Poloha je vzdálenost od začátku skladového kusu. Nejprve řežte na délku, potom na šířku; již podélně nařezané kusy zachovávají svou délku.</p><div className="table-scroll"><table><thead><tr><th>Deska</th><th>Fošna / kus</th><th>Dřevina</th><th>Tloušťka</th><th>Délka lamely</th><th>Šířka pro řezání</th><th>Šířka po srovnání</th><th>Poloha na délku</th><th>Poloha na šířku</th></tr></thead><tbody>{plan.lamellas.map((l, i) => <tr key={i}><td>#{l.panelId}</td><td><strong>#{l.boardId}</strong></td><td>{l.woodType}</td><td>{num(l.boardThicknessMm)} mm</td><td>{num(l.lengthMm)} mm</td><td>{num(l.widthMm)} mm</td><td>{num(l.finishedWidthMm)} mm</td><td>{num(l.offsetLengthMm)} mm</td><td>{num(l.offsetWidthMm)} mm</td></tr>)}</tbody></table></div></>}
    </>}
    {printing && plan && createPortal(<CuttingPlanPrint input={input} plan={plan}/>, document.body)}
  </section>
}
