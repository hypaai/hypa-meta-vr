# Hypa Story (tickets 3A-3C)

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
| 1-3, suggestion buttons | Pinch a suggestion | Ask a suggested question (no microphone or no key) |
| Esc, **Back** | Pinch Back | Cancel the question / back to the list / back to start |

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
| `stories.js`, `story-kiko.js`, `story-marlow.js` | `Data/StoryLibrary` (same text, timings, translations, weather). |
| Story player panel | `Ui/PlayerView`: script with the current line's words lighting up with the voice, progress, `<<` / Pause / `>>`, Ask. |
| `storyteller.js` | `World/Storyteller`: seated pose, breathing, talking gestures, head to the fire / to you on interrupt, `mouthOpen` lip sync from the voice. Stand-in figure without the model. |
| `audio.js` | `Audio/StoryAudio` (beds per scene, spatial fire, ducking under the voice, random wolves and crickets, storm, thunder, chime, tap) and `Audio/ProceduralClips` (the synthesised sounds, filtered offline). |
| `ask.js` | `Core/AskFlow`: interrupt → listen → filler (second after 2 s) → answer sentence by sentence → "Now, where was I…" → restart the interrupted line. Same edge cases and timing log. |
| `speech.js` `listen()` / `speak()` | `Audio/MicListener` (mic, silence detection, **Hypa `speech_to_text`**) and `Audio/StoryVoice` (**Hypa `text_to_speech`**, cached, prefetched, from the storyteller's mouth). |
| `qa.js` | `Core/StoryQa`: `IStoryQa` seam + the same spoiler-guarded stub. |
| `public/languages.json` | `Data/StoryLanguages`. |
| Swipe up / `H` to raise a hand | `Core/RaiseHand` + `Quest/QuestStoryInput` (tracked hand or controller above the head, held 0.35 s; thumbsticks walk and snap-turn). |

## Hypa API use

- **TTS** (`text_to_speech`, verified schema): every line the storyteller says: the narration of both stories in every language,
  fillers, answers. Lines are cached for the session and the next line is fetched while the current one plays.
- **ASR** (`speech_to_text`, schema read from `tools/list` on 2026-11-13, not yet called with a key): 16 kHz mono WAV, the story
  language as `sourceLang`. Nothing is sent if nothing louder than the noise floor was heard.
- **Chat:** Hypa's MCP endpoint has no chat tool, and the REST `chat` endpoint is unverified, so answers still come from
  `StubStoryQa` (as in the prototype). Implement `IStoryQa` on the real endpoint and pass it to `AskFlow` in `StoryApp`.
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
