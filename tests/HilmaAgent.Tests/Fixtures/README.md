# Fixtures

**These payloads are hand-written approximations, not recorded responses.** The Hilma AVP API
requires registration and we have not yet confirmed the real field names against the live API.

They exist to pin the *tolerance* of the parser — that it survives two differently-shaped payloads
and never loses the raw JSON — not to assert the real schema.

**Replace them with recorded responses as soon as a subscription key is available**, then tighten
the assertions. Until that happens, a green test suite here says the pipeline holds together; it
does not say the mapping is correct.
