# Hypa Story (tickets 3A-3C, 3F)

The Unity / Quest port of the Three.js ideation prototype (`Ideation/`, "Hypa Stories: Three.js visual ideation prototype").
Same three screens, same two stories, same interrupt-and-ask loop, rebuilt for a headset: real scale, look anywhere, no camera
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
| | Pinch **Hut** / **Gathering** on the *Places (test)* panel (left of you at the camp) | Go there (until the 3L portal lands) |
| | Pinch **Back to the camp** on a stub place's sign | Back to the camp, the story where you left it |

Done: the `Places` and `PlaceEntry` API, the switcher with fade and pause/resume, one rig after any number of moves, stub
scenes, the builder helper, editor keys, EditMode tests (`PlacesTests`). Not done: the real hut and gathering (3H, 3M), the
portal (3L); the *Places (test)* panel goes when the portal lands (`testDoors` on `StoryApp`).

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
| `stories.js`, `story-kiko.js`, `story-marlow.js` | `Resources/Stories/` data files, loaded by `Data/StoryLibrary` (same text and translations; see Stories). |
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

- `story.json`: the card (`theme`, `accent`, `eyebrow`, `title`, `description`), the `model` that answers questions
  (`"provider/model"`, default `meta/muse-wonder-1.0`), the storyteller's `voices` (`[{ "model", "voice" }]`, as Hypa
  `list_voices` names them; `voice` is one name for every language or `{ "language code": name }`, since Hypa's voices are per
  language; each language uses the first entry with a voice for it, and the next ones are its fallbacks; empty = Hypa's Auto
  routing, which both stories use for now), the starting `rain` /
  `wind` (0..1), and the English `lines`, each
  `{ "id", "text" }` with optional `ask` (questions to suggest once the line is told) and cues: `rain` / `wind` (reached by the end
  of that line) and `flashes` (lightning, seconds after the line starts).
- `{code}.json` per translation (`fr`, `es`, `ha`, `ig`, `pcm`, `yo`): `{ "line id": "text", "line id.ask": ["question"] }`. A missing
  file or line is told in English, and a test lists every untranslated line of the offered languages. **The Hausa, Igbo, Pidgin and
  Yoruba scripts (and their storyteller lines in `StoryLanguages`) are machine translations (GPT-6 Astra via Hypa, given the whole
  story): have a native speaker check them before release.**

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
  the stub (`stub`, `slow`, `offline`).
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
