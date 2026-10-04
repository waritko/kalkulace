import { Printer } from 'lucide-react'
import type { Calculation, ProjectInput } from './types'

const num = (value: number) => new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 2 }).format(value)

export function GlueBoardPurchase({ input, result, update, onPrint, printWorkshop }: {
  input: ProjectInput
  result: Calculation | null
  update: <K extends keyof ProjectInput>(key: K, value: ProjectInput[K]) => void
  onPrint: () => void
  printWorkshop: boolean
}) {
  return <section className="panel form-panel wood-purchase">
    <div className="section-title"><div><h2>Nákupní seznam lamel pro spárovku</h2><p>Potřebná šířka podle dřeviny, tloušťky a nákupní délky</p></div><button type="button" className="secondary" onClick={onPrint} disabled={!result?.glueBoardPurchase?.length}><Printer size={17}/> Tisk lamel a dílců</button></div>
    <div className="pricing-grid glue-board-settings">
      <label className="field"><span>Přídavek k délce lamely (mm)</span><input type="number" min="0" step="any" value={input.lamellaLengthExtraMm} onChange={e => update('lamellaLengthExtraMm', Number(e.target.value))}/></label>
      <label className="field"><span>Sloučit délky v rozmezí (mm)</span><input type="number" min="0" step="any" value={input.lamellaMergeToleranceMm} onChange={e => update('lamellaMergeToleranceMm', Number(e.target.value))}/></label>
      <label className="field"><span>Prořez (%)</span><input type="number" min="0" max="100" step="any" value={input.glueBoardWastePercent} onChange={e => update('glueBoardWastePercent', Number(e.target.value))}/></label>
    </div>
    <div className="info-strip">Délka lamely vychází z delší strany dílu a přídavku. Šířky kratších stran se násobí počtem kusů, sečtou a navýší o prořez. Podobné délky se zařadí k nejdelší délce v dané skupině.</div>
    {!!result?.glueBoardPurchase?.length && <div className="table-scroll"><table><thead><tr><th>Dřevina</th><th>Tloušťka spárovky</th><th>Délka lamely</th><th>Celková šířka s prořezem</th></tr></thead><tbody>{result.glueBoardPurchase.map(item => <tr key={`${item.woodType}-${item.thicknessMm}-${item.lamellaLengthMm}`}><td><strong>{item.woodType}</strong></td><td>{num(item.thicknessMm)} mm</td><td>{num(item.lamellaLengthMm)} mm</td><td className="table-total">{num(item.totalWidthMm)} mm</td></tr>)}</tbody></table></div>}
    {printWorkshop && result && <article className="workshop-print-sheet">
      <h1>Dílenský seznam – {input.name || 'Zakázka'}</h1>
      {input.customerName && <p>Odběratel: {input.customerName}</p>}
      <h2>Lamely pro spárovku</h2>
      <table><thead><tr><th>Dřevina</th><th>Tloušťka</th><th>Délka lamely</th><th>Celková šířka s prořezem</th></tr></thead><tbody>{result.glueBoardPurchase.map(item => <tr key={`${item.woodType}-${item.thicknessMm}-${item.lamellaLengthMm}`}><td>{item.woodType}</td><td>{num(item.thicknessMm)} mm</td><td>{num(item.lamellaLengthMm)} mm</td><td>{num(item.totalWidthMm)} mm</td></tr>)}</tbody></table>
      <p className="workshop-print-note">Přídavek k délce: {num(input.lamellaLengthExtraMm)} mm · Sloučení délek: {num(input.lamellaMergeToleranceMm)} mm · Prořez: {num(input.glueBoardWastePercent)} %</p>
      <h2>Dílce</h2>
      <table><thead><tr><th>Název dílce</th><th>Dřevina</th><th>Šířka</th><th>Délka</th><th>Tloušťka</th><th>Počet</th></tr></thead><tbody>{input.woodParts.map((part, index) => <tr key={index}><td>{part.name}</td><td>{part.woodType}</td><td>{num(part.widthMm)} mm</td><td>{num(part.lengthMm)} mm</td><td>{num(part.thicknessMm)} mm</td><td>{num(part.quantity)} ks</td></tr>)}</tbody></table>
    </article>}
  </section>
}
