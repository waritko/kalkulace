const assert = require('node:assert/strict')

async function authenticatedFetch(baseUrl, username = process.env.KALKULACE_TEST_USERNAME || 'waritko', password = process.env.KALKULACE_TEST_PASSWORD || 'blade666') {
  let cookie = ''
  const client = async (url, init = {}) => {
    const headers = new Headers(init.headers)
    headers.set('X-Kalkulace-Request', '1')
    if (cookie) headers.set('Cookie', cookie)
    const response = await fetch(url, { ...init, headers })
    const cookies = response.headers.getSetCookie()
    if (cookies.length) cookie = cookies.map(value => value.split(';')[0]).join('; ')
    return response
  }
  if (arguments.length === 1 && process.env.KALKULACE_TEST_COOKIE) {
    cookie = process.env.KALKULACE_TEST_COOKIE
    return client
  }
  const response = await client(`${baseUrl}/api/auth/login`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ username, password })
  })
  assert.equal(response.status, 200, 'Test account must be able to log in')
  return client
}
module.exports = { authenticatedFetch }
