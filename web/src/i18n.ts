import { createContext, useContext } from 'react'

/**
 * Interface language.
 *
 * The corpus, the queries and the generated narratives are Finnish, so the people who would actually
 * use this read Finnish — an English-only interface sitting on top of Finnish content was always the
 * odd part. Finnish is the default; English stays available because the notices themselves are
 * sometimes Swedish or English and the project is read by people who are not Finnish.
 *
 * Hand-rolled rather than react-i18next: this is one flat dictionary with no pluralisation rules
 * worth speaking of, and the app ships with two runtime dependencies. A translation library would be
 * the third and the largest.
 */
export type Lang = 'fi' | 'en'

const STORAGE_KEY = 'hilma.lang'

const fi = {
  'app.title': 'Hilma-seulonta',
  'app.tagline':
    'Pisteet lasketaan koodissa. Malli kirjoittaa perustelun ja saa olla eri mieltä — se ei koskaan kumoa pisteitä.',

  'nav.label': 'Näkymät',
  'nav.queue': 'Jono',
  'nav.assess': 'arvioi',
  'nav.shortlist': 'kärkilista',
  'nav.decided': 'päätetyt',
  'nav.profile': 'profiili',
  'nav.models': 'mallit',

  'models.heading': 'Mallit ja rajapinta-avaimet',
  'models.intro':
    'Valitse mikä malli kirjoittaa arvioiden perustelut, ja tallenna tarvittavat rajapinta-avaimet. Avaimet salataan tallennettaessa eikä niitä palauteta koskaan takaisin.',
  'models.activeHeading': 'Perustelut kirjoittaa',
  'models.activeHint': 'Asetus koskee kaikkia uusia arvioita. Vanhoihin arvioihin on tallennettu se malli, joka ne kirjoitti.',
  'models.notReady': 'ei käytettävissä',
  'models.reason.no-api-key': 'rajapinta-avain puuttuu',
  'models.reason.no-endpoint': 'osoite puuttuu',
  'models.providerGemini': 'Google Gemini',
  'models.providerOpenAi': 'OpenAI-yhteensopiva',
  'models.providerOpenAiHint': 'OpenAI, Azure, OpenRouter — tai paikallinen malli, esimerkiksi Ollama tai LM Studio.',
  'models.apiKey': 'Rajapinta-avain',
  'models.apiKeyStored': 'tallennettu',
  'models.apiKeyFromEnv': 'ympäristömuuttujasta',
  'models.apiKeyPlaceholder': 'liitä avain — tyhjä jättää nykyisen ennalleen',
  'models.apiKeyNone': 'ei avainta',
  'models.apiKeyUnreadable':
    'Tallennettua avainta ei voitu purkaa: salausavaimet ovat vaihtuneet. Syötä avain uudelleen.',
  'models.baseUrl': 'Rajapinnan osoite',
  'models.baseUrlPlaceholder': 'http://localhost:11434/v1',
  'models.baseUrlWarning':
    'Osoitteen vaihtaminen poistaa tallennetun avaimen, jottei sitä lähetetä uuteen kohteeseen vahingossa. Syötä avain uudelleen samalla.',
  'models.model': 'Mallin tunniste',
  'models.modelPlaceholder': 'esim. llama3.1:8b',
  'models.save': 'Tallenna',
  'models.saving': 'Tallennetaan…',
  'models.saved': 'Tallennettu.',
  'models.savedKeyCleared': 'Tallennettu. Avain poistettiin, koska osoite vaihtui — syötä se uudelleen.',
  'models.forget': 'Poista tiedot',
  'models.updatedBy': 'päivittänyt',
  'models.localTitle': 'Paikallinen malli',
  'models.localHint':
    'Aseta osoitteeksi paikallisen palvelimesi rajapinta, esimerkiksi Ollaman http://host.docker.internal:11434/v1. Avainta ei tarvita.',
  'models.security':
    'Sovelluksessa ei ole vielä kirjautumista: kuka tahansa, joka ylettyy palvelimeen, voi vaihtaa nämä. Salaus suojaa vuotaneelta tietokantavedokselta, ei siltä.',
  'models.embeddingsNote':
    'Upotusmalli valitaan käynnistyksessä (Embeddings-asetukset). Sen vaihtaminen mitätöisi koko vektori-indeksin, joten se ei ole ajonaikainen valinta.',

  'reviewer.label': 'Käsittelijä',
  'reviewer.placeholder': 'nimesi',
  'reviewer.aria': 'Nimesi, joka kirjataan jokaiseen päätökseen',
  'reviewer.required': 'Aseta nimesi yläpalkkiin kirjataksesi päätöksen.',

  'lang.label': 'Kieli',

  'metrics.assessed': 'arvioitu',
  'metrics.pending': 'odottaa käsittelyä',
  'metrics.overrideRate': 'muutosaste',
  'metrics.modelVsScore': 'malli vs. pisteet',
  'metrics.overrideHint': 'Kuinka usein ihminen muutti agentin vastausta. Tärkein laatuluku.',
  'metrics.disagreementHint': 'Kuinka usein malli ja laskennalliset pisteet päätyivät eri tulokseen.',
  'metrics.none': 'Ei vielä arvioita — aja yksi Arvioi-välilehdeltä.',
  'metrics.closingSoon': '{n} päättyy viikossa',
  'metrics.closingSoonHint': 'Määräaika umpeutuu seitsemässä päivässä eikä päätöstä ole vielä tehty.',
  'metrics.bidSoon': '{n} tarjottavaa',
  'metrics.bidSoonHint': 'Kärkilistalla, määräaika umpeutuu seitsemässä päivässä.',

  'common.loading': 'Ladataan…',

  'filters.sortBy': 'Järjestys',
  'filters.sortDisagreement': 'erimielisyys, sitten pisteet',
  'filters.sortScore': 'pisteet',
  'filters.sortDeadline': 'määräaika — lähin ensin',
  'filters.disagreementsOnly': 'vain erimielisyydet',
  'filters.minScore': 'Vähimmäispisteet',

  'queue.empty': 'Ei käsittelyä odottavia. Jokaisesta arviosta on kirjattu päätös.',
  'queue.emptyFiltered': 'Mikään ei vastaa näitä rajauksia. Väljennä niitä nähdäksesi loput jonosta.',

  'card.scoreSays': 'Pisteet sanovat',
  'card.modelSays': 'Malli sanoo',
  'card.disagreement': '⚠ erimielisyys — sinä päätät',
  'card.disagreementHint':
    'Malli päätyi eri tulokseen kuin pisteet. Kumpaakaan ei kumottu.',
  'card.scoreHint': 'Laskettu koodissa, ei koskaan mallin toimesta',
  'card.deadline': 'Määräaika',
  'card.value': 'Arvo',
  'card.model': 'Malli',
  'card.noBuyer': 'Hankintayksikköä ei ilmoitettu',
  'card.valueMissing': 'ei ilmoitettu',
  'card.citations': 'Viitatut kohdat:',
  'card.citationAria': 'Viite {n}, kohdasta {section}',
  'card.noExcerpt': '(otetta ei tallennettu)',
  'card.showBreakdown': 'Näytä pisteiden erittely',
  'card.hideBreakdown': 'Piilota pisteiden erittely',
  'card.notePlaceholder':
    'Miksi? (vapaaehtoinen — mutta tämä on hyödyllisin sarake, kun luet jälkikäteen mikä meni pieleen)',
  'card.approve': 'Hyväksy',
  'card.reject': 'Hylkää',
  'card.editTo': 'Muuta:',
  'card.blocks': 'estää',

  'assess.notice': 'Ilmoitus',
  'assess.buyer': 'Hankintayksikkö',
  'assess.cpv': 'CPV',
  'assess.deadline': 'Määräaika',
  'assess.run': 'Arvioi',
  'assess.empty': 'Avoimia ilmoituksia ei ole vielä haettu.',
  'assess.searchPlaceholder': 'Hae ilmoituksia merkityksen perusteella — esim. ohjelmistokehitys ja integraatiot',
  'assess.searchAria': 'Semanttinen hakulause',
  'assess.openOnly': 'vain avoimet',
  'assess.search': 'Hae',
  'assess.searching': 'Haetaan…',
  'assess.clear': 'tyhjennä',
  'assess.resultCount': '{n} ilmoitusta merkityksen mukaan. Rajaukset ajetaan vektorihaun sisällä, joten lukumäärää ei kutisteta jälkikäteen.',
  'assess.noResults': 'Ei osumia. Jos aineisto on juuri haettu, sitä ei ehkä ole vielä upotettu — POST /api/search/index.',
  'assess.similarityHint': 'Parhaan osuman samankaltaisuus. Vertailukelpoinen vain tämän tulosjoukon sisällä.',
  'assess.closes': 'päättyy',

  'runner.assessing': 'Arvioidaan',
  'runner.done': 'Valmis — se on nyt jonon kärjessä.',
  'runner.lost': 'Yhteys katkesi.',

  'shortlist.empty':
    'Kärkilista on tyhjä. Ilmoitus päätyy tänne, kun hyväksyt sen — tai muutat sen GO- tai INVESTIGATE-tulokseksi — eikä määräaika ole umpeutunut.',
  'shortlist.summary':
    '{n} avoinna, lähin määräaika ensin. Hylätyt eivät päädy tänne, ja ilmoitus poistuu määräajan umpeuduttua — päätös jää luettavaksi Päätetyt-välilehdelle.',
  'shortlist.daysLeft': 'päivää jäljellä',
  'shortlist.dayLeft': 'päivä jäljellä',
  'shortlist.noDeadline': 'ei määräaikaa',
  'shortlist.scored': 'pisteet {score}/100',
  'shortlist.backedBy': 'puoltanut',
  'shortlist.documents': 'Tarjousasiakirjat ↗',
  'shortlist.noLink': 'ei linkkiä ilmoituksessa',

  'decided.empty': 'Päätöksiä ei ole vielä kirjattu. Ratkaise jokin jonosta, niin se ilmestyy tänne.',
  'decided.summary':
    '{n} päätettyä. Päätökset ovat vain lisääviä — uudelleenharkinta kirjaa uuden päätöksen ja säilyttää vanhan.',
  'decided.scoreSaid': 'Pisteet sanoivat',
  'decided.modelSaid': 'Malli sanoi',
  'decided.stoodBehind': 'Käsittelijä puolsi',
  'decided.by': 'kirjannut',
  'decided.unattributed': 'ei kirjaajaa',
  'decided.revisions': '{n} päätöstä',
  'decided.revisionsHint': 'Tästä arviosta on päätetty useammin kuin kerran.',
  'decided.noNote': 'Ei kirjattua perustelua.',
  'decided.showHistory': 'Näytä päätöshistoria',
  'decided.hideHistory': 'Piilota päätöshistoria',
  'decided.changeMind': 'Muutan mieleni',
  'decided.cancel': 'Peruuta',
  'decided.revisitPlaceholder': 'Miksi muutit mielesi? Tämä näkyy historiassa.',
  'decided.noNoteShort': 'ei perustelua',

  'profile.heading': 'Yritysprofiili',
  'profile.intro':
    'Kaikki, mitä vasten ilmoituksia pisteytetään. Kuvitteellinen demoyritys — korvaa se sillä, mitä haluat seuloa.',
  'profile.name': 'Nimi',
  'profile.description': 'Kuvaus',
  'profile.descriptionHint': '(käytetään myös mallille näytettävien kohtien järjestämiseen)',
  'profile.cpv': 'CPV-koodit',
  'profile.cpvHint': '(yksi riviä kohden; täsmäytetään hierarkkisesti)',
  'profile.regions': 'NUTS-alueet',
  'profile.regionsHint': '(yksi riviä kohden; FI1B kattaa FI1B1:n)',
  'profile.minValue': 'Hankinnan vähimmäisarvo (EUR)',
  'profile.maxValue': 'Hankinnan enimmäisarvo (EUR)',
  'profile.technologies': 'Teknologiat',
  'profile.technologiesHint': '(yksi riviä kohden)',
  'profile.references': 'Referenssiprojektit',
  'profile.referencesHint': '(yksi riviä kohden)',
  'profile.save': 'Tallenna profiili',
  'profile.saving': 'Tallennetaan…',
  'profile.saved': 'Tallennettu. Uudet arviot käyttävät näitä arvoja; vanhat säilyvät ennallaan.',
  'profile.loading': 'Ladataan profiilia…',


  'score.relation.exact': 'tarkka osuma',
  'score.relation.contained': 'sisältyy',
  'score.relation.related': 'lähisukuinen',
  'score.relation.unrelated': 'ei yhteyttä',

  'score.cpv.no_profile_codes': 'Profiilissa ei ole CPV-koodeja, joten osumaa ei voi laskea.',
  'score.cpv.no_notice_codes': 'Ilmoituksessa ei ole CPV-koodeja.',
  'score.cpv.best_match': 'Paras CPV-osuma: {notice} vs {profile} ({relation}).',
  'score.cpv.no_overlap': 'Ilmoituksen CPV-koodeilla ({codes}) ei ole yhteistä pääluokkaa profiilin kanssa.',

  'score.region.no_limit': 'Profiili ei rajaa aluetta; käsitellään valtakunnallisena.',
  'score.region.notice_unstated': 'Ilmoitus ei kerro aluetta; sitä ei voi sulkea pois maantieteen perusteella.',
  'score.region.best_match': 'Paras alueosuma: {notice} vs {profile} ({relation}).',
  'score.region.outside': 'Ilmoituksen alueet ({regions}) ovat profiilin alueiden ulkopuolella.',

  'score.value.no_limits': 'Profiili ei rajaa hankinnan arvoa.',
  'score.value.unknown': 'Hankinnan arvo ei ole tiedossa; pisteytetty neutraalisti.',
  'score.value.within_band': 'Arvioitu arvo {value} on profiilin haarukassa.',
  'score.value.below_minimum': 'Arvioitu arvo {value} alittaa profiilin alarajan {limit}.',
  'score.value.above_maximum': 'Arvioitu arvo {value} ylittää profiilin ylärajan {limit}.',

  'score.deadline.none': 'Määräaikaa ei ole ilmoitettu.',
  'score.deadline.days_left': '{days} päivää määräaikaan {date}.',
  'score.deadline.passed': 'Määräaika {date} on umpeutunut.',

  'score.gate.deadline_passed': 'Määräaika umpeutui {date}; tarjousta ei voi enää jättää.',
  'score.gate.cancelled': 'Hankintayksikkö on perunut ilmoituksen.',
  'score.gate.not_biddable': 'Ilmoitustyyppi {type} kertoo tuloksesta tai aikeesta, ei avoimesta tarjouskilpailusta.',

  'score.warn.value_withheld': 'Hankintayksikkö ei julkaissut hankinnan arvoa, joten arvon sopivuutta ei voitu arvioida.',
  'score.warn.value_unstated': 'Ilmoituksessa ei kerrota hankinnan arvoa, joten arvon sopivuutta ei voitu arvioida.',
  'score.warn.deadline_unstated': 'Ilmoituksessa ei kerrota määräaikaa, joten valmisteluaikaa ei voitu arvioida.',
  'score.warn.deadline_imminent': 'Aikaa jättää tarjous on enää {days} päivää; valmisteluaika on hyvin lyhyt.',

  'error.card': 'Tämän kortin näyttäminen epäonnistui',
} as const

