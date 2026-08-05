import { useCallback, useEffect, useState } from 'react'
import { api, type ProvidersResponse, type ProviderStatus } from '../api/client'
import { useT, type StringKey, type Translate } from '../i18n'

const PROVIDER_LABELS: Record<string, StringKey> = {
  gemini: 'models.providerGemini',
  'openai-compatible': 'models.providerOpenAi',
}

/**
 * Which model narrates, and the credentials it needs.
 *
 * The key fields are write-only by construction: the API cannot return a stored key, so this screen
 * shows a four-character hint and an empty input. Leaving that input blank keeps whatever is stored,
 * which is what makes it possible to edit an endpoint or a model name without retyping a secret you
 * are not allowed to read.
 */
export function ModelSettings({ reviewer }: { reviewer: string }) {
  const t = useT()
  const [data, setData] = useState<ProvidersResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    try {
      setData(await api.providers())
      setError(null)
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function choose(provider: string) {
    try {
      await api.setNarratorProvider(provider)
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    }
  }

  if (error) return <p className="error">{error}</p>
  if (!data) return <p className="muted">{t('common.loading')}</p>

  return (
    <main className="profile">
      <h2>{t('models.heading')}</h2>
      <p className="muted">{t('models.intro')}</p>

      <h3>{t('models.activeHeading')}</h3>
      <p className="muted">{t('models.activeHint')}</p>

      <div className="filters">
        {data.options.map((option) => (
          <label key={option.provider} className="checkbox">
            <input
              type="radio"
              name="narrator"
              value={option.provider}
              checked={data.active === option.provider}
              disabled={!option.ready}
              onChange={() => choose(option.provider)}
            />
            {t(PROVIDER_LABELS[option.provider] ?? 'models.providerGemini')} · <code>{option.model}</code>
            {!option.ready && (
              <span className="flag">
                {' '}
                {t('models.notReady')}: {t(`models.reason.${option.reason}` as StringKey)}
              </span>
            )}
          </label>
        ))}
      </div>

      {data.providers.map((provider) => (
        <ProviderForm key={provider.provider} status={provider} reviewer={reviewer} onSaved={load} t={t} />
      ))}

      <p className="muted">{t('models.embeddingsNote')}</p>
      {/* Said plainly rather than buried: storing secrets in an app with no sign-in is a real
          trade-off, and the person configuring it is the one who should know. */}
      <p className="error">{t('models.security')}</p>
    </main>
  )
}

function ProviderForm({
  status,
  reviewer,
  onSaved,
  t,
}: {
  status: ProviderStatus
  reviewer: string
  onSaved: () => Promise<void>
  t: Translate
}) {
  const [apiKey, setApiKey] = useState('')
  const [baseUrl, setBaseUrl] = useState(status.baseUrl ?? '')
  const [model, setModel] = useState(status.model ?? '')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const isLocal = status.provider === 'openai-compatible'
  const endpointMoved = isLocal && status.hasKey && (status.baseUrl ?? '') !== baseUrl.trim()

  async function save() {
    setBusy(true)
    setError(null)
    setMessage(null)
    try {
      const result = await api.saveProvider(status.provider, {
        // Omitted rather than empty: an empty string would *delete* the stored key.
        ...(apiKey.trim() ? { apiKey: apiKey.trim() } : {}),
        baseUrl: baseUrl.trim() || undefined,
        model: model.trim() || undefined,
        updatedBy: reviewer || undefined,
      })
      setApiKey('')
      setMessage(result.keyClearedByEndpointChange ? t('models.savedKeyCleared') : t('models.saved'))
      await onSaved()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  async function forget() {
    setBusy(true)
    try {
      await api.forgetProvider(status.provider)
      setApiKey('')
      setBaseUrl('')
      setModel('')
      setMessage(null)
      await onSaved()
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="provider">
      <h3>{t(PROVIDER_LABELS[status.provider] ?? 'models.providerGemini')}</h3>
      {isLocal && <p className="muted">{t('models.providerOpenAiHint')}</p>}

      {status.keyUnreadable && <p className="error">{t('models.apiKeyUnreadable')}</p>}

      <label>
        {t('models.apiKey')}{' '}
        <span className="muted">
          {status.hasKey
            ? `— ${t('models.apiKeyStored')} ····${status.keyHint ?? ''}${
                status.configuredFromEnvironment ? ` (${t('models.apiKeyFromEnv')})` : ''
              }`
            : `— ${t('models.apiKeyNone')}`}
        </span>
        <input
          type="password"
          autoComplete="off"
          value={apiKey}
          placeholder={t('models.apiKeyPlaceholder')}
          onChange={(e) => setApiKey(e.target.value)}
        />
      </label>

      {isLocal && (
        <>
          <label>
            {t('models.baseUrl')}
            <input
              value={baseUrl}
              placeholder={t('models.baseUrlPlaceholder')}
              onChange={(e) => setBaseUrl(e.target.value)}
            />
          </label>
          {endpointMoved && <p className="flag">{t('models.baseUrlWarning')}</p>}
        </>
      )}

      <label>
        {t('models.model')}
        <input value={model} placeholder={t('models.modelPlaceholder')} onChange={(e) => setModel(e.target.value)} />
      </label>

      {error && <p className="error">{error}</p>}
      {message && <p className="muted">{message}</p>}
      {status.updatedBy && (
        <p className="muted">
          {t('models.updatedBy')} <strong>{status.updatedBy}</strong>
        </p>
      )}

      <div className="actions">
        <button className="approve" disabled={busy} onClick={save}>
          {busy ? t('models.saving') : t('models.save')}
        </button>
        <button className="reject" disabled={busy} onClick={forget}>
          {t('models.forget')}
        </button>
      </div>
    </section>
  )
}
