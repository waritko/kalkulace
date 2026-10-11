import type { Calculation, ProjectInput } from './types'

const num = (value: number) => new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 1 }).format(value)

export function TransportCutsTable({ result }: { result: Calculation }) {
  return <div className="table-scroll"><table>
    <thead><tr><th>Dřevina</th><th>Tloušťka fošny</th><th>Délka lamely</th><th>Fošna 3 m → kusy</th><th>Fošna 4 m → kusy</th><th>Fošna 5 m → kusy</th></tr></thead>
    <tbody>{result.glueBoardPurchase.map(item => <tr key={`${item.woodType}-${item.boardThicknessMm}-${item.lamellaLengthMm}`}>
      <td>{item.woodType}</td><td>{num(item.boardThicknessMm)} mm</td><td>{num(item.lamellaLengthMm)} mm</td>
      {item.transportCuts?.map(cut => <td key={cut.boardLengthMm}>{cut.warning || <>{cut.pieceLengthsMm.map(length => num(length)).join(' + ')} mm<br/><small>{cut.lamellaCount}× délka lamely z jedné fošny</small></>}</td>)}
    </tr>)}</tbody>
  </table></div>
}

export function TransportPurchase({ input, result, update }: {
  input: ProjectInput
  result: Calculation
  update: <K extends keyof ProjectInput>(key: K, value: ProjectInput[K]) => void
}) {
  return <section className="panel form-panel board-purchase">
    <div className="section-title"><div><h2>Rozřezání nákupních fošen pro převoz</h2><p>Doporučené délky kusů z fošen 3, 4 a 5 m</p></div></div>
    <div className="pricing-grid"><label className="field"><span>Největší délka na vozík (mm)</span><input type="number" min="21" step="1" value={input.transportMaxLengthMm} onChange={e => update('transportMaxLengthMm', Number(e.target.value))}/><small className="field-help">Výchozí 2 700 mm (2,7 m). Ukládá se se zakázkou.</small></label></div>
    <div className="info-strip">Každý řádek doporučuje řezy pro uvedenou délku lamely včetně přídavku. Zachovává největší počet délek lamely a poté volí nejméně přepravních řezů. Hrubé dělení pro převoz počítá s prořezem 20 mm na řez, následné řezání lamel s 3 mm. Počet platí v délkovém směru; šířka fošny se zde neřeší. Různé řádky jsou alternativy, nikoli společný nářezový plán.</div>
    <TransportCutsTable result={result}/>
  </section>
}
