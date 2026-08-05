import { expect, test } from '@playwright/test'

/**
 * The seam the unit tests cannot reach: the built bundle talking to the real API.
 *
 * Component tests fake the network, and backend tests never render anything, so a mismatch between
 * the two — a renamed field, a projection that stopped being returned, static files not served —
 * passes both suites and breaks the app. That is what these cover.
 *
 * Read-only by design. Decisions are append-only, so a test that recorded one could not clean up
 * after itself and every run would leave real rows in the audit trail.
 */

test('serves the bundled SPA from the same origin as the API', async ({ page }) => {
  const response = await page.goto('/')

  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Hilma screening' })).toBeVisible()
  // The claim the whole project rests on, stated on the page itself.
  await expect(page.getByText(/Scores computed in code/)).toBeVisible()
})

test('client-side routes fall back to the SPA rather than 404ing', async ({ page }) => {
  const response = await page.goto('/decided')

  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Hilma screening' })).toBeVisible()
})

test('renders queue cards from the live API', async ({ page }) => {
  await page.goto('/')

  const cards = page.locator('.card')
  await expect(cards.first()).toBeVisible()

  // Score and model verdict side by side, never merged.
  const first = cards.first()
  await expect(first.locator('.score strong')).toHaveText(/^\d+$/)
  await expect(first.getByText('Score says')).toBeVisible()
  await expect(first.getByText('Model says')).toBeVisible()
})

test('expands a score breakdown fetched on demand', async ({ page }) => {
  await page.goto('/')

  const first = page.locator('.card').first()
  await first.getByRole('button', { name: /Show score breakdown/ }).click()

  // The breakdown is a second request; if the projection or the JSON casing drifted, this is where
  // it shows up rather than in a mocked component test.
  const rows = first.locator('table.breakdown tr')
  await expect(rows.first()).toBeVisible()
  await expect(first.getByText('cpv_overlap')).toBeVisible()
})

test('refuses to record a decision until a reviewer is named', async ({ page }) => {
  await page.goto('/')

  const first = page.locator('.card').first()
  await expect(first.getByRole('button', { name: 'Approve' })).toBeDisabled()
  await expect(page.getByText(/Set your name in the header/).first()).toBeVisible()

  await page.getByLabel(/Your name/).fill('E2E Reviewer')

  await expect(first.getByRole('button', { name: 'Approve' })).toBeEnabled()
})

test('filters the queue by deadline and by disagreement', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('.card').first()).toBeVisible()

  const before = await page.locator('.card').count()

  await page.getByLabel('disagreements only').check()

  // Poll rather than assert once: the filter round-trips to the API, and disagreements already sort
  // first, so the pre-filter DOM briefly satisfies a naive "a flagged card is visible" check.
  await expect
    .poll(async () => {
      const total = await page.locator('.card').count()
      const flagged = await page.locator('.card-flagged').count()
      return total > 0 && total === flagged
    })
    .toBe(true)

  const filtered = await page.locator('.card').count()
  expect(filtered).toBeLessThanOrEqual(before)

  await page.getByLabel('disagreements only').uncheck()
  await expect.poll(async () => page.locator('.card').count()).toBe(before)

  await page.getByLabel('Sort by').selectOption('deadline')

  // Deadlines ascending, with the undated ones last — the ordering is the whole point of the option.
  await expect
    .poll(async () => {
      const dates = (await page.locator('.card .facts div:first-child dd').allTextContents())
        .map((text) => text.trim().slice(0, 10))
        .filter((text) => /^\d{4}-\d{2}-\d{2}$/.test(text))

      return dates.length > 1 && dates.every((date, i) => i === 0 || dates[i - 1] <= date)
    })
    .toBe(true)
})

test('searches the notice index semantically', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('tab', { name: 'assess' }).click()

  await page.getByLabel('Semantic search query').fill('ohjelmistokehitys ja integraatiot')
  await page.getByRole('button', { name: 'Search' }).click()

  // Phase 2 end to end: embed the query, hit Qdrant, group chunks up to notices, render the passage.
  await expect(page.locator('.result').first()).toBeVisible({ timeout: 20_000 })
  await expect(page.locator('.result').first().locator('.passage')).toBeVisible()
})

test('shows decided assessments with their reviewer note', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('tab', { name: 'decided' }).click()

  const card = page.locator('.card-decided').first()
  await expect(card).toBeVisible()

  // What the reviewer stood behind, and who recorded it — the read side of the audit trail.
  await expect(card.getByText('Reviewer stood behind')).toBeVisible()
  await expect(card.locator('.decision')).toBeVisible()

  await card.getByRole('button', { name: /Show decision history/ }).click()
  await expect(card.locator('table.breakdown tr').first()).toBeVisible()
})
