# Using the Hilma screening agent

A practical guide: what this tool is for, when to reach for it, and the order to do things in.

[`README.md`](../README.md) explains *why* the system is built the way it is. This file assumes you
accept that and just want to run it.

---

## 1. What it is for

You are a supplier. Finland publishes roughly **60 public procurement notices a day** — about 5,400
in a 90-day window. Almost all of them are irrelevant to you, a handful are worth an afternoon, and
the cost of missing the handful is losing work you would have won.

This tool does the triage:

- **ingests** published notices from Hilma,
- **indexes** them so you can search by meaning rather than keyword,
- **scores** each one against your company profile in code — deterministic, replayable, auditable,
- **has a model justify that score** and pick GO / NO-GO / INVESTIGATE, citing passages,
- **puts every proposal in front of a human** before anything counts as decided.

### When it earns its place

- You screen public tenders regularly and the volume is more than you want to read by hand.
- You need to **explain** a screening decision afterwards — to a colleague, a manager, an auditor.
  Every score decomposes into named rules, and every decision is an append-only row with your note.
- You want to change what you screen for and see what that changes. Edit the profile, re-assess,
  compare.

### When it is the wrong tool

- **It does not write bids.** It decides what is worth reading, nothing past that.
- **It is not a legal or eligibility check.** It never verifies you meet the qualification criteria,
  turnover requirements, or references a buyer demands.
- **Nothing here is a final answer.** A GO means "a human should look at this today", not "bid".
- **It cannot see anything outside the notice text and your profile** — not your current capacity,
  not who else is bidding, not your relationship with the buyer.

---

## 2. Before you start

You need Docker, Node, and a `.env` at the repo root. Copy the template and fill it in:

```bash
cp .env.example .env
```

