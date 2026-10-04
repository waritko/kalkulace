import type { Calculation, ProjectInput } from './types'

const num = (value: number) => new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 2 }).format(value)

export function GlueBoardPurchase({ input, result, update }: {
  input: ProjectInput
  result: Calculation | null
  update: <K extends keyof ProjectInput>(key: K, value: ProjectInput[K]) => void
}) {
  return <section className="panel form-panel wood-purchase">
    <div className="section-title"><div><h2>Seznam pro průběžnou spárovku</h2><p>Celková šířka spárovky pro každou nákupní délku lamel</p></div></div>
    <div className="pricing-grid glue-board-settings">
      <label className="field"><span>Přídavek k délce lamely (mm)</span><input type="number" min="0" step="any" value={input.lamellaLengthExtraMm} onChange={e => update('lamellaLengthExtraMm', Number(e.target.value))}/></label>
      <label className="field"><span>Sloučit délky v rozmezí (mm)</span><input type="number" min="0" step="any" value={input.lamellaMergeToleranceMm} onChange={e => update('lamellaMergeToleranceMm', Number(e.target.value))}/></label>
      <label className="field"><span>Prořez (%)</span><input type="number" min="0" max="100" step="any" value={input.glueBoardWastePercent} onChange={e => update('glueBoardWastePercent', Number(e.target.value))}/></label>
    </div>
    <div className="info-strip">Délka lamely vychází z delší strany dílu a přídavku. Šířky kratších stran se násobí počtem kusů, sečtou a navýší o prořez. Podobné délky se zařadí k nejdelší délce v dané skupině.</div>
    {!!result?.glueBoardPurchase?.length && <div className="table-scroll"><table><thead><tr><th>Dřevina</th><th>Tloušťka spárovky</th><th>Délka lamely</th><th>Celková šířka s prořezem</th></tr></thead><tbody>{result.glueBoardPurchase.map(item => <tr key={`${item.woodType}-${item.thicknessMm}-${item.lamellaLengthMm}`}><td><strong>{item.woodType}</strong></td><td>{num(item.thicknessMm)} mm</td><td>{num(item.lamellaLengthMm)} mm</td><td className="table-total">{num(item.totalWidthMm)} mm</td></tr>)}</tbody></table></div>}
  </section>
}
