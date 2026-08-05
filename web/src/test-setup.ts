import '@testing-library/jest-dom/vitest'
import { afterEach, beforeEach } from 'vitest'
import { cleanup } from '@testing-library/react'

// Both storages are real state in this app — the reviewer's name and their in-progress notes — so
// a leaked value from one test would quietly change what another one is asserting.
beforeEach(() => {
  localStorage.clear()
  sessionStorage.clear()
})

afterEach(cleanup)
