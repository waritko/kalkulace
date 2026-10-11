import type { CuttingPlanResult, ProjectInput } from './types'

const num = (value: number) => new Intl.NumberFormat('cs-CZ', { maximumFractionDigits: 2 }).format(value)
type Lamella = CuttingPlanResult['lamellas'][number]

export function groupCuttingSections(lamellas: Lamella[]) {
  const boards = new Map<number, { boardId: number; woodType: string; boardThicknessMm: number; sections: { lengthMm: number; offsetLengthMm: number; lamellas: Lamella[] }[] }>()
  for (const lamella of lamellas) {
    let board = boards.get(lamella.boardId)
    if (!board) {
      board = { boardId: lamella.boardId, woodType: lamella.woodType, boardThicknessMm: lamella.boardThicknessMm, sections: [] }
      boards.set(lamella.boardId, board)
    }
    let section = board.sections.find(s => s.offsetLengthMm === lamella.offsetLengthMm && s.lengthMm === lamella.lengthMm)
    if (!section) {
      section = { lengthMm: lamella.lengthMm, offsetLengthMm: lamella.offsetLengthMm, lamellas: [] }
      board.sections.push(section)
    }
    section.lamellas.push(lamella)
  }
  return [...boards.values()].sort((a, b) => a.boardId - b.boardId).map(board => ({
    ...board,
    sections: board.sections.sort((a, b) => a.offsetLengthMm - b.offsetLengthMm || a.lengthMm - b.lengthMm).map((section, index) => ({
      ...section,
      label: `#${board.boardId} / ${index + 1}`,
      lamellas: section.lamellas.sort((a, b) => a.offsetWidthMm - b.offsetWidthMm),
    })),
  }))
}

export function CuttingPlanPrint({ input, plan }: { input: ProjectInput; plan: CuttingPlanResult }) {
  const boards = groupCuttingSections(plan.lamellas)
  return <article className="cutting-print-sheet">
    <h1>Nářezový plán – {input.name || 'Zakázka'}</h1>
    {input.customerName && <p>Odběratel: {input.customerName}</p>}
    <p>{plan.complete ? 'Kompletní plán' : 'NEÚPLNÝ PLÁN – chybí materiál'} · {num(plan.lamellas.length)} lamel · {num(boards.length)} skladových kusů</p>
    <p>Všechny rozměry a polohy jsou v mm. Prořez pily: {num(plan.kerfMm)} mm na řez. Úběr při srovnávání: {num(input.lamellaWidthLossMm)} mm na lamelu.</p>
    <p>Polohy označují začátek úseku nebo lamely od začátku původního skladového kusu, včetně mezer pro prořez. Již podélně nařezané kusy ponechte v celé délce. Plán sklad nemění ani nerezervuje materiál; před řezáním jej obnovte a zkontrolujte stav dřeva.</p>
    {plan.warnings.map((warning, i) => <p key={i}><strong>Upozornění:</strong> {warning}</p>)}
    {!!plan.shortages.length && <>
      <h2>Chybějící materiál</h2>
      <table><thead><tr><th>Dřevina</th><th>Tloušťka fošny</th><th>Délka</th><th>Chybějící šířka po srovnání</th></tr></thead><tbody>{plan.shortages.map((s, i) => <tr key={i}><td>{s.woodType}</td><td>{num(s.boardThicknessMm)}</td><td>{num(s.lengthMm)}</td><td>{num(s.missingWidthMm)}</td></tr>)}</tbody></table>
    </>}
    <section>
      <h2>1. Rozřezání na délku podle fošen</h2>
      <p>Řežte úseky v uvedeném pořadí a označte je pro následné rozřezání na šířku. Jeden úsek může poskytnout více lamel.</p>
      {boards.map(board => <div key={board.boardId}>
        <h3>Fošna / skladový kus #{board.boardId} · {board.woodType} · tloušťka {num(board.boardThicknessMm)} mm</h3>
        <table><thead><tr><th>Označení úseku</th><th>Poloha na délku</th><th>Délka úseku</th><th>Počet lamel</th></tr></thead><tbody>{board.sections.map(section => <tr key={section.label}><td>{section.label}</td><td>{num(section.offsetLengthMm)}</td><td><strong>{num(section.lengthMm)}</strong></td><td>{section.lamellas.length}</td></tr>)}</tbody></table>
      </div>)}
      {!boards.length && <p>Ze skladu nebyl naplánován žádný úsek.</p>}
    </section>
    <section className="cutting-print-widths">
      <h2>2. Rozřezání jednotlivých délek na šířku</h2>
      <p>Šířka pro řezání je rozměr před srovnáním. Číslo desky určuje spárovku, do které lamela patří.</p>
      {boards.flatMap(board => board.sections.map(section => <div key={section.label}>
        <h3>Úsek {section.label} · délka {num(section.lengthMm)} mm · {board.woodType} · tloušťka {num(board.boardThicknessMm)} mm</h3>
        <table><thead><tr><th>Lamela</th><th>Poloha na šířku</th><th>Šířka pro řezání</th><th>Šířka po srovnání</th><th>Deska</th></tr></thead><tbody>{section.lamellas.map((lamella, i) => <tr key={i}><td>{i + 1}</td><td>{num(lamella.offsetWidthMm)}</td><td><strong>{num(lamella.widthMm)}</strong></td><td>{num(lamella.finishedWidthMm)}</td><td>#{lamella.panelId}</td></tr>)}</tbody></table>
      </div>))}
      {!boards.length && <p>Ze skladu nebyla naplánována žádná lamela.</p>}
    </section>
  </article>
}
