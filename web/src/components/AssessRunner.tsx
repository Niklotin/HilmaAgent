import { useEffect, useRef, useState } from 'react'
import { useT } from '../i18n'

interface ProgressEvent {
  step: string
  detail: string
  data?: unknown
}

/**
 * Runs an assessment over SSE and shows each step as it happens.
 *
 * The point of streaming here is not a progress bar. The order of the events *is* the architecture:
 * the deterministic score arrives, visibly, before the model is ever called — so a reviewer can see
 * that the number was not produced by the thing writing the prose.
 */
export function AssessRunner({ noticeId, onFinished }: { noticeId: string; onFinished: () => void }) {
  const [events, setEvents] = useState<ProgressEvent[]>([])
  const [error, setError] = useState<string | null>(null)
  const [lost, setLost] = useState(false)
  const [done, setDone] = useState(false)
  const sourceRef = useRef<EventSource | null>(null)
  const t = useT()

  useEffect(() => {
    const source = new EventSource(`/api/assess/${encodeURIComponent(noticeId)}/stream`)
    sourceRef.current = source

    source.addEventListener('progress', (e) => {
      setEvents((previous) => [...previous, JSON.parse((e as MessageEvent).data) as ProgressEvent])
    })

    source.addEventListener('assessment', () => {
      setDone(true)
      source.close()
      onFinished()
    })

    source.addEventListener('error', (e) => {
      const data = (e as MessageEvent).data
      // Translated at render rather than here: pulling `t` into the effect would tear down and
      // reopen the stream every time the interface language changed.
      if (data) setError((JSON.parse(data) as { error: string }).error)
      else setLost(true)
      source.close()
    })

    return () => source.close()
  }, [noticeId, onFinished])

  return (
    <div className="runner">
      <h3>
        {t('runner.assessing')} <code>{noticeId}</code>
      </h3>
      <ol className="steps">
        {events.map((event, index) => (
          <li key={index} className={`step step-${event.step}`}>
            <strong>{event.step}</strong> {event.detail}
          </li>
        ))}
      </ol>
      {error && <p className="error">{error}</p>}
      {lost && <p className="error">{t('runner.lost')}</p>}
      {done && <p className="muted">{t('runner.done')}</p>}
    </div>
  )
}
