using System.IO;
using System.Linq;
using HypaCore;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace HypaStories.EditorTools
{
    /// <summary>
    /// Hypa > Stories > Create Story Scene: gathers the story's shaders, models (glTF via glTFast), audio and logo into
    /// Assets/HypaStories/StoryAssets.asset, sets their import settings for Quest, and (re)builds the HypaStory scene: a camera for
    /// standalone play (removed under the shell) and the StoryApp. Missing optional assets (the git-ignored Local folder) just leave
    /// fields empty; the scene falls back to procedural stand-ins.
    /// </summary>
    public static class StorySceneBuilder
    {
        const string Root = "Assets/HypaStories";
        const string AssetsPath = Root + "/StoryAssets.asset";
        const string Art = Root + "/Art", Local = Root + "/Local", Shaders = Root + "/Shaders", Materials = Root + "/Materials";
        static readonly string[] Rocks = { "rock_moss_set_01", "rock_moss_set_02", "boulder_01" };

        [MenuItem("Hypa/Stories/Create Story Scene")]
        public static void Create()
        {
            var assets = BuildAssets();
            Directory.CreateDirectory(Root + "/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var sun = Object.FindAnyObjectByType<Light>();
            if (sun != null) Object.DestroyImmediate(sun.gameObject); // the camp brings its own lights
            var cam = Camera.main;
            var rig = BuildMetaRig();
            if (rig != null)
            {
                Object.DestroyImmediate(cam.gameObject);
                cam = rig.GetComponentsInChildren<Camera>(true).FirstOrDefault(c => c.CompareTag("MainCamera"))
                      ?? rig.GetComponentInChildren<Camera>(true);
            }
            else
            {
                Debug.LogWarning("Hypa Stories: Meta XR SDK rig not found; the scene gets a plain camera (no hands or controllers in VR).");
                cam.transform.SetPositionAndRotation(new Vector3(0, 1.2f, 0), Quaternion.identity);
                cam.gameObject.AddComponent<StandaloneOnly>(); // removed when the shell provides the camera rig
                cam.gameObject.AddComponent<EditorMouseLook>();
                cam.fieldOfView = 75f;
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = CampWorld.FogColor;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 300f;

            // NewScene unloads unused assets, so load the asset again before referencing it
            assets = AssetDatabase.LoadAssetAtPath<StoryAssets>(AssetsPath);
            var app = new GameObject("HypaStories").AddComponent<StoryApp>();
            var so = new SerializedObject(app);
            so.FindProperty("assets").objectReferenceValue = assets;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (so.FindProperty("assets").objectReferenceValue == null) Debug.LogError("Hypa Stories: couldn't reference " + AssetsPath + " from the scene.");

            var path = $"{Root}/Scenes/{HypaScenes.Story}.unity";
            EditorSceneManager.SaveScene(scene, path);
            if (EditorBuildSettings.scenes.All(s => s.path != path)) EditorApplication.ExecuteMenuItem("Hypa/Sync Build Settings");
            Debug.Log("Hypa: created the HypaStory scene. Press Play: in the headset / Meta XR Simulator, or with the mouse " +
                      "(right mouse = look, Enter = choose, Q = ask).");
        }

        const string MetaPrefabs = "Packages/com.meta.xr.sdk.core/Prefabs/";

        /// <summary>
        /// The Meta camera rig (OVRCameraRig + OVRManager: head, controllers) with tracked hands (OVRHandPrefab, OpenXR hand
        /// skeletons, as in the Lens scene), so the scene plays in a headset, Link or the Meta XR Simulator on its own. It carries
        /// StandaloneOnly, so under the shell the shell's rig is used instead. Without a headset, the rig's camera works as a plain
        /// camera and EditorMouseLook turns the rig. Built from prefabs by path: Stories has no code dependency on the SDK.
        /// </summary>
        static GameObject BuildMetaRig()
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MetaPrefabs + "OVRCameraRig.prefab");
            var handPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MetaPrefabs + "OVRHandPrefab.prefab");
            if (rigPrefab == null) return null;
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "Story Camera Rig";
            rig.AddComponent<StandaloneOnly>();
            rig.AddComponent<EditorMouseLook>();
            var manager = rig.GetComponents<MonoBehaviour>().FirstOrDefault(m => m != null && m.GetType().Name == "OVRManager");
            if (manager != null) Set(manager, "_trackingOriginType", 1); // floor level: the scene's seats are on the floor

            if (handPrefab != null)
                foreach (var (anchor, hand, skeleton) in new[] { ("LeftHandAnchor", 0, 4), ("RightHandAnchor", 1, 5) })
                {
                    var parent = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == anchor);
                    if (parent == null) continue;
                    var h = (GameObject)PrefabUtility.InstantiatePrefab(handPrefab, parent);
                    h.name = hand == 0 ? "Left Hand" : "Right Hand";
                    foreach (var c in h.GetComponents<MonoBehaviour>())
                    {
                        if (c == null) continue;
                        switch (c.GetType().Name)
                        {
                            case "OVRHand": Set(c, "HandType", hand); break;
                            case "OVRSkeleton": Set(c, "_skeletonType", skeleton); break; // XRHandLeft / XRHandRight
                            case "OVRMesh": Set(c, "_meshType", skeleton); break;
                        }
                    }
                }
            return rig;
        }

        static void Set(Object target, string property, int value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            if (p == null) { Debug.LogWarning($"Hypa Stories: {target.GetType().Name}.{property} not found; check the rig in the scene."); return; }
            p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Creates or refreshes Assets/HypaStories/StoryAssets.asset.</summary>
        public static StoryAssets BuildAssets()
        {
            Directory.CreateDirectory(Materials);
            EnsureTmpResources();
            ConfigureImports();
            var a = AssetDatabase.LoadAssetAtPath<StoryAssets>(AssetsPath);
            if (a == null) { a = ScriptableObject.CreateInstance<StoryAssets>(); AssetDatabase.CreateAsset(a, AssetsPath); }

            Shader S(string name) => AssetDatabase.LoadAssetAtPath<Shader>($"{Shaders}/{name}.shader");
            a.sky = S("StorySky"); a.flame = S("StoryFlame"); a.particles = S("StoryParticles"); a.rain = S("StoryRain");
            a.mist = S("StoryMist"); a.additive = S("StoryAdditive"); a.card = S("StoryCard"); a.cutout = S("StoryCutout");
            a.veil = S("StoryVeil"); a.pine = S("StoryPine"); a.uiPanel = S("StoryUIPanel"); a.uiIcon = S("StoryUIIcon");

            a.lit = Material($"{Materials}/StoryLit.mat", m => { m.color = Color.white; m.SetFloat("_Glossiness", 0.1f); });
            a.ground = Material($"{Materials}/ForestGround.mat", m =>
            {
                const string g = Art + "/PolyHaven/forest_ground_04/";
                m.color = new Color(0.6f, 0.6f, 0.6f);
                m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(g + "Diffuse.jpg");
                m.mainTextureScale = new Vector2(35f, 35f);
                var nor = AssetDatabase.LoadAssetAtPath<Texture2D>(g + "nor_gl.jpg");
                if (nor != null) { m.SetTexture("_BumpMap", nor); m.EnableKeyword("_NORMALMAP"); }
                m.SetFloat("_Glossiness", 0.05f);
            });

            a.log = Material($"{Materials}/BonfireLog.mat", m =>
            {
                const string t = Art + "/PolyHaven/dead_tree_trunk/textures/";
                m.color = new Color(0.8f, 0.72f, 0.64f);
                m.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(t + "dead_tree_trunk_diff_1k.jpg");
                m.EnableKeyword("_EMISSION"); // a faint ember glow, so the logs read against the flames
                m.SetColor("_EmissionColor", new Color(0.32f, 0.09f, 0.02f));
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                m.mainTextureScale = new Vector2(1f, 2f);
                m.SetFloat("_Glossiness", 0.05f);
            });
            (a.uiFont, a.uiFontBold, a.serifFont, a.serifItalic) = Fonts();
            a.icons = AssetDatabase.FindAssets("t:Texture2D", new[] { Art + "/Icons" })
                .Select(g => AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null).ToArray();

            GameObject M(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject Ph(string id) => M($"{Art}/PolyHaven/{id}/{id}_1k.gltf");
            a.campFire = M($"{Art}/Models/camp_fire.glb");
            a.storyteller = M($"{Local}/Models/storyteller.glb");
            a.deadTrunk = Ph("dead_tree_trunk"); a.stump = Ph("tree_stump_02"); a.fern = Ph("fern_02"); a.grass = Ph("grass_medium_01");
            a.rocks = Rocks.Select(Ph).Where(r => r != null).ToArray();

            AudioClip C(string file) => AssetDatabase.LoadAssetAtPath<AudioClip>($"{Local}/Audio/{file}");
            a.ambienceSplash = C("amb-1.mp3"); a.ambienceStory = C("amb-2.mp3");
            a.fireFar = C("fire-1.mp3"); a.fireNear = C("fire-2.mp3"); a.wolf = C("wolf-1.mp3");
            a.logo = AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/hypa-reality.png");

            EditorUtility.SetDirty(a);
            AssetDatabase.SaveAssets();
            var missing = new (Object asset, string name)[] { (a.campFire, "camp fire"), (a.storyteller, "storyteller"), (a.ambienceStory, "ambience") }
                .Where(x => x.asset == null).Select(x => x.name).ToArray();
            Debug.Log("Hypa Stories: StoryAssets updated." + (missing.Length > 0
                ? " Missing (fallbacks will be used): " + string.Join(", ", missing) + ". Models need glTFast; audio and the avatar live in the git-ignored " + Local + "."
                : ""));
            return a;
        }

        // ---------- text ----------

        /// <summary>TextMeshPro's shaders and settings (TMP Essential Resources), imported once into Assets/TextMesh Pro.</summary>
        [MenuItem("Hypa/Stories/Import Text Resources")]
        public static void EnsureTmpResources()
        {
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") != null) return;
            var ugui = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            if (ugui == null) { Debug.LogWarning("Hypa Stories: com.unity.ugui not found; can't import TMP Essential Resources."); return; }
            var pkg = Path.Combine(ugui.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(pkg, false);
            AssetDatabase.Refresh();
            Debug.Log("Hypa Stories: imported TMP Essential Resources.");
        }

        // Latin-1 plus the punctuation the stories use (English, French, Spanish), and the letters and tone marks of Yoruba, Igbo and
        // Hausa (dots below, hooked letters, combining accents for tones on dotted letters).
        static readonly string Charset = BuildCharset();
        static string BuildCharset()
        {
            var sb = new System.Text.StringBuilder();
            for (var c = 32; c < 127; c++) sb.Append((char)c);
            for (var c = 160; c < 256; c++) sb.Append((char)c);
            sb.Append("œŒ‘’“”…–—•·€");
            sb.Append("ẹẸọỌṣṢịỊụỤṅṄńŃǹǸḿḾɓƁɗƊƙƘƴƳʼ\u0300\u0301\u0304\u0323");
            return sb.ToString();
        }

        /// <summary>
        /// Outfit (UI) and Cormorant Garamond (story) font assets, with Noto Sans behind them for the letters they lack (Outfit has no
        /// dotted Yoruba / Igbo letters, neither has Hausa's hooked ones). Rerun after adding a language with new letters.
        /// </summary>
        [MenuItem("Hypa/Stories/Update Fonts")]
        public static void UpdateFonts() => Fonts();

        static (TMP_FontAsset ui, TMP_FontAsset uiBold, TMP_FontAsset serif, TMP_FontAsset serifItalic) Fonts()
        {
            var noto = FontAsset("NotoSans-Regular.ttf", "Noto Sans SDF");
            var fonts = (FontAsset("Outfit-Regular.ttf", "Outfit Regular SDF"), FontAsset("Outfit-SemiBold.ttf", "Outfit SemiBold SDF"),
                FontAsset("CormorantGaramond-Medium.ttf", "Cormorant Garamond Medium SDF"),
                FontAsset("CormorantGaramond-SemiBoldItalic.ttf", "Cormorant Garamond SemiBold Italic SDF"));
            foreach (var fa in new[] { fonts.Item1, fonts.Item2, fonts.Item3, fonts.Item4 })
            {
                if (fa == null || noto == null || fa.fallbackFontAssetTable.Contains(noto)) continue;
                fa.fallbackFontAssetTable.Add(noto);
                EditorUtility.SetDirty(fa);
            }
            AssetDatabase.SaveAssets();
            return fonts;
        }

        /// <summary>A static SDF font asset (atlas pre-filled with <see cref="Charset"/>) next to its TTF in Art/Fonts.</summary>
        static TMP_FontAsset FontAsset(string ttf, string name)
        {
            var path = $"{Art}/Fonts/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) { AddCharset(existing, ttf); return existing; }
            if (TMP_Settings.instance == null)
            {
                Debug.LogWarning("Hypa Stories: TextMeshPro resources are still importing; run Hypa > Stories > Create Story Scene again for the fonts.");
                return null;
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>($"{Art}/Fonts/{ttf}");
            if (font == null) { Debug.LogWarning("Hypa Stories: font not found: " + ttf); return null; }
            var fa = TMP_FontAsset.CreateFontAsset(font, 56, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, false);
            if (fa == null) { Debug.LogWarning("Hypa Stories: couldn't create a font asset for " + ttf); return null; }
            fa.name = name;
            fa.TryAddCharacters(Charset, out var missing);
            if (!string.IsNullOrEmpty(missing)) Debug.Log($"Hypa Stories: {ttf} has no glyphs for: {missing}");
            var mobile = Shader.Find("TextMeshPro/Mobile/Distance Field");
            if (mobile != null) fa.material.shader = mobile; // cheaper on Quest
            AssetDatabase.CreateAsset(fa, path);
            fa.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            fa.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            fa.atlasPopulationMode = AtlasPopulationMode.Static; // filled now: no runtime atlas edits, no source font needed in the build
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        /// <summary>Adds the <see cref="Charset"/> letters an existing font asset is missing (the font's own glyphs only).</summary>
        static void AddCharset(TMP_FontAsset fa, string ttf)
        {
            var have = new System.Collections.Generic.HashSet<uint>(fa.characterTable.Select(c => c.unicode));
            var add = new string(Charset.Where(c => !have.Contains(c)).ToArray());
            if (add.Length == 0) return;
            fa.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            fa.TryAddCharacters(add, out var missing);
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            EditorUtility.SetDirty(fa);
            if (missing != add) Debug.Log($"Hypa Stories: added {add.Length - (missing?.Length ?? 0)} letters to {fa.name}" +
                                          (string.IsNullOrEmpty(missing) ? "" : $"; {ttf} has none for: {missing} (Noto Sans covers them)"));
        }

        static Material Material(string path, System.Action<Material> setup)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            setup(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Quest-friendly import settings: compressed audio (narration stays exact for the story clock), ground textures tiled.</summary>
        static void ConfigureImports()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { Local + "/Audio" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(path) is AudioImporter imp)) continue;
                var voice = path.Contains("voice");
                var s = imp.defaultSampleSettings;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = voice ? 0.7f : 0.5f;
                s.loadType = voice ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.Streaming;
                s.preloadAudioData = voice;
                imp.defaultSampleSettings = s;
                imp.forceToMono = !path.Contains("amb");
                imp.SaveAndReimport();
            }
            const string g = Art + "/PolyHaven/forest_ground_04/";
            foreach (var f in new[] { "Diffuse.jpg", "nor_gl.jpg", "arm.jpg" })
            {
                if (!(AssetImporter.GetAtPath(g + f) is TextureImporter t)) continue;
                t.wrapMode = TextureWrapMode.Repeat;
                t.anisoLevel = 4;
                t.maxTextureSize = 1024;
                if (f == "nor_gl.jpg") t.textureType = TextureImporterType.NormalMap;
                if (f == "arm.jpg") t.sRGBTexture = false;
                t.SaveAndReimport();
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Art + "/Icons" }))
            {
                if (!(AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is TextureImporter t)) continue;
                if (t.alphaIsTransparency && t.wrapMode == TextureWrapMode.Clamp) continue;
                t.alphaIsTransparency = true;
                t.mipmapEnabled = true;  // icons are seen small and at an angle in the headset
                t.wrapMode = TextureWrapMode.Clamp;
                t.textureCompression = TextureImporterCompression.Uncompressed;
                t.SaveAndReimport();
            }
            if (AssetImporter.GetAtPath(Art + "/hypa-reality.png") is TextureImporter logo)
            {
                logo.alphaIsTransparency = true;
                logo.mipmapEnabled = false;
                logo.wrapMode = TextureWrapMode.Clamp;
                logo.SaveAndReimport();
            }
        }
    }
}
