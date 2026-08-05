import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AssessmentCard } from './AssessmentCard'
import type { QueueItem } from '../api/client'

const decide = vi.fn()

vi.mock('../api/client', async (importOriginal) => ({
  // parseBreakdown is a pure reader and worth exercising for real; only the network is faked.
  ...(await importOriginal<typeof import('../api/client')>()),
  api: {
    decide: (...args: unknown[]) => decide(...args),
    assessment: vi.fn(),
  },
}))

const item: QueueItem = {
  id: 'assessment-1',
  noticeId: 'EF-53432',
  noticeTitle: 'VMware lisenssien ja Red Hat tukitilausten hankinta',
  buyerName: 'Teknologian tutkimuskeskus VTT Oy',
  submissionDeadline: '2026-08-14T12:00:00Z',
  estimatedValue: 2_400_000,
  currency: 'EUR',
  deterministicScore: 93,
  scoreRecommendation: 'GO',
  modelRecommendation: 'NO_GO',
  recommendationDisagreement: true,
  reasoning: 'Suositus on NO_GO laskennallisesta piste-ehdotuksesta huolimatta.',
  citations: [],
  modelId: 'gemini-3.6-flash',
  createdAt: '2026-08-05T08:00:00Z',
}

beforeEach(() => decide.mockReset())

describe('AssessmentCard', () => {
  it('shows the score and the model verdict side by side, never merged', () => {
    const { container } = render(<AssessmentCard item={item} reviewer="Niko" onDecided={vi.fn()} />)

    // Scoped to the verdict row: "GO" also appears as an edit option further down the card.
    const verdicts = within(container.querySelector('.verdicts')!)

    expect(screen.getByText('93')).toBeInTheDocument()
    expect(verdicts.getByText('GO')).toBeInTheDocument()
    expect(verdicts.getByText('NO-GO')).toBeInTheDocument()
  })

  it('says plainly when the model and the score disagree', () => {
    render(<AssessmentCard item={item} reviewer="Niko" onDecided={vi.fn()} />)

    expect(screen.getByText(/disagreement — you decide/)).toBeInTheDocument()
  })

  it('does not claim a disagreement when there is none', () => {
    render(
      <AssessmentCard
        item={{ ...item, modelRecommendation: 'GO', recommendationDisagreement: false }}
        reviewer="Niko"
        onDecided={vi.fn()}
      />,
    )

    expect(screen.queryByText(/disagreement/)).not.toBeInTheDocument()
  })

  // The audit trail is the point of the project, and a row with no author is not evidence.
  it('refuses to record a decision until a reviewer is named', () => {
    render(<AssessmentCard item={item} reviewer="" onDecided={vi.fn()} />)

    expect(screen.getByRole('button', { name: 'Approve' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Reject' })).toBeDisabled()
    expect(screen.getByText(/Set your name in the header/)).toBeInTheDocument()
  })

  it('records the reviewer and their note against the decision', async () => {
    const user = userEvent.setup()
    const onDecided = vi.fn()
    decide.mockResolvedValue({})

    render(<AssessmentCard item={item} reviewer="Niko" onDecided={onDecided} />)

    await user.type(screen.getByRole('textbox'), 'Licence resale, not development work.')
    await user.click(screen.getByRole('button', { name: 'Approve' }))

    expect(decide).toHaveBeenCalledWith('assessment-1', {
      decision: 'APPROVED',
      editedRecommendation: undefined,
      reviewerNote: 'Licence resale, not development work.',
      reviewedBy: 'Niko',
    })
    expect(onDecided).toHaveBeenCalled()
  })

  it('sends the replacement recommendation when the reviewer overrides', async () => {
    const user = userEvent.setup()
    decide.mockResolvedValue({})

    render(<AssessmentCard item={item} reviewer="Niko" onDecided={vi.fn()} />)

    await user.click(screen.getByRole('button', { name: 'INVESTIGATE' }))

    expect(decide).toHaveBeenCalledWith(
      'assessment-1',
      expect.objectContaining({ decision: 'EDITED', editedRecommendation: 'INVESTIGATE' }),
    )
  })

  // Offering the verdict the model already gave would be inviting a no-op "override".
  it('does not offer the model’s own recommendation as an edit', () => {
    const { container } = render(<AssessmentCard item={item} reviewer="Niko" onDecided={vi.fn()} />)

    // Array.from rather than a spread: this tsconfig's lib omits DOM.Iterable.
    const options = Array.from(container.querySelectorAll('.edit-group button'), (b) => b.textContent)

    // The model said NO-GO, so only the two it did not say are offered.
    expect(options).toEqual(['GO', 'INVESTIGATE'])
  })
})
