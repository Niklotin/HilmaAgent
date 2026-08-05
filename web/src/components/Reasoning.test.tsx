import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { Reasoning } from './Reasoning'
import { renderWithLang } from '../test-utils'
import type { Citation } from '../api/client'

const A = '6f8e9733-7085-46a2-a58e-018326081dd5'
const B = '3caa47e0-804b-4d41-ac83-ca93164fa6b7'
const C = 'c7c54378-abc8-4782-ad82-5a97d4580c73'

const citations: Citation[] = [
  { chunkId: A, section: 'summary', quote: 'Hankinta: Tietoturvatestauspalvelut' },
  { chunkId: B, section: 'description', quote: 'penetraatiotestausta' },
  { chunkId: C, section: 'description', quote: 'potilastietojen käsittely' },
]

function setup(text: string, onToggle = vi.fn()) {
  const result = renderWithLang(
    <Reasoning text={text} citations={citations} openCitation={null} onToggleCitation={onToggle} />,
  )
  return { ...result, onToggle }
}

describe('Reasoning', () => {
  it('replaces a chunk id with the number of its citation', () => {
    const { container } = setup(`Suositus on INVESTIGATE [${A}].`)

    const markers = Array.from(container.querySelectorAll('.cite-marker'), (b) => b.textContent)
    expect(markers).toEqual(['1'])
    // The raw identifier must not survive into prose a human is meant to read.
    expect(container.textContent).not.toContain(A)
  })

  it('numbers markers to match the chips, in citation order', () => {
    const { container } = setup(`Alku [${B}] ja loppu [${C}].`)

    expect(Array.from(container.querySelectorAll('.cite-marker'), (b) => b.textContent)).toEqual(['2', '3'])
  })

  it('handles several ids inside one bracket', () => {
    const { container } = setup(`Perustelu [${A}, ${B}].`)

    expect(Array.from(container.querySelectorAll('.cite-marker'), (b) => b.textContent)).toEqual(['1', '2'])
  })

  it('handles brackets written back to back', () => {
    const { container } = setup(`Perustelu [${A}][${C}].`)

    expect(Array.from(container.querySelectorAll('.cite-marker'), (b) => b.textContent)).toEqual(['1', '3'])
  })

  it('keeps the surrounding prose intact', () => {
    const { container } = setup(`Hankinta koskee tietoturvatestausta [${A}] ja auditointia.`)

    expect(container.textContent).toContain('Hankinta koskee tietoturvatestausta')
    expect(container.textContent).toContain('ja auditointia.')
  })

  it('drops a reference to an id that is not among the citations', () => {
    // The service verifies citations against what the model was given and discards the rest, so a
    // leftover reference resolves to nothing. A dangling "[]" would be worse than silence.
    const { container } = setup(`Väite [11111111-2222-3333-4444-555555555555] ilman lähdettä.`)

    expect(container.querySelectorAll('.cite-marker')).toHaveLength(0)
    expect(container.textContent).not.toContain('11111111')
    expect(container.textContent).toContain('Väite')
  })

  it('opens the cited passage when a marker is clicked', async () => {
    const user = userEvent.setup()
    const onToggle = vi.fn()
    setup(`Suositus [${B}].`, onToggle)

    await user.click(screen.getByRole('button', { name: /2/ }))

    expect(onToggle).toHaveBeenCalledWith(B)
  })

  it('renders text with no citations unchanged', () => {
    const { container } = setup('Ei viitteitä lainkaan.')

    expect(container.querySelectorAll('.cite-marker')).toHaveLength(0)
    expect(container.textContent).toBe('Ei viitteitä lainkaan.')
  })
})
