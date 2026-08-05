import { describe, expect, it } from 'vitest'
import { parseBreakdown } from './client'

/**
 * `parseBreakdown` is the reader that lets stored assessments stay unrewritten.
 *
 * Assessments written before the serializer was fixed hold PascalCase keys; newer ones hold
 * camelCase. The project's rule is that the *reader* adapts rather than the history being migrated,
 * so both casings have to keep working — which is exactly the kind of quiet compatibility shim that
 * gets "tidied up" by someone who does not know why it is there.
 */
describe('parseBreakdown', () => {
  const camel = JSON.stringify({
    total: 78,
    rules: [{ rule: 'cpv_overlap', awarded: 45, max: 45, detail: 'Best CPV match: exact.' }],
    gates: [],
    warnings: ['The buyer withheld the contract value.'],
  })

  const pascal = JSON.stringify({
    Total: 78,
    Rules: [{ Rule: 'cpv_overlap', Awarded: 45, Max: 45, Detail: 'Best CPV match: exact.' }],
    Gates: [],
    Warnings: ['The buyer withheld the contract value.'],
  })

  it('reads camelCase, as written today', () => {
    const breakdown = parseBreakdown(camel)

    expect(breakdown?.total).toBe(78)
    expect(breakdown?.rules).toHaveLength(1)
    expect(breakdown?.rules[0]).toEqual({
      rule: 'cpv_overlap',
      awarded: 45,
      max: 45,
      detail: 'Best CPV match: exact.',
    })
    expect(breakdown?.warnings).toEqual(['The buyer withheld the contract value.'])
  })

  it('reads PascalCase, as older rows were written', () => {
    expect(parseBreakdown(pascal)).toEqual(parseBreakdown(camel))
  })

  it('reads gates, which are what force a score to zero', () => {
    const json = JSON.stringify({
      total: 0,
      rules: [],
      gates: [{ gate: 'deadline_passed', detail: 'Submission deadline passed on 2026-07-01.' }],
      warnings: [],
    })

    expect(parseBreakdown(json)?.gates).toEqual([
      { gate: 'deadline_passed', detail: 'Submission deadline passed on 2026-07-01.' },
    ])
  })

  it('reads PascalCase gates too', () => {
    const json = JSON.stringify({
      Total: 0,
      Gates: [{ Gate: 'cancelled', Detail: 'The notice has been cancelled by the buyer.' }],
    })

    expect(parseBreakdown(json)?.gates).toEqual([
      { gate: 'cancelled', detail: 'The notice has been cancelled by the buyer.' },
    ])
  })

  it('defaults missing collections rather than throwing', () => {
    const breakdown = parseBreakdown(JSON.stringify({ total: 12 }))

    expect(breakdown).toEqual({ total: 12, rules: [], gates: [], warnings: [] })
  })

  // A card that cannot parse its breakdown should still render the rest of the assessment, so the
  // failure mode is "no breakdown shown", never a thrown error inside the queue.
  it.each([
    ['null', null],
    ['undefined', undefined],
    ['an empty string', ''],
    ['malformed JSON', '{not json'],
  ])('returns null for %s', (_label, input) => {
    expect(parseBreakdown(input)).toBeNull()
  })
})
