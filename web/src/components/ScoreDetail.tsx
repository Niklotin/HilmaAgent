import { useT, type StringKey, type Translate } from '../i18n'

/**
 * One line of a score breakdown, rendered in the reader's language where possible.
 *
 * `FitScorer` writes its reasons in English and stores them inside the assessment, which is never
 * rewritten. So the sentence cannot be translated after the fact — instead the scorer also emits a
 * code and its arguments, and this renders those. Anything assessed before codes existed still has
 * only the sentence, and gets it verbatim rather than a blank.
 */
export function ScoreDetail({
  detail,
  code,
  args,
}: {
  detail: string
  code?: string | null
  args?: Record<string, string> | null
}) {
  const t = useT()
  return <>{translateDetail(t, detail, code, args)}</>
}

function translateDetail(
  t: Translate,
  detail: string,
  code?: string | null,
  args?: Record<string, string> | null,
): string {
  if (!code) return detail

  const key = `score.${code}` as StringKey
  const params: Record<string, string> = { ...(args ?? {}) }

  // A relation is itself a code — "exact", "contained" — so it needs translating before it is
  // substituted into the sentence, otherwise the Finnish reads with an English word inside it.
  if (params.relation) params.relation = t(`score.relation.${params.relation}` as StringKey)

  const translated = t(key, params)

  // A missing key renders as the key itself. Falling back to the stored English sentence is always
  // better than showing the reader `score.cpv.best_match`.
  return translated === key ? detail : translated
}
