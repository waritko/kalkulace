import { useState } from 'react'
import { CalendarDays, Plus, X } from 'lucide-react'
import type { Catalog, CostLine } from './types'
import { formatTime, isTimedLine, roundedTimeQuantities, timeParts } from './workTime'

const today = () => {
  const day = new Date()
  return `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, '0')}-${String(day.getDate()).padStart(2, '0')}`
}
const formatDay = (value: string) => value ? new Intl.DateTimeFormat('cs-CZ', { day: 'numeric', month: 'numeric', year: 'numeric' }).format(new Date(`${value}T12:00:00`)) : 'Bez data'
const money = (value: number) => new Intl.NumberFormat('cs-CZ', { style: 'currency', currency: 'CZK', maximumFractionDigits: 2 }).format(value || 0)
const isDaily = (line: CostLine, catalog: Catalog) => line.category === 'labor' || line.category === 'service' && (line.serviceCategory || catalog.items.find(item => item.category === 'service' && item.name === line.name)?.serviceCategory) === 'machinery'

type Props = {
  lines: CostLine[]
  catalog: Catalog
  onChange: (lines: CostLine[]) => void
}

export function DailyEntries({ lines, catalog, onChange }: Props) {
  const [selectedDay, setSelectedDay] = useState<string | null>(null)
  const [newDay, setNewDay] = useState(today)
  const daily = lines.map((line, index) => ({ line, index })).filter(({ line }) => isDaily(line, catalog))
  const days = [...new Set([...daily.map(({ line }) => line.workDate || ''), ...(selectedDay ? [selectedDay] : [])])].sort((a, b) => b.localeCompare(a))
  const activeDay = selectedDay !== null && days.includes(selectedDay) ? selectedDay : days[0]
  const entries = daily.filter(({ line }) => (line.workDate || '') === activeDay)
  const billableQuantities = roundedTimeQuantities(lines)
  const laborTime = entries.filter(({ line }) => line.category === 'labor' && isTimedLine(line)).reduce((sum, { index }) => sum + billableQuantities[index], 0)
  const machineryTime = entries.filter(({ line }) => line.category === 'service' && !line.automaticMachineryCharge && isTimedLine(line)).reduce((sum, { line }) => sum + line.quantity, 0)
  const laborCatalog = catalog.items.filter(item => item.category === 'labor')
  const machineryCatalog = catalog.items.filter(item => item.category === 'service' && item.serviceCategory === 'machinery' && !['Odsávání', 'Vysavač'].includes(item.name))

  const updateLine = (index: number, patch: Partial<CostLine>) => onChange(lines.map((line, i) => i === index ? { ...line, ...patch } : line))
  const updateTime = (index: number, hours: number, minutes: number) => updateLine(index, { quantity: Math.max(0, Math.trunc(hours)) + Math.max(0, Math.min(59, Math.trunc(minutes))) / 60 })
  const addLine = (kind: 'labor' | 'machinery') => {
    if (activeDay === undefined) return
    const item = kind === 'labor' ? laborCatalog[0] : machineryCatalog[0]
    onChange([...lines, {
      name: item?.name || '', category: kind === 'labor' ? 'labor' : 'service',
      ...(kind === 'machinery' ? { serviceCategory: 'machinery' as const, usesExtraction: !!item?.usesExtraction, usesVacuum: !!item?.usesVacuum } : {}),
      workDate: activeDay || null, unit: item?.unit || 'hod', quantity: 1,
      unitPrice: item?.unitPrice ?? 0, vatRate: item?.vatRate ?? 21,
    }])
  }

  return <section className="panel form-panel daily-panel">
    <div className="section-title"><div className="section-icon"><CalendarDays size={20}/></div><div><h2>Práce a mechanizace po dnech</h2><p>Vyberte den a přidejte odpracovaný čas i použitou mechanizaci.</p></div></div>
    <div className="day-create"><label className="field"><span>Datum nového dne</span><input type="date" value={newDay} onChange={event => setNewDay(event.target.value)}/></label><button className="secondary" disabled={!newDay} onClick={() => setSelectedDay(newDay)}><Plus size={17}/> Přidat den</button></div>
    {days.length > 0 && <div className="day-tabs" aria-label="Dny zakázky">{days.map(day => <button type="button" key={day} className={activeDay === day ? 'active' : ''} aria-current={activeDay === day ? 'date' : undefined} onClick={() => setSelectedDay(day)}>{formatDay(day)} <span>{daily.filter(({ line }) => (line.workDate || '') === day && !line.automaticMachineryCharge).length}</span></button>)}</div>}
    {activeDay === undefined ? <div className="inline-empty">Zvolte datum a přidejte první den.</div> : <>
      <div className="day-heading"><div><strong>{formatDay(activeDay)}</strong><span>Práce {formatTime(laborTime)} · mechanizace {formatTime(machineryTime)} (práce zaokrouhlená po položkách na 15 minut, mechanizace v zadaném čase)</span><span>Mechanizace se zaokrouhluje nahoru na 15 minut až po součtu všech dní pro stejnou položku, cenu a DPH. Rozdíl je zahrnutý v ceně posledního záznamu.</span></div></div>
      {(['labor', 'machinery'] as const).map(kind => {
        const rows = entries.filter(({ line }) => kind === 'labor' ? line.category === 'labor' : line.category === 'service')
        const choices = kind === 'labor' ? laborCatalog : machineryCatalog
        return <div className="day-group" key={kind}><h3>{kind === 'labor' ? 'Odpracovaný čas' : 'Mechanizace'}</h3>
          {rows.length > 0 && <div className="table-scroll"><table><thead><tr><th>Položka</th><th>Den</th><th>MJ</th><th>Čas / množství</th><th>Kč/MJ</th><th>DPH</th><th>Celkem</th><th/></tr></thead><tbody>{rows.map(({ line, index }) => <tr key={index}><td><input className="table-input name" list={`daily-${kind}`} aria-label={`${kind === 'labor' ? 'Práce' : 'Mechanizace'} ${index + 1}`} value={line.name} readOnly={!!line.automaticMachineryCharge} onChange={event => { const match = choices.find(item => item.name === event.target.value); updateLine(index, match ? { name: match.name, unit: match.unit, unitPrice: match.unitPrice, vatRate: match.vatRate, ...(kind === 'machinery' ? { serviceCategory: 'machinery' as const, usesExtraction: !!match.usesExtraction, usesVacuum: !!match.usesVacuum } : {}) } : { name: event.target.value, ...(kind === 'machinery' ? { usesExtraction: false, usesVacuum: false } : {}) }) }}/></td><td><input className="table-input work-date" type="date" aria-label={`Den: ${line.name || 'položka'}`} value={line.workDate || ''} readOnly={!!line.automaticMachineryCharge} onChange={event => { const day = event.target.value; updateLine(index, { workDate: day || null }); setSelectedDay(day) }}/></td><td><input className="table-input tiny" aria-label={`Jednotka: ${line.name}`} value={line.unit} readOnly={!!line.automaticMachineryCharge} onChange={event => updateLine(index, { unit: event.target.value })}/></td><td>{isTimedLine(line) ? <div className="time-inputs"><label><input className="table-input" type="number" min="0" step="1" aria-label={`Hodiny: ${line.name}`} value={timeParts(line.quantity).hours} readOnly={!!line.automaticMachineryCharge} onChange={event => updateTime(index, Number(event.target.value), timeParts(line.quantity).minutes)}/> h</label><label><input className="table-input" type="number" min="0" max="59" step="1" aria-label={`Minuty: ${line.name}`} value={timeParts(line.quantity).minutes} readOnly={!!line.automaticMachineryCharge} onChange={event => updateTime(index, timeParts(line.quantity).hours, Number(event.target.value))}/> min</label></div> : <input className="table-input small" type="number" min="0" step="any" aria-label={`Množství: ${line.name}`} value={line.quantity} readOnly={!!line.automaticMachineryCharge} onChange={event => updateLine(index, { quantity: Number(event.target.value) })}/>}</td><td><input className="table-input price" type="number" min="0" step="any" aria-label={`Cena za jednotku: ${line.name}`} value={line.unitPrice} onChange={event => updateLine(index, { unitPrice: Number(event.target.value) })}/></td><td><select className="table-input vat" aria-label={`DPH: ${line.name}`} value={line.vatRate} onChange={event => updateLine(index, { vatRate: Number(event.target.value) })}><option value={21}>21 %</option><option value={12}>12 %</option></select></td><td className="table-total">{money(billableQuantities[index] * line.unitPrice)}</td><td>{!line.automaticMachineryCharge && <button className="row-delete" title="Odebrat" onClick={() => onChange(lines.filter((_, i) => i !== index))}><X size={16}/></button>}</td></tr>)}</tbody></table></div>}
          <datalist id={`daily-${kind}`}>{choices.map(item => <option key={item.name} value={item.name}/>)}</datalist>
          {rows.length === 0 && <div className="inline-empty">{kind === 'labor' ? 'Zatím žádný odpracovaný čas.' : 'Zatím žádná mechanizace.'}</div>}
          <button className="add-button" onClick={() => addLine(kind)}><Plus size={17}/> {kind === 'labor' ? 'Přidat práci' : 'Přidat mechanizaci'}</button>
        </div>
      })}
    </>}
  </section>
}
