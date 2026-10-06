const assert = require('node:assert/strict')
const base = `${process.argv[2] || 'http://127.0.0.1:5080'}/api`
async function api(path, method = 'GET', body) {
  const response = await fetch(base + path, { method, headers: { 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) })
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`)
  return response.json()
}
;(async () => {
  const input = { name: 'Finish test', customerName: '', budgetLimit: 0, nonVatPayer: false, woodReservePercent: 0, materialOverheadPercent: 0, materialMarginPercent: 0, laborMarginPercent: 0, serviceMarginPercent: 0, financeMarginPercent: 0, discountPercent: 0,
    woodParts: [{ name: 'Police', woodType: 'Dub', widthMm: 500, lengthMm: 1000, thicknessMm: 25, quantity: 2, pricePerM3: 21900, finish: '', applyFinish: true }], lines: [] }
  const calculated = await api('/calculate', 'POST', input)
  const finish = calculated.lines.find(line => line.name === 'Osmo')
  assert.equal(finish.quantity, 2.2)
  assert.equal(finish.cost, 82.1)
  const created = await api('/projects', 'POST', input)
  try {
    const loaded = await api(`/projects/${created.id}`)
    assert.equal(loaded.input.woodParts[0].applyFinish, true)
    assert.equal(loaded.input.lines.find(line => line.automaticFinish).quantity, 2.2)

    // The seeded catalog has multiple finishes; selecting a later one must survive synchronization.
    const catalog = await api('/catalog')
    const finishes = catalog.items.filter(item => item.category === 'material' && item.materialType === 'finish')
    assert.ok(finishes.length >= 2)
    const selected = finishes[1]
    input.lines = [{ ...selected, quantity: 999, automaticFinish: true }]
    const changed = await api('/calculate', 'POST', input)
    const selectedLine = changed.lines.find(line => line.name === selected.name)
    assert.equal(selectedLine.quantity, 2.2)
    assert.equal(selectedLine.unit, selected.unit)
    assert.equal(selectedLine.unitPrice, selected.unitPrice)
    assert.equal(selectedLine.vatRate, selected.vatRate)
    assert.equal(changed.lines.some(line => line.name === finishes[0].name), false)

    await api(`/projects/${created.id}`, 'PUT', input)
    const reloaded = await api(`/projects/${created.id}`)
    const savedFinish = reloaded.input.lines.find(line => line.automaticFinish)
    assert.equal(savedFinish.name, selected.name)
    assert.equal(savedFinish.unit, selected.unit)
    assert.equal(savedFinish.unitPrice, selected.unitPrice)
    assert.equal(savedFinish.vatRate, selected.vatRate)
    assert.equal(savedFinish.quantity, 2.2)

    // Changes to area update only the quantity and preserve the project-specific price and VAT.
    input.lines = [{ ...savedFinish, unitPrice: 123, vatRate: 12 }]
    input.woodParts[0].quantity = 3
    const resized = await api(`/projects/${created.id}`, 'PUT', input)
    const resizedFinish = resized.input.lines.find(line => line.automaticFinish)
    assert.equal(resizedFinish.name, selected.name)
    assert.equal(resizedFinish.unit, selected.unit)
    assert.equal(resizedFinish.unitPrice, 123)
    assert.equal(resizedFinish.vatRate, 12)
    assert.equal(resizedFinish.quantity, 3.3)

    input.woodParts[0].applyFinish = false
    const updated = await api(`/projects/${created.id}`, 'PUT', input)
    assert.equal(updated.input.lines.some(line => line.automaticFinish), false)
  } finally { await fetch(`${base}/projects/${created.id}`, { method: 'DELETE' }) }
  console.log('Finish calculation and persistence passed')
})().catch(error => { console.error(error); process.exitCode = 1 })
