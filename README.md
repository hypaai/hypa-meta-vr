# Hypa Stories VR — Judges' Setup

Hypa Stories is a hands-first Meta Quest app where a storyteller sits by a campfire and tells African folk tales aloud in your language. Raise your hand and she stops, listens and answers your question without spoiling the ending. Each story ends in a game where you play out what you just heard. On Quest 3, the campfire comes into your own room in mixed reality, and your real doors become portals to a village hut, the village and the campfire. Narration, questions and answers work in eight languages: English, Français, Español, Hausa, Igbo, Nigerian Pidgin, Kiswahili and Yorùbá.

This guide covers two ways to run it:

1. **Install the submitted build** from the Competition release channel. Nothing to set up; this is the recommended way to judge it.
2. **Build it from this repository** in Unity and run it in the editor or on a Quest.

Built for the Meta VR Start Developer Competition 2026 (Entertainment track, New division). Apache 2.0 licensed. See [LICENSE](./LICENSE).

## Table of contents

- [Install the submitted build](#install-the-submitted-build)
- [Ten-minute walkthrough](#ten-minute-walkthrough)
- [What was built during the competition](#what-was-built-during-the-competition)
- [Project components](#project-components)
- [Build from GitHub](#build-from-github)
  - [Prerequisites](#prerequisites)
  - [1. Clone](#1-clone)
  - [2. Open in Unity](#2-open-in-unity)
  - [3. Add a Hypa API key](#3-add-a-hypa-api-key)
  - [4. Run in the editor (no headset)](#4-run-in-the-editor-no-headset)
  - [5. Build and run on a Quest](#5-build-and-run-on-a-quest)
- [Troubleshooting](#troubleshooting)
- [Developer notes](#developer-notes)
- [Security](#security)

## Install the submitted build

**You need:** a Meta Quest 3 or 3S (Quest 2 and Pro run it, but slower and without the depth-based mixed reality), an internet connection, and a Meta account.

1. Open the invite link from the Devpost submission, [meta.com/s/82MgvJ09I](https://www.meta.com/s/82MgvJ09I), in a browser signed in to the Meta account your headset uses, and accept the invite.
2. On the headset, open **Library**, find **Hypa Stories** and install it. If it does not show yet, wait a minute and check **Library → Updates**.
3. Turn on hand tracking: **Settings → Movement tracking → Hand and body tracking**. Controllers also work, but no controller is ever needed.
4. Launch **Hypa Stories**. Allow the **microphone** (for asking questions) and **spatial data** (for the campfire in your room) when asked.

The submitted build needs no API key or other setup: the storyteller's voice, answers and translations come from Hypa's API.

## Ten-minute walkthrough

Every step works seated, with hands only. Pinch = touch your thumb and index finger.

1. **Home:** pinch **Stories**. **How it works** explains Stories and Lens.
2. **The campfire:** pinch the fire, or **Gather round**, to see the story cards. Pick a language with the language buttons, then pinch a card and **Begin story**. Try *Seven Hot Stones* (Hausa) or *The King's Magic Drum* (Efik, from Calabar).
3. **Ask a question:** while she tells it, **raise a hand above your head** for a moment. She stops and listens. Ask out loud, or pinch a suggested question. She answers in your language, then says "Now, where was I…" and picks the story back up.
4. **Play the story:** when a story ends, pinch the game button above the panel:
   - *Seven Hot Stones* → **Face Dodo**: heat seven stones in the fire, shout "I am!" when Dodo asks who will fight, and throw the hot stones into its mouth.
   - *Eshu at the Crossroads* → **Turn the hat**: pinch Eshu's red-and-white hat and spin it so each friend sees the other colour, before their quarrel boils over. Then carry the palm wine to them.
   - *The King's Magic Drum* → **Play the drum**: listen to the beat, then play it back on the king's two drums with your hands.
5. **Try Hypa Translate:** at the end of a story, pinch **Try Hypa Translate**. Tap a word from the story to hear it in another language, or pinch **Speak** and say a sentence of your own.
6. **In my room (Quest 3):** pinch **In my room** under the story panel. The forest fades away and the fire and the storyteller sit on your real floor, clear of your furniture. A real chair or table in front of them hides them. Pinch **Back to the forest** to return. If you have no room scan, the headset offers to make one first.
7. **Portals:** while in your room, a doorway appears on each real door from your room scan, showing the place behind it live. The sign over it picks **Hut**, **Village** or **Campfire**. Step through to go there (seated: **long-pinch** the doorway), and step back to come home. The story keeps going.
8. **The hut:** inside, pick up the clay pot, the mortar and pestle, the broom or the oil lamp with a pinch or a full-hand grab, and carry, set down or toss them. Pinch a card over an item and the old woman tells you what it is for.
9. **The village elder:** in the village, walk up to the elder (or pinch **Meet the locals** at the gate), then pinch **Narrate story** for two more tales: *The Death of the Rice Farmer* and *The Girl Who Chose Hare*.
10. **Lens (beta):** from Home, pinch **Lens (beta)**. Look at a sign, menu or book, pinch **Capture**, and read and hear the translation. Allow the headset camera when asked.

To leave: return to the campfire and select **Back** to go Home. In Lens, select **X**.

## What was built during the competition

This is a new project. The repository's first commit is November 10, 2026, inside the submission period (from September 24, 2026), and it contains no code from Hypa's other repositories. Hypa's existing language services (translation, text-to-speech, speech-to-text and chat for African and other under-served languages) are called through Hypa's public API.

Built during the competition:

- **The storyteller:** narration through Hypa text-to-speech with one consistent voice per story, words lighting up with the voice, and lip sync.
- **Raise your hand to ask:** a raised-hand gesture interrupts the story; the question goes through speech-to-text and a spoiler-guarded chat model (Meta's Muse models first, with fallbacks), and the answer is spoken in the listener's language.
- **Six stories in eight languages**, plus a story for each object in the hut, with suggested questions and practice words.
- **Three story games:** Face Dodo, Turn the hat and Play the drum.
- **The campfire world:** sky, forest, fire, rain, lightning and night effects driven by each story's script, with spatial audio. All shaders are our own.
- **In my room:** placement in your real room from the MR Utility Kit room scan, and Depth API occlusion in our own fire, ember and glow shaders.
- **Portals:** live doorways on your real doors (MRUK `DOOR_FRAME` anchors) into the hut, the village and the campfire, rendered per eye.
- **The hut:** grabbable physics objects with the Meta Interaction SDK, and the old woman's telling for each one.
- **The village and its elder**, with two more stories.
- **Try Hypa Translate** and **Hypa Lens (beta)** with on-device text recognition.
- EditMode tests for the room placement, the portal maths, the game rules and the story content.

## Project components

| Component | Location | Purpose |
| --- | --- | --- |
| HypaCore | [`Assets/HypaCore`](./Assets/HypaCore) | Hypa API client, languages, audio, hands and pinch, world UI, and the app shell (Home) |
| Hypa Stories | [`Assets/HypaStories`](./Assets/HypaStories) | Campfire, storyteller, ask loop, games, room mode, portals, hut and village. Details in [its README](./Assets/HypaStories/README.md) |
| Hypa Lens (beta) | [`Assets/HypaLens`](./Assets/HypaLens) | Capture real-world text with the passthrough camera, then translate and hear it |
| Story scripts | [`Assets/HypaStories/Resources/Stories`](./Assets/HypaStories/Resources/Stories) | One folder per story: the English script, cues and one file per translation. Sources in [SOURCES.md](./Assets/HypaStories/Resources/Stories/SOURCES.md) |
| API notes | [`docs/api-contract.md`](./docs/api-contract.md) | The Hypa API calls and what has been verified live |

## Build from GitHub

### Prerequisites

- **Unity 6000.6.8f1** (Unity 6) with **Android Build Support** (OpenJDK, Android SDK and NDK), installed through Unity Hub
- **Git** and **Git LFS** (`brew install git-lfs` on macOS)
- **A Hypa API key** (see [step 3](#3-add-a-hypa-api-key))
- For the headset: a **Meta Quest 3 or 3S** in [developer mode](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/), a USB-C cable, and `adb` (bundled with Unity's Android SDK, or [Android platform tools](https://developer.android.com/tools/releases/platform-tools))

The Meta XR SDK (All-in-One, v209), OpenXR and glTFast are listed in [`Packages/manifest.json`](./Packages/manifest.json), so Unity installs them when it opens the project.

### 1. Clone

```bash
git clone https://github.com/hypaai/hypa-meta-vr.git
cd hypa-meta-vr
tools/setup-git.sh
```

`tools/setup-git.sh` sets up Git LFS (the 3D models and textures), Unity Smart Merge and the secret-scan hooks. If models show up as small text files, run `git lfs pull`.

### 2. Open in Unity

1. In Unity Hub, choose **Add → Add project from disk** and pick the `hypa-meta-vr` folder. The first import takes several minutes.
2. Switch to Android: **File → Build Profiles → Android → Switch Platform**.
3. Open **Meta → Tools → Project Setup Tool** and click **Fix All** for Android.
4. Check **Project Settings → Player → Active Input Handling** is **Both** (the editor's mouse controls use it).

The scenes are committed, so you can play straight away. To rebuild them from code, use the **Hypa** menu (**Create Shell and Core Test Scenes**, **Stories → Create Story Scene**, then **Sync Build Settings**).

The licensed background sounds (ambience, fire, wolves) and the storyteller's avatar are not in this public repository. Without them the app uses synthesised wind and fire sounds and a stand-in storyteller; the storyteller's voice, the story and every feature still work. The submitted build includes them.

### 3. Add a Hypa API key

The storyteller's voice, her answers and translation need a Hypa API key. For a judging key, email [chris@hypaintelligence.com](mailto:chris@hypaintelligence.com).

```bash
cp .env.example .env
```

Set the key in `.env` (git-ignored):

```dotenv
HYPA_API_KEY=<your-hypa-key>
HYPA_BASE_URL=https://api.hypaintelligence.com
```

Without a key the app still runs: lines are "told" silently with subtitles, and questions use the suggested-question buttons.

### 4. Run in the editor (no headset)

Open [`Assets/HypaCore/Scenes/HypaShell.unity`](./Assets/HypaCore/Scenes/HypaShell.unity) and press **Play**. Press **M** for Home.

| Key | Effect |
| --- | --- |
| Right mouse drag | Look around |
| WASD | Walk |
| Click, Enter | Choose (the fire, a card, **Begin story**) |
| Space, ← → | Pause or resume, previous or next line |
| H or Q | Raise your hand to ask |
| 1–3 | Ask a suggested question |
| T | Try Hypa Translate (after a story) |
| R | In my room / back to the forest |
| P, G | Step through a portal / step through or back |
| Esc | Back |

With **Meta Quest Link** or the **Meta XR Simulator** running, Play shows the headset view and uses hands or controllers instead.

Tests: **Window → General → Test Runner → EditMode → Run All**.

### 5. Build and run on a Quest

1. Connect the Quest with USB and accept **Allow USB debugging** inside the headset.
2. In **File → Build Profiles → Android**, check that **HypaShell** is first in the scene list, then click **Build And Run**.
3. Copy your key to the headset (once per install):

   ```bash
   adb push .env /sdcard/Android/data/ai.hypa.lens/files/.env
   ```

4. Relaunch **Hypa Stories**: on the headset it is under **Library → Unknown Sources**. Then follow the [walkthrough](#ten-minute-walkthrough).

The package name is `ai.hypa.lens` (the app started as Hypa Lens). A build you make yourself is signed with a different key from the store build, so uninstall the store build first: `adb uninstall ai.hypa.lens`.

## Troubleshooting

### The storyteller is silent (subtitles only)

The app has no Hypa API key or no internet. In a GitHub build, push `.env` as in [step 5](#5-build-and-run-on-a-quest) and relaunch. The headset log says which: `adb logcat -s Unity | grep "Hypa: API"` prints `key found` or `key missing`.

### She doesn't hear my question

Allow the microphone: **Settings → Apps → Hypa Stories → Permissions**. You can always pinch a suggested question instead.

### Raising my hand does nothing

Check hand tracking is on (**Settings → Movement tracking**) and that your hand is in view above your head for about a third of a second.

### In my room puts the fire right in front of me

The headset has no room scan, or the room permission was declined. Run **Settings → Environment setup → Space setup**, then try again. Allow **spatial data** for Hypa Stories in its app permissions.

### No doorway appears on my door

Doors come from the room scan. Re-run Space setup and mark your doors. With no door in the scan, the doorway goes on a clear stretch of wall, or 2 m in front of you.

### It runs slowly

Close other apps first. Quest 2 and Quest Pro have older chips, and the live portal view draws the scene twice; Quest 3 or 3S gives the intended experience.

### Lens shows no camera

Allow the headset camera: **Settings → Apps → Hypa Stories → Permissions**. Lens needs Quest 3 or 3S.

### Install fails with `INSTALL_FAILED_UPDATE_INCOMPATIBLE`

A build signed with another key is installed. Run `adb uninstall ai.hypa.lens` and install again.

## Developer notes

- **Structure:** `HypaCore`, `HypaStories` and `HypaLens` each have their own assembly definitions. Stories and Lens never reference each other; both use HypaCore. An EditMode test enforces this.
- **API key lookup order:** environment variables (`HYPA_API_KEY`, `HYPA_BASE_URL`), then `.env`, then `hypa.local.json` (**Hypa → Create API Key File**), all at the project root in the editor and in `/sdcard/Android/data/ai.hypa.lens/files/` on a Quest.
- **Stories in depth:** places, room mode, portals, the hut, the games, the story file format and the API use are documented in [Assets/HypaStories/README.md](./Assets/HypaStories/README.md).
- **Translations:** the Hausa, Igbo, Pidgin, Kiswahili and Yorùbá scripts are machine translations checked by hand; they still need a native speaker's review before a public release. Story sources and adaptations are in [SOURCES.md](./Assets/HypaStories/Resources/Stories/SOURCES.md).
- **Before every push:** `tools/scan-secrets.sh` runs from the Git hooks (gitleaks if installed, otherwise a grep scan). If it reports anything, stop.

## Security

- Never commit `.env`, `hypa.local.json`, API keys, keystores or signing passwords. Only `.env.example` is tracked.
- Never put a key in `Assets/`, `StreamingAssets` or `Resources` in this repository.
- The licensed audio and avatar live in `Assets/HypaStories/Local/`, which is git-ignored.
- Rotate a key immediately if it is exposed.