| Key | What it is for | Required? |
|---|---|---|
| `HILMA_SUBSCRIPTION_KEY` | Fetching notices. Register at the [Hilma APIM portal](https://hns-hilma-prod-apim.developer.azure-api.net/). | Yes, to ingest |
| `EMBEDDINGS_PROVIDER` | `azure` or `fake` | Yes |
| `AZURE_OPENAI_ENDPOINT` / `AZURE_OPENAI_API_KEY` | Embeddings for semantic search | Only if `azure` |
| `GEMINI_API_KEY` | Writing the assessment narrative | Yes, to assess |

**Trying it without accounts:** set `EMBEDDINGS_PROVIDER=fake`. The stack runs end to end, but the
vectors match shared tokens rather than meaning, so semantic search gets much worse and
`/api/search/evaluate` will **refuse to score** rather than report a misleading number. Fine for
seeing the shape of the thing; not fine for judging retrieval.

---

## 3. Start the stack

```bash
COMPOSE_BAKE=false docker compose up -d --build
```

That is `postgres`, `qdrant`, `worker`, and `api`. The worker applies migrations, then ingests on a
schedule. API on <http://localhost:5080>.

The API image includes the built UI, so <http://localhost:5080> is already the whole app — nothing
else to start.

The interface is **in Finnish**. Switch it to English with the **Kieli / Language** picker in the
header; the choice is remembered in your browser.

For development, run Vite instead so edits hot-reload:

```bash
npm run dev --prefix web
```

<http://localhost:5173>, which proxies `/api` to the backend so the browser stays on one origin.

**Stopping:** `docker compose down` keeps the named volumes, so a restart resumes from the ingestion
checkpoint. Use `down -v` only when you genuinely want a clean slate — it discards the corpus *and*
the embeddings, and re-embedding costs money.

---

## 4. Check what landed

```bash
curl http://localhost:5080/api/notices/stats
```

Look at `total`, and at `checkpoint.lastSeenPublicationDate` — that is how current you are. First run
on an empty volume takes a while; it is rate-limited on purpose.

---

## 5. Set your profile — do this before anything else

**This is the step that matters.** Every score is computed against the profile, so a wrong profile
produces confidently wrong screening. Open the **Profile** tab.

| Field | How it is used | Get this wrong and… |
|---|---|---|
| **CPV codes** | 45 of 100 points. Matched hierarchically — `72000000` contains `72200000`. | Everything scores near zero, or everything scores high |
| **NUTS regions** | 20 points. Also hierarchical — `FI1B` covers `FI1B1`. Leave empty to mean nationwide | Leaving it empty awards full marks to every notice |
| **Min / max contract value** | 20 points. Both optional | Leaving both empty awards full marks to every notice |
| **Description** | Not scored. Ranks which passages the model is shown, and tells it who you are | The model's verdict drifts; the score does not move |
| **Technologies, references** | Not scored. Context for the model | Same |

Two things worth knowing:

- **Only CPV, region, and value move the number.** Description, technologies and references are for
  the model's narrative. If you want different *scores*, change the top three.
- **Editing the profile does not re-score existing assessments.** That is deliberate — they record
  what was true when they were made. To see the effect of an edit, assess something again.

Start from the shipped demo profile (Demo Firma Oy — fictional, see the README) and replace it with
yours. CPV codes are validated as digits, so a typo is rejected rather than silently costing you
every future score.

---

## 6. Find candidates

Two ways in.

**Browse** — the **Assess** tab lists notices with buyer, CPV and deadline. Fine when you just want
to work through what is open.

**Search semantically** — better when you know what you are looking for. The search box at the top of
the **Assess** tab ranks notices by meaning and shows the matching passage under each hit, with the
**open only** box ticked by default. Query in **Finnish**; the corpus is Finnish and matching is
cross-lingual enough to return Swedish- and English-language notices too.

The same thing from the API:

```bash
curl -X POST http://localhost:5080/api/search \
  -H 'Content-Type: application/json' \
  -d '{"query":"ohjelmistokehitys ja integraatiot","limit":10,"cpvPrefixes":["72"],"nutsPrefixes":["FI1B"],"openOnly":true}'
```

`openOnly: true` is almost always what you want — it drops notices you can no longer bid on. Filters
run *inside* the vector search, so asking for 10 gets you 10.

If search returns nothing, the notices may be ingested but not embedded yet:

```bash
curl -X POST "http://localhost:5080/api/search/index?max=500"
curl http://localhost:5080/api/search/stats     # vector count, and which model made them
```

---

## 7. Assess

In the **Assess** tab, hit **Assess** on a row. It streams over SSE, and the order is the point: the
**deterministic score arrives before the model is called**. You are watching a number get computed,
then narrated — never negotiated.

From the API:

```bash
curl -X POST http://localhost:5080/api/assess/EF-53343
```

**Cost:** a fraction of a cent per assessment, dominated by the model call — roughly 400× what
embedding that notice cost. Cheap, but it is a per-notice cost, which is exactly why the deterministic
filter runs first. Assess the ones that survive triage, not everything.

---

## 8. Read the card

Open the **Queue** tab. Disagreements sort first, then highest score, because those need you most.

Three controls sit above the list:

- **Sort by** — *disagreement* (default), *score*, or **deadline — closing soonest**. Switch to
  deadline when you are working against the clock rather than triaging fresh notices; notices with no
  stated deadline sort last.
- **disagreements only** — the cases where the model and the rules parted ways.
- **Min score** — hide everything below a threshold.

Your half-written note survives switching tabs and reloading the page, so you can go and check
something without losing it.

Each card shows, side by side and never merged:

- **the score** out of 100, computed in code,
- **the model's recommendation**, written from that score,
- **⚠ disagreement — you decide** when they differ.

**Expand the score breakdown.** This is the part people skip and shouldn't — it tells you *which rule
earned what*, so a 90 you disagree with becomes an argument you can actually locate.

### How the number is built

| Rule | Max | Full marks when | Notes |
|---|---|---|---|
| `cpv_overlap` | 45 | Exact code, or one code contains the other at 5+ significant digits | Related-but-not-containing codes score 10–38 by shared digits |
| `region_match` | 20 | One region contains the other | Same country, different area = 8. **No regions declared = full 20** |
| `value_band` | 20 | Value inside your band | Unknown value = 10 + a warning. Within 2× of the edge = 10; beyond = 0 |
| `deadline_headroom` | 15 | 28+ days left | 14 days = 12, 7 days = 8, under 7 = 4 and a warning |

**Gates override everything.** If any gate fires the score is **0**, not its would-be points, because
a high number next to an unbiddable tender misleads at a glance:

- `deadline_passed` — the deadline is behind you,
- `cancelled` — the buyer withdrew it,
- `not_biddable` — the notice type announces a result or an intention: award notices, prior
  information notices, modifications, transparency notices. Useful market intelligence, nothing to bid on.

### Reading a high score critically

Measured over 24 assessments on this corpus, the model disagreed with the score **58% of the time** —
and the disagreements were nearly all one thing: **the CPV code is right, the engagement model is
wrong.**

CPV `72000000` covers custom development, SaaS resale, security monitoring and licence reselling
alike, and it is worth 45 points. A notice buying **VMware licences** scored **93/100**. Right code,
right region, right value, right deadline — and nothing a development consultancy should bid on.

So, in practice:

> **A high score means "not obviously excluded", not "good fit."** The rules are strong on *is this
> biddable, is it the right size and place*. They are structurally blind to *is this the kind of work
> we do*. That second question is what the model's narrative is for — read it, especially when it
> disagrees.

Citations appear twice: as superscript numbers **inside** the narrative, and as chips below it. They
are the same references — click either to open the passage. The model cites by identifier rather than
by quotation so that an invented reference can be caught; anything it could not verify was dropped
before it ever reached you.

---

## 9. Decide

Four options per card: **Approve**, **Reject**, or **Edit to** GO / INVESTIGATE.

**Write the note.** It is optional and it is the most valuable column in the table when you read back
what went wrong three months later. "Right CPV, but they want a product not a build" is worth more
than any score.

**Set your name in the header first.** Every decision records who made it, and the buttons stay
disabled until it is filled in. It is stored in your browser, not on the server — there is no auth
yet, so this is a label rather than an identity, but an unattributed audit trail is worthless.

Decisions are **append-only**. Changing your mind writes a new row; only the latest counts toward
metrics, and the earlier ones stay as history. There is no edit and no delete, on purpose — an audit
trail you can quietly revise is not an audit trail.

### Then what? — the shortlist

Approving is not the end. Anything you backed — approved, or edited to GO or INVESTIGATE — appears in
the **Shortlist** tab: still-open notices, **soonest deadline first**, with the days remaining in
large type because that is the number that runs out.

Each row links straight to **Tender documents**, the buyer's own tendering portal, which is where
bids are actually submitted. Roughly half of eForms notices state that link and legacy notices never
do; where it is missing the row says *"no link in notice"* rather than sending you somewhere invented.

Three things deliberately never reach the shortlist:

- **Rejections**, whatever the agent had recommended.
- **Approved NO-GOs** — approving a NO-GO records that you agree *not* to bid; it is a decision, not
  a task.
- **Closed tenders.** A notice drops off once its deadline passes. The decision stays readable under
  Decided; it just stops nagging you about something you can no longer bid on.

This is the tab to open on a Monday. The queue is where you think; the shortlist is where you act.

### Reading decisions back

A decided assessment leaves the queue and appears in the **Decided** tab: what the score said, what
the model said, what you stood behind, your note, and when. **Show decision history** expands every
verdict ever recorded against it; **Change my mind** records a new one without erasing the old.

This is where the notes earn their keep, so write them.

```bash
curl -X POST http://localhost:5080/api/assessments/<assessment-guid>/decision \
  -H 'Content-Type: application/json' \
  -d '{"decision":"EDITED","editedRecommendation":"NO_GO","reviewerNote":"SaaS product, not a custom build","reviewedBy":"niko"}'
```

`decision` is `APPROVED`, `REJECTED`, or `EDITED`; `EDITED` requires `editedRecommendation`.

---

## 10. Read the metrics

In the header bar, or `GET /api/metrics`. Two numbers, measuring different things:

- **override rate** — how often a human changed the agent's answer. **The headline quality number.**
  It only becomes meaningful once you have actually worked the queue; it is computed from the latest
  decision per assessment.
- **model vs score disagreement rate** — how often the model and the rules reached different
  conclusions from the same inputs, *independent of what you then did*.

The second one moving without the first is interesting: it means the model is flagging things, and
you are agreeing with the flags. The first one climbing means the agent is wrong about your business
in some way you can now go and locate.

---

## 11. A working rhythm

**Once, at setup:** fill in `.env`, start the stack, set your profile carefully.

**Daily, ~10 minutes:** open the **Shortlist** first — anything closing this week needs a decision today. Then let ingestion run. Search or browse for open notices in your CPV range.
Assess the plausible ones. Work the queue — disagreements first, since those are where your judgement
is actually needed. Write notes.

**Weekly, or when something nags at you:** skim the **Decided** tab. Past notes are the cheapest way
to notice you keep rejecting the same category, which is usually a profile problem rather than a
judgement one.

**Weekly:** check the override rate. If it is high, your profile is describing a company you are not —
usually the CPV list is too broad. Adjust and watch whether it falls.

**When something changes** — new capability, new region, different contract sizes — edit the profile.
Existing assessments stay as they were; new ones reflect the change.

---

## 12. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `docker compose up` fails in buildx bake | Known on some machines | Prefix with `COMPOSE_BAKE=false` |
| Search returns nothing | Notices ingested but not embedded | `POST /api/search/index?max=500` |
| `/api/search/evaluate` returns 409 | `EMBEDDINGS_PROVIDER=fake` | Expected — it refuses to score meaningless vectors. Set `azure` |
| Everything scores 0 | A gate fired — most often `not_biddable` | Expand the breakdown; check the notice type. Use `openOnly` when searching |
| Everything scores high | Profile too broad, or empty region / value fields | Empty regions and empty value bands award **full marks**. Fill them in |
| Notice count stuck | Checkpoint at the newest publication | Normal — it only advances past notices actually stored |
| API rejects a Finnish query from the shell | Git Bash mangles `ä` into Latin-1 | Send UTF-8 properly, or use the UI |

---

## 13. What it costs

Embedding is nearly free: the whole 90-day window of ~5,400 notices is about **$1.00**, ongoing
around **$0.35/month**. Searching is a rounding error.

Assessment is where the money is — the model call per notice, roughly 400× the cost of embedding that
notice. Still small at this scale, and the reason the pipeline filters deterministically first and
pays for prose only on what survives.

---

## 14. Known limits

- **eForms structured fields come from the search index**, not the XML. If the index truncated
  something, this inherits it. The raw XML is stored, so a fix is a reparse rather than a re-crawl.
- **`Language` and `IsLatest` are null for eForms notices** — the index has no equivalent.
- **Procurement plans are excluded.** Different contract, out of scope.
- **The score cannot see engagement model** — product vs build, resale vs development, specialist vs
  generalist. See §8. This is a property of CPV, not a bug to be tuned away.
