# Hilma Tender Screening Agent

[![CI](https://github.com/Niklotin/HilmaAgent/actions/workflows/ci.yml/badge.svg)](https://github.com/Niklotin/HilmaAgent/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Finland publishes about **60 public procurement notices a day**. Almost all are irrelevant to any
given supplier, a handful are worth an afternoon, and missing one of the handful means losing work
you would have won.

This tool does the triage. It ingests notices from [Hilma](https://www.hankintailmoitukset.fi),
indexes them for semantic search, scores each against a company profile, and has a language model
write the justification — then puts every proposal in front of a human before anything counts as
decided.

**The scores are computed in code. The model only narrates them.**

![A queue card scoring 97/100 with an exact CPV match, where the model nevertheless returns
INVESTIGATE, shown beside the rule-by-rule breakdown](docs/images/queue-disagreement.png)

That card is the whole idea. The rules give it **97/100** — an exact CPV match, right region, right
value, comfortable deadline — and the model still says INVESTIGATE, because the buyer wants
penetration testing and health-sector compliance work, which no CPV code distinguishes from custom
development. Neither answer is discarded. The card says *you decide*, and every point can be argued
with line by line.

---

## What it does

### Screens notices against a profile, deterministically

CPV overlap (45 points), region (20), contract value (20), deadline headroom (15) — matched
hierarchically, so `72000000` contains `72200000` and a notice in `FI1B1` sits inside a profile
declaring `FI1B`. Gates for a passed deadline, a cancelled notice, or a notice type that announces a
result rather than opening a tender force NO-GO and score **zero**, because a high number beside an
unbiddable tender misleads at a glance.

Same inputs, same score, every time — so a score can be replayed months later and a disagreement with
the model means something.

### Finds notices by meaning, not keyword

![Semantic search results for a Finnish query, each notice showing its similarity score and the
matching passage](docs/images/search.png)

Notices are chunked along their own boundaries — one chunk per lot, because a bidder cares whether
*a lot* fits them, not whether a notice does — then embedded and indexed in Qdrant. Filters run
inside the vector search rather than over its results, so asking for ten gets ten.

Measured over 12 Finnish queries against 300 notices: **hit@5 100%, MRR 0.875**. A Finnish query for
*"terveydenhuollon laitteet ja tarvikkeet"* returns Swedish-language notices too.

### Turns an approval into something you can act on

![The shortlist, each row leading with days remaining and ending in a link to the buyer's tender
portal](docs/images/shortlist.png)

Approving is not the end. Anything backed and still open appears on the shortlist, soonest deadline
first, each row linking to the buyer's own tendering portal — where bids are actually submitted.
Rejections never appear, and a notice drops off once its deadline passes.

### Keeps an audit trail you can read back

![A decided assessment showing an EDITED verdict, a "2 decisions" badge, and the decision history
listing both the edit and the earlier rejection with their notes and author](docs/images/decided-audit-trail.png)

Decisions are append-only: changing your mind writes a new row and leaves the old one visible. Every
row records who decided, when, and why. An audit trail you can quietly revise is not an audit trail.

### Runs on whichever model you point it at

![The model settings screen, showing a stored key as a four-character hint beside an empty password
field](docs/images/models.png)

Gemini, OpenAI, Azure, OpenRouter — or Ollama and LM Studio on your own machine, which need no key at
all. Keys are entered in the UI and **encrypted at rest**; the API has no way to return one, so it
reports a four-character hint and nothing more.

Swapping models changes the prose and never the ranking, because the narrator receives a finished
score and has no way to alter it.

### Speaks Finnish

The corpus, the queries and the generated narratives are Finnish, so the interface is too. English is
a toggle in the header.

---

## Running it

Requires Docker and a `.env` file. Everything else is in the compose stack.

```bash
git clone https://github.com/Niklotin/HilmaAgent.git
cd HilmaAgent
cp .env.example .env          # then add your keys — see below
docker compose up --build
```

That starts Postgres, Qdrant, the ingestion worker and the API. The worker applies migrations, then
ingests on a schedule. **The whole app is on <http://localhost:5080>** — the API image bakes in the
built frontend, so there is nothing else to start.

*(If the build fails inside buildx bake, prefix the command with `COMPOSE_BAKE=false`.)*

### What each key unlocks

| Key | Without it | Cost |
|---|---|---|
| `HILMA_SUBSCRIPTION_KEY` | No notices are ingested, so the app is empty | Free — [register here](https://hns-hilma-prod-apim.developer.azure-api.net/) |
| `AZURE_OPENAI_*` | Set `EMBEDDINGS_PROVIDER=fake` and it still runs; vectors carry no meaning, so search degrades and the evaluation harness refuses to score | ~€0.35/month at this corpus size |
| `GEMINI_API_KEY` | Scoring works, but nothing can be assessed — no model to write the narrative | Free tier is enough |

The deterministic half — ingestion, chunking, scoring, gates — needs no model at all.

### Running it safely

**Every port is published to `127.0.0.1` only.** Docker's default is to publish to all interfaces,
which would put the database on whatever network your machine is joined to. That matters here:
Postgres holds both the encrypted provider credentials *and* the key ring that decrypts them.

**The application has no authentication.** Anyone who can reach it can approve tenders and set API
keys. That is fine on your own laptop, which is what this is built for. To put it on a network, put
something that authenticates in front of it — do not simply change the port binding.

### Developing

```bash
npm install --prefix web
npm run dev --prefix web      # http://localhost:5173, proxies /api to the backend
```

`docker compose down` keeps the named volumes, so a restart resumes from the ingestion checkpoint.
Use `down -v` only for a genuinely clean slate — re-embedding costs money.

---

## Tests

```bash
dotnet test                        # 105 backend tests
npm test --prefix web              # 61 frontend tests
npm run test:e2e --prefix web      # 12 end-to-end tests, against the running container
```

The backend suite includes the decision write path, hosted with `WebApplicationFactory` against a
throwaway Postgres started by Testcontainers — needs Docker, needs no keys. The end-to-end suite
drives the real container and is deliberately read-only: decisions are append-only, so a test that
recorded one could not clean up after itself.

CI runs the first two on every push. The third needs a populated corpus, so it stays a local check.

---

## Stack

ASP.NET Core · PostgreSQL + EF Core · Qdrant · Polly · Docker Compose · React 19 + TypeScript on
Vite. Azure OpenAI for embeddings, Google Gemini for narration — two vendors for two jobs, because
Anthropic has no embeddings endpoint.

## Documentation

- **[docs/GUIDE.md](docs/GUIDE.md)** — how to operate it: setting a profile, reading a score, the
  daily rhythm.
- **[docs/DESIGN.md](docs/DESIGN.md)** — why it is built this way. The measured retrieval numbers,
  why `noticeId` is not unique, what the model is and is not allowed to do, and what this would need
  to become a product.

## One thing worth knowing

Over 24 assessments, the model and the deterministic score disagreed **58%** of the time — and almost
every disagreement was the same sentence: *the CPV code is right, the engagement model is wrong.*
CPV `72000000` covers custom development, SaaS resale, security monitoring and licence reselling
alike, and it is worth 45 of the 100 points. A notice buying **VMware licences** scored **93/100**.

The rules are strong on *is this biddable, the right size, the right place*. They are structurally
blind to *is this the kind of work we do*. That is the argument for keeping both answers and asking a
human — arrived at from data rather than asserted up front. The full analysis, including why that 58%
is not an estimate of anything, is in [docs/DESIGN.md](docs/DESIGN.md#measured-outcomes).

## Licence

MIT — see [LICENSE](LICENSE).

**Demo Firma Oy, the company profile shipped with this, does not exist.** The notices are public
records, but screening them against a real supplier's stated capabilities and publishing the
resulting GO/NO-GO calls would put words in someone else's mouth. Replace it with your own.
