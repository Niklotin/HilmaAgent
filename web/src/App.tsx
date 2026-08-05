import { useCallback, useEffect, useState } from 'react'
import { api, type Metrics, type QueueItem, type QueueSort, type SearchResult } from './api/client'
import { AssessmentCard } from './components/AssessmentCard'
import { AssessRunner } from './components/AssessRunner'
import { DecidedList } from './components/DecidedList'
import { ErrorBoundary } from './components/ErrorBoundary'
import { ModelSettings } from './components/ModelSettings'
import { ProfileEditor } from './components/ProfileEditor'
import { Shortlist } from './components/Shortlist'
import { useLanguage, useT, type Lang, type StringKey } from './i18n'
import { useReviewer } from './reviewer'
import './App.css'

type Tab = 'queue' | 'assess' | 'shortlist' | 'decided' | 'profile' | 'models'

const TAB_KEYS: Record<Tab, StringKey> = {
  queue: 'nav.queue',
  assess: 'nav.assess',
  shortlist: 'nav.shortlist',
  decided: 'nav.decided',
  profile: 'nav.profile',
  models: 'nav.models',
}

function MetricsBar({ metrics }: { metrics: Metrics | null }) {
  const t = useT()

  if (!metrics) return null
  if (!metrics.assessments) return <p className="muted">{t('metrics.none')}</p>

  const percent = (value: number | null | undefined) =>
    value == null ? '—' : `${Math.round(value * 100)}%`

  return (
    <div className="metrics">
      <span>
        <strong>{metrics.assessments}</strong> {t('metrics.assessed')}
      </span>
      <span>
        <strong>{metrics.pending}</strong> {t('metrics.pending')}
      </span>
      <span title={t('metrics.overrideHint')}>
        {t('metrics.overrideRate')} <strong>{percent(metrics.overrideRate)}</strong>
      </span>
      <span title={t('metrics.disagreementHint')}>
        {t('metrics.modelVsScore')} <strong>{percent(metrics.modelScoreDisagreementRate)}</strong>
      </span>

      {/* Escalation. Two separate problems: one needs a decision, the other needs a bid — saying
          only "5 urgent" would leave the reviewer guessing which. Hidden entirely at zero, so the
          bar stays quiet when nothing is burning. */}
      {!!metrics.undecidedClosingSoon && (
        <span className="escalation" title={t('metrics.closingSoonHint')}>
          ⏳ {t('metrics.closingSoon', { n: metrics.undecidedClosingSoon })}
        </span>
      )}
      {!!metrics.backedClosingSoon && (
        <span className="escalation" title={t('metrics.bidSoonHint')}>
          ✎ {t('metrics.bidSoon', { n: metrics.backedClosingSoon })}
        </span>
      )}
    </div>
  )
}

/** Finnish by default; English kept because the project is read by people who do not read Finnish. */
function LanguagePicker() {
  const { lang, setLang, t } = useLanguage()

  return (
    <label className="reviewer">
      {t('lang.label')}
      <select value={lang} onChange={(e) => setLang(e.target.value as Lang)} aria-label={t('lang.label')}>
        <option value="fi">Suomi</option>
        <option value="en">English</option>
      </select>
    </label>
  )
}

