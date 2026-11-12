# Hypa Meta VR

One Quest 3 / 3S app, one competition entry (Meta VR Start Developer Competition, Productivity track):

- **Hypa Lens:** look at real text, pinch a word or sentence, and get the translation, audio and meaning, pinned in the room.
- **Hypa Story:** a mode inside the same app (tickets 3A-3E).

Licensed under Apache 2.0. No code from `hypa-v2`: every Hypa feature comes from Hypa's public API through `HypaCore`.

```
Assets/
  HypaCore/      shared core: API client, languages, audio, settings, hands/pinch, world UI, app shell
  HypaLens/      Lens (HYP-163)
  HypaStories/   Stories (3A-3E), stub for now
```

Each folder has its own assembly definitions. **Lens never references Stories and Stories never references Lens**; both use HypaCore. An EditMode test enforces this. Each team edits only its own scenes and prefabs.

## Setup

1. Install **Unity 6000.0.85f2** (Unity 6 LTS) with **Android Build Support** (OpenJDK, SDK & NDK).
2. Clone, then run once: `tools/setup-git.sh`. This sets up Git LFS, Unity Smart Merge, and the secret-scan hooks. Install `git-lfs` first.
3. Open the folder in Unity. Asset Serialization is forced to **Force Text** and **Visible Meta Files** on open.
4. **Project Settings > Player > Active Input Handling = Both** (editor mouse input uses the legacy `Input` class).
5. Install the **Meta XR SDK (All-in-One) v207+** and **Meta XR Simulator** from the Package Manager / Asset Store. Run **Meta > Tools > Project Setup Tool** and fix all issues for Android. The Quest-only assemblies (`HypaCore.Quest`, `HypaLens.Quest`) compile only once those packages are present.
6. Create the scenes from the menu, then sync Build Settings:
   - **Hypa > Create Shell and Core Test Scenes**
   - **Hypa > Lens > Create Lens Scene**
   - **Hypa > Stories > Create Story Stub Scene**
   - **Hypa > Sync Build Settings** (order: HypaShell, HypaLens, HypaStory, HypaCoreTest)

## Hypa API key

The key is never in the repo, the scenes, `StreamingAssets`, `Resources` or the APK.

- **Editor (recommended):** `cp .env.example .env` at the project root and set `HYPA_API_KEY`. `.env` is git-ignored;
  only `.env.example` (no key) is committed.
- **Alternatives:** the environment variable `HYPA_API_KEY`, or **Hypa > Create API Key File** (`hypa.local.json`, git-ignored).
  Lookup order: environment variables, then `.env`, then `hypa.local.json`.
- **Quest (local builds):** push the same file to the app's data folder:
  `adb push .env /sdcard/Android/data/ai.hypa.lens/files/.env`
- **Judges' build:** set `baseUrl` to the private proxy (built in `hypa-v2`, not here); no key in the app.

```dotenv
HYPA_API_KEY=
HYPA_BASE_URL=https://api.hypaintelligence.com
```

Free tools (`list_languages`, `check_language_pair`) work without a key. Translation, speech and the dictionary need one.
See [docs/api-contract.md](docs/api-contract.md) for what has been verified live.

## Run without a headset

- **HypaLens scene, Play:** simulated camera, simulated text, canned translations. Space = capture, hold left mouse = pinch (over 0.6 s = sentence), O = offline, N = no text, H = help. Untick **Use Simulated Api** on the `HypaLens` object to call the real API with your key.
- **HypaCoreTest scene, Play:** translates and speaks a sentence through the live API (needs a key). R repeats.
- **HypaShell scene, Play:** the full app. M or the **Menu** button opens Lens / Practice (Saved words, Hypa Story) / API test.
- **Meta XR Simulator:** enable it from the Meta menu, put the Meta camera rig in HypaShell, and set `HypaInput.MouseInEditor = false` to test hands.
- **Tests:** Window > General > Test Runner > EditMode.

## Build for Quest

1. File > Build Profiles > **Android**, switch platform. Texture compression ASTC.
2. Player: IL2CPP, ARM64, minimum API level per Meta's Project Setup Tool.
3. Put the **Meta camera rig** (OVRCameraRig with hand tracking, passthrough enabled) in **HypaShell** in place of the plain camera. Mode scenes keep their own camera only for standalone play (`StandaloneOnly` removes it under the shell).
4. Build and Run to a Quest in developer mode, or upload to the **Competition** release channel in the Meta developer dashboard.

## Before every push

`tools/scan-secrets.sh` runs automatically from the hooks (gitleaks if installed, else a grep scan). If it reports anything, stop.

## Status

| Area | Status |
| -- | -- |
| HypaCore MCP client | Built; transport and schemas verified live with free tools; paid tools not yet called with a key |
| HypaCore REST client (`mt`, `tts`, `asr`, `chat`, `dictionary`, `ocr`) | Generic client built; field names unverified |
| Audio player (queue, fade stop, volume), settings, languages, shell, Stories stub | Built, not yet run in Unity |
| Lens flow in the simulator | Built, not yet run in Unity |
| Hands (`OVRHand`), MRUK placement | Compiles against Meta XR SDK v209; runs in the Meta XR Simulator |
| Passthrough camera frames (MRUK `PassthroughCameraAccess`) | Runs in the Meta XR Simulator (frame, pose and intrinsics); not yet run on a headset |
| On-headset text recognition (Google ML Kit, bundled Latin model) | Java bridge in `Assets/HypaLens/Plugins/Android/HypaMlKit.androidlib`; not yet built into an APK or run on a headset |
| Server text recognition fallback | Not used: Hypa's `extract_text_from_image` returns no word positions, which HYP-163 requires for a fallback |
| 60 fps, 3 rooms x 3 languages, Competition channel | Needs a headset |

The project compiles in Unity 6000.6.8f1 and all EditMode tests pass. Anything marked "not yet run on a headset" still needs a Quest 3 test.
