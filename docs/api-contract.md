# Hypa API contract used by this app

Derived only from **public** calls to Hypa's API (no private source). Re-check with a live call when anything looks off and update this file in the same change.

## Verified live (2026-11-10, keyless, free tools only)

- **MCP endpoint:** `POST https://api.hypaintelligence.com/v2/mcp`, JSON-RPC 2.0 `tools/call`, stateless.
  Send `Content-Type: application/json` and `Accept: application/json, text/event-stream`.
- **Response:** `text/event-stream`, one `event: message` whose `data:` is the JSON-RPC envelope.
  `result.structuredContent` holds the typed output; `result.content[0].text` holds the same as a JSON string.
- **Rate limit headers** observed: `x-ratelimit-limit: 500`, window 60 s.
- **Languages are labels.** Tool inputs take labels such as `"Yoruba"`; `check_language_pair` also accepted codes (`"yo"`) in testing, but use labels as the schemas say.
- `tools/list` exposes `inputSchema`/`outputSchema` for every tool. The ones this app uses:

| Tool | Input | Output | Cost |
| -- | -- | -- | -- |
| `list_languages` | none | `languages[{name, code, voices[]}]` (about 150) | free |
| `check_language_pair` | `targetLang`, `sourceLang?` (omit = auto-detect) | `supported`, `models[]`, `unknown[]?` | free |
| `translate_message` | `text` (1..5000), `targetLang`, `sourceLang?` (omit = auto-detect), `model?` | `translation`, `language` (detected source, string or null), `provider?`, `model?` | paid |
| `text_to_speech` | `text` (1..5000), `language`, `voice?`, `model?`, `format?` (`url` default, `base64`) | `mime`, `audioUrl?` (valid ~1 h) or `audioBase64?`, `sampleRate?`, `voice?` | paid |
| `translate_and_speak` | `text`, `targetLang`, `sourceLang?`, `voice?`, `format?` | `translatedText`, `mime`, `audioUrl?`... (speaks the translation only) | paid |
| `look_up_term` | `word` (1..120), `language?` (omit = English) | `result` (open object), `provider?`, `model?` | paid |

Other tools listed: `translate_document`, `speech_to_text`, `process_voice_message`, `extract_text_from_image`, `translate_image`, `list_voices`, `list_models`, `word_of_the_day` and general-purpose tools.

## Not yet verified (needs a keyed call)

- Paid tool responses end to end (translation quality, `mime` per voice, whether Quest decodes both WAV and MPEG).
- Auto-detect quality on one-word inputs. `translate_message` returns `language: null` when it can't tell; the Lens then asks the user to pick a source.
- REST `/v2/developer/{mt,tts,asr,chat,dictionary,ocr}` request/response field names (header `x-api-key`). `HypaRestClient` marks its typed methods unverified.
- Which languages support ASR (`list_languages` doesn't say). Stories needs this.
- Error bodies: 401 bad key, 400 `INSUFFICIENT_BALANCE`, 429, 503 (keyless allowance). Mapped in `HypaException.FromHttp`; confirm the exact shapes.

## Rules that follow from it

- `extract_text_from_image` returns text only, with no positions, and needs a full image upload. It can't pin labels and breaks the no-full-frames rule, so the Lens doesn't use it.
- Keyless calls run on a shared, capped anonymous budget. Only the free tools are called without a key; paid tools require a key (or the judges' proxy via `baseUrl`).
- Audio links expire after about an hour: play them, never save them. Saved words re-request audio.