export default function App() {
  const [tab, setTab] = useState<Tab>('queue')
  const [queue, setQueue] = useState<QueueItem[]>([])
  const [metrics, setMetrics] = useState<Metrics | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const { reviewer, setReviewer } = useReviewer()
  const t = useT()

  const [sort, setSort] = useState<QueueSort>('disagreement')
  const [disagreementsOnly, setDisagreementsOnly] = useState(false)
  const [minScore, setMinScore] = useState(0)

  const refresh = useCallback(async () => {
    try {
      const [items, stats] = await Promise.all([
        api.queue({ sort, disagreementsOnly, minScore }),
        api.metrics(),
      ])
      setQueue(items)
      setMetrics(stats)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [sort, disagreementsOnly, minScore])

  useEffect(() => {
    void refresh()
  }, [refresh])

  return (
    <div className="app">
      <header className="topbar">
        <div>
          <h1>{t('app.title')}</h1>
          <p className="muted">{t('app.tagline')}</p>
        </div>
        <div className="topbar-right">
          {/* Every decision records an author. No auth yet, so the name is kept locally — but an
              unattributed audit trail is not an audit trail, so it is asked for rather than faked. */}
          <label className="reviewer">
            {t('reviewer.label')}
            <input
              value={reviewer}
              placeholder={t('reviewer.placeholder')}
              onChange={(e) => setReviewer(e.target.value)}
              aria-label={t('reviewer.aria')}
            />
          </label>
          <LanguagePicker />
          <nav role="tablist" aria-label={t('nav.label')}>
            {(['queue', 'assess', 'shortlist', 'decided', 'profile', 'models'] as Tab[]).map((value) => (
              <button
                key={value}
                role="tab"
                aria-selected={tab === value}
                className={tab === value ? 'active' : ''}
                onClick={() => setTab(value)}
              >
                {value === 'queue'
                  ? `${t('nav.queue')}${queue.length ? ` (${queue.length})` : ''}`
                  : t(TAB_KEYS[value])}
              </button>
            ))}
          </nav>
        </div>
      </header>

      <MetricsBar metrics={metrics} />

      {error && <p className="error">{error}</p>}

      {tab === 'queue' && (
        <main>
          <div className="filters">
            <label>
              {t('filters.sortBy')}
              <select value={sort} onChange={(e) => setSort(e.target.value as QueueSort)}>
                <option value="disagreement">{t('filters.sortDisagreement')}</option>
                <option value="score">{t('filters.sortScore')}</option>
                <option value="deadline">{t('filters.sortDeadline')}</option>
              </select>
            </label>
            <label className="checkbox">
              <input
                type="checkbox"
                checked={disagreementsOnly}
                onChange={(e) => setDisagreementsOnly(e.target.checked)}
              />
              {t('filters.disagreementsOnly')}
            </label>
            <label>
              {t('filters.minScore')}
              <input
                type="number"
                min={0}
                max={100}
                step={5}
                value={minScore}
                onChange={(e) => setMinScore(Number(e.target.value) || 0)}
              />
            </label>
          </div>

          {loading && <p className="muted">{t('common.loading')}</p>}
          {!loading && queue.length === 0 && (
            <p className="muted">
              {disagreementsOnly || minScore > 0 ? t('queue.emptyFiltered') : t('queue.empty')}
            </p>
          )}
          {queue.map((item) => (
            <ErrorBoundary key={item.id} label={item.noticeTitle ?? item.noticeId}>
              <AssessmentCard item={item} reviewer={reviewer} onDecided={refresh} />
            </ErrorBoundary>
          ))}
        </main>
      )}

      {tab === 'assess' && <AssessTab onFinished={refresh} />}
      {tab === 'shortlist' && <Shortlist />}
      {tab === 'decided' && <DecidedList reviewer={reviewer} onChanged={refresh} />}
      {tab === 'profile' && <ProfileEditor />}
      {tab === 'models' && <ModelSettings reviewer={reviewer} />}
    </div>
  )
}

/** Pick an ingested notice and watch it being assessed step by step. */
function AssessTab({ onFinished }: { onFinished: () => void }) {
  const [notices, setNotices] = useState<Awaited<ReturnType<typeof api.notices>>['items']>([])
  const [running, setRunning] = useState<string | null>(null)

  // Semantic search, which until now had no UI at all despite being the whole of Phase 2. Browsing
  // the newest 30 notices only works while the corpus is small; searching is how you find one.
  const [query, setQuery] = useState('')
  const [openOnly, setOpenOnly] = useState(true)
  const [results, setResults] = useState<SearchResult[] | null>(null)
  const [searching, setSearching] = useState(false)
  const [searchError, setSearchError] = useState<string | null>(null)
  const t = useT()

  useEffect(() => {
    api.notices({ take: 30, openOnly: true }).then((r) => setNotices(r.items)).catch(() => setNotices([]))
  }, [])

  async function search(e: React.FormEvent) {
    e.preventDefault()
    if (!query.trim()) return setResults(null)

    setSearching(true)
    setSearchError(null)
    try {
      const response = await api.search({ query: query.trim(), limit: 15, openOnly })
      setResults(response.results)
    } catch (err) {
      setSearchError(err instanceof Error ? err.message : String(err))
      setResults(null)
    } finally {
      setSearching(false)
    }
  }

  function clear() {
    setQuery('')
    setResults(null)
    setSearchError(null)
  }

  return (
    <main>
      {running && <AssessRunner noticeId={running} onFinished={onFinished} />}

      <form className="search" onSubmit={search}>
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={t('assess.searchPlaceholder')}
          aria-label={t('assess.searchAria')}
        />
        <label className="checkbox">
          <input type="checkbox" checked={openOnly} onChange={(e) => setOpenOnly(e.target.checked)} />
          {t('assess.openOnly')}
        </label>
        <button type="submit" disabled={searching || !query.trim()}>
          {searching ? t('assess.searching') : t('assess.search')}
        </button>
        {results && (
          <button type="button" className="link" onClick={clear}>
            {t('assess.clear')}
          </button>
        )}
      </form>

      {searchError && <p className="error">{searchError}</p>}

      {results ? (
        <>
          <p className="muted">{t('assess.resultCount', { n: results.length })}</p>
          {results.length === 0 && <p className="muted">{t('assess.noResults')}</p>}
          {results.map((result) => (
            <article className="result" key={result.noticeId}>
              <header>
                <div>
                  <h3>{result.title ?? result.noticeId}</h3>
                  <p className="muted">
                    {result.buyerName ?? t('card.noBuyer')} · <code>{result.noticeId}</code> ·{' '}
                    {result.cpvCodes.slice(0, 4).join(', ')}
                    {result.submissionDeadline &&
                      ` · ${t('assess.closes')} ${result.submissionDeadline.slice(0, 10)}`}
                  </p>
                </div>
                <div className="result-actions">
                  <span className="similarity" title={t('assess.similarityHint')}>
                    {result.score.toFixed(3)}
                  </span>
                  <button onClick={() => setRunning(result.noticeId)} disabled={running === result.noticeId}>
                    {t('assess.run')}
                  </button>
                </div>
              </header>
              {result.chunks[0] && (
                <blockquote className="passage">
                  <span className="muted">[{result.chunks[0].section}]</span> {result.chunks[0].content.slice(0, 320)}
                  {result.chunks[0].content.length > 320 && '…'}
                </blockquote>
              )}
            </article>
          ))}
        </>
      ) : (
        <>
          <table className="notices">
            <thead>
              <tr>
                <th>{t('assess.notice')}</th>
                <th>{t('assess.buyer')}</th>
                <th>{t('assess.cpv')}</th>
                <th>{t('assess.deadline')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {notices.map((notice) => (
                <tr key={notice.id}>
                  <td>{notice.title ?? notice.id}</td>
                  <td className="muted">{notice.buyerName ?? '—'}</td>
                  <td className="muted">{notice.cpvCodes.slice(0, 3).join(', ')}</td>
                  <td className="muted">{notice.submissionDeadline?.slice(0, 10) ?? '—'}</td>
                  <td>
                    <button onClick={() => setRunning(notice.id)} disabled={running === notice.id}>
                      {t('assess.run')}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {notices.length === 0 && <p className="muted">{t('assess.empty')}</p>}
        </>
      )}
    </main>
  )
}
