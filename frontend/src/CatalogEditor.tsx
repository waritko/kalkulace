import { Plus, Save, Trash2, Trees, Wrench, Package } from 'lucide-react'
import { useState } from 'react'
import type { Catalog } from './types'

type Section = 'wood' | 'machinery' | 'material'
const specialMachinery = (name: string) => name === 'Odsávání' || name === 'Vysavač'

export function CatalogEditor({ value, onChange, onSave, saving, dirty }: {
  value: Catalog
  onChange: (value: Catalog) => void
  onSave: () => void
  saving: boolean
  dirty: boolean
}) {
  const [section, setSection] = useState<Section>('wood')
  const items = value.items.map((item, index) => ({ item, index })).filter(({ item }) => section === 'machinery' ? item.category === 'service' && item.serviceCategory === 'machinery' : item.category === 'material')
  const updateWood = (index: number, patch: Partial<Catalog['wood'][number]>) => onChange({ ...value, wood: value.wood.map((wood, i) => i === index ? { ...wood, ...patch } : wood) })
  const updateItem = (index: number, patch: Partial<Catalog['items'][number]>) => onChange({ ...value, items: value.items.map((item, i) => i === index ? { ...item, ...patch } : item) })
  const addItem = () => onChange({ ...value, items: [...value.items, { category: section === 'machinery' ? 'service' : 'material', ...(section === 'machinery' ? { serviceCategory: 'machinery' as const } : {}), name: '', unit: section === 'machinery' ? 'hod' : 'ks', unitPrice: 0, vatRate: 21 }] })

  return <>
    <div className="page-heading"><div><div className="eyebrow">SPRÁVA CENÍKU</div><h1>Ceník<span className="heading-period">.</span></h1><p>Výchozí ceny pro nové položky zakázek. Ceny v již uložených zakázkách zůstávají podle jejich zadání.</p></div><button className="primary" onClick={onSave} disabled={!dirty || saving}><Save size={17}/>{saving ? 'Ukládám...' : 'Uložit ceník'}</button></div>
    <div className="catalog-tabs" role="tablist" aria-label="Kategorie ceníku">
      <button role="tab" aria-selected={section === 'wood'} className={section === 'wood' ? 'active' : ''} onClick={() => setSection('wood')}><Trees size={18}/> Dřevo <span>{value.wood.length}</span></button>
      <button role="tab" aria-selected={section === 'machinery'} className={section === 'machinery' ? 'active' : ''} onClick={() => setSection('machinery')}><Wrench size={18}/> Mechanizace <span>{value.items.filter(i => i.category === 'service' && i.serviceCategory === 'machinery').length}</span></button>
      <button role="tab" aria-selected={section === 'material'} className={section === 'material' ? 'active' : ''} onClick={() => setSection('material')}><Package size={18}/> Ostatní materiál <span>{value.items.filter(i => i.category === 'material').length}</span></button>
    </div>
    <section className="panel form-panel catalog-panel"><div className="section-title"><div className="section-icon">{section === 'wood' ? <Trees size={20}/> : section === 'machinery' ? <Wrench size={20}/> : <Package size={20}/>}</div><div><h2>{section === 'wood' ? 'Ceny dřeva' : section === 'machinery' ? 'Ceny mechanizace' : 'Ceny ostatního materiálu'}</h2><p>{section === 'wood' ? 'Cena za m³ podle tloušťky fošny' : 'Název, měrná jednotka a cena za jednotku'}</p></div></div>
      <div className="table-scroll"><table className="catalog-table"><thead><tr><th>Název</th>{section === 'wood' ? <><th>Cena 32 mm / m³</th><th>Cena 50 mm / m³</th></> : <><th>MJ</th><th>Cena / MJ</th><th>DPH</th>{section === 'machinery' && <><th>Odsávání</th><th>Vysavač</th></>}</>}<th/></tr></thead><tbody>
        {section === 'wood' ? value.wood.map((wood, index) => <tr key={index}><td><input aria-label="Dřevina" className="table-input name" value={wood.name} onChange={e => updateWood(index, { name: e.target.value })}/></td><td><input aria-label={`Cena 32 mm ${wood.name}`} className="table-input price" type="number" min="0" step="any" value={wood.price32} onChange={e => updateWood(index, { price32: Number(e.target.value) })}/></td><td><input aria-label={`Cena 50 mm ${wood.name}`} className="table-input price" type="number" min="0" step="any" value={wood.price50} onChange={e => updateWood(index, { price50: Number(e.target.value) })}/></td><td><button className="row-delete" title={`Odebrat ${wood.name}`} onClick={() => onChange({ ...value, wood: value.wood.filter((_, i) => i !== index) })}><Trash2 size={16}/></button></td></tr>) : items.map(({ item, index }) => <tr key={index}><td><input aria-label="Položka" className="table-input name" value={item.name} readOnly={section === 'machinery' && specialMachinery(item.name)} onChange={e => updateItem(index, { name: e.target.value })}/></td><td><input aria-label={`Jednotka ${item.name}`} className="table-input tiny" value={item.unit} readOnly={section === 'machinery' && specialMachinery(item.name)} onChange={e => updateItem(index, { unit: e.target.value })}/></td><td><input aria-label={`Cena ${item.name}`} className="table-input price" type="number" min="0" step="any" value={item.unitPrice} onChange={e => updateItem(index, { unitPrice: Number(e.target.value) })}/></td><td><select aria-label={`DPH ${item.name}`} className="table-input vat" value={item.vatRate} onChange={e => updateItem(index, { vatRate: Number(e.target.value) })}><option value={21}>21 %</option><option value={12}>12 %</option></select></td>{section === 'machinery' && <><td>{!specialMachinery(item.name) && <input type="checkbox" aria-label={`Odsávání ${item.name}`} checked={!!item.usesExtraction} onChange={e => updateItem(index, { usesExtraction: e.target.checked })}/>}</td><td>{!specialMachinery(item.name) && <input type="checkbox" aria-label={`Vysavač ${item.name}`} checked={!!item.usesVacuum} onChange={e => updateItem(index, { usesVacuum: e.target.checked })}/>}</td></>}<td>{!(section === 'machinery' && specialMachinery(item.name)) && <button className="row-delete" title={`Odebrat ${item.name}`} onClick={() => onChange({ ...value, items: value.items.filter((_, i) => i !== index) })}><Trash2 size={16}/></button>}</td></tr>)}
      </tbody></table></div>
      <button className="add-button" onClick={() => section === 'wood' ? onChange({ ...value, wood: [...value.wood, { name: '', price32: 0, price50: 0 }] }) : addItem()}><Plus size={17}/> {section === 'wood' ? 'Přidat dřevinu' : 'Přidat položku'}</button>
    </section>
  </>
}
