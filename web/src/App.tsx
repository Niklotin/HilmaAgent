import { useCallback, useEffect, useState } from 'react'
import { api, type Metrics, type QueueItem, type QueueSort, type SearchResult } from './api/client'
import { AssessmentCard } from './components/AssessmentCard'
import { AssessRunner } from './components/AssessRunner'
import { DecidedList } from './components/DecidedList'
import { ErrorBoundary } from './components/ErrorBoundary'
import { ProfileEditor } from './components/ProfileEditor'
import { useReviewer } from './reviewer'
import './App.css'

type Tab = 'queue' | 'assess' | 'decided' | 'profile'

function MetricsBar({ metrics }: { metrics: Metrics | null }) {
  if (!metrics) return null
  if (!metrics.assessments) return <p className="muted">No assessments yet — run one from the Assess tab.</p>

  const percent = (value: number | null | undefined) =>
    value == null ? '—' : `${Math.round(value * 100)}%`

  return (
    <div className="metrics">
      <span>
        <strong>{metrics.assessments}</strong> assessed
      </span>
      <span>
        <strong>{metrics.pending}</strong> awaiting review
      </span>
      <span title="How often a human changed the agent's answer. The headline quality number.">
        override rate <strong>{percent(metrics.overrideRate)}</strong>
      </span>
      <span title="How often the model and the deterministic score reached different conclusions.">
        model vs score <strong>{percent(metrics.modelScoreDisagreementRate)}</strong>
      </span>
    </div>
  )
}

export default function App() {
  const [tab, setTab] = useState<Tab>('queue')
  const [queue, setQueue] = useState<QueueItem[]>([])
  const [metrics, setMetrics] = useState<Metrics | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const { reviewer, setReviewer } = useReviewer()

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
          <h1>Hilma screening</h1>
          <p className="muted">
            Scores computed in code. The model writes the justification and may disagree — it never
            overrules.
          </p>
        </div>
        <div className="topbar-right">
          {/* Every decision records an author. No auth yet, so the name is kept locally — but an
              unattributed audit trail is not an audit trail, so it is asked for rather than faked. */}
          <label className="reviewer">
            Reviewer
            <input
              value={reviewer}
              placeholder="your name"
              onChange={(e) => setReviewer(e.target.value)}
              aria-label="Your name, recorded against every decision"
            />
          </label>
          <nav role="tablist" aria-label="Views">
            {(['queue', 'assess', 'decided', 'profile'] as Tab[]).map((value) => (
              <button
                key={value}
                role="tab"
                aria-selected={tab === value}
                className={tab === value ? 'active' : ''}
                onClick={() => setTab(value)}
              >
                {value === 'queue' ? `Queue${queue.length ? ` (${queue.length})` : ''}` : value}
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
              Sort by
              <select value={sort} onChange={(e) => setSort(e.target.value as QueueSort)}>
                <option value="disagreement">disagreement, then score</option>
                <option value="score">score</option>
                <option value="deadline">deadline — closing soonest</option>
              </select>
            </label>
            <label className="checkbox">
              <input
                type="checkbox"
                checked={disagreementsOnly}
                onChange={(e) => setDisagreementsOnly(e.target.checked)}
              />
              disagreements only
            </label>
            <label>
              Min score
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

          {loading && <p className="muted">Loading…</p>}
          {!loading && queue.length === 0 && (
            <p className="muted">
              {disagreementsOnly || minScore > 0
                ? 'Nothing matches these filters. Widen them to see the rest of the queue.'
                : 'Nothing awaiting review. Every assessment has a decision recorded against it.'}
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
      {tab === 'decided' && <DecidedList reviewer={reviewer} onChanged={refresh} />}
      {tab === 'profile' && <ProfileEditor />}
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
          placeholder="Search notices by meaning — in Finnish, e.g. ohjelmistokehitys ja integraatiot"
          aria-label="Semantic search query"
        />
        <label className="checkbox">
          <input type="checkbox" checked={openOnly} onChange={(e) => setOpenOnly(e.target.checked)} />
          open only
        </label>
        <button type="submit" disabled={searching || !query.trim()}>
          {searching ? 'Searching…' : 'Search'}
        </button>
        {results && (
          <button type="button" className="link" onClick={clear}>
            clear
          </button>
        )}
      </form>

      {searchError && <p className="error">{searchError}</p>}

      {results ? (
        <>
          <p className="muted">
            {results.length} notice{results.length === 1 ? '' : 's'} by semantic similarity. Filters run
            inside the vector search, so the count is not shrunk after the fact.
          </p>
          {results.length === 0 && (
            <p className="muted">
              Nothing matched. If the corpus was just ingested it may not be embedded yet — POST
              /api/search/index.
            </p>
          )}
          {results.map((result) => (
            <article className="result" key={result.noticeId}>
              <header>
                <div>
                  <h3>{result.title ?? result.noticeId}</h3>
                  <p className="muted">
                    {result.buyerName ?? 'Buyer not stated'} · <code>{result.noticeId}</code> ·{' '}
                    {result.cpvCodes.slice(0, 4).join(', ')}
                    {result.submissionDeadline && ` · closes ${result.submissionDeadline.slice(0, 10)}`}
                  </p>
                </div>
                <div className="result-actions">
                  <span className="similarity" title="Best-matching chunk's similarity. Comparable within this result set only.">
                    {result.score.toFixed(3)}
                  </span>
                  <button onClick={() => setRunning(result.noticeId)} disabled={running === result.noticeId}>
                    Assess
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
                <th>Notice</th>
                <th>Buyer</th>
                <th>CPV</th>
                <th>Deadline</th>
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
                      Assess
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {notices.length === 0 && <p className="muted">No open notices ingested yet.</p>}
        </>
      )}
    </main>
  )
}
