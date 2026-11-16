# Hypa Story (tickets 3A-3C, 3F, 3K)

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
| **Q** | Raise a hand above your head | Ask; **Done** / **Skip** = done talking / skip the line being said |
| **T**, after the story ends | Pinch **Try Hypa Translate** | Opens Hypa Translate in the story panel (see [Try Hypa](#try-hypa)); Esc / **x** closes it. The corner button becomes **Speak** |
| 1-3, suggestion buttons | Pinch a suggestion | Ask a suggested question, or translate a suggested word |
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

## In my room (ticket 3K): the campfire in your real room

On a Quest 3, the fire and the storyteller come into your own room, whichever story is playing. Pinch **In my room** (under the
player panel; editor key **R**). The same button sits under the panel of the game after a story (Face Dodo, Turn the hat). The sky,
trees, ground, trails, mist, rain and fog fade out through the warm veil, the headset's passthrough shows your room, and the fire
(its stones, flames, light, sound and embers), the storyteller, and the game around the fire stand on your real floor. Pinch
**Back to the forest** (or **R**) to return. The story, the voice and the fire's sound carry on through both: only the world around
the fire changes.

- **Where the fire goes (Scene API / MRUK):** `Quest/Mruk/QuestRoomScan` reads your scanned room: the floor height, and for any
  floor point how far it is from the nearest wall or furniture (checked at knee, table and head height). `Core/RoomSpot` (pure
  maths, tested) picks an open spot a few metres ahead of you with room round the fire (0.65 m) and the storyteller (0.5 m), you,
  the fire and the storyteller in a line as at the camp. **Small rooms:** the gap between the fire and the storyteller shrinks from
  2.1 m (the forest) down to 1.1 m before a spot is rejected.
- **No room scan:** Meta's own room set-up flow is started (you scan, then come back) and the app waits. If you don't, or you
  decline the room permission, or nothing in the room is open, the fire goes 2 m straight in front of you, with a line saying why.
- **Real things hide it (Depth API):** `Quest/QuestRoom` turns on `EnvironmentDepthManager` (soft occlusion, Quest 3 and later). The
  flame, glow and ember shaders (`StoryFlame`, `StoryAdditive`, `StoryParticles`) carry Meta's `HARD/SOFT_OCCLUSION` test, and in
  the room the storyteller, the stones and the logs draw with Meta's `OcclusionSurfaceLit` (`StoryAssets.depthLit`). A real chair,
  table, sofa or you in front of them hides them. Outside the room mode nothing changes (no keyword, no depth). The panels are never
  hidden.
- **The mood stays:** the fire's light, the fire and story sound, the thunder and the embers stay. Rain (falling and its sound),
  mist, fog, the moon, fireflies and motes go (they would drift through your walls).
- **You walk for yourself:** in the room the thumbsticks don't move the fire, the walk limits are off, and the trail panels stay.
  A place button (the hut, the village) puts the forest back first; leaving the camp does too.
- **Seams:** `Core/StoryRoom` holds the headset hooks (`Scan`, `Depth`, `Passthrough`), set by `HypaStories.Quest` (Depth,
  `HypaCore.Quest/PassthroughKeep.Active`) and `HypaStories.Quest.Mruk` (needs the MR Utility Kit). Without them the camp still
  goes into the room, fire in front, no hiding. `CampWorld.SetRoom` is the scene side.
- **Needs** a Quest 3 with the room permission (`USE_SCENE`; the manifest has it) and passthrough (kept on by `PassthroughKeep`).
  Re-run **Hypa > Stories > Create Story Scene** once if the **StoryAssets** asset doesn't show `depthLit`.

Tested: the spot finder (`RoomTests`: open room, small room, furniture, no spot, deterministic) and the scene side (hiding,
restoring, material swap) in the editor. **Not tried on a headset** (none here): the scan, the depth hiding and the passthrough look
need a Quest 3 pass.

## Try Hypa

When a story ends, **Try Hypa Translate** appears in the story panel. It opens in that same panel. The story's language is the
source. **To Yoruba** (or whichever language you last chose) cycles through the story languages. Three words from the story sit
where suggested questions do: tap one and it is translated, then spoken. **Speak** on the corner button listens, translates what
you said, and speaks that too. **Hear** plays the translation again. Translation and speech use Hypa's auto router (`model: auto`
on `translate_message` and `text_to_speech`).

Code: `Core/TryHypaFlow`, `Core/TryHypaText.Suggest`, `Ui/TryHypaView` (drawn inside `PlayerView`), `Ui/TryFonts` (device fonts so
Yoruba tone marks, Igbo dots and Hausa hooks can be drawn; the story fonts are Latin-1 only). Unit tests: `TryHypaTests`.

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
- Typed questions: replaced by suggested-question buttons in VR. Try Hypa takes a tap on a word, or your voice.
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
