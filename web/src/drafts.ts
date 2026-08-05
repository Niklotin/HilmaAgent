import { useCallback, useState } from 'react'

/**
 * The reviewer's in-progress note.
 *
 * This was plain component state, so switching tabs unmounted the card and silently discarded
 * whatever had been typed — in the one field the UI goes out of its way to ask people to fill in.
 * Kept in sessionStorage so a draft survives a tab switch and a reload, and dropped once the
 * decision that consumed it is recorded.
 */
const key = (assessmentId: string) => `hilma.note.${assessmentId}`

export function useDraftNote(assessmentId: string) {
  const [note, setNote] = useState(() => sessionStorage.getItem(key(assessmentId)) ?? '')

  const update = useCallback(
    (value: string) => {
      setNote(value)
      if (value.trim()) sessionStorage.setItem(key(assessmentId), value)
      else sessionStorage.removeItem(key(assessmentId))
    },
    [assessmentId],
  )

  const clear = useCallback(() => {
    sessionStorage.removeItem(key(assessmentId))
    setNote('')
  }, [assessmentId])

  return { note, setNote: update, clearNote: clear }
}
