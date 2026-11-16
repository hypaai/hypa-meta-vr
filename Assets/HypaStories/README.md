# Hypa Story (tickets 3A-3C, 3F)

The Unity / Quest port of the Three.js ideation prototype (`Ideation/`, "Hypa Stories: Three.js visual ideation prototype").
Same three screens, three stories (the prototype's Kiko is replaced by the Hausa folk tale *Seven Hot Stones*, and *Eshu at the Crossroads* is a third card), same interrupt-and-ask loop, rebuilt for a headset: real scale, look anywhere, no camera
moves, pinch instead of click. Uses HypaCore only and never references HypaLens.

## Run it

The scene carries its own Meta camera rig (OVRCameraRig + tracked hands, like the Lens scene), so it runs in VR on its own.
Under the shell, the shell's rig is used instead (`StandaloneOnly`).

1. **Hypa > Stories > Create Story Scene** (rebuilds the scene and adds it to Build Settings).
2. Open `Assets/HypaStories/Scenes/HypaStory.unity` and press Play:
   - **Meta XR Simulator** (runtime enabled) or **Quest Link**: you are in the headset view; use controllers or hands.
   - **Neither**: the same scene on screen with the mouse and keyboard below.
3. On a Quest: Build and Run with HypaShell first in Build Settings, then **Menu > Practice > Hypa Story**; or move HypaStory
   to the top of Build Settings to start straight in it.

| Input (editor) | Quest | Effect |
|---|---|---|
| Right mouse drag | Head | Look around |
| WASD | Left thumbstick | Walk around the camp (right thumbstick: snap turn). Panels glide back in front of you when you turn or move |
| Click the fire / **Gather round**, Enter | Pinch the fire | Mode select → story list |
| Click a card, arrows, `<` `>`, Enter | Pinch | Focus a card, **Begin story** |
| Language buttons | Pinch | Narration, questions and answers in English / Français / Español |
| Space, ← → | Pause, `<<` `>>` | Pause / resume / replay, previous / next line |
| **H** | Raise a hand above your head | Interrupt and ask |
| **Q** (tap or hold), **Ask** (tap or hold) | Pinch Ask (hold = push-to-talk) | Ask; tap again = done talking / skip the line being said |
| 1-3, suggestion buttons | Pinch a suggestion | Ask a suggested question (about the lines told so far) |
| Esc, **Back** | Pinch Back | Cancel the question / back to the list / back to start |

## Places (ticket 3F): camp, hut, gathering

`Places.GoTo("camp" | "hut" | "gathering")` (`Runtime/Core/Places.cs`) fades through the warm veil, switches place and fades back.
Each place is its own scene: `HypaStory` (the camp), `HypaStoryHut`, `HypaStoryGathering`.

- **The camp is home.** It stays loaded but hidden while you're away (`Places.Leaving` / `Places.Arrived`): the story pauses
  when you leave and picks up where it was when you come back; its sound stops and comes back with it. Other places load
  additively and unload when you leave them.
- **Where you stand:** each place scene has one `PlaceEntry` (position on the floor, blue arrow = the way you face). Arriving
  moves the place's roots so the entry is under your head, facing the way you face. The camera never moves.
- **Walking:** inside a place you walk and turn as at the camp (left thumbstick / WASD, right thumbstick snap-turns).
  `PlaceWalk` (added by the `PlaceEntry`) moves the place, never the camera, and keeps you within `WalkRadius` of
  `WalkCenter` (set both on the entry to fit your room) at a sensible eye height.
- **One rig, one audio listener:** a place scene's own rig (`StandaloneOnly`) is removed under the shell *or* when another
  rig is already running (the campfire's). Played on its own, a place keeps its rig alive across moves. If the shell unloads
  the camp, any place scenes go with it.
- **Build a place:** call `PlaceSceneBuilder.CreatePlaceScene(HypaScenes.StoryHut, root => { ... })` from your own menu item
  (`Editor/PlaceSceneBuilder.cs`). It makes the rig, the `Place` root with the entry at the origin facing +Z, the one
  real-time light (no shadows), saves the scene and adds it to Build Settings. Build the place around the entry under `root`.
- **Stand-ins (3G/3H/3M start here):** **Hypa > Stories > Create Place Scenes (stubs)** makes `HypaStoryHut` and
  `HypaStoryGathering` with a floor and a sign with pinchable doors back. **Hypa > Sync Build Settings** includes them.

| Editor | Quest | Effect |
|---|---|---|
| **1** / **2** / **3** | | Camp / hut / gathering (while a story plays, 1-3 still pick a suggested question) |
| WASD, right mouse drag | Left thumbstick, right thumbstick | Walk and turn inside the hut or the gathering, as at the camp |
| Click a light button | Pinch a light button | Same row in the camp, the hut, and the village. The solid button is the place you are in. A light button takes you there. **Back** returns to the Hypa home menu. |

Done: the `Places` and `PlaceEntry` API, the switcher with fade and pause/resume, one rig after any number of moves, stub
scenes, the builder helper, editor keys, EditMode tests (`PlacesTests`). Not done: the real hut and gathering (3H, 3M), the
portal (3L).

## Locals: talk to someone who lives here (ticket 3I)

A `Local` (`Runtime/Core/Local.cs`) is anyone you can talk to. Come close and look at them: they greet you. **Raise a hand**
(or pinch **Talk**, or **T** in the editor) → they listen (Hypa `speech_to_text`) → think (Hypa chat, in character) → answer
sentence by sentence (Hypa `text_to_speech`) with lip sync and subtitles, looking at you. A filler line starts at once and a second
one after about 2 s while they think. They remember the last 4 exchanges of the visit; walk away and the visit ends.

- **Try it:** the camp seats a test local, **Ama the weaver** (`Locals/Ama.asset`, from Kiko's village), in the firelight
  front-left of where you start; an ember ring glows under her when you can talk to her. Without a microphone or key, pinch one
  of her suggested questions.
- **Knowing she hears you:** a soft ding, her panel glows ember with *Listening… speak now*, and a voice meter bounces with your
  voice (if it doesn't move, the microphone isn't hearing you). When you stop: a lower ding and *You said: "…"* with exactly
  what she heard. Nothing heard: *I didn't hear anything. Is your microphone on?* While she listens, the world's sound drops to
  15% (the camp's wind and fire from the speakers otherwise reach the microphone and drown you out); the listening cue is
  ignored by the microphone for its first 0.35 s. The console says which microphone she uses. **In the editor on a Mac**, allow
  Unity in System Settings > Privacy & Security > Microphone.
- **Wake word (Meta Voice SDK):** near a local, say their name: "Hey Ama." and she listens; "Hey Ama, what do you weave?" and she
  answers straight away. Their role works too ("Hey weaver, ..."), and Wit always spells that right. The Voice SDK's dictation
  (`Runtime/Voice/VoiceSdkWakeListener.cs`, its own assembly) starts the first time a local wants it, runs only while you're within
  talking distance of an idle local, and stops before her microphone opens; when you only say the name, the question still goes
  through Hypa `speech_to_text`. The name must be the first word (after "hey", "excuse me"...). Wit rarely spells an unfamiliar
  name right ("Ama" came back as Anna, Emma, Amma, Amer, "Hayama"), so a word that sounds the same counts (`WakeWord.Sound`); add
  other names in the persona's `WakeNames`. Tested in the editor, spoken aloud: all three ways worked, first audio 6.4–7.4 s in
  English. Wit sometimes drops the end of the last word ("what do you we"); the brain still understood. Dictation streams audio to Wit.ai, so it
  needs a free Wit client token as `WIT_CLIENT_TOKEN` in `.env` (see `.env.example`; on Quest, the pushed `.env`). No token: the
  console says the wake word is off, and raise a hand / Talk work as before. Uncheck `WakeWordEnabled` on a `Local` to turn it off.
- **Add a local to your place (3H, 3M):** add a `Local` to the character, give it a `LocalPersona` (Assets > Create > Hypa >
  Stories > Local Persona: name, role, home, what they know and don't know, voice, greeting, 3 suggested questions, offline
  answers) and `StoryAssets`. Optional: `Figure` (a Storyteller: lip sync and head turn), or `Head` and `Mouth`.
- **The brain (one setting, no code change):** **Hypa > Stories > Local Brain**: Muse (default, `meta/muse-spark-1.3`), OpenAI
  (`openai/gpt-5.6-luna`), Gemini, Hypa, or offline answers. In code, `LocalBrains.Model`; per local, its `Brain` field. Any
  `provider/model` id Hypa's chat serves works. Calls use the dev key or the judges' proxy (`baseUrl`); no key in the app.
- **Never stalls:** no key → the persona's canned answers (`StubLocalBrain`); chat fails or is busy → the canned answers take over
  for that question (`FallbackBrain`; the log shows `meta/muse-spark-1.3→stub`). Out-of-world questions get an in-character "I
  wouldn't know".
- **Timing log:** each question logs `Hypa Stories local Ama Q&A …` with the filler, ASR, first sentence, first audio and playback
  times (same as the campfire's `AskFlow`).

**Measured** (picked question, so no speech-to-text; in the editor): filler 8–39 ms; Muse's first sentence 2.2–4.3 s; first
audio **5.0–6.3 s** with the faster speech settings (8.2 s with Hypa's defaults). Speaking adds speech-to-text: ~1.5 s with
`openai/gpt-4o-mini-transcribe` (the `Local` default; Hypa's default took ~3.5 s). So a spoken question gets its answer in about
6.5–8 s with Muse, and about 2 s sooner with the OpenAI brain. **The ticket's ~3 s isn't reachable with today's API timings**: the
filler covers the wait. Details in `docs/api-contract.md`.

Done: real chat call documented, `Local`, `LocalPersona`, `ILocalBrain` (chat, stub, fallback), the brain setting and menu,
in-character prompt with visit memory, faster speech models, the test local, the wake word (Meta Voice SDK), EditMode tests
(`LocalTests`, `ChatTests`).
Not done: the 3 s target (see above); answers stream sentence by sentence only after the whole reply arrives (chat streaming
saved ~0.4 s at best); canned offline answers are English; the wake word is tested in the editor only, not yet on the Quest.

## What came across from the prototype

| Prototype (`Ideation/src`) | Here |
|---|---|
| `flow.js` states + GSAP timelines | `StoryApp`: splash → list → play, coroutine tweens. Transitions fade through a warm veil and **move the world, not the camera** (the prototype's dolly is a comfort risk). |
| `environment.js` world | `World/CampWorld`: sky dome (look anywhere), far treeline cards all round, pines, Poly Haven props, ground, fire and moon light pools, ambient fill + moon + fire + lightning lights, two mist bands, particles, rain, storyteller. Real-world scale, fire at the origin. |
| `fire.js` + flame shader | `World/StoryFire` + `Shaders/StoryFlame`: six crossed cards, swirl, halo, jittering colour-shifting point light (no shadows). |
| EZ-Tree pines | `StoryMeshes.Pine`: low-poly vertex-coloured pines, wind sway in `StoryPine`. |
| `shaders.js` (sky, mist, particles) | `Shaders/StorySky` (360° with moon, stars, flash), `StoryMist`, `StoryParticles` (fireflies / motes / embers, all motion on the GPU, one draw each). |
| `rain.js` | `Shaders/StoryRain`: camera-following streaks (quads, not 1 px lines). |
| `card.js` | `Shaders/StoryCard` + `Ui/StoryListView`: same art (lantern pines, noir alley), ripples from the pinch ray's hit point, tilt, press burst, reveal, dim. Text is real TextMesh on the card. |
| `director.js` | `Core/StoryDirector`: pure function of the story clock (captions, rain, wind, lightning, thunder). |
| `stories.js`, `story-kiko.js`, `story-marlow.js` | `Resources/Stories/` data files, loaded by `Data/StoryLibrary` (Marlow: same text and translations; Kiko replaced by `seven-hot-stones`; see Stories). |
| Story player panel | `Ui/PlayerView`: script with the current line's words lighting up with the voice, progress, `<<` / Pause / `>>`, Ask. |
| `storyteller.js` | `World/Storyteller`: seated pose, breathing, talking gestures, head to the fire / to you on interrupt, `mouthOpen` lip sync from the voice. Stand-in figure without the model. |
| `audio.js` | `Audio/StoryAudio` (beds per scene, spatial fire, ducking under the voice, random wolves and crickets, storm, thunder, chime, tap) and `Audio/ProceduralClips` (the synthesised sounds, filtered offline). |
| `ask.js` | `Core/AskFlow`: interrupt → listen → filler (second after 2 s) → answer sentence by sentence → "Now, where was I…" → restart the interrupted line. Same edge cases and timing log. |
| `speech.js` `listen()` / `speak()` | `Audio/MicListener` (mic, silence detection, **Hypa `speech_to_text`**) and `Audio/StoryVoice` (**Hypa `text_to_speech`**, cached, prefetched, from the storyteller's mouth). |
| `qa.js` | `Core/ChatStoryQa` (the story's chat model) behind the `IStoryQa` seam in `Core/StoryQa`, which keeps the spoiler-guarded stub for tests and offline runs. |
| `public/languages.json` | `Data/StoryLanguages`. |
| Swipe up / `H` to raise a hand | `Core/RaiseHand` + `Quest/QuestStoryInput` (tracked hand or controller above the head, held 0.35 s; thumbsticks walk and snap-turn). |

## Stories

Each story is a folder in `Resources/Stories/{id}/`, listed in card order in `StoryLibrary.Ids`:

- `story.json`: the card (`theme`, `accent`, `eyebrow`, `title`, `description`), the `models` that answer questions
  (`"provider/model"`, in order: every question starts with the first, and a model that fails hands the same question to the
  next at once, within the 12 s the storyteller waits; one the service says it doesn't have is skipped for the session. All three
  stories use Meta's `meta/meta-models/Muse-Spark-Open-816B-A42B:baseten`, then `google/gemini-3.5-flash`, then
  `meta/meta-models/Muse-Glimmer-30B:deepinfra`, then `meta/muse-wonder-1.0` (last until the service can reach it again); an older single
  `model` still works), the storyteller's `voices` (`[{ "model", "voice" }]`, as Hypa
  `list_voices` names them; `voice` is one name for every language or `{ "language code": name }`, since Hypa's voices are per
  language; each language uses the first entry with a voice for it, and the next ones are its fallbacks; empty = Hypa's Auto
  routing, which both stories use for now), the starting `rain` /
  `wind` (0..1), and the English `lines`, each
  `{ "id", "text" }` with optional `ask` (questions to suggest once the line is told) and cues: `rain` / `wind` (reached by the end
  of that line) and `flashes` (lightning, seconds after the line starts). Also reached by the end of their line: `fire` (the
  campfire's strength, 1 as it is: below shrinks and gutters, above roars up), `dark` (a deeper night), `dawn` (daybreak: the sky
  warms, the stars fade) `police` (red and blue lights) and `spirit` (a god's light: the fire's glow swings from deep red to pale white). And `sounds`: `{ "roar": [0.4], "door": [0.2, 0.9] }`, seconds after the
  line starts, by name from `StoryAudio.Effects` (roar, groan, stomp, fall, door, sizzle, sparks, throw, gulp, neigh, gallop,
  rooster, heartbeat, cheer, drums, siren, clink, bricks, steps, laugh, rattle, birds), all made in code; a hot stone, sparks or a throw also flare the
  fire, a roar, stomp or slammed door makes it gutter. When the night gets deep (`dark` above about 0.4), pairs of eyes open one by one
  in the trees round the clearing, blink and watch you, and close as it lifts. A test checks every name a story uses.
- `{code}.json` per translation (`fr`, `es`, `ha`, `ig`, `pcm`, `sw`, `yo`): `{ "line id": "text", "line id.ask": ["question"] }`. A missing
  file or line is told in English, and a test lists every untranslated line of the offered languages. **The Hausa, Igbo, Pidgin and
  Yoruba scripts (and their storyteller lines in `StoryLanguages`) are machine translations (GPT-6 Astra via Hypa, given the whole
  story): have a native speaker check them before release.** *Seven Hot Stones*: the English and Yoruba were supplied by the team
  (the Yoruba with corrections where it contradicted the English); French, Spanish, Hausa, Igbo and Pidgin are machine translations
  (Claude) and need the same check. *Eshu at the Crossroads* was written in English; French, Spanish, Hausa, Igbo, Pidgin and Yoruba come from Hypa's auto router (`translate_message`, model `auto`), sent a few numbered lines at a time with a short summary of the story for context, then checked by hand: lines the router left in English or shifted were sent again, and a few words fixed (Hausa: Eshu is *ƙaramin abin bauta*, not *Allah*; palm wine is *giyar kwakwa*). They need the same native check. Swahili (`sw`, shown as Kiswahili) for all three stories also comes from that auto router and needs the same native check.

When a listener asks, the suggestions are the `ask` questions of the lines told so far, newest first (three at most).

Lines have no times: the storyteller's TTS paces the story, so each line's slot is estimated from its English word count. To add a
language, add it to `StoryLanguages.All` (with its storyteller lines) and a `{code}.json` to each story; if it has letters the fonts
lack, add them to the builder's `Charset` and run **Hypa > Stories > Update Fonts** (Noto Sans fills in what Outfit and Cormorant don't have). Keep line ids stable when editing a script: they are what
the translations are matched on.

## Hypa API use

- **TTS** (`text_to_speech`, verified schema): every line the storyteller says: the narration of both stories in every language,
  fillers, answers. Both stories use Hypa's Auto routing (no `voices`): hypa-v2 ranks the speech models for the language by its
  benchmarks, uses the top model's first voice and fails over to the next model on the server (on 2026-11-14: English OpenAI `alloy`,
  Spanish Google `Achernar`, French / Hausa / Igbo / Pidgin / Yoruba Hypa Orpheus `Adaugo` / `Juliet` / `Amarachi` / `Prince` /
  `Eniola`; 2.7-8 s a line). A story's `voices` pins voices instead (tried in order, then Auto). Meta has no TTS. Lines are cached for
  the session and the next line is fetched while the current one plays; a line not ready after 12 s is read silently. Auto can't
  take a chosen voice over MCP (hypa-v2 needs the voice's model with it; REST `/v2/developer/tts` can).
- **ASR** (`speech_to_text`, schema read from `tools/list` on 2026-11-13, not yet called with a key): 16 kHz mono WAV, the story
  language as `sourceLang`, model picked by Hypa's auto-router. Nothing is sent if nothing louder than the noise floor was heard.
- **Chat** (REST `/v2/developer/chat`, `HypaRestClient.ChatAsync`, not streamed): `ChatStoryQa` sends the storyteller persona, the
  story so far up to the interrupted line (in the listener's language) and the question to the story's `model`, which answers in that
  language in 2-3 sentences without spoilers. The reply is spoken sentence by sentence; over 12 s counts as offline. Muse Wonder
  took 2.6-4.2 s in English and French (it reasons before answering, even with `thinking: false`). StoryApp's `qaMode` switches to
  the stub (`stub`, `slow`, `offline`). Locals (3I) use the same chat call with their own brain setting.
- Without a key, the story still runs: lines are "spoken" silently for about their length (subtitles keep time), and questions
  use the suggestion buttons.

## Assets and licences

- Committed: shaders and code (original), Poly Haven props and forest floor (`Art/PolyHaven`, CC0), Camp Fire by JalenJacobson
  (`Art/Models/camp_fire.glb`, **CC BY 4.0, credit required**, re-encoded without meshopt / WebP so glTFast imports it), the Hypa
  Reality mark (`Art/hypa-reality.png`, team-supplied).
- **Not committed** (`Assets/HypaStories/Local/`, git-ignored, because this repo is public): the Epidemic Sound beds
  (`amb-1/2`, `fire-1/2`, `wolf-1`; subscription licence, no redistribution) and the storyteller avatar
  (`Models/storyteller.glb`; a Ready Player Me placeholder from the three.js examples).
  Copy them from `Ideation/public/{audio,models}` into `Local/Audio` and `Local/Models`, then rerun **Create Story Scene**.
  Without them: synthesised wind and fire and a stand-in storyteller.
- Before release: replace the avatar with one from the team's own Ready Player Me account (or any humanoid with `Hips`/`Spine`/
  `Head`/`LeftArm`… bones and a `mouthOpen` blend shape).

## Not ported (yet)

- The alley world (`alley.js`): the prototype keeps it for later too; both V1 stories are told at the campfire.
- The "Voice" cue toggle (say "pause" / "question"): needs an on-device wake-word recogniser (Meta Voice SDK); raise a hand instead.
- Typed questions: replaced by suggested-question buttons in VR.
- Loader, grain, bloom, adaptive quality, debug panel: desktop-preview features. On Quest use OVR fixed foveation and the
  Immersive Debugger; the Q&A timing log goes to the console (and the player's note line in the editor).
- An Animator with Idle / Talking clips for the storyteller (the bones are driven directly, as in the prototype without clips).

## UI

Built from a small kit (`Ui/Kit.cs`): rounded glass panels drawn with a distance field (`Shaders/StoryUIPanel`), Lucide icons,
TextMeshPro text in Outfit (interface) and Cormorant Garamond (story), ember accents, hover lift and glow. Panels use a lazy
follow (`FollowView`): they stay put while you glance around and glide back in front of you when you turn away or walk.
The player sits just below eye level so the storyteller stays in view above it.

## Quest performance notes

No shadows, no post, one real-time point light, two mist bands, halved particle counts, 3-octave noise. Still to measure on a
headset: the glTF props (grass and rock sets have many sub-meshes; merge or instance them if draw calls are high), the card shader
(full-screen-ish fbm on two cards), and the fire light's per-pixel cost.

## The Dodo game (after *Seven Hot Stones*)

When *Seven Hot Stones* ends, **Face Dodo** appears above the player panel: you are Auta. The night turns blood red and Dodo
(Quaternius' Demon, CC0, `Art/Models/dodo_demon.glb`: recoloured, glowing eyes, halo and trident hidden) bursts out of the trees.

- **Heat the stones:** pinch each of the seven stones on the slab by your seat and drop it in the fire (or point at a stone from
  afar and pinch: it flies to your hand). Each flares the fire and glows red hot.
- **Answer:** Dodo roars "Who is able to fight with me?" (the storyteller says the line in your language). Shout back "I am!":
  any loud answer counts (`ShoutDetector`, no speech-to-text), or pinch the **I am!** button.
- **Throw:** Dodo stoops at the fire with its mouth open (a ring of fire marks it, shrinking as time runs out). Grab a hot stone
  and throw. A throw roughly toward the mouth curves in; letting go while pointing at it lobs the stone there.
- **Three lives:** a stone that misses, being too slow, or not answering twice costs a life (Dodo steps closer). The third ends
  it: **Try again**, **The story** or **Stories**. **Back** during the fight returns to the end of the story.
- **Seven stones swallowed:** Dodo falls, dawn breaks, and the villagers come out of the trees to dance round the fire.

Hands first; controllers work the same way (trigger = pinch). Rules: `Core/DodoRules` (unit tested). Game: `World/DodoGame`,
creature: `World/DodoCreature`. In the editor: **P** puts a stone in the fire, **T** throws into the mouth, **M** throws wide,
**Space** shouts.
