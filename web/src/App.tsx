import { useCallback, useEffect, useState } from 'react'
import { api, type Metrics, type QueueItem } from './api/client'
import { AssessmentCard } from './components/AssessmentCard'
import { AssessRunner } from './components/AssessRunner'
import { ErrorBoundary } from './components/ErrorBoundary'
import { ProfileEditor } from './components/ProfileEditor'
import './App.css'

type Tab = 'queue' | 'assess' | 'profile'

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

  const refresh = useCallback(async () => {
    try {
      const [items, stats] = await Promise.all([api.queue(), api.metrics()])
      setQueue(items)
      setMetrics(stats)
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setLoading(false)
    }
  }, [])

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
        <nav>
          {(['queue', 'assess', 'profile'] as Tab[]).map((value) => (
            <button key={value} className={tab === value ? 'active' : ''} onClick={() => setTab(value)}>
              {value === 'queue' ? `Queue${queue.length ? ` (${queue.length})` : ''}` : value}
            </button>
          ))}
        </nav>
      </header>

      <MetricsBar metrics={metrics} />

      {error && <p className="error">{error}</p>}

      {tab === 'queue' && (
        <main>
          {loading && <p className="muted">Loading…</p>}
          {!loading && queue.length === 0 && (
            <p className="muted">
              Nothing awaiting review. Every assessment has a decision recorded against it.
            </p>
          )}
          {queue.map((item) => (
            <ErrorBoundary key={item.id} label={item.noticeTitle ?? item.noticeId}>
              <AssessmentCard item={item} onDecided={refresh} />
            </ErrorBoundary>
          ))}
        </main>
      )}

      {tab === 'assess' && <AssessTab onFinished={refresh} />}
      {tab === 'profile' && <ProfileEditor />}
    </div>
  )
}

/** Pick an ingested notice and watch it being assessed step by step. */
function AssessTab({ onFinished }: { onFinished: () => void }) {
  const [notices, setNotices] = useState<Awaited<ReturnType<typeof api.notices>>['items']>([])
  const [running, setRunning] = useState<string | null>(null)

  useEffect(() => {
    api.notices({ take: 30, openOnly: true }).then((r) => setNotices(r.items)).catch(() => setNotices([]))
  }, [])

  return (
    <main>
      {running && <AssessRunner noticeId={running} onFinished={onFinished} />}
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
    </main>
  )
}
