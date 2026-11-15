# Third-party notices

This project is licensed under Apache 2.0 (see `LICENSE`). Dependencies and their licenses:

| Dependency | Used for | License | In repo? |
| -- | -- | -- | -- |
| Newtonsoft.Json for Unity (`com.unity.nuget.newtonsoft-json`) | JSON parsing in HypaCore | MIT | No, Unity package |
| Unity Test Framework (`com.unity.test-framework`) | EditMode tests | Unity Companion License | No, Unity package |
| Meta XR SDK (All-in-One, v207+), incl. Interaction SDK and MR Utility Kit | Hands, passthrough, room surfaces on Quest | Meta Platform Technologies SDK License | No, installed per developer |
| Lucide icons (`Assets/HypaCore/Resources/Icons`, rendered to PNG) | Shared UI icons (Lens, shell) | ISC (`LUCIDE-LICENSE.txt`) | Yes |
| Inter typeface (`Assets/HypaCore/Runtime/Resources/Fonts/Inter-Regular.ttf`) | UI text | SIL Open Font License 1.1 | Yes |
| glTFast (`com.unity.cloud.gltfast`) | Importing the Stories glTF models | Apache 2.0 | No, Unity package |
| Poly Haven models and textures (`Assets/HypaStories/Art/PolyHaven`) | Stories campfire props and forest floor | CC0 1.0 | Yes |
| "Camp Fire" by JalenJacobson (`Assets/HypaStories/Art/Models/camp_fire.glb`) | Stories fire pit | CC BY 4.0, **credit required** (below) | Yes |
| Outfit, Cormorant Garamond and Noto Sans fonts (`Assets/HypaStories/Art/Fonts`) | Stories UI and story text | SIL OFL 1.1 (licence files next to the fonts) | Yes |
| Lucide icons (`Assets/HypaStories/Art/Icons`, rendered to PNG) | Stories UI icons | ISC (`LUCIDE-LICENSE.txt`) | Yes |
| TextMeshPro Essential Resources (`Assets/TextMesh Pro`) | SDF text shaders and settings | Unity Companion License | Yes, Unity's standard import |
| Epidemic Sound beds, Ready Player Me placeholder avatar | Stories audio and storyteller | Subscription / RPM terms | **No**: git-ignored `Assets/HypaStories/Local/`, never commit |

Required credit: This work is based on "Camp Fire" (https://sketchfab.com/3d-models/camp-fire-b7967d9c16b64f2790b87e5ff8a80b52) by JalenJacobson (https://sketchfab.com/JalenJacobson) licensed under CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/). Re-encoded (meshopt and WebP removed) for import.

Poly Haven authors: Rico Cilliers (Boulder 01), Rob Tuytel (Dead Tree Trunk, Tree Stump 02, Fern 02, Grass Medium 01, Forest Ground 04, with Rico Cilliers), Kless Gyzen (Rock Moss Set 01 / 02).

Add every new dependency here with its license before merging. It must be compatible with Apache 2.0 for anything committed to this repo.
