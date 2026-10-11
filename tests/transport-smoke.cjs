const assert = require('node:assert/strict')
const base = `${process.argv[2] || 'http://127.0.0.1:5080'}/api`
async function api(path, method = 'GET', body) {
  const response = await fetch(base + path, { method, headers: { 'Content-Type': 'application/json' }, body: body && JSON.stringify(body) })
  assert.ok(response.ok, `${method} ${path}: ${response.status} ${await (!response.ok ? response.text() : Promise.resolve(''))}`)
  return response.json()
}
const input = { name: 'Převoz fošen', customerName: '', budgetLimit: 0, nonVatPayer: false, woodReservePercent: 0,
  materialOverheadPercent: 0, materialMarginPercent: 0, laborMarginPercent: 0, serviceMarginPercent: 0,
  financeMarginPercent: 0, discountPercent: 0, lines: [], lamellaLengthExtraMm: 0,
  woodParts: [{ name: 'Díl', woodType: 'Dub', widthMm: 200, lengthMm: 1600, thicknessMm: 25, quantity: 1, pricePerM3: 21900 }] }
;(async () => {
  const initial = await api('/calculate', 'POST', input)
  const cuts = initial.glueBoardPurchase[0].transportCuts
  assert.deepEqual(cuts.map(c => c.boardLengthMm), [3000, 4000, 5000])
  assert.deepEqual(cuts[1].pieceLengthsMm, [2380, 1600])
  assert.deepEqual(cuts.map(c => c.lamellaCount), [1, 2, 3])
  assert.deepEqual(cuts[2].pieceLengthsMm, [1760, 1600, 1600])
  for (const max of [21, 350, 1700, 2000, 2700, 5000, 2147483647]) {
    for (const length of [20, 350, 994, 1000, 1249, 1600, 1980, 2480, 2700, 5000, 5001]) {
      const result = await api('/calculate', 'POST', { ...input, transportMaxLengthMm: max,
        woodParts: [{ ...input.woodParts[0], lengthMm: length, widthMm: 1 }] })
      for (const cut of result.glueBoardPurchase[0].transportCuts) {
        if (length > max || length > cut.boardLengthMm) { assert.ok(cut.warning); assert.deepEqual(cut.pieceLengthsMm, []); continue }
        assert.equal(cut.warning, null)
        assert.ok(cut.pieceLengthsMm.every(p => p > 0 && p <= max))
        assert.equal(cut.pieceLengthsMm.reduce((a, b) => a + b, 0) + 20 * (cut.pieceLengthsMm.length - 1), cut.boardLengthMm)
        assert.equal(cut.pieceLengthsMm.reduce((n, p) => n + Math.floor((p + 3) / (length + 3)), 0), cut.lamellaCount)
        // Independent capacity bound for each possible number of transport pieces.
        let optimum = 0, fewest = Infinity
        for (let n = 1; n <= cut.boardLengthMm; n++) {
          const available = cut.boardLengthMm - 20 * (n - 1)
          if (available < n || available > n * max) continue
          for (let units = 1; units <= Math.floor((cut.boardLengthMm + 3) / (length + 3)); units++) {
            if (units > n * Math.floor((max + 3) / (length + 3))) continue
            const occupied = Math.min(units, n)
            const needed = units * length + 3 * (units - occupied) + n - occupied
            if (needed <= available && units > optimum) { optimum = units; fewest = n }
          }
        }
        assert.equal(cut.lamellaCount, optimum)
        assert.equal(cut.pieceLengthsMm.length, fewest)
      }
    }
  }
  const changed = await api('/calculate', 'POST', { ...input, transportMaxLengthMm: 2000 })
  assert.equal(changed.totalWithVat, initial.totalWithVat)
  assert.equal(changed.woodVolumeM3, initial.woodVolumeM3)
  const created = await api('/projects', 'POST', { ...input, transportMaxLengthMm: 2000 })
  try { assert.equal((await api(`/projects/${created.id}`)).input.transportMaxLengthMm, 2000) }
  finally { await fetch(`${base}/projects/${created.id}`, { method: 'DELETE' }) }
  const defaults = await api('/pricing-defaults')
  assert.equal(defaults.transportMaxLengthMm, 2700)
  try {
    await api('/pricing-defaults', 'PUT', { ...defaults, transportMaxLengthMm: 2300 })
    assert.equal((await api('/pricing-defaults')).transportMaxLengthMm, 2300)
  } finally { await api('/pricing-defaults', 'PUT', defaults) }
  for (const bad of [0, -1, 20, 2700.5]) {
    const response = await fetch(`${base}/calculate`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ...input, transportMaxLengthMm: bad }) })
    assert.equal(response.status, 400)
  }
  console.log('Transport cuts, 20 mm kerf, capacity, settings and persistence passed')
})().catch(error => { console.error(error); process.exitCode = 1 })
