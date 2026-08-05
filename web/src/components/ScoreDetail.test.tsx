import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ScoreDetail } from './ScoreDetail'
import { renderWithLang } from '../test-utils'

describe('ScoreDetail', () => {
  it('renders the stored English sentence when there is no code', () => {
    // Every assessment made before codes existed looks like this, and they are never rewritten.
    renderWithLang(<ScoreDetail detail="Best CPV match: 72000000 vs 72000000 (exact)." />, 'fi')

    expect(screen.getByText('Best CPV match: 72000000 vs 72000000 (exact).')).toBeInTheDocument()
  })

  it('renders the Finnish sentence when a code is present', () => {
    renderWithLang(
      <ScoreDetail
        detail="Best CPV match: 72000000 vs 72000000 (exact)."
        code="cpv.best_match"
        args={{ notice: '72000000', profile: '72000000', relation: 'exact' }}
      />,
      'fi',
    )

    expect(screen.getByText('Paras CPV-osuma: 72000000 vs 72000000 (tarkka osuma).')).toBeInTheDocument()
  })

  it('translates the relation too, not just the sentence around it', () => {
    // Otherwise the Finnish reads with an English word embedded in the middle of it.
    renderWithLang(
      <ScoreDetail detail="x" code="region.best_match" args={{ notice: 'FI1B1', profile: 'FI1B', relation: 'contained' }} />,
      'fi',
    )

    expect(screen.getByText('Paras alueosuma: FI1B1 vs FI1B (sisältyy).')).toBeInTheDocument()
  })

  it('substitutes numbers into the sentence', () => {
    renderWithLang(
      <ScoreDetail detail="x" code="value.above_maximum" args={{ value: '12 000 000', limit: '3 000 000' }} />,
      'en',
    )

    expect(
      screen.getByText('Estimated value 12 000 000 exceeds the profile maximum of 3 000 000.'),
    ).toBeInTheDocument()
  })

  it('falls back to the stored sentence when the code has no translation', () => {
    // A key with no entry renders as the key itself; showing `score.cpv.invented` to a reviewer
    // would be strictly worse than showing them the English the scorer already wrote.
    renderWithLang(<ScoreDetail detail="Something the scorer said." code="cpv.invented" />, 'fi')

    expect(screen.getByText('Something the scorer said.')).toBeInTheDocument()
    expect(screen.queryByText(/score\.cpv\.invented/)).not.toBeInTheDocument()
  })
})
