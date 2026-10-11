const assert = require('node:assert/strict')
const base = `${process.argv[2] || 'http://127.0.0.1:5080'}/api/stock/boards`
async function api(path = '', body, status = 200) {
  const response = await fetch(base + path, body === undefined ? undefined : {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
  })
  const text = await response.text()
  assert.equal(response.status, status, text)
  return text ? JSON.parse(text) : undefined
}
;(async () => {
  const initial = await api()
  const input = { woodType: ' Dub ', widthMm: 300, lengthMm: 3000 }
  for (const invalid of [{ ...input, woodType: '' }, { ...input, widthMm: 0 }, { ...input, lengthMm: 2000 }])
    await api('', invalid, 400)
  const board = await api('', input, 201)
  assert.equal(board.woodType, 'Dub')
  assert.equal(board.stage, 'original')
  assert.equal(board.isCut, false)
  await api(`/${board.id}/cut`, { axis: 'width', sizeMm: 100 }, 400)
  await api(`/${board.id}/cut`, { axis: 'other', sizeMm: 100 }, 400)
  await api(`/${board.id}/cut`, { axis: 'length', sizeMm: 2997, kerfMm: 3 }, 400)
  await api(`/${board.id}/cut`, { axis: 'length', sizeMm: 1000, kerfMm: -1 }, 400)
  await api(`/${board.id}/cut`, { axis: 'length', sizeMm: 0 }, 400)
  assert.equal((await api()).length, initial.length + 1, 'Invalid cuts must not change stock')
  const lengths = await api(`/${board.id}/cut`, { axis: 'length', sizeMm: 1000, kerfMm: 3 })
  assert.deepEqual(lengths.map(b => [b.widthMm, b.lengthMm, b.parentId, b.stage]), [[300, 1000, board.id, 'length'], [300, 1997, board.id, 'length']])
  await api(`/${board.id}/cut`, { axis: 'length', sizeMm: 500 }, 409)
  const widths = await api(`/${lengths[0].id}/cut`, { axis: 'width', sizeMm: 100, kerfMm: 3 })
  assert.deepEqual(widths.map(b => [b.widthMm, b.lengthMm, b.parentId, b.stage]), [[100, 1000, lengths[0].id, 'width'], [197, 1000, lengths[0].id, 'width']])
  await api(`/${widths[0].id}/cut`, { axis: 'length', sizeMm: 500 }, 400)
  const strips = await api(`/${widths[1].id}/cut`, { axis: 'width', sizeMm: 50, kerfMm: 0 })
  assert.equal(strips[1].widthMm, 147)
  const shorter = await api(`/${lengths[1].id}/cut`, { axis: 'length', sizeMm: 700 })
  assert.equal(shorter[1].lengthMm, 1294, 'Default kerf is 3 mm')
  const stored = await api()
  const original = stored.find(b => b.id === board.id)
  assert.deepEqual([original.isCut, original.cutAxis, original.cutSizeMm, original.kerfMm], [true, 'length', 1000, 3])
  assert.equal(stored.filter(b => b.id >= board.id && !b.isCut).length, 5)
  for (const lengthMm of [4000, 5000]) {
    const received = await api('', { ...input, lengthMm }, 201)
    assert.equal(received.lengthMm, lengthMm)
  }
  // Racing requests must never create four children from the same piece.
  const concurrent = await api('', input, 201)
  const requests = await Promise.all([1, 2].map(() => fetch(`${base}/${concurrent.id}/cut`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ axis: 'length', sizeMm: 1000 })
  })))
  assert.deepEqual(requests.map(r => r.status).sort(), [200, 409])
  assert.equal((await api()).filter(b => b.parentId === concurrent.id).length, 2)
  await api('/2147483647/cut', { axis: 'length', sizeMm: 100 }, 404)
  console.log('Stock receipt, cut order, kerf, history and concurrent cuts passed.')
})().catch(error => { console.error(error); process.exitCode = 1 })
