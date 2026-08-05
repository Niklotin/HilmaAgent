import type { components } from './schema'

/**
 * Types come from `npm run gen-types`, which reads the API's own OpenAPI document.
 * Nothing in here is hand-written: if an endpoint's shape changes, this file stops compiling,
 * which is the point.
 */
export type CompanyProfile = components['schemas']['CompanyProfile']
export type FitAssessment = components['schemas']['FitAssessment']
export type ApprovalDecision = components['schemas']['ApprovalDecision']
export type Citation = components['schemas']['Citation']

/** The queue projection is an anonymous shape server-side, so it is described once here. */
export interface QueueItem {
  id: string
  noticeId: string
  noticeTitle: string | null
  buyerName: string | null
  submissionDeadline: string | null
  estimatedValue: number | null
  currency: string | null
  deterministicScore: number
  scoreRecommendation: string
  modelRecommendation: string
  recommendationDisagreement: boolean
  reasoning: string
  citations: Citation[]
  modelId: string | null
  createdAt: string
}

/**
 * A decided assessment. Same projection as the queue, plus the latest human verdict — also an
 * anonymous shape server-side, so it is described here rather than generated.
 */
export interface DecidedItem extends QueueItem {
  decision: string
  editedRecommendation: string | null
  reviewerNote: string | null
  reviewedBy: string | null
  decidedAt: string
  /** What the reviewer stood behind: their edit if they made one, else the model's recommendation. */
  effectiveRecommendation: string
  /** How many decisions exist against this assessment. More than one means somebody changed their mind. */
  revisionCount: number
}

/**
 * A notice the reviewer backed and can still act on.
 *
 * The queue answers "what should I look at", the decided tab answers "what did I conclude". This
 * answers the question that actually matters afterwards: what am I bidding on, and by when.
 */
export interface ShortlistItem {
  assessmentId: string
  noticeId: string
  noticeTitle: string | null
  buyerName: string | null
  submissionDeadline: string | null
  estimatedValue: number | null
  currency: string | null
  deterministicScore: number
  /** Where bids are actually submitted. Null for legacy notices, which carry no such field. */
  procurementDocumentsUrl: string | null
  /** What the reviewer stood behind: GO or INVESTIGATE. NO_GO never reaches the shortlist. */
  standing: string
  reviewedBy: string | null
  decidedAt: string
  reviewerNote: string | null
  daysLeft: number | null
}

/**
 * What the UI is allowed to know about a provider's credentials.
 *
 * There is deliberately no field for the key itself: the API cannot return one. `keyHint` is the
 * last four characters, which is enough to tell two keys apart and useless to anyone who sees it.
 */
export interface ProviderStatus {
  provider: string
  hasKey: boolean
  keyHint: string | null
  baseUrl: string | null
  model: string | null
  /** A key is stored but could not be decrypted — the data-protection key ring changed. */
  keyUnreadable: boolean
  /** The key came from the environment rather than the database. */
  configuredFromEnvironment: boolean
  updatedAt: string | null
  updatedBy: string | null
}

export interface NarratorOption {
  provider: string
  model: string
  /** False when the provider could not actually run — no key, or no endpoint. */
  ready: boolean
  reason: string | null
}

export interface ProvidersResponse {
  active: string
  options: NarratorOption[]
  providers: ProviderStatus[]
}

export interface SearchChunk {
  chunkId: string
  section: string
  lotId: string | null
  score: number
  content: string
}

export interface SearchResult {
  noticeId: string
  title: string | null
  buyerName: string | null
  cpvCodes: string[]
  estimatedValue: number | null
  currency: string | null
  submissionDeadline: string | null
  score: number
  chunks: SearchChunk[]
}

export interface Metrics {
  assessments: number
  reviewed: number
  pending: number
  overrides?: number
  overrideRate?: number | null
  approvalRate?: number | null
  byDecision?: Record<string, number>
  modelScoreDisagreements?: number
  modelScoreDisagreementRate?: number
  /** Undecided and closing within a week — these need a decision today. */
  undecidedClosingSoon?: number
  /** Backed, still open, closing within a week — these need a bid today. */
  backedClosingSoon?: number
  note?: string
}

/**
 * A rule's contribution.
 *
 * `detail` is the English sentence the scorer wrote and stored. `detailCode` and `detailArgs` are
 * the same fact in a form the UI can render in either language — absent on assessments written
 * before codes existed, which is why `detail` remains the fallback rather than being replaced.
 */
export interface ScoreRule {
  rule: string
  awarded: number
  max: number
  detail: string
  detailCode?: string | null
  detailArgs?: Record<string, string> | null
}

export interface ScoreGate {
  gate: string
  detail: string
  detailCode?: string | null
  detailArgs?: Record<string, string> | null
}

/** Older records store a warning as a bare string; newer ones as an object with a code. */
export interface ScoreNote {
  text: string
  code?: string | null
  args?: Record<string, string> | null
}

export interface ScoreBreakdown {
  total: number
  rules: ScoreRule[]
  gates: ScoreGate[]
  warnings: ScoreNote[]
}

