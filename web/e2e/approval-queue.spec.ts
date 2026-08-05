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
  await expect(page.getByRole('heading', { name: 'Hilma-seulonta' })).toBeVisible()
  // The claim the whole project rests on, stated on the page itself.
  await expect(page.getByText(/Pisteet lasketaan koodissa/)).toBeVisible()
})

test('client-side routes fall back to the SPA rather than 404ing', async ({ page }) => {
  const response = await page.goto('/decided')

  expect(response?.status()).toBe(200)
  await expect(page.getByRole('heading', { name: 'Hilma-seulonta' })).toBeVisible()
})

test('renders queue cards from the live API', async ({ page }) => {
  await page.goto('/')

  const cards = page.locator('.card')
  await expect(cards.first()).toBeVisible()

  // Score and model verdict side by side, never merged.
  const first = cards.first()
  await expect(first.locator('.score strong')).toHaveText(/^\d+$/)
  await expect(first.getByText('Pisteet sanovat')).toBeVisible()
  await expect(first.getByText('Malli sanoo')).toBeVisible()
})

test('expands a score breakdown fetched on demand', async ({ page }) => {
  await page.goto('/')

  const first = page.locator('.card').first()
  await first.getByRole('button', { name: /Näytä pisteiden erittely/ }).click()

  // The breakdown is a second request; if the projection or the JSON casing drifted, this is where
  // it shows up rather than in a mocked component test.
  const rows = first.locator('table.breakdown tr')
  await expect(rows.first()).toBeVisible()
  await expect(first.getByText('cpv_overlap')).toBeVisible()
})

test('refuses to record a decision until a reviewer is named', async ({ page }) => {
  await page.goto('/')

  const first = page.locator('.card').first()
  await expect(first.getByRole('button', { name: 'Hyväksy' })).toBeDisabled()
  await expect(page.getByText(/Aseta nimesi yläpalkkiin/).first()).toBeVisible()

  await page.getByLabel(/Nimesi/).fill('E2E Reviewer')

  await expect(first.getByRole('button', { name: 'Hyväksy' })).toBeEnabled()
})

test('filters the queue by deadline and by disagreement', async ({ page }) => {
  await page.goto('/')
  await expect(page.locator('.card').first()).toBeVisible()

  const before = await page.locator('.card').count()

  await page.getByLabel('vain erimielisyydet').check()

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

  await page.getByLabel('vain erimielisyydet').uncheck()
  await expect.poll(async () => page.locator('.card').count()).toBe(before)

  await page.getByLabel('Järjestys').selectOption('deadline')

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
  await page.getByRole('tab', { name: 'arvioi' }).click()

  await page.getByLabel('Semanttinen hakulause').fill('ohjelmistokehitys ja integraatiot')
  await page.getByRole('button', { name: 'Hae' }).click()

  // Phase 2 end to end: embed the query, hit Qdrant, group chunks up to notices, render the passage.
  await expect(page.locator('.result').first()).toBeVisible({ timeout: 20_000 })
  await expect(page.locator('.result').first().locator('.passage')).toBeVisible()
})

test('shortlists what a reviewer backed, with a link to where bids go', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('tab', { name: 'kärkilista' }).click()

  const row = page.locator('.shortlist-row').first()
  await expect(row).toBeVisible()

  // The countdown leads, because the deadline is what runs out.
  await expect(row.locator('.shortlist-when')).toBeVisible()

  // And the row ends in something actionable: either the buyer's portal or an honest admission that
  // the notice gave none.
  const link = row.getByRole('link', { name: /Tarjousasiakirjat/ })
  const hasLink = (await link.count()) > 0

  if (hasLink) {
    await expect(link).toHaveAttribute('href', /^https?:\/\//)
    await expect(link).toHaveAttribute('target', '_blank')
  } else {
    await expect(row.getByText('ei linkkiä ilmoituksessa')).toBeVisible()
  }
})

test('ships in Finnish and switches to English on request', async ({ page }) => {
  await page.goto('/')

  // Finnish is the default: the corpus, the queries and the narratives are all Finnish.
  await expect(page.getByRole('heading', { name: 'Hilma-seulonta' })).toBeVisible()
  await expect(page.locator('html')).toHaveAttribute('lang', 'fi')

  await page.getByLabel('Kieli').selectOption('en')

  await expect(page.getByRole('heading', { name: 'Hilma screening' })).toBeVisible()
  await expect(page.getByRole('tab', { name: 'shortlist' })).toBeVisible()

  // And the choice survives a reload rather than snapping back.
  await page.reload()
  await expect(page.getByRole('heading', { name: 'Hilma screening' })).toBeVisible()
})

test('renders citations as numbers, never as raw identifiers', async ({ page }) => {
  await page.goto('/')

  const card = page.locator('.card').filter({ has: page.locator('.cite-marker') }).first()
  await expect(card).toBeVisible()

  // The prose must not contain a bare chunk id — that was the bug.
  const prose = (await card.locator('.reasoning').innerText()).trim()
  expect(prose).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i)

  // Clicking a marker opens the passage that identifier points at.
  await card.locator('.cite-marker').first().click()
  await expect(card.locator('.citations blockquote')).toBeVisible()
})

test('shows decided assessments with their reviewer note', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('tab', { name: 'päätetyt' }).click()

  const card = page.locator('.card-decided').first()
  await expect(card).toBeVisible()

  // What the reviewer stood behind, and who recorded it — the read side of the audit trail.
  await expect(card.getByText('Käsittelijä puolsi')).toBeVisible()
  await expect(card.locator('.decision')).toBeVisible()

  await card.getByRole('button', { name: /Näytä päätöshistoria/ }).click()
  await expect(card.locator('table.breakdown tr').first()).toBeVisible()
})
