import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { LanguageProvider } from './LanguageProvider'
import type { Lang } from './i18n'

/**
 * Renders a component inside the language provider.
 *
 * Behaviour tests pin English so the assertions read as the thing being asserted rather than as a
 * translation exercise. What actually ships is Finnish, and that is covered directly in
 * `i18n.test.ts` — including the check that neither dictionary has drifted from the other.
 */
export function renderWithLang(ui: ReactElement, lang: Lang = 'en') {
  return render(<LanguageProvider initial={lang}>{ui}</LanguageProvider>)
}
