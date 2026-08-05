import { screen } from '@testing-library/react'
import { renderWithLang } from '../test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { Shortlist } from './Shortlist'
import type { ShortlistItem } from '../api/client'

const shortlist = vi.fn()

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  api: { shortlist: () => shortlist() },
}))

const item: ShortlistItem = {
  assessmentId: 'a-1',
  noticeId: 'EF-53362',
  noticeTitle: 'Internal Control System',
  buyerName: 'Veikkaus Oy',
  submissionDeadline: '2026-08-24T12:00:00Z',
  estimatedValue: 250_000,
  currency: 'EUR',
  deterministicScore: 87,
  procurementDocumentsUrl: 'https://tarjouspalvelu.fi/veikkaus?id=613395',
  standing: 'GO',
  reviewedBy: 'Niko',
  decidedAt: '2026-08-05T13:08:31Z',
  reviewerNote: null,
  daysLeft: 19,
}

beforeEach(() => shortlist.mockReset())

describe('Shortlist', () => {
  it('leads with the time left, because that is what runs out', async () => {
    shortlist.mockResolvedValue([item])

    renderWithLang(<Shortlist />)

    expect(await screen.findByText('19')).toBeInTheDocument()
    expect(screen.getByText('days left')).toBeInTheDocument()
  })

  it('links to where bids are actually submitted', async () => {
    shortlist.mockResolvedValue([item])

    renderWithLang(<Shortlist />)

    const link = await screen.findByRole('link', { name: /Tender documents/ })
    expect(link).toHaveAttribute('href', 'https://tarjouspalvelu.fi/veikkaus?id=613395')
    // Opening the buyer's portal must not navigate the queue away, nor leak the referrer.
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', expect.stringContaining('noopener'))
  })

  it('says so plainly when the notice carries no link', async () => {
    // Legacy notices have no documents URL. Inventing one would be worse than admitting it.
    shortlist.mockResolvedValue([{ ...item, procurementDocumentsUrl: null }])

    renderWithLang(<Shortlist />)

    expect(await screen.findByText('no link in notice')).toBeInTheDocument()
    expect(screen.queryByRole('link', { name: /Tender documents/ })).not.toBeInTheDocument()
  })

  it('marks a tender closing within the week as urgent', async () => {
    shortlist.mockResolvedValue([{ ...item, daysLeft: 3 }])

    const { container } = renderWithLang(<Shortlist />)

    expect(await screen.findByText('3')).toBeInTheDocument()
    expect(container.querySelector('.shortlist-urgent')).not.toBeNull()
  })

  it('does not cry urgency for a deadline six weeks out', async () => {
    shortlist.mockResolvedValue([{ ...item, daysLeft: 42 }])

    const { container } = renderWithLang(<Shortlist />)

    await screen.findByText('42')
    expect(container.querySelector('.shortlist-urgent')).toBeNull()
  })

  it('explains how something gets here when nothing has', async () => {
    shortlist.mockResolvedValue([])

    renderWithLang(<Shortlist />)

    expect(await screen.findByText(/Nothing on the shortlist/)).toBeInTheDocument()
  })

  it('shows the standing the reviewer backed, not the raw enum', async () => {
    shortlist.mockResolvedValue([{ ...item, standing: 'INVESTIGATE' }])

    renderWithLang(<Shortlist />)

    expect(await screen.findByText('INVESTIGATE')).toBeInTheDocument()
    expect(screen.getByText('Niko')).toBeInTheDocument()
  })
})
