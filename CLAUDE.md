# Working in this repo

Screening tool for Finnish public procurement notices. See `README.md` for the architecture and the
reasoning behind it; this file covers the conventions that are easy to violate by accident.

## The load-bearing invariant

**Scores are computed in code; the model only narrates them.** `FitScorer` is a pure function of
*(notice, profile, clock)*. Anything that lets a model produce, adjust, or re-derive a number breaks
the property the whole project is built to demonstrate — so `IAssessmentNarrator` receives a finished
`ScoreBreakdown` and has no way to alter it. Keep it that way.

Where the model and the score disagree, **store both and flag it**. Never resolve it silently in
either direction.

## Stored records are never rewritten

Assessments and approval decisions record what was true when they were made. When a format changes,
make the *reader* tolerant rather than migrating the rows — see `parseBreakdown` in
`web/src/api/client.ts`, which accepts two casings for exactly this reason. Decisions are
append-only: a changed mind writes a new row.

Editing a company profile does **not** re-score existing assessments.

## Verifying changes

```bash
dotnet test                              # 72 tests; keep them green
cd web && npm test                       # 21 frontend tests (Vitest)
cd web && npm run build                  # frontend type check + production build
```

**Do not use `npx tsc --noEmit` as the frontend check.** `web/tsconfig.json` is a solution file
(`"files": []`, references only), so that command type-checks *nothing* and exits 0 no matter what is
broken. `npm run build` runs `tsc -b`, which honours the references and actually checks the code — it
is the only command that catches a type error here.

The frontend's API types are **generated**, not written: `cd web && npm run gen-types` runs
`openapi-typescript` against the running API's OpenAPI document. Don't hand-edit `src/api/schema.d.ts`.

For a DTO to reach that document the endpoint must declare it — `.Produces<T>()` on the route, or a
typed result. An endpoint returning bare `Results.Ok(x)` contributes **no response schema**, so the
generated types silently lose it and the frontend stops compiling on the next regeneration.

## Running the stack

```bash
COMPOSE_BAKE=false docker compose up -d --build
```

`COMPOSE_BAKE=false` avoids a buildx bake failure seen on this machine. The API is on
<http://localhost:5080>, the frontend dev server on <http://localhost:5173> (`npm run dev --prefix web`).

Postgres and Qdrant data live in named volumes — `docker compose down` keeps them, so a restart
resumes from the ingestion checkpoint instead of re-fetching and re-embedding. Use `down -v` only
when a clean slate is genuinely wanted; re-embedding costs money.

## Credentials

Three keys in `.env` at the repo root, gitignored: Hilma, Azure OpenAI (embeddings), Gemini
(narration). Read them through environment variables; don't print the values. `.env.example`
documents each one.

## The company profile is fictional

**Demo Firma Oy does not exist**, deliberately. The notices are public records, but screening them
against a real supplier's stated capabilities and publishing the resulting GO/NO-GO calls would put
words in someone else's mouth. Keep it invented.

## Finnish

The corpus, the queries, and the generated narratives are Finnish. When testing search or assessment
by hand, send UTF-8 properly — Git Bash mangles `ä` into Latin-1 and the API rejects the body.