export type StringKey = keyof typeof fi

const en: Record<StringKey, string> = {
  'app.title': 'Hilma screening',
  'app.tagline':
    'Scores computed in code. The model writes the justification and may disagree — it never overrules.',

  'nav.label': 'Views',
  'nav.queue': 'Queue',
  'nav.assess': 'assess',
  'nav.shortlist': 'shortlist',
  'nav.decided': 'decided',
  'nav.profile': 'profile',
  'nav.models': 'models',

  'models.heading': 'Models and API keys',
  'models.intro':
    'Choose which model writes the assessment narratives, and store the API keys it needs. Keys are encrypted when stored and are never returned.',
  'models.activeHeading': 'Narratives written by',
  'models.activeHint': 'Applies to every new assessment. Existing ones record the model that wrote them.',
  'models.notReady': 'not usable',
  'models.reason.no-api-key': 'no API key',
  'models.reason.no-endpoint': 'no endpoint',
  'models.providerGemini': 'Google Gemini',
  'models.providerOpenAi': 'OpenAI-compatible',
  'models.providerOpenAiHint': 'OpenAI, Azure, OpenRouter — or a local model such as Ollama or LM Studio.',
  'models.apiKey': 'API key',
  'models.apiKeyStored': 'stored',
  'models.apiKeyFromEnv': 'from environment',
  'models.apiKeyPlaceholder': 'paste a key — blank leaves the stored one alone',
  'models.apiKeyNone': 'no key',
  'models.apiKeyUnreadable':
    'A stored key could not be decrypted: the encryption keys have changed. Enter the key again.',
  'models.baseUrl': 'Endpoint',
  'models.baseUrlPlaceholder': 'http://localhost:11434/v1',
  'models.baseUrlWarning':
    'Changing the endpoint clears the stored key, so it cannot be sent somewhere it was not meant for. Re-enter it at the same time.',
  'models.model': 'Model identifier',
  'models.modelPlaceholder': 'e.g. llama3.1:8b',
  'models.save': 'Save',
  'models.saving': 'Saving…',
  'models.saved': 'Saved.',
  'models.savedKeyCleared': 'Saved. The key was cleared because the endpoint changed — enter it again.',
  'models.forget': 'Remove stored details',
  'models.updatedBy': 'updated by',
  'models.localTitle': 'Local model',
  'models.localHint':
    'Point the endpoint at your own server, e.g. Ollama on http://host.docker.internal:11434/v1. No key needed.',
  'models.security':
    'There is no sign-in yet: anyone who can reach the server can change these. Encryption protects a leaked database dump, not that.',
  'models.embeddingsNote':
    'The embedding model is chosen at startup (Embeddings settings). Changing it would invalidate the whole vector index, so it is not a runtime choice.',

  'reviewer.label': 'Reviewer',
  'reviewer.placeholder': 'your name',
  'reviewer.aria': 'Your name, recorded against every decision',
  'reviewer.required': 'Set your name in the header to record a decision.',

  'lang.label': 'Language',

  'metrics.assessed': 'assessed',
  'metrics.pending': 'awaiting review',
  'metrics.overrideRate': 'override rate',
  'metrics.modelVsScore': 'model vs score',
  'metrics.overrideHint': "How often a human changed the agent's answer. The headline quality number.",
  'metrics.disagreementHint': 'How often the model and the deterministic score reached different conclusions.',
  'metrics.none': 'No assessments yet — run one from the Assess tab.',
  'metrics.closingSoon': '{n} closing this week',
  'metrics.closingSoonHint': 'Deadline within seven days and no decision recorded yet.',
  'metrics.bidSoon': '{n} to bid',
  'metrics.bidSoonHint': 'On the shortlist, with a deadline within seven days.',

  'common.loading': 'Loading…',

  'filters.sortBy': 'Sort by',
  'filters.sortDisagreement': 'disagreement, then score',
  'filters.sortScore': 'score',
  'filters.sortDeadline': 'deadline — closing soonest',
  'filters.disagreementsOnly': 'disagreements only',
  'filters.minScore': 'Min score',

  'queue.empty': 'Nothing awaiting review. Every assessment has a decision recorded against it.',
  'queue.emptyFiltered': 'Nothing matches these filters. Widen them to see the rest of the queue.',

  'card.scoreSays': 'Score says',
  'card.modelSays': 'Model says',
  'card.disagreement': '⚠ disagreement — you decide',
  'card.disagreementHint': 'The model reached a different conclusion from the score. Neither was overruled.',
  'card.scoreHint': 'Computed in code, never by the model',
  'card.deadline': 'Deadline',
  'card.value': 'Value',
  'card.model': 'Model',
  'card.noBuyer': 'Buyer not stated',
  'card.valueMissing': 'not stated',
  'card.citations': 'Cited passages:',
  'card.citationAria': 'Citation {n}, from the {section}',
  'card.noExcerpt': '(no excerpt stored)',
  'card.showBreakdown': 'Show score breakdown',
  'card.hideBreakdown': 'Hide score breakdown',
  'card.notePlaceholder':
    'Why? (optional — but this note is the most useful column when reading back what went wrong)',
  'card.approve': 'Approve',
  'card.reject': 'Reject',
  'card.editTo': 'Edit to:',
  'card.blocks': 'blocks',

  'assess.notice': 'Notice',
  'assess.buyer': 'Buyer',
  'assess.cpv': 'CPV',
  'assess.deadline': 'Deadline',
  'assess.run': 'Assess',
  'assess.empty': 'No open notices ingested yet.',
  'assess.searchPlaceholder': 'Search notices by meaning — in Finnish, e.g. ohjelmistokehitys ja integraatiot',
  'assess.searchAria': 'Semantic search query',
  'assess.openOnly': 'open only',
  'assess.search': 'Search',
  'assess.searching': 'Searching…',
  'assess.clear': 'clear',
  'assess.resultCount':
    '{n} notices by semantic similarity. Filters run inside the vector search, so the count is not shrunk after the fact.',
  'assess.noResults':
    'Nothing matched. If the corpus was just ingested it may not be embedded yet — POST /api/search/index.',
  'assess.similarityHint': "Best-matching chunk's similarity. Comparable within this result set only.",
  'assess.closes': 'closes',

  'runner.assessing': 'Assessing',
  'runner.done': 'Done — it is now at the top of the queue.',
  'runner.lost': 'Connection lost.',

  'shortlist.empty':
    'Nothing on the shortlist. Approving a notice — or editing one to GO or INVESTIGATE — puts it here, so long as its deadline has not passed.',
  'shortlist.summary':
    '{n} still open, soonest deadline first. Rejected notices never appear here, and a notice drops off once its deadline passes — the decision itself stays readable under Decided.',
  'shortlist.daysLeft': 'days left',
  'shortlist.dayLeft': 'day left',
  'shortlist.noDeadline': 'no deadline stated',
  'shortlist.scored': 'scored {score}/100',
  'shortlist.backedBy': 'backed by',
  'shortlist.documents': 'Tender documents ↗',
  'shortlist.noLink': 'no link in notice',

  'decided.empty': 'No decisions recorded yet. Rule on something in the queue and it appears here.',
  'decided.summary':
    '{n} decided. Decisions are append-only — revisiting one records a new verdict and keeps the old.',
  'decided.scoreSaid': 'Score said',
  'decided.modelSaid': 'Model said',
  'decided.stoodBehind': 'Reviewer stood behind',
  'decided.by': 'by',
  'decided.unattributed': 'unattributed',
  'decided.revisions': '{n} decisions',
  'decided.revisionsHint': 'This assessment has been decided more than once.',
  'decided.noNote': 'No note recorded.',
  'decided.showHistory': 'Show decision history',
  'decided.hideHistory': 'Hide decision history',
  'decided.changeMind': 'Change my mind',
  'decided.cancel': 'Cancel',
  'decided.revisitPlaceholder': 'Why the change of mind? This is what the history will show.',
  'decided.noNoteShort': 'no note',

  'profile.heading': 'Company profile',
  'profile.intro':
    'Everything notices are scored against. Fictional demo company — replace it with whatever you want to screen for.',
  'profile.name': 'Name',
  'profile.description': 'Description',
  'profile.descriptionHint': '(also used to rank which passages the model is shown)',
  'profile.cpv': 'CPV codes',
  'profile.cpvHint': '(one per line; matched hierarchically)',
  'profile.regions': 'NUTS regions',
  'profile.regionsHint': '(one per line; FI1B covers FI1B1)',
  'profile.minValue': 'Minimum contract value (EUR)',
  'profile.maxValue': 'Maximum contract value (EUR)',
  'profile.technologies': 'Technologies',
  'profile.technologiesHint': '(one per line)',
  'profile.references': 'Reference projects',
  'profile.referencesHint': '(one per line)',
  'profile.save': 'Save profile',
  'profile.saving': 'Saving…',
  'profile.saved': 'Saved. New assessments will use these values; existing ones are unchanged.',
  'profile.loading': 'Loading profile…',


  'score.relation.exact': 'exact',
  'score.relation.contained': 'contained',
  'score.relation.related': 'related',
  'score.relation.unrelated': 'unrelated',

  'score.cpv.no_profile_codes': 'The profile declares no CPV codes, so no overlap can be computed.',
  'score.cpv.no_notice_codes': 'The notice carries no CPV codes.',
  'score.cpv.best_match': 'Best CPV match: {notice} vs {profile} ({relation}).',
  'score.cpv.no_overlap': 'No shared CPV division between the notice ({codes}) and the profile.',

  'score.region.no_limit': 'The profile declares no region limit; treated as nationwide.',
  'score.region.notice_unstated': 'The notice declares no region; it cannot be excluded on geography.',
  'score.region.best_match': 'Best region match: {notice} vs {profile} ({relation}).',
  'score.region.outside': 'The notice regions ({regions}) fall outside the profile areas.',

  'score.value.no_limits': 'The profile declares no contract value limits.',
  'score.value.unknown': 'Contract value unknown; scored neutrally.',
  'score.value.within_band': 'Estimated value {value} is within the profile band.',
  'score.value.below_minimum': 'Estimated value {value} falls below the profile minimum of {limit}.',
  'score.value.above_maximum': 'Estimated value {value} exceeds the profile maximum of {limit}.',

  'score.deadline.none': 'No submission deadline stated.',
  'score.deadline.days_left': '{days} day(s) until the {date} deadline.',
  'score.deadline.passed': 'Deadline {date} has passed.',

  'score.gate.deadline_passed': 'Submission deadline passed on {date}; the tender can no longer be bid.',
  'score.gate.cancelled': 'The notice has been cancelled by the buyer.',
  'score.gate.not_biddable': 'Notice type {type} announces a result or an intention rather than an open tender.',

  'score.warn.value_withheld': 'The buyer withheld the contract value, so value fit could not be assessed.',
  'score.warn.value_unstated': 'The notice states no contract value, so value fit could not be assessed.',
  'score.warn.deadline_unstated': 'The notice states no submission deadline, so time to bid could not be assessed.',
  'score.warn.deadline_imminent': 'Only {days} day(s) left to submit; preparation time is very short.',

  'error.card': 'This card failed to render',
}

/** Exported so a test can assert the two tables have not drifted apart. */
export const dictionaries: Record<Lang, Record<StringKey, string>> = { fi, en }

export type Translate = (key: StringKey, params?: Record<string, string | number>) => string

export interface LanguageValue {
  lang: Lang
  setLang: (lang: Lang) => void
  t: Translate
}

export const LanguageContext = createContext<LanguageValue | null>(null)

export function readStoredLang(): Lang {
  const stored = localStorage.getItem(STORAGE_KEY)
  return stored === 'en' || stored === 'fi' ? stored : 'fi'
}

export function useLanguage(): LanguageValue {
  const value = useContext(LanguageContext)
  if (!value) throw new Error('useLanguage must be used inside a LanguageProvider')
  return value
}

/** Convenience for components that only need the lookup. */
export function useT(): Translate {
  return useLanguage().t
}
