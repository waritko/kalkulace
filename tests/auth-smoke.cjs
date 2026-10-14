const assert = require('node:assert/strict')
const { authenticatedFetch } = require('./auth-helper.cjs')
const base = process.argv[2] || 'http://127.0.0.1:5080'
const password = 'test-password-123'
async function request(client, path, method = 'GET', body, status = 200) {
  const response = await client(`${base}/api${path}`, { method, headers: { 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await response.text()
  assert.equal(response.status, status, `${method} ${path}: ${text}`)
  return text ? JSON.parse(text) : undefined
}
;(async () => {
  assert.equal((await fetch(base)).status, 200, 'Login shell must be public')
  for (const path of ['/projects', '/invoices', '/catalog', '/pricing-defaults', '/stock/boards', '/users', '/auth/me'])
    await request(fetch, path, 'GET', undefined, 401)
  await request(fetch, '/auth/login', 'POST', { username: 'waritko', password }, 400) // CSRF header missing
  const anonymous = (url, init) => fetch(url, { ...init, headers: { ...init.headers, 'X-Kalkulace-Request': '1' } })
  await request(anonymous, '/auth/login', 'POST', { username: 'waritko', password }, 401)
  await request(anonymous, '/auth/login', 'POST', { username: 'unknown', password }, 401)
  const admin = await authenticatedFetch(base)
  const me = await request(admin, '/auth/me')
  assert.equal(me.isAdmin, true)
  const users = await request(admin, '/users')
  assert.ok(users.every(user => !('passwordHash' in user) && !('securityStamp' in user)))
  if (users.filter(user => user.isAdmin).length === 1) {
    await request(admin, `/users/${me.id}`, 'DELETE', undefined, 409)
    await request(admin, `/users/${me.id}`, 'PUT', { username: me.username, isAdmin: false }, 409)
  }
  const username = `smoke_${Date.now()}`
  let user, second
  try {
    await request(admin, '/users', 'POST', { username, password: 'short', isAdmin: false }, 400)
    user = await request(admin, '/users', 'POST', { username, password, isAdmin: false }, 201)
    await request(admin, '/users', 'POST', { username: username.toUpperCase(), password, isAdmin: false }, 409)
    const regular = await authenticatedFetch(base, username.toUpperCase(), password)
    assert.equal((await request(regular, '/auth/me')).isAdmin, false)
    const catalog = await request(regular, '/catalog')
    const defaults = await request(regular, '/pricing-defaults')
    await request(regular, '/catalog', 'PUT', catalog, 403)
    await request(regular, '/pricing-defaults', 'PUT', defaults, 403)
    await request(regular, '/users', 'GET', undefined, 403)
    await request(regular, '/users', 'POST', { username: 'forbidden', password, isAdmin: true }, 403)
    await request(regular, `/users/${me.id}`, 'PUT', { username: me.username, isAdmin: false }, 403)
    await request(regular, `/users/${me.id}`, 'DELETE', undefined, 403)
    await request(regular, '/projects')
    await request(regular, '/stock/boards')
    const projectInput = { ...defaults, name: 'Authentication smoke', customerName: '', woodParts: [], lines: [] }
    await request(regular, '/calculate', 'POST', projectInput)
    const project = await request(regular, '/projects', 'POST', projectInput, 201)
    try {
      await request(regular, `/projects/${project.id}`)
      await request(regular, `/projects/${project.id}`, 'PUT', { ...projectInput, name: 'Updated by regular user' })
    } finally { await request(regular, `/projects/${project.id}`, 'DELETE', undefined, 204) }
    await request(admin, '/catalog', 'PUT', catalog)
    await request(admin, '/pricing-defaults', 'PUT', defaults)
    await request(admin, `/users/${user.id}`, 'PUT', { username, password: '', isAdmin: true })
    await request(regular, '/auth/me', 'GET', undefined, 401)
    second = await authenticatedFetch(base, username, password)
    assert.equal((await request(second, '/auth/me')).isAdmin, true)
    await request(second, '/users')
    await request(admin, `/users/${user.id}`, 'PUT', { username, password: 'replacement-password', isAdmin: false })
    await request(second, '/auth/me', 'GET', undefined, 401)
    await request(anonymous, '/auth/login', 'POST', { username, password }, 401)
    second = await authenticatedFetch(base, username, 'replacement-password')
    await request(second, '/auth/logout', 'POST', undefined, 204)
    await request(second, '/projects', 'GET', undefined, 401)
    second = await authenticatedFetch(base, username, 'replacement-password')
    await request(admin, `/users/${user.id}`, 'DELETE', undefined, 204)
    user = null
    await request(second, '/auth/me', 'GET', undefined, 401)
    await request(admin, '/auth/logout', 'POST', undefined, 204)
    await request(admin, '/auth/me', 'GET', undefined, 401)
  } finally {
    if (user) { const cleanup = await authenticatedFetch(base); await request(cleanup, `/users/${user.id}`, 'DELETE', undefined, 204) }
  }
  let rateLimited = false
  for (let attempt = 0; attempt < 11; attempt++) {
    const response = await anonymous(`${base}/api/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username: 'unknown', password }) })
    if (response.status === 429) { rateLimited = true; break }
    assert.equal(response.status, 401)
  }
  assert.ok(rateLimited, 'Repeated login attempts must be rate limited')
  console.log('Authentication, roles, users, password resets, revocation, logout and rate limiting passed.')
})().catch(error => { console.error(error); process.exitCode = 1 })
