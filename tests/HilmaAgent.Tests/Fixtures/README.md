# Fixtures

`notice-contract-example.json` is the response example published in the Hilma AVP **Read API**
OpenAPI document (`info.title: "Read API"`, version 1.0), extracted verbatim from
`paths./api/avp/notices/{noticeId}.get.responses.200.content.text/plain.example`.

It is the API vendor's own example, so the **shape and field names are authoritative** — that is
what the parser tests assert against. The *values* are synthetic (notice id `0`, "Cars for the
London office", a London NUTS code), so don't read anything into them.

Still worth doing once ingestion runs: drop in a real Finnish notice alongside this one. The
vendor example won't exercise everything real data will — notably `datePublished`, which the example
omits entirely.