/** Vite proxies /api to the backend in dev; in the container the app is served from the same origin. */
const BASE = '/api'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BASE}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })

  if (!response.ok) {
    const body = await response.text()
    throw new Error(body || `${response.status} ${response.statusText}`)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export type QueueSort = 'disagreement' | 'score' | 'deadline'

export interface QueueOptions {
  sort?: QueueSort
  disagreementsOnly?: boolean
  minScore?: number
}

export const api = {
  queue: ({ sort = 'disagreement', disagreementsOnly = false, minScore }: QueueOptions = {}) => {
    const params = new URLSearchParams({ take: '50', sort })
    if (disagreementsOnly) params.set('disagreementsOnly', 'true')
    if (minScore != null && minScore > 0) params.set('minScore', String(minScore))
    return request<QueueItem[]>(`/queue?${params}`)
  },

  decided: () => request<DecidedItem[]>('/decided?take=50'),

  shortlist: () => request<ShortlistItem[]>('/shortlist?take=50'),

  providers: () => request<ProvidersResponse>('/providers'),

  /** Omit `apiKey` to leave the stored one alone; send `''` to remove it. */
  saveProvider: (
    provider: string,
    body: { apiKey?: string; baseUrl?: string; model?: string; updatedBy?: string },
  ) =>
    request<{ provider: string; keyClearedByEndpointChange: boolean }>(`/providers/${provider}`, {
      method: 'PUT',
      body: JSON.stringify(body),
    }),

  forgetProvider: (provider: string) =>
    request<void>(`/providers/${provider}`, { method: 'DELETE' }),

  setNarratorProvider: (provider: string) =>
    request<{ active: string; model: string }>('/providers/active', {
      method: 'PUT',
      body: JSON.stringify({ provider }),
    }),

  assessment: (id: string) => request<FitAssessment>(`/assessments/${id}`),

  search: (body: {
    query: string
    limit?: number
    cpvPrefixes?: string[]
    nutsPrefixes?: string[]
    openOnly?: boolean
  }) =>
    request<{ query: string; count: number; results: SearchResult[] }>('/search', {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  decisions: (assessmentId: string) =>
    request<ApprovalDecision[]>(`/assessments/${assessmentId}/decisions`),

  decide: (
    assessmentId: string,
    body: { decision: string; editedRecommendation?: string; reviewerNote?: string; reviewedBy?: string },
  ) =>
    request<ApprovalDecision>(`/assessments/${assessmentId}/decision`, {
      method: 'POST',
      body: JSON.stringify(body),
    }),

  metrics: () => request<Metrics>('/metrics'),

  profiles: () => request<CompanyProfile[]>('/profiles'),

  saveProfile: (id: string, profile: Partial<CompanyProfile>) =>
    request<CompanyProfile>(`/profiles/${id}`, { method: 'PUT', body: JSON.stringify(profile) }),

  notices: (params: { take?: number; openOnly?: boolean } = {}) =>
    request<{ total: number; items: Array<{ id: string; title: string | null; buyerName: string | null; cpvCodes: string[]; submissionDeadline: string | null }> }>(
      `/notices?take=${params.take ?? 25}&openOnly=${params.openOnly ?? false}`,
    ),
}

/**
 * Parsed `ScoreBreakdownJson`, which the API stores as an opaque string.
 *
 * Accepts both camelCase and PascalCase keys. Assessments written before the serializer was fixed
 * stored PascalCase, and they are deliberately never rewritten — an assessment records what was true
 * when it was made, so the reader adapts rather than the history being edited.
 */
export function parseBreakdown(json: string | null | undefined): ScoreBreakdown | null {
  if (!json) return null

  try {
    const raw = JSON.parse(json) as Record<string, unknown>
    const pick = <T>(lower: string, upper: string, fallback: T): T =>
      (raw[lower] ?? raw[upper] ?? fallback) as T

    const args = (source: Record<string, unknown>, lower: string, upper: string) =>
      (source[lower] ?? source[upper] ?? null) as Record<string, string> | null

    return {
      total: pick('total', 'Total', 0),
      rules: pick<ScoreRule[]>('rules', 'Rules', []).map((rule) => {
        const r = rule as unknown as Record<string, unknown>
        return {
          rule: (r.rule ?? r.Rule ?? '') as string,
          awarded: (r.awarded ?? r.Awarded ?? 0) as number,
          max: (r.max ?? r.Max ?? 0) as number,
          detail: (r.detail ?? r.Detail ?? '') as string,
          detailCode: (r.detailCode ?? r.DetailCode ?? null) as string | null,
          detailArgs: args(r, 'detailArgs', 'DetailArgs'),
        }
      }),
      gates: pick<ScoreGate[]>('gates', 'Gates', []).map((gate) => {
        const g = gate as unknown as Record<string, unknown>
        return {
          gate: (g.gate ?? g.Gate ?? '') as string,
          detail: (g.detail ?? g.Detail ?? '') as string,
          detailCode: (g.detailCode ?? g.DetailCode ?? null) as string | null,
          detailArgs: args(g, 'detailArgs', 'DetailArgs'),
        }
      }),
      // Warnings used to be bare strings and are now objects. Both shapes are in the database and
      // neither will be rewritten, so the reader normalises rather than the history being migrated.
      warnings: pick<unknown[]>('warnings', 'Warnings', []).map((warning) => {
        if (typeof warning === 'string') return { text: warning, code: null, args: null }

        const w = warning as Record<string, unknown>
        return {
          text: (w.text ?? w.Text ?? '') as string,
          code: (w.code ?? w.Code ?? null) as string | null,
          args: args(w, 'args', 'Args'),
        }
      }),
    }
  } catch {
    return null
  }
}
