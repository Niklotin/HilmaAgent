import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DecidedList } from './DecidedList'
import type { DecidedItem } from '../api/client'

const decided = vi.fn()
const decide = vi.fn()
const decisions = vi.fn()

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  api: {
    decided: () => decided(),
    decide: (...args: unknown[]) => decide(...args),
    decisions: (...args: unknown[]) => decisions(...args),
  },
}))

const base: DecidedItem = {
  id: 'assessment-1',
  noticeId: 'EF-53730',
  noticeTitle: 'Ennallistamisen ja luonnonhoidon kaivinkonetyöt',
  buyerName: 'Metsähallitus',
  submissionDeadline: '2026-08-28T12:00:00Z',
  estimatedValue: 200_000,
  currency: 'EUR',
  deterministicScore: 52,
  scoreRecommendation: 'INVESTIGATE',
  modelRecommendation: 'NO_GO',
  recommendationDisagreement: true,
  reasoning: 'Kaivinkonetöitä, ei ohjelmistokehitystä.',
  citations: [],
  modelId: 'gemini-3.6-flash',
  createdAt: '2026-08-05T08:00:00Z',
  decision: 'REJECTED',
  editedRecommendation: null,
  reviewerNote: 'Ei kuulu profiiliin.',
  reviewedBy: 'Niko',
  decidedAt: '2026-08-05T12:56:38Z',
  effectiveRecommendation: 'NO_GO',
  revisionCount: 1,
}

beforeEach(() => {
  decided.mockReset()
  decide.mockReset()
  decisions.mockReset()
})

describe('DecidedList', () => {
  it('shows the verdict the reviewer stood behind, and who recorded it', async () => {
    decided.mockResolvedValue([base])

    render(<DecidedList reviewer="Niko" onChanged={vi.fn()} />)

    expect(await screen.findByText('REJECTED')).toBeInTheDocument()
    expect(screen.getByText('Niko')).toBeInTheDocument()
    expect(screen.getByText('Ei kuulu profiiliin.')).toBeInTheDocument()
  })

  it('says plainly when nothing has been decided yet', async () => {
    decided.mockResolvedValue([])

    render(<DecidedList reviewer="Niko" onChanged={vi.fn()} />)

    expect(await screen.findByText(/No decisions recorded yet/)).toBeInTheDocument()
  })

  it('flags an assessment that has been decided more than once', async () => {
    decided.mockResolvedValue([{ ...base, revisionCount: 2 }])

    render(<DecidedList reviewer="Niko" onChanged={vi.fn()} />)

    expect(await screen.findByText('2 decisions')).toBeInTheDocument()
  })

  /**
   * Recording a revision has to reload this list.
   *
   * The parent's refresh only touches the queue and the metrics, so before this was wired the card
   * kept displaying the verdict you had just replaced — the write succeeded and the UI quietly
   * disagreed with the database until you left the tab and came back. Found by driving the real UI.
   */
  it('reloads itself after a change of mind, not just the queue', async () => {
    const user = userEvent.setup()
    decided.mockResolvedValueOnce([base])
    decide.mockResolvedValue({})
    decided.mockResolvedValueOnce([
      { ...base, decision: 'EDITED', editedRecommendation: 'NO_GO', revisionCount: 2 },
    ])

    const onChanged = vi.fn()
    render(<DecidedList reviewer="Niko" onChanged={onChanged} />)

    await user.click(await screen.findByRole('button', { name: /Change my mind/ }))
    const revisit = document.querySelector('.revisit') as HTMLElement
    await user.click(within(revisit).getByRole('button', { name: 'NO-GO' }))

    expect(decide).toHaveBeenCalledWith(
      'assessment-1',
      expect.objectContaining({ decision: 'EDITED', editedRecommendation: 'NO_GO', reviewedBy: 'Niko' }),
    )

    // The list refetched and now shows the new verdict and the revision count.
    await waitFor(() => expect(decided).toHaveBeenCalledTimes(2))
    expect(await screen.findByText('EDITED')).toBeInTheDocument()
    expect(await screen.findByText('2 decisions')).toBeInTheDocument()

    // And the parent still hears about it, so the metrics bar stays in step.
    expect(onChanged).toHaveBeenCalled()
  })

  it('will not record a revision without a reviewer name', async () => {
    const user = userEvent.setup()
    decided.mockResolvedValue([base])

    render(<DecidedList reviewer="" onChanged={vi.fn()} />)

    await user.click(await screen.findByRole('button', { name: /Change my mind/ }))

    const revisit = document.querySelector('.revisit') as HTMLElement
    expect(within(revisit).getByRole('button', { name: 'Approve' })).toBeDisabled()
    expect(screen.getByText(/Set your name in the header/)).toBeInTheDocument()
  })
})
