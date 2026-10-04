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
    input.woodParts[0].applyFinish = false
    const updated = await api(`/projects/${created.id}`, 'PUT', input)
    assert.equal(updated.input.lines.some(line => line.automaticFinish), false)
  } finally { await fetch(`${base}/projects/${created.id}`, { method: 'DELETE' }) }
  console.log('Finish calculation and persistence passed')
})().catch(error => { console.error(error); process.exitCode = 1 })
