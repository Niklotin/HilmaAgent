import { useEffect, useState } from 'react'
import { api, type CompanyProfile } from '../api/client'
import { useT } from '../i18n'

/**
 * Edits the profile every score is computed against.
 *
 * This exists because the scoring inputs should not live in source code. Change the CPV codes here
 * and the next assessment scores differently — but existing assessments are deliberately left alone,
 * since they record what was true when they were made.
 */
export function ProfileEditor() {
  const [profile, setProfile] = useState<CompanyProfile | null>(null)
  const [status, setStatus] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const t = useT()

  useEffect(() => {
    api.profiles().then((profiles) => setProfile(profiles[0] ?? null)).catch((e) => setError(String(e)))
  }, [])

  if (error) return <p className="error">{error}</p>
  if (!profile) return <p className="muted">{t('profile.loading')}</p>

  function set<K extends keyof CompanyProfile>(key: K, value: CompanyProfile[K]) {
    setProfile((previous) => (previous ? { ...previous, [key]: value } : previous))
    setStatus(null)
  }

  const list = (value: string) =>
    value
      .split('\n')
      .map((line) => line.trim())
      .filter(Boolean)

  async function save() {
    if (!profile) return
    setStatus(t('profile.saving'))
    setError(null)
    try {
      await api.saveProfile(profile.id!, profile)
      setStatus(t('profile.saved'))
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setStatus(null)
    }
  }

  return (
    <section className="profile">
      <h2>{t('profile.heading')}</h2>
      <p className="muted">
{t('profile.intro')}
      </p>

      <label>
        {t('profile.name')}
        <input value={profile.name ?? ''} onChange={(e) => set('name', e.target.value)} />
      </label>

      <label>
        {t('profile.description')} <span className="muted">{t('profile.descriptionHint')}</span>
        <textarea rows={4} value={profile.description ?? ''} onChange={(e) => set('description', e.target.value)} />
      </label>

      <div className="grid-2">
        <label>
          {t('profile.cpv')} <span className="muted">{t('profile.cpvHint')}</span>
          <textarea
            rows={6}
            value={(profile.preferredCpvCodes ?? []).join('\n')}
            onChange={(e) => set('preferredCpvCodes', list(e.target.value))}
          />
        </label>

        <label>
          {t('profile.regions')} <span className="muted">{t('profile.regionsHint')}</span>
          <textarea
            rows={6}
            value={(profile.regions ?? []).join('\n')}
            onChange={(e) => set('regions', list(e.target.value))}
          />
        </label>
      </div>

      <div className="grid-2">
        <label>
          {t('profile.minValue')}
          <input
            type="number"
            value={profile.minContractValue ?? ''}
            onChange={(e) => set('minContractValue', e.target.value ? Number(e.target.value) : null)}
          />
        </label>
        <label>
          {t('profile.maxValue')}
          <input
            type="number"
            value={profile.maxContractValue ?? ''}
            onChange={(e) => set('maxContractValue', e.target.value ? Number(e.target.value) : null)}
          />
        </label>
      </div>

      <label>
        {t('profile.technologies')} <span className="muted">{t('profile.technologiesHint')}</span>
        <textarea
          rows={4}
          value={(profile.technologies ?? []).join('\n')}
          onChange={(e) => set('technologies', list(e.target.value))}
        />
      </label>

      <label>
        {t('profile.references')} <span className="muted">{t('profile.referencesHint')}</span>
        <textarea
          rows={6}
          value={(profile.referenceProjects ?? []).join('\n')}
          onChange={(e) => set('referenceProjects', list(e.target.value))}
        />
      </label>

      {error && <p className="error">{error}</p>}
      {status && <p className="muted">{status}</p>}

      <button className="approve" onClick={save}>
        {t('profile.save')}
      </button>
    </section>
  )
}
