// Regenerates the README screenshots from the running container.
//   node shots.mjs      (from web/, with the stack up on :5080)
import { chromium } from '@playwright/test'

const OUT = '../docs/images'
const browser = await chromium.launch()
const page = await browser.newPage({ viewportSize: { width: 1180, height: 900 }, deviceScaleFactor: 2 })

try {
  await page.goto('http://localhost:5080')
  await page.getByLabel(/Nimesi/).fill('Niko')

  // 1. The queue, with the disagreement card opened up — the project's whole argument in one frame:
  //    a high score, a model that disagrees, and the rule-by-rule reason it can be argued with.
  const flagged = page.locator('.card-flagged').first()
  await flagged.waitFor()
  await flagged.getByRole('button', { name: /Näytä pisteiden erittely/ }).click()
  await flagged.locator('table.breakdown tr').first().waitFor()
  await flagged.locator('.citation-chip').first().click()
  await flagged.locator('blockquote').first().waitFor()
  // Element screenshots rather than viewport clips: these cards are taller than the window, and a
  // clip would cut the breakdown off exactly where it gets interesting.
  await flagged.screenshot({ path: `${OUT}/queue-disagreement.png` })

  // 2. Semantic search: a Finnish query, ranked notices, the matching passage under each.
  await page.getByRole('tab', { name: 'arvioi' }).click()
  await page.getByLabel('Semanttinen hakulause').fill('ohjelmistokehitys ja integraatiot')
  await page.getByRole('button', { name: 'Hae' }).click()
  await page.locator('.result').first().locator('.passage').waitFor()
  await page.screenshot({ path: `${OUT}/search.png`, clip: { x: 0, y: 0, width: 1180, height: 760 } })

  // 3. What an approval actually leads to: still-open notices somebody backed, deadline first,
  //    each linking to where bids are submitted.
  await page.getByRole('tab', { name: 'kärkilista' }).click()
  await page.locator('.shortlist-row').first().waitFor()
  await page.locator('main').screenshot({ path: `${OUT}/shortlist.png` })

  // 4. The read side of the audit trail, with a revision expanded.
  await page.getByRole('tab', { name: 'päätetyt' }).click()
  const decided = page.locator('.card-decided').first()
  await decided.waitFor()
  await decided.getByRole('button', { name: /Näytä päätöshistoria/ }).click()
  await decided.locator('table.breakdown tr').first().waitFor()
  await decided.screenshot({ path: `${OUT}/decided-audit-trail.png` })

  console.log('wrote queue-disagreement.png, search.png, shortlist.png, decided-audit-trail.png')
} finally {
  await browser.close()
}
