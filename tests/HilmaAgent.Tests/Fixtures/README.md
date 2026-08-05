# Fixtures

## `notice-contract-example.json`

The response example published in the Hilma AVP **Read API** OpenAPI document
(`info.title: "Read API"`, version 1.0), extracted verbatim from
`paths./api/avp/notices/{noticeId}.get.responses.200.content.text/plain.example`.

It is the API vendor's own example, so the **shape and field names are authoritative** — that is
what the parser tests assert against. The *values* are synthetic (notice id `0`, "Cars for the
London office", a London NUTS code), so don't read anything into them.

## `notice-contract-real-fi.json`

A real published notice — `OLD-160424`, *Kiinteistöjen esteettömyyskartoituspalvelun hankinta* by
Kotkan Julkiset Kiinteistöt Oy — as returned by the Read API and stored in `RawPayload`. Asserted
against in `RealNoticeParsingTests`.

It exists because the vendor example cannot exercise what real data does:

- **Finnish text**, so an encoding mistake anywhere in the chain shows up as mojibake in a test
  rather than silently in a citation.
- **`datePublished`**, which the vendor example omits entirely — and which arrives **without a
  timezone offset**. Adding this fixture is what caught the parser reading offsetless timestamps as
  *local* time, so an ingested deadline depended on the machine that fetched it.
- **A national notice type** (`9912`, `NationalSmallValueProcurement`) rather than the example's
  `200`.
- **Hierarchical NUTS** (`FI1C7` and `FI` together) and **no contract value at all**, which is the
  common case and is distinct from a withheld one.

### One redaction

The `contactPerson` block held a named individual's work email and mobile number. Procurement
notices are public records, but republishing one person's contact details into a source repository
is a different context from the Hilma portal, so that block — and only that block — was replaced:

```json
"contactPerson": { "name": "Testi Tilaaja", "email": "testi.tilaaja@example.fi", "phone": "+358 400000000" }
```

Everything else is byte-for-byte as fetched. The buyer organisation's own details are left intact:
they identify a company, not a person, and the parser asserts against them.

### Refreshing it

The payload is whatever `GET /api/notices/{id}` returns in `rawPayload`. Decode the response as UTF-8
explicitly when regenerating — on Windows a naive `json.load(sys.stdin)` decodes through the console
codepage and silently double-encodes every `ä` and `ö`.
