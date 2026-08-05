import { useEffect, useState } from 'react'
import { api, type CompanyProfile } from '../api/client'

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

  useEffect(() => {
    api.profiles().then((profiles) => setProfile(profiles[0] ?? null)).catch((e) => setError(String(e)))
  }, [])

  if (error) return <p className="error">{error}</p>
  if (!profile) return <p className="muted">Loading profile…</p>

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
    setStatus('Saving…')
    setError(null)
    try {
      await api.saveProfile(profile.id!, profile)
      setStatus('Saved. New assessments will use these values; existing ones are unchanged.')
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e))
      setStatus(null)
    }
  }

  return (
    <section className="profile">
      <h2>Company profile</h2>
      <p className="muted">
        Everything notices are scored against. Fictional demo company — replace it with whatever you
        want to screen for.
      </p>

      <label>
        Name
        <input value={profile.name ?? ''} onChange={(e) => set('name', e.target.value)} />
      </label>

      <label>
        Description <span className="muted">(also used to rank which passages the model is shown)</span>
        <textarea rows={4} value={profile.description ?? ''} onChange={(e) => set('description', e.target.value)} />
      </label>

      <div className="grid-2">
        <label>
          CPV codes <span className="muted">(one per line; matched hierarchically)</span>
          <textarea
            rows={6}
            value={(profile.preferredCpvCodes ?? []).join('\n')}
            onChange={(e) => set('preferredCpvCodes', list(e.target.value))}
          />
        </label>

        <label>
          NUTS regions <span className="muted">(one per line; FI1B covers FI1B1)</span>
          <textarea
            rows={6}
            value={(profile.regions ?? []).join('\n')}
            onChange={(e) => set('regions', list(e.target.value))}
          />
        </label>
      </div>

      <div className="grid-2">
        <label>
          Minimum contract value (EUR)
          <input
            type="number"
            value={profile.minContractValue ?? ''}
            onChange={(e) => set('minContractValue', e.target.value ? Number(e.target.value) : null)}
          />
        </label>
        <label>
          Maximum contract value (EUR)
          <input
            type="number"
            value={profile.maxContractValue ?? ''}
            onChange={(e) => set('maxContractValue', e.target.value ? Number(e.target.value) : null)}
          />
        </label>
      </div>

      <label>
        Technologies <span className="muted">(one per line)</span>
        <textarea
          rows={4}
          value={(profile.technologies ?? []).join('\n')}
          onChange={(e) => set('technologies', list(e.target.value))}
        />
      </label>

      <label>
        Reference projects <span className="muted">(one per line)</span>
        <textarea
          rows={6}
          value={(profile.referenceProjects ?? []).join('\n')}
          onChange={(e) => set('referenceProjects', list(e.target.value))}
        />
      </label>

      {error && <p className="error">{error}</p>}
      {status && <p className="muted">{status}</p>}

      <button className="approve" onClick={save}>
        Save profile
      </button>
    </section>
  )
}
