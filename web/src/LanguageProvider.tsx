import { useCallback, useMemo, useState, type ReactNode } from 'react'
import {
  LanguageContext,
  dictionaries,
  readStoredLang,
  type Lang,
  type LanguageValue,
  type Translate,
} from './i18n'

const STORAGE_KEY = 'hilma.lang'

/**
 * Supplies the interface language to the tree.
 *
 * Kept apart from the dictionary module so that neither file exports both a component and plain
 * functions — React Fast Refresh cannot reload a module that mixes the two.
 */
export function LanguageProvider({ children, initial }: { children: ReactNode; initial?: Lang }) {
  const [lang, setStored] = useState<Lang>(() => initial ?? readStoredLang())

  const setLang = useCallback((next: Lang) => {
    setStored(next)
    localStorage.setItem(STORAGE_KEY, next)
    document.documentElement.lang = next
  }, [])

  const value = useMemo<LanguageValue>(() => {
    const table = dictionaries[lang]

    const t: Translate = (key, params) => {
      const template = table[key] ?? key
      if (!params) return template
      return Object.entries(params).reduce(
        (text, [name, replacement]) => text.replaceAll(`{${name}}`, String(replacement)),
        template,
      )
    }

    return { lang, setLang, t }
  }, [lang, setLang])

  return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>
}
