const { authenticatedFetch } = require('./auth-helper.cjs')
let client
const assert = require('node:assert/strict')
const base = `${process.argv[2] || 'http://127.0.0.1:5080'}/api`
async function api(path, body, status = 200, method = 'POST') {
  const response = await client(base + path, body === undefined ? undefined : { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
  const text = await response.text()
  assert.equal(response.status, status, text)
  return text ? JSON.parse(text) : undefined
}
const project = (woodType, overrides = {}) => ({ name: 'Test nářezového plánu', budgetLimit: 0, nonVatPayer: false, woodReservePercent: 0, materialOverheadPercent: 0, materialMarginPercent: 5, laborMarginPercent: 0, serviceMarginPercent: 15, financeMarginPercent: 15, discountPercent: 0, lamellaLengthExtraMm: 50, lamellaMergeToleranceMm: 50, glueBoardWastePercent: 10, lamellaMinLengthMm: 350, lamellaMinWidthMm: 50, lamellaMaxWidthMm: 110, lamellaWidthLossMm: 4, glueBoardMaxWidthMm: 900, offcutMinWidthMm: 50, offcutMinLengthMm: 350, woodParts: [{ name: 'Police', woodType, widthMm: 200, lengthMm: 500, thicknessMm: 25, quantity: 1, pricePerM3: 10000 }], lines: [], ...overrides })
;(async () => {
  client = await authenticatedFetch(process.argv[2] || 'http://127.0.0.1:5080')
  const wood = `Plan-${Date.now()}`
  const input = project(wood)
  const defaults = await api('/pricing-defaults')
  assert.deepEqual([defaults.lamellaMinLengthMm, defaults.lamellaMinWidthMm, defaults.lamellaMaxWidthMm], [350, 50, 110])
  assert.equal(defaults.lamellaWidthLossMm, 4)
  assert.equal(defaults.glueBoardMaxWidthMm, 900)
  assert.deepEqual([defaults.offcutMinWidthMm, defaults.offcutMinLengthMm], [50, 350])
  for (const invalid of [{ offcutMinWidthMm: 0 }, { offcutMinLengthMm: -1 }, { offcutMinWidthMm: 50.5 }, { offcutMinLengthMm: 350.5 }, { glueBoardMaxWidthMm: 0 }, { glueBoardMaxWidthMm: 900.5 }, { lamellaMinLengthMm: 0 }, { lamellaMinWidthMm: -1 }, { lamellaMaxWidthMm: 49 }, { lamellaMinWidthMm: 50.5 }, { lamellaWidthLossMm: -1 }, { lamellaWidthLossMm: 50 }, { lamellaWidthLossMm: 4.5 }]) {
    await api('/cutting-plan', { ...input, ...invalid }, 400)
    await api('/pricing-defaults', { ...defaults, ...invalid }, 400, 'PUT')
  }
  let plan = await api('/cutting-plan', input)
  assert.equal(plan.complete, false)
  assert.equal(plan.shortages[0].missingWidthMm, 200)
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
  assert.ok(plan.lamellas.reduce((sum, l) => sum + l.finishedWidthMm, 0) >= 200)
  assert.deepEqual(await api('/stock/boards'), before, 'Planning must not mutate or reserve stock')
  assert.ok(plan.lamellas.every(l => l.finishedWidthMm === l.widthMm - 4))
  assert.ok(plan.offcuts.length > 0)
  assert.ok(plan.offcuts.every(p => p.boardId === unknown.id))
  const rectangles = [...plan.lamellas, ...plan.offcuts]
  for (const p of plan.offcuts) {
    assert.ok(p.lengthMm > 0 && p.widthMm > 0)
    assert.ok(p.offsetWidthMm + p.widthMm <= unknown.widthMm)
    assert.ok(p.offsetLengthMm + p.lengthMm <= unknown.lengthMm)
    for (const other of rectangles) if (p !== other)
      assert.ok(p.offsetWidthMm + p.widthMm <= other.offsetWidthMm || other.offsetWidthMm + other.widthMm <= p.offsetWidthMm || p.offsetLengthMm + p.lengthMm <= other.offsetLengthMm || other.offsetLengthMm + other.lengthMm <= p.offsetLengthMm, 'Waste including kerf must not overlap other rectangles')
  }
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
  assert.deepEqual(impossible.offcuts, [])
  assert.equal(rectangles.reduce((sum, p) => sum + p.lengthMm * p.widthMm, 0), unknown.lengthMm * unknown.widthMm, 'Lamellas, offcuts and kerf account for the entire used board')
  // Report length and width scraps and both saw cuts.
  const scrapWood = wood + '-offcuts'
  const scrapBoard = await api('/stock/boards', { woodType: scrapWood, widthMm: 80, lengthMm: 1000, thicknessMm: 32 }, 201)
  const scrapInput = project(scrapWood, { lamellaLengthExtraMm: 0, woodParts: [{ ...input.woodParts[0], woodType: scrapWood, widthMm: 50 }] })
  const scrap = await api('/cutting-plan', scrapInput)
  assert.equal(scrap.complete, true)
  assert.deepEqual(scrap.offcuts, [
    { boardId: scrapBoard.id, woodType: scrapWood, boardThicknessMm: 32, lengthMm: 500, widthMm: 3, offsetWidthMm: 54, offsetLengthMm: 0, isKerf: true, keepInStock: false },
    { boardId: scrapBoard.id, woodType: scrapWood, boardThicknessMm: 32, lengthMm: 500, widthMm: 23, offsetWidthMm: 57, offsetLengthMm: 0, isKerf: false, keepInStock: false },
    { boardId: scrapBoard.id, woodType: scrapWood, boardThicknessMm: 32, lengthMm: 3, widthMm: 80, offsetWidthMm: 0, offsetLengthMm: 500, isKerf: true, keepInStock: false },
    { boardId: scrapBoard.id, woodType: scrapWood, boardThicknessMm: 32, lengthMm: 497, widthMm: 80, offsetWidthMm: 0, offsetLengthMm: 503, isKerf: false, keepInStock: true },
  ])
  for (const [width, length, count] of [[23, 497, 2], [24, 497, 1], [80, 498, 0]]) {
    const result = await api('/cutting-plan', { ...scrapInput, offcutMinWidthMm: width, offcutMinLengthMm: length })
    assert.equal(result.offcuts.filter(p => p.keepInStock).length, count, 'Both thresholds are inclusive and use dimensions after kerf')
    assert.deepEqual(result.lamellas, scrap.lamellas, 'Storage thresholds do not change cutting')
    assert.ok(result.offcuts.filter(p => p.isKerf).every(p => !p.keepInStock))
  }
  // Trim smaller than the saw width is wholly sawdust and stays inside the board.
  const edgeWood = wood + '-edge'
  await api('/stock/boards', { woodType: edgeWood, widthMm: 55, lengthMm: 502, thicknessMm: 32 }, 201)
  const edge = await api('/cutting-plan', project(edgeWood, { lamellaLengthExtraMm: 0, woodParts: [{ ...input.woodParts[0], woodType: edgeWood, widthMm: 50 }] }))
  assert.deepEqual(edge.offcuts.map(p => [p.widthMm, p.lengthMm, p.isKerf]), [[1, 500, true], [55, 2, true]])
  // Exact fits leave no offcuts, including already ripped stock.
  const exactWood = wood + '-exact'
  const exactBoard = await api('/stock/boards', { woodType: exactWood, widthMm: 103, lengthMm: 1003, thicknessMm: 32 }, 201)
  const exactSections = await api(`/stock/boards/${exactBoard.id}/cut`, { axis: 'length', sizeMm: 500 })
  await api(`/stock/boards/${exactSections[0].id}/cut`, { axis: 'width', sizeMm: 50 })
  const exact = await api('/cutting-plan', project(exactWood, { lamellaLengthExtraMm: 0, woodParts: [{ ...input.woodParts[0], woodType: exactWood, widthMm: 46 }] }))
  assert.equal(exact.complete, true)
  assert.deepEqual(exact.offcuts, [])
  // Parent is no longer available after cutting; descendants carry the thickness.
  const children = await api(`/stock/boards/${unknown.id}/cut`, { axis: 'length', sizeMm: 600 })
  assert.ok(children.every(b => b.thicknessMm === 32))
  const strips = await api(`/stock/boards/${children[0].id}/cut`, { axis: 'width', sizeMm: 55 })
  plan = await api('/cutting-plan', input)
  assert.ok(plan.lamellas.every(l => l.boardId !== unknown.id && l.boardId !== children[0].id))
  assert.ok(plan.lamellas.some(l => strips.some(s => s.id === l.boardId) && l.lengthMm === 600), 'Previously ripped stock must keep its length')
  const saved = await api('/projects', { ...input, lamellaMinLengthMm: 410, lamellaMinWidthMm: 60, lamellaMaxWidthMm: 95, lamellaWidthLossMm: 8, glueBoardMaxWidthMm: 750, offcutMinWidthMm: 65, offcutMinLengthMm: 450 }, 201)
  const changedDefaults = { ...defaults, lamellaMinLengthMm: 600, lamellaMinWidthMm: 70, lamellaMaxWidthMm: 100, lamellaWidthLossMm: 6, glueBoardMaxWidthMm: 800, offcutMinWidthMm: 75, offcutMinLengthMm: 550 }
  await api('/pricing-defaults', changedDefaults, 200, 'PUT')
  assert.equal((await api('/pricing-defaults')).lamellaMinLengthMm, 600)
  assert.equal((await api('/pricing-defaults')).lamellaWidthLossMm, 6)
  const loaded = await api(`/projects/${saved.id}`)
  assert.deepEqual([loaded.input.lamellaMinLengthMm, loaded.input.lamellaMinWidthMm, loaded.input.lamellaMaxWidthMm], [410, 60, 95])
  assert.equal(loaded.input.lamellaWidthLossMm, 8)
  assert.equal(loaded.input.glueBoardMaxWidthMm, 750)
  assert.deepEqual([loaded.input.offcutMinWidthMm, loaded.input.offcutMinLengthMm], [65, 450])
  const newDefaults = await api('/pricing-defaults')
  assert.deepEqual([newDefaults.offcutMinWidthMm, newDefaults.offcutMinLengthMm], [75, 550])
  assert.equal((await api('/pricing-defaults')).glueBoardMaxWidthMm, 800)
  await api(`/projects/${saved.id}`, { ...loaded.input, lamellaMinLengthMm: 420 }, 200, 'PUT')
  assert.equal((await api(`/projects/${saved.id}`)).input.lamellaMinLengthMm, 420)
  await api('/pricing-defaults', defaults, 200, 'PUT')
  // Older clients/projects without the new fields retain documented defaults.
  const legacy = { ...input }
  for (const key of ['lamellaMinLengthMm', 'lamellaMinWidthMm', 'lamellaMaxWidthMm', 'lamellaWidthLossMm', 'glueBoardMaxWidthMm', 'offcutMinWidthMm', 'offcutMinLengthMm']) delete legacy[key]
  const old = await api('/projects', legacy, 201)
  assert.deepEqual([old.input.lamellaMinLengthMm, old.input.lamellaMinWidthMm, old.input.lamellaMaxWidthMm], [350, 50, 110])
  assert.equal(old.input.lamellaWidthLossMm, 4)
  assert.equal(old.input.glueBoardMaxWidthMm, 900)
  assert.deepEqual([old.input.offcutMinWidthMm, old.input.offcutMinLengthMm], [50, 350])
  // Stock fits five 110 mm lamellas; jointing reduces their usable width.
  const jointWood = wood + '-jointing'
  const jointBoard = await api('/stock/boards', { woodType: jointWood, widthMm: 110, lengthMm: 5012, thicknessMm: 32 }, 201)
  await api(`/stock/boards/${jointBoard.id}/cut`, { axis: 'length', sizeMm: 1000 })
  const jointInput = project(jointWood, { lamellaLengthExtraMm: 0, glueBoardWastePercent: 0, woodParts: [{ ...input.woodParts[0], woodType: jointWood, widthMm: 550, lengthMm: 1000, quantity: 1 }] })
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
  // Planning ignores pricing reserves and reports how every piece fits in its panel.
  assert.deepEqual(await api('/cutting-plan', { ...input, glueBoardWastePercent: 100, woodReservePercent: 100 }), await api('/cutting-plan', { ...input, glueBoardWastePercent: 0 }))
  const packed = project(wood + '-packing', { woodParts: [{ ...input.woodParts[0], woodType: wood + '-packing', widthMm: 200, lengthMm: 600, quantity: 5 }] })
  const wide = await api('/cutting-plan', packed)
  assert.deepEqual(wide.panels.map(p => p.widthMm), [809, 200])
  assert.deepEqual((await api('/cutting-plan', { ...packed, glueBoardMaxWidthMm: 403 })).panels.map(p => p.widthMm), [403, 403, 200])
  const stacked = await api('/cutting-plan', { ...short, woodParts: [{ ...short.woodParts[0], quantity: 6 }] })
  assert.equal(stacked.panels.length, 1)
  assert.equal(stacked.panels[0].lengthMm, 456)
  assert.equal(stacked.panels[0].widthMm, 123)
  assert.equal(stacked.panels[0].parts.length, 6)
  assert.deepEqual(stacked.panels[0].parts.map(p => [p.offsetLengthMm, p.offsetWidthMm]), [[0, 0], [153, 0], [306, 0], [0, 63], [153, 63], [306, 63]])
  for (const panel of [...wide.panels, ...stacked.panels]) for (const part of panel.parts) {
    assert.ok(part.offsetLengthMm + part.lengthMm <= panel.lengthMm)
    assert.ok(part.offsetWidthMm + part.widthMm <= panel.widthMm)
    for (const other of panel.parts) if (part !== other)
      assert.ok(part.offsetWidthMm + part.widthMm + 3 <= other.offsetWidthMm || other.offsetWidthMm + other.widthMm + 3 <= part.offsetWidthMm || part.offsetLengthMm + part.lengthMm + 3 <= other.offsetLengthMm || other.offsetLengthMm + other.lengthMm + 3 <= part.offsetLengthMm)
  }
  const mixedShort = await api('/cutting-plan', { ...short, woodParts: [{ ...short.woodParts[0], quantity: 2 }, { ...short.woodParts[0], widthMm: 80 }] })
  assert.equal(mixedShort.panels[0].lengthMm, 456)
  assert.equal(mixedShort.panels[0].widthMm, 80)
  assert.equal(mixedShort.panels[0].parts.length, 3)
  const singleWide = await api('/cutting-plan', { ...packed, glueBoardMaxWidthMm: 100 })
  assert.equal(singleWide.panels.length, 5)
  assert.ok(singleWide.warnings.some(w => w.includes('širší')))
  assert.ok(plan.lamellas.every(l => plan.panels.some(p => p.id === l.panelId)))
  const separated = await api('/cutting-plan', { ...packed, woodParts: [...packed.woodParts, { ...packed.woodParts[0], thicknessMm: 35 }, { ...packed.woodParts[0], woodType: 'Other' }] })
  assert.equal(separated.panels.length, 6)
  assert.equal((await api('/cutting-plan', { ...packed, woodParts: [{ ...packed.woodParts[0], quantity: 20001 }] })).complete, false)
  // Saving a verified snapshot atomically consumes stock and returns only reusable offcuts.
  {
  const saveInput = project(`${wood}-save`, { customerName: '' })
  const source = await api('/stock/boards', { woodType: saveInput.woodParts[0].woodType, widthMm: 400, lengthMm: 3000, thicknessMm: 32 }, 201)
  const unused = await api('/stock/boards', { woodType: saveInput.woodParts[0].woodType, widthMm: 400, lengthMm: 5000, thicknessMm: 32 }, 201)
  const saveProject = await api('/projects', saveInput, 201)
  saveInput.woodParts = saveProject.input.woodParts
  const preview = await api('/cutting-plan', saveInput)
  const request = { id: require('node:crypto').randomUUID(), projectId: saveProject.id, input: saveInput, plan: preview }
  const stockBeforeSave = await api('/stock/boards')
  assert.ok((await api('/projects/' + saveProject.id)).input.woodParts.every(p => !p.isProduced), 'Preview does not mark production')
  const tampered = structuredClone(request)
  tampered.plan.offcuts.find(p => p.keepInStock).widthMm++
  await api('/cutting-plans', tampered, 409)
  assert.deepEqual(await api('/stock/boards'), stockBeforeSave)
  assert.ok((await api('/projects/' + saveProject.id)).input.woodParts.every(p => !p.isProduced), 'Rejected plan does not mark production')
  await api('/cutting-plans', { ...request, plan: { ...preview, complete: false } }, 400)
  await api('/cutting-plans', { ...request, projectId: 2147483647 }, 400)
  const saved = await api('/cutting-plans', request)
  assert.deepEqual(saved.plan, preview)
  assert.deepEqual(saved.input, saveProject.input)
  const afterSave = await api('/stock/boards')
  assert.equal(afterSave.find(b => b.id === source.id).isCut, true)
  assert.equal(afterSave.find(b => b.id === source.id).cutAxis, 'plan')
  assert.deepEqual(afterSave.find(b => b.id === unused.id), unused)
  const children = afterSave.filter(b => b.parentId === source.id)
  const reusable = preview.offcuts.filter(p => p.keepInStock && !p.isKerf)
  assert.equal(children.length, reusable.length)
  const dimensions = pieces => pieces.map(p => [p.lengthMm, p.widthMm]).sort((a, b) => a[0] - b[0] || a[1] - b[1])
  assert.deepEqual(dimensions(children), dimensions(reusable))
  assert.ok(children.every(b => !b.isCut && b.thicknessMm === 32 && b.woodType === source.woodType))
  for (const piece of reusable) assert.equal(children.find(b => b.lengthMm === piece.lengthMm && b.widthMm === piece.widthMm).stage, piece.offsetWidthMm > 0 ? 'width' : 'length')
  assert.deepEqual(await api('/cutting-plans', request), saved, 'Retry returns the same snapshot')
  await api('/cutting-plans', { ...request, id: require('node:crypto').randomUUID() }, 409)
  assert.deepEqual(await api('/stock/boards'), afterSave, 'Retry or duplicate cannot consume stock twice')
  const produced = (await api('/projects/' + saveProject.id)).input
  assert.ok(produced.woodParts.every(p => p.isProduced && p.partId))
  assert.deepEqual(await api('/calculate', produced), await api('/calculate', saveInput), 'Production status does not change pricing')
  assert.equal((await api('/cutting-plan', produced)).panels.length, 0)
  for (const patch of [{ name: 'Změna' }, { quantity: 2 }, { isProduced: false }, { partId: undefined }]) {
    await api('/projects/' + saveProject.id, { ...produced, woodParts: [{ ...produced.woodParts[0], ...patch }] }, 409, 'PUT')
  }
  await api('/projects/' + saveProject.id, { ...produced, woodParts: [] }, 409, 'PUT')
  await api('/projects/' + saveProject.id, { ...produced, woodParts: [produced.woodParts[0], produced.woodParts[0]] }, 400, 'PUT')
  await api('/projects/' + saveProject.id, { ...saveInput, name: 'Změněná zakázka' }, 409, 'PUT')
  const next = await api('/projects/' + saveProject.id, { ...produced, name: 'Změněná zakázka', woodParts: [{ ...saveInput.woodParts[0], name: 'Nový dílec', partId: undefined, isProduced: false }, ...produced.woodParts] }, 200, 'PUT')
  const nextPlan = await api('/cutting-plan', next.input)
  assert.ok(nextPlan.complete)
  assert.ok(nextPlan.panels.flatMap(p => p.parts).every(p => p.partIndex === 0 && p.name === 'Nový dílec'))
  const nextSaved = await api('/cutting-plans', { id: require('node:crypto').randomUUID(), projectId: saveProject.id, input: next.input, plan: nextPlan })
  assert.ok((await api('/projects/' + saveProject.id)).input.woodParts.every(p => p.isProduced))
  await api('/cutting-plans', { ...request, id: require('node:crypto').randomUUID() }, 409)
  assert.deepEqual(await api(`/projects/${saveProject.id}/cutting-plans`), [nextSaved, saved], 'Saved snapshot survives edits')
  // A thickness change or manual cut between preview and save rejects the entire operation.
  const staleInput = project(`${wood}-stale`)
  const staleBoard = await api('/stock/boards', { woodType: staleInput.woodParts[0].woodType, widthMm: 400, lengthMm: 3000, thicknessMm: 32 }, 201)
  const staleProject = await api('/projects', staleInput, 201)
  staleInput.woodParts = staleProject.input.woodParts
  const staleRequest = { id: require('node:crypto').randomUUID(), projectId: staleProject.id, input: staleInput, plan: await api('/cutting-plan', staleInput) }
  await api(`/stock/boards/${staleBoard.id}/thickness`, { thicknessMm: 50 })
  const staleStock = await api('/stock/boards')
  await api('/cutting-plans', staleRequest, 409)
  assert.deepEqual(await api('/stock/boards'), staleStock)
  await api(`/stock/boards/${staleBoard.id}/cut`, { axis: 'length', sizeMm: 1000 })
  await api('/cutting-plans', staleRequest, 409)
  assert.deepEqual(await api(`/projects/${staleProject.id}/cutting-plans`), [])
  const raceInput = project(`${wood}-race`)
  const raceBoards = []
  for (let i = 0; i < 2; i++) raceBoards.push(await api('/stock/boards', { woodType: raceInput.woodParts[0].woodType, widthMm: 120, lengthMm: 550, thicknessMm: 32 }, 201))
  const raceProject = await api('/projects', raceInput, 201)
  raceInput.woodParts = raceProject.input.woodParts
  const racePlan = await api('/cutting-plan', raceInput)
  assert.equal(new Set(racePlan.lamellas.map(l => l.boardId)).size, 2)
  const attempts = await Promise.all([0, 1].map(() => client(`${base}/cutting-plans`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ id: require('node:crypto').randomUUID(), projectId: raceProject.id, input: raceInput, plan: racePlan })
  })))
  assert.deepEqual(attempts.map(r => r.status).sort(), [200, 409], 'Only one concurrent plan can claim the boards')
  assert.equal((await api(`/projects/${raceProject.id}/cutting-plans`)).length, 1)
  assert.ok((await api('/stock/boards')).filter(b => raceBoards.some(r => r.id === b.id)).every(b => b.isCut))
  }
  console.log('Cutting plan geometry, stock saving, retries, stale plans and immutable snapshots passed.')
})().catch(error => { console.error(error); process.exitCode = 1 })
