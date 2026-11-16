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
| `translate_image` | `image` (base64 with `imageType: "base64"`, or a URL), `targetLang`, `sourceLang?`, `model?`, `translationModel?` | `text`, `translation`, `language` (string or null), `noTextFound`, `provider?`, `model?` (text only, no positions) | paid |

| `speech_to_text` | `audio` (base64, or a URL with `audioType: "url"`), `audioType?`, `sourceLang?` (label; omit = auto), `model?` | `transcript`, `provider?`, `model?` | paid |

The `speech_to_text` row was confirmed with a keyed call on 2026-11-14 (see below). Stories sends 16 kHz mono 16-bit WAV.

The `translate_image` row is from `tools/list` (2026-11-14). `translate_message`, `text_to_speech` and `translate_image` take an optional `model`: omit or `"auto"` = best-scoring model for the language (with fallback), `"default"` = catalog default, or a `provider/model` id. The app always sends `"auto"`.

Other tools listed: `translate_document`, `process_voice_message`, `extract_text_from_image`, `list_voices`, `list_models`, `word_of_the_day` and general-purpose tools.

## Verified with the dev key (2026-11-14, Stories 3I)

### Chat: `POST /v2/developer/chat` (REST, header `x-api-key`)

The locals' brain (`HypaRestClient.ChatAsync`). **`provider` and `model` are required**; without them:
`400 {"message":["model must be a string","provider must be a string"],"error":"Bad Request"}`.

```json
POST https://api.hypaintelligence.com/v2/developer/chat
{ "messages": [ { "role": "system", "content": "You are Ama, a weaver who lives in a small village hut. Stay in character. Answer in 2 short sentences." },
                { "role": "user",   "content": "Hello! What are you making?" } ],
  "stream": false, "provider": "meta", "model": "muse-spark-1.3", "top_p": 1 }
```

Reply: **HTTP 201**, `text/event-stream` even with `stream: false`:

```
id: 1
data: {"choices":[{"message":{"role":"assistant","content":"Welcome to my small hut, traveler. I am weaving a soft blanket."},"finish_reason":"stop"}],
       "usage":{"completion_tokens":310,"prompt_tokens":45,"total_tokens":355,"completion_tokens_details":{"reasoning_tokens":286}},
       "model":"muse-spark-1.3","provider":"meta"}
```

- Errors can come as an SSE **`event: error`** with a plain-text `data:` (e.g. `Hypa is currently experiencing high traffic. Please try again in 5 minutes.`), not JSON.
- Model ids are the `provider/model` names from `list_models` (free), split at the first `/`. Chat serves fewer than `list_models`
  lists: `openai/gpt-4o-mini`, `openai/gpt-5-nano` and `meta/muse-glimmer-30b` → `400 {"message":"The selected model is invalid or unavailable…","code":10002}`.
- OpenAI models reject the provider's default sampling: send `top_p: 1` (always sent).
- Gemini puts a `<think>…</think>` block before the answer: stripped by the client.
- `reasoning_effort` made no difference to Muse. `stream: true` gives OpenAI's first tokens ~0.4 s sooner; Muse's arrive at the end anyway.

| Brain (`LocalBrains`) | provider / model | Chat time (in character, 2 sentences) |
| -- | -- | -- |
| **Muse** (default) | `meta` / `muse-spark-1.3` | 2.2–4.3 s (most of it hidden reasoning) |
| OpenAI | `openai` / `gpt-5.6-luna` (with `top_p: 1`) | 1.9–2.3 s |
| Gemini | `google` / `gemini-3.5-flash` | ~4 s |
| Hypa | `hypaai` / `Hypa-Llama3.1-8b-SFT` | not measured (high-traffic error at the time) |

### Speech, measured for the 3-second answer target

| Step | Default | Faster option used by locals |
| -- | -- | -- |
| `speech_to_text` (one short question) | 3.4–3.7 s (`openai/gpt-4o-transcribe`) | **1.5 s** with `model: "openai/gpt-4o-mini-transcribe"` (same transcript) |
| `text_to_speech` (one sentence) + download | 2.8 s + ~1 s for the `audioUrl` | **2.2 s** with `model: "openai/gpt-4o-mini-tts"` and `format: "base64"` (no download) |

`speech_to_text` reply: `{"transcript":"What are you weaving today?","provider":"openai","model":"gpt-4o-transcribe"}`.
`list_voices` (free) gives the voices per model; `openai/gpt-4o-mini-tts`: alloy, ash, ballad, coral, echo, fable, onyx, nova, sage, shimmer, verse, marin, cedar.

## Not yet verified (needs a keyed call)

- Paid tool responses end to end (translation quality, `mime` per voice, whether Quest decodes both WAV and MPEG).
- Auto-detect quality on one-word inputs. `translate_message` returns `language: null` when it can't tell; the Lens then asks the user to pick a source.
- REST `/v2/developer/{mt,tts,asr,chat,dictionary,ocr}` request/response field names (header `x-api-key`). `HypaRestClient` marks its typed methods unverified.
- Which languages support ASR (`list_languages` doesn't say). Stories offers English, French and Spanish.
- A chat / Q&A tool: none on MCP (tools/list, 2026-11-14). REST `chat` is now verified (above); the campfire storyteller's Q&A still uses its stub.
- Error bodies: 401 bad key, 400 `INSUFFICIENT_BALANCE`, 429, 503 (keyless allowance). Mapped in `HypaException.FromHttp`; confirm the exact shapes.

## Rules that follow from it

- `extract_text_from_image` returns text only, with **no positions** (output `text`, `provider?`, `model?`; re-checked via tools/list 2026-11-12), and needs an image upload. The Lens doesn't use it. **Capture** (pinchable words) reads text on the headset with Google ML Kit and never uploads frames.
- **Caption** is the one feature that uploads a frame: one JPEG of the view goes to `translate_image`, only after the user says yes on a one-time consent card (`LensPrefs.CaptionConsent`). The photo is not stored on the headset.
- Keyless calls run on a shared, capped anonymous budget. Only the free tools are called without a key; paid tools require a key (or the judges' proxy via `baseUrl`).
- Audio links expire after about an hour: play them, never save them. Saved words re-request audio.
