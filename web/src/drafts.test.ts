import { act, renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useDraftNote } from './drafts'

/**
 * The note survives the card being unmounted.
 *
 * Switching tabs unmounts the queue, and the note field is the one thing the UI explicitly asks
 * reviewers to fill in — losing it silently was the bug this hook exists to fix.
 */
describe('useDraftNote', () => {
  const id = 'a1b2c3'

  it('starts empty when nothing was drafted', () => {
    const { result } = renderHook(() => useDraftNote(id))

    expect(result.current.note).toBe('')
  })

  it('survives an unmount and remount, which is what a tab switch does', () => {
    const first = renderHook(() => useDraftNote(id))
    act(() => first.result.current.setNote('SaaS product, not a custom build'))
    first.unmount()

    const second = renderHook(() => useDraftNote(id))

    expect(second.result.current.note).toBe('SaaS product, not a custom build')
  })

  it('keeps drafts separate per assessment', () => {
    const one = renderHook(() => useDraftNote('first'))
    act(() => one.result.current.setNote('note for the first'))

    const two = renderHook(() => useDraftNote('second'))

    expect(two.result.current.note).toBe('')
  })

  it('drops the draft once the decision that consumed it is recorded', () => {
    const { result, unmount } = renderHook(() => useDraftNote(id))
    act(() => result.current.setNote('worth a call with the buyer'))
    act(() => result.current.clearNote())
    unmount()

    expect(renderHook(() => useDraftNote(id)).result.current.note).toBe('')
  })

  it('does not persist whitespace as if it were a draft', () => {
    const { result, unmount } = renderHook(() => useDraftNote(id))
    act(() => result.current.setNote('   '))
    unmount()

    expect(renderHook(() => useDraftNote(id)).result.current.note).toBe('')
  })
})
