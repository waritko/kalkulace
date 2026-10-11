const assert = require('node:assert/strict')
const base = `${process.argv[2] || 'http://127.0.0.1:5080'}/api`
async function api(path, body, status = 200, method = 'POST') {
  const response = await fetch(base + path, body === undefined ? undefined : { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  const text = await response.text()
  assert.equal(response.status, status, text)
  return text ? JSON.parse(text) : undefined
}
const project = (woodType, overrides = {}) => ({ name: 'Test nářezového plánu', budgetLimit: 0, nonVatPayer: false, woodReservePercent: 0, materialOverheadPercent: 0, materialMarginPercent: 5, laborMarginPercent: 0, serviceMarginPercent: 15, financeMarginPercent: 15, discountPercent: 0, lamellaLengthExtraMm: 50, lamellaMergeToleranceMm: 50, glueBoardWastePercent: 10, lamellaMinLengthMm: 350, lamellaMinWidthMm: 50, lamellaMaxWidthMm: 110, lamellaWidthLossMm: 4, woodParts: [{ name: 'Police', woodType, widthMm: 200, lengthMm: 500, thicknessMm: 25, quantity: 1, pricePerM3: 10000 }], lines: [], ...overrides })
;(async () => {
  const wood = `Plan-${Date.now()}`
  const input = project(wood)
  const defaults = await api('/pricing-defaults')
  assert.deepEqual([defaults.lamellaMinLengthMm, defaults.lamellaMinWidthMm, defaults.lamellaMaxWidthMm], [350, 50, 110])
  assert.equal(defaults.lamellaWidthLossMm, 4)
  for (const invalid of [{ lamellaMinLengthMm: 0 }, { lamellaMinWidthMm: -1 }, { lamellaMaxWidthMm: 49 }, { lamellaMinWidthMm: 50.5 }, { lamellaWidthLossMm: -1 }, { lamellaWidthLossMm: 50 }, { lamellaWidthLossMm: 4.5 }]) {
    await api('/cutting-plan', { ...input, ...invalid }, 400)
    await api('/pricing-defaults', { ...defaults, ...invalid }, 400, 'PUT')
  }
  let plan = await api('/cutting-plan', input)
  assert.equal(plan.complete, false)
  assert.equal(plan.shortages[0].missingWidthMm, 220)
  // Unknown thickness must not be guessed; user can fill it in for existing stock.
  const unknown = await api('/stock/boards', { woodType: wood, widthMm: 120, lengthMm: 3000 }, 201)
  plan = await api('/cutting-plan', input)
  assert.equal(plan.complete, false)
  assert.ok(plan.warnings.some(w => w.includes('tloušťka')))
  await api(`/stock/boards/${unknown.id}/thickness`, { thicknessMm: 32 })
  const wrongThickness = await api('/stock/boards', { woodType: wood, widthMm: 1000, lengthMm: 3000, thicknessMm: 50 }, 201)
  const wrongSpecies = await api('/stock/boards', { woodType: wood + '-other', widthMm: 1000, lengthMm: 3000, thicknessMm: 32 }, 201)
  const before = await api('/stock/boards')
  plan = await api('/cutting-plan', input)
  assert.equal(plan.complete, true)
  assert.equal(plan.kerfMm, 3)
  assert.ok(plan.lamellas.length > 0)
  assert.ok(plan.lamellas.every(l => l.boardId === unknown.id && l.boardId !== wrongThickness.id && l.boardId !== wrongSpecies.id && l.widthMm >= 50 && l.widthMm <= 110 && l.lengthMm >= 550))
  assert.ok(plan.lamellas.reduce((sum, l) => sum + l.finishedWidthMm, 0) >= 220)
  assert.deepEqual(await api('/stock/boards'), before, 'Planning must not mutate or reserve stock')
  assert.ok(plan.lamellas.every(l => l.finishedWidthMm === l.widthMm - 4))
  // Check geometry including kerf, not just the reported totals.
  for (const l of plan.lamellas) {
    assert.ok(l.offsetWidthMm + l.widthMm <= unknown.widthMm)
    assert.ok(l.offsetLengthMm + l.lengthMm <= unknown.lengthMm)
    for (const other of plan.lamellas) if (l !== other && l.boardId === other.boardId)
      assert.ok(l.offsetWidthMm + l.widthMm + 3 <= other.offsetWidthMm || other.offsetWidthMm + other.widthMm + 3 <= l.offsetWidthMm || l.offsetLengthMm + l.lengthMm + 3 <= other.offsetLengthMm || other.offsetLengthMm + other.lengthMm + 3 <= l.offsetLengthMm, 'Lamellas must not overlap, including kerf')
  }
  const short = project(wood, { woodParts: [{ ...input.woodParts[0], widthMm: 60, lengthMm: 100 }], glueBoardWastePercent: 0 })
  assert.ok((await api('/cutting-plan', short)).lamellas.every(l => l.lengthMm === 350))
  const impossible = await api('/cutting-plan', project(wood, { woodParts: [{ ...input.woodParts[0], lengthMm: 4000 }] }))
  assert.equal(impossible.complete, false)
  assert.equal(impossible.lamellas.length, 0)
  // Parent is no longer available after cutting; descendants carry the thickness.
  const children = await api(`/stock/boards/${unknown.id}/cut`, { axis: 'length', sizeMm: 600 })
  assert.ok(children.every(b => b.thicknessMm === 32))
  const strips = await api(`/stock/boards/${children[0].id}/cut`, { axis: 'width', sizeMm: 55 })
  plan = await api('/cutting-plan', input)
  assert.ok(plan.lamellas.every(l => l.boardId !== unknown.id && l.boardId !== children[0].id))
  assert.ok(plan.lamellas.some(l => strips.some(s => s.id === l.boardId) && l.lengthMm === 600), 'Previously ripped stock must keep its length')
  const saved = await api('/projects', { ...input, lamellaMinLengthMm: 410, lamellaMinWidthMm: 60, lamellaMaxWidthMm: 95, lamellaWidthLossMm: 8 }, 201)
  const changedDefaults = { ...defaults, lamellaMinLengthMm: 600, lamellaMinWidthMm: 70, lamellaMaxWidthMm: 100, lamellaWidthLossMm: 6 }
  await api('/pricing-defaults', changedDefaults, 200, 'PUT')
  assert.equal((await api('/pricing-defaults')).lamellaMinLengthMm, 600)
  assert.equal((await api('/pricing-defaults')).lamellaWidthLossMm, 6)
  const loaded = await api(`/projects/${saved.id}`)
  assert.deepEqual([loaded.input.lamellaMinLengthMm, loaded.input.lamellaMinWidthMm, loaded.input.lamellaMaxWidthMm], [410, 60, 95])
  assert.equal(loaded.input.lamellaWidthLossMm, 8)
  await api(`/projects/${saved.id}`, { ...loaded.input, lamellaMinLengthMm: 420 }, 200, 'PUT')
  assert.equal((await api(`/projects/${saved.id}`)).input.lamellaMinLengthMm, 420)
  await api('/pricing-defaults', defaults, 200, 'PUT')
  // Older clients/projects without the new fields retain documented defaults.
  const legacy = { ...input }
  for (const key of ['lamellaMinLengthMm', 'lamellaMinWidthMm', 'lamellaMaxWidthMm', 'lamellaWidthLossMm']) delete legacy[key]
  const old = await api('/projects', legacy, 201)
  assert.deepEqual([old.input.lamellaMinLengthMm, old.input.lamellaMinWidthMm, old.input.lamellaMaxWidthMm], [350, 50, 110])
  assert.equal(old.input.lamellaWidthLossMm, 4)
  // Stock fits five 110 mm lamellas; jointing reduces their usable width.
  const jointWood = wood + '-jointing'
  const jointBoard = await api('/stock/boards', { woodType: jointWood, widthMm: 110, lengthMm: 3000, thicknessMm: 32 }, 201)
  await api(`/stock/boards/${jointBoard.id}/cut`, { axis: 'length', sizeMm: 550 })
  const jointInput = project(jointWood, { glueBoardWastePercent: 0, woodParts: [{ ...input.woodParts[0], woodType: jointWood, widthMm: 110, lengthMm: 500, quantity: 5 }] })
  const noLoss = await api('/cutting-plan', { ...jointInput, lamellaWidthLossMm: 0 })
  assert.equal(noLoss.complete, true)
  assert.equal(noLoss.lamellas.length, 5)
  assert.ok(noLoss.lamellas.every(l => l.finishedWidthMm === 110))
  for (const loss of [4, 8]) {
    const withLoss = await api('/cutting-plan', { ...jointInput, lamellaWidthLossMm: loss })
    assert.equal(withLoss.complete, false)
    assert.equal(withLoss.shortages[0].missingWidthMm, 5 * loss)
    assert.ok(withLoss.lamellas.every(l => l.widthMm === 110 && l.finishedWidthMm === 110 - loss))
  }
  console.log('Cutting plan geometry, shortage, cut stock, thickness, validation and persistence passed.')
})().catch(error => { console.error(error); process.exitCode = 1 })
