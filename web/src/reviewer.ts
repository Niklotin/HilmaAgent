import { useCallback, useState } from 'react'

/**
 * Who is reviewing.
 *
 * Every decision row records an author. This was previously the hardcoded string "reviewer", which
 * made the audit trail unattributable — the one thing an audit trail cannot be. Kept in
 * localStorage because there is no auth yet: a real deployment replaces this with the signed-in
 * identity, and nothing else has to change, since the name only ever travels as `reviewedBy`.
 */
const KEY = 'hilma.reviewer'

export function useReviewer() {
  const [reviewer, setStored] = useState(() => localStorage.getItem(KEY) ?? '')

  const setReviewer = useCallback((name: string) => {
    const trimmed = name.trim()
    setStored(trimmed)
    if (trimmed) localStorage.setItem(KEY, trimmed)
    else localStorage.removeItem(KEY)
  }, [])

  return { reviewer, setReviewer }
}
