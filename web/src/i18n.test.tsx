import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import { LanguageProvider } from './LanguageProvider'
import { dictionaries, useLanguage } from './i18n'

function Probe() {
  const { lang, setLang, t } = useLanguage()
  return (
    <div>
      <span data-testid="lang">{lang}</span>
      <span data-testid="title">{t('app.title')}</span>
      <span data-testid="interpolated">{t('decided.revisions', { n: 3 })}</span>
      <button onClick={() => setLang('en')}>to english</button>
    </div>
  )
}

describe('language', () => {
  it('defaults to Finnish, because the corpus and the users are Finnish', () => {
    localStorage.clear()

    render(
      <LanguageProvider>
        <Probe />
      </LanguageProvider>,
    )

    expect(screen.getByTestId('lang')).toHaveTextContent('fi')
    expect(screen.getByTestId('title')).toHaveTextContent('Hilma-seulonta')
  })

  it('remembers the choice across a reload', async () => {
    localStorage.clear()
    const user = userEvent.setup()

    const { unmount } = render(
      <LanguageProvider>
        <Probe />
      </LanguageProvider>,
    )

    await user.click(screen.getByRole('button', { name: 'to english' }))
    expect(screen.getByTestId('title')).toHaveTextContent('Hilma screening')
    unmount()

    render(
      <LanguageProvider>
        <Probe />
      </LanguageProvider>,
    )

    expect(screen.getByTestId('lang')).toHaveTextContent('en')
  })

  it('interpolates parameters rather than printing the placeholder', () => {
    render(
      <LanguageProvider initial="en">
        <Probe />
      </LanguageProvider>,
    )

    expect(screen.getByTestId('interpolated')).toHaveTextContent('3 decisions')
    expect(screen.getByTestId('interpolated')).not.toHaveTextContent('{n}')
  })

  /**
   * The dictionaries must not drift apart.
   *
   * A key added to one language and forgotten in the other falls back to printing the key itself —
   * `card.approve` on a button. That is the failure mode of every hand-rolled translation layer, so
   * it is the one thing worth testing mechanically.
   */
  it('defines every key in both languages', () => {
    const fi = Object.keys(dictionaries.fi).sort()
    const en = Object.keys(dictionaries.en).sort()

    expect(en).toEqual(fi)
  })

  it('leaves no string untranslated by accident', () => {
    // A few values are the same in both languages on purpose — a product name is not translated,
    // and neither is an example URL. Listing them explicitly keeps the check sharp: anything else
    // that matches is almost certainly an English sentence pasted into the Finnish table.
    const identicalOnPurpose = new Set(['models.providerGemini', 'models.baseUrlPlaceholder'])

    const suspicious = Object.keys(dictionaries.fi).filter((key) => {
      const k = key as keyof typeof dictionaries.fi
      return (
        !identicalOnPurpose.has(key) &&
        dictionaries.fi[k] === dictionaries.en[k] &&
        dictionaries.fi[k].length > 12
      )
    })

    expect(suspicious).toEqual([])
  })
})
