using System.Linq;
using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Dodo in the camp, for the Seven Hot Stones game: Quaternius' Demon (CC0) scaled up to a hulking 3.4 m, recoloured to a
    /// dark blood red with glowing eyes, its halo and trident hidden. Its own animations play by name (Idle, Run, Walk, Duck,
    /// HitReact, Yes, Punch, Death). Like the storyteller, its skinned meshes are baked into plain meshes every frame, so it
    /// draws in the headset's two-eye view.
    /// </summary>
    public sealed class DodoCreature : MonoBehaviour
    {
        public const float Height = 3.4f;

        Animation _anim;
        readonly Dictionary<string, string> _clips = new Dictionary<string, string>();
        sealed class Baked { public SkinnedMeshRenderer Skin; public Mesh Mesh; public MeshRenderer Draw; public bool Fitted; public int Black = -1; }
        readonly List<Baked> _baked = new List<Baked>();
        Transform _head, _model;
        string _playing;
        public bool Built => _model != null;

        /// <summary>Where a stone has to go: just in front of its jaw (world space).</summary>
        public Vector3 Mouth => _head != null ? _head.position + transform.forward * 0.38f - Vector3.up * 0.12f : transform.position + Vector3.up * Height * 0.8f;

        public bool Build(GameObject prefab, Material lit)
        {
            if (prefab == null) return false;
            var m = Instantiate(prefab, transform);
            m.name = "Dodo Model";
            _model = m.transform;
            _model.localPosition = Vector3.zero;
            _model.localRotation = Quaternion.identity;
            foreach (var t in m.GetComponentsInChildren<Transform>(true)) if (t.name == "Head") _head = t;

            // recolour: blood red hide, black horns and claws, eyes that glow like coals
            Material Mat(Color c, Color? glow = null)
            {
                var mat = new Material(lit) { name = "Dodo", color = c };
                mat.SetFloat("_Glossiness", 0.25f);
                if (glow is Color g) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", g); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
                return mat;
            }
            var hide = Mat(new Color(0.36f, 0.04f, 0.035f));
            var black = Mat(new Color(0.05f, 0.04f, 0.04f));
            var eye = Mat(new Color(1f, 0.6f, 0.15f), new Color(1.6f, 0.75f, 0.12f));
            var pupil = Mat(new Color(0.02f, 0.01f, 0.01f));
            foreach (var r in m.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMesh != null && r.sharedMesh.name.ToLowerInvariant().Contains("trident")) { r.gameObject.SetActive(false); continue; }
                var mats = r.sharedMaterials;
                var names = new string[mats.Length];
                for (var i = 0; i < mats.Length; i++)
                {
                    var n = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                    names[i] = n;
                    mats[i] = n.Contains("eye_white") ? eye : n.Contains("eye_black") ? pupil : n.Contains("black") ? black : hide;
                }
                r.sharedMaterials = mats;
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // draw a mesh baked from the skin each frame (GPU skinning didn't draw in the two-eye view)
                var go = new GameObject(r.name + " (baked)");
                go.transform.SetParent(r.transform, false);
                var baked = new Mesh { name = "Dodo baked" };
                baked.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = baked;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.enabled = false;
                _baked.Add(new Baked { Skin = r, Mesh = baked, Draw = mr, Black = System.Array.FindIndex(names, n => n.Contains("black") && !n.Contains("eye")) });
            }
            foreach (var t in m.GetComponentsInChildren<Transform>(true)) if (t.name.ToLowerInvariant().Contains("trident")) t.gameObject.SetActive(false);

            // scale to Dodo's size, standing on the ground
            var b = Bounds(m);
            var s = b.size.y > 1e-3f ? Height / b.size.y : 1f;
            _model.localScale = Vector3.one * s;
            _model.localPosition = new Vector3(0f, -b.min.y * s, 0f);

            _anim = m.GetComponentInChildren<Animation>(true);
            if (_anim != null)
                foreach (AnimationState st in _anim)
                {
                    var key = st.name.Contains("|") ? st.name.Substring(st.name.LastIndexOf('|') + 1) : st.name;
                    _clips[key] = st.name;
                    st.wrapMode = key == "Idle" || key == "Run" || key == "Walk" ? WrapMode.Loop : WrapMode.ClampForever;
                }
            Play("Idle");
            return true;
        }

        static Bounds Bounds(GameObject m)
        {
            var any = false; var b = new Bounds();
            foreach (var r in m.GetComponentsInChildren<SkinnedMeshRenderer>(false))
            {
                if (!r.gameObject.activeInHierarchy || r.sharedMesh == null) continue;
                var w = r.bounds;
                if (!any) { b = w; any = true; } else b.Encapsulate(w);
            }
            if (!any) return new Bounds(Vector3.up, Vector3.one * 2f);
            // to the model root's space (it's at the origin, unscaled, when this runs)
            var root = m.transform;
            return new Bounds(root.InverseTransformPoint(b.center), b.size);
        }

        /// <summary>
        /// The Demon wears a little black halo between its horns. Of the separate black pieces (horns, claws, halo…), it is the
        /// one centred over the body, high up: drop it. Judged in the world, standing, so it doesn't matter which way the model
        /// file's axes point; the baked mesh has the same vertex order as the skin's, so the cut applies to the skin's own mesh.
        /// </summary>
        static void RemoveHalo(Baked b)
        {
            var src = b.Skin.sharedMesh;
            if (src == null || !src.isReadable || b.Black < 0 || b.Black >= src.subMeshCount) return;
            var baked = b.Mesh.vertices;
            if (baked.Length != src.vertexCount) return;
            var w = new Vector3[baked.Length];
            var all = new Bounds(b.Draw.transform.TransformPoint(baked[0]), Vector3.zero);
            for (var i = 0; i < baked.Length; i++) { w[i] = b.Draw.transform.TransformPoint(baked[i]); all.Encapsulate(w[i]); }
            var tris = src.GetTriangles(b.Black);

            // the black triangles in connected pieces (union-find over shared vertices)
            var parent = new Dictionary<int, int>();
            int Find(int x) { if (!parent.TryGetValue(x, out var p)) { parent[x] = x; return x; } if (p == x) return x; var r = Find(p); parent[x] = r; return r; }
            for (var t = 0; t < tris.Length; t += 3) { var r0 = Find(tris[t]); parent[Find(tris[t + 1])] = r0; parent[Find(tris[t + 2])] = r0; }
            var pieces = new Dictionary<int, Bounds>();
            foreach (var v in parent.Keys.ToList())
            {
                var r = Find(v);
                if (pieces.TryGetValue(r, out var bb)) { bb.Encapsulate(w[v]); pieces[r] = bb; } else pieces[r] = new Bounds(w[v], Vector3.zero);
            }
            var width = Mathf.Max(all.size.x, all.size.z);
            // every piece over the crown of the head, near the middle (the ring may come in several pieces); horns stand to the sides
            var halo = new HashSet<int>();
            foreach (var kv in pieces)
            {
                var c = kv.Value.center;
                var off = new Vector2(c.x - all.center.x, c.z - all.center.z).magnitude;
                if (off < width * 0.2f && kv.Value.min.y > all.max.y - all.size.y * 0.2f) halo.Add(kv.Key);
            }
            if (halo.Count == 0) return;
            var keep = new List<int>();
            for (var t = 0; t < tris.Length; t += 3)
                if (!halo.Contains(Find(tris[t]))) { keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]); }
            if (keep.Count == tris.Length) return;
            var copy = Instantiate(src);
            copy.name = src.name + " (no halo)";
            copy.SetTriangles(keep, b.Black);
            b.Skin.sharedMesh = copy;
        }

        /// <summary>Plays one of its animations ("Idle", "Run", "Walk", "Duck", "HitReact", "Yes", "Punch", "Death"…), blending in.</summary>
        public void Play(string name, float fade = 0.25f, float speed = 1f)
        {
            if (_anim == null || !_clips.TryGetValue(name, out var clip)) return;
            _anim[clip].speed = speed;
            if (_playing == name && _anim.IsPlaying(clip) && _anim[clip].wrapMode == WrapMode.Loop) return;
            _anim[clip].time = 0f;
            _anim.CrossFade(clip, fade);
            _playing = name;
        }

        public bool Has(string name) => _clips.ContainsKey(name);

        public bool IsPlaying(string name) => _anim != null && _clips.TryGetValue(name, out var c) && _anim.IsPlaying(c);

        /// <summary>Freezes an animation once it reaches <paramref name="normalized"/> (0..1) of the way: stooped and waiting.</summary>
        public void Hold(string name, float normalized)
        {
            if (_anim == null || !_clips.TryGetValue(name, out var c)) return;
            var st = _anim[c];
            if (st.normalizedTime >= normalized) { st.speed = 0f; st.normalizedTime = normalized; }
        }

        bool _grounded;
        int _groundFrames;
        float _lowest = float.MaxValue;

        void LateUpdate()
        {
            // once every baked copy is fitted: stand on the ground. The model's bounds at load are a little loose (and the baked
            // copy is rescaled about its own origin), which left Dodo floating a few centimetres up. Over a full stride (a run
            // has moments with both feet up), find the lowest point drawn, then lower Dodo by that much once.
            if (!_grounded && _baked.Count > 0 && _baked.TrueForAll(x => x.Fitted || x.Skin == null))
            {
                foreach (var x in _baked) if (x.Draw != null && x.Draw.enabled) _lowest = Mathf.Min(_lowest, x.Draw.bounds.min.y - transform.position.y);
                if (++_groundFrames >= 40)
                {
                    _grounded = true;
                    if (_lowest > 0.005f && _lowest < 0.5f && _model != null)
                        _model.localPosition -= new Vector3(0f, _lowest / Mathf.Max(1e-3f, transform.lossyScale.y), 0f);
                }
            }
            foreach (var b in _baked)
            {
                if (b.Skin == null || !b.Skin.gameObject.activeInHierarchy) continue;
                b.Skin.BakeMesh(b.Mesh, false);
                b.Mesh.RecalculateBounds();
                if (b.Fitted) continue;
                // The baked mesh comes out in the model file's own units (this one: centimetres), so it would draw ~100x too
                // big. Once, compare it with the skin's own bounds and scale the baked copy to match, about its origin.
                b.Fitted = true;
                var want = b.Skin.bounds.size.y;
                var got = b.Draw.bounds.size.y;
                if (want > 1e-4f && got > 1e-4f && Mathf.Abs(got / want - 1f) > 0.02f)
                    b.Draw.transform.localScale *= want / got;
                RemoveHalo(b);
            }
        }
    }
}
