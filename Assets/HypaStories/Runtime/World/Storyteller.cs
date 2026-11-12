using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The storyteller on a log across the fire (port of the prototype's storyteller.js). With a humanoid model (standard bone names
    /// Hips, Spine, Neck, Head, LeftArm..., and a mouthOpen / mouthSmile blend shape, e.g. a Ready Player Me avatar imported by
    /// glTFast) the bones are driven directly: seated pose, breathing and sway, hands resting while listening and opening into small
    /// gestures while talking, the head turning to the fire while telling and to you when you ask. mouthOpen follows the voice's
    /// loudness. Without a model, a simple stand-in figure does the head turn and mouth.
    /// Unity follow-up: an Animator with Idle / Talking clips on the same skeleton can replace the bone-driven body.
    /// </summary>
    public sealed class Storyteller : MonoBehaviour
    {
        /// <summary>Voice loudness 0..1 (lip sync, talking gestures).</summary>
        public float Level;
        /// <summary>1 while a question is open: turn to the listener.</summary>
        public float Attend;
        public Transform Fire;
        public Transform Mouth { get; private set; }
        /// <summary>
        /// Draw the avatar as a plain mesh re-baked from the skeleton each frame, instead of GPU skinning. The project's
        /// "GPU (Batched)" skinning didn't draw the avatar in the two-eye XR view (Meta XR Simulator), while plain meshes did. The avatar
        /// is ~9.6k vertices, so baking costs well under a millisecond. Turn off to compare once skinning works in XR.
        /// </summary>
        public bool BakeSkinning = true;

        const float SeatTop = 0.36f;
        readonly Dictionary<string, Transform> _b = new Dictionary<string, Transform>();
        readonly Dictionary<string, Quaternion> _base = new Dictionary<string, Quaternion>();
        readonly List<(SkinnedMeshRenderer skin, Mesh mesh, MeshRenderer draw)> _baked = new List<(SkinnedMeshRenderer, Mesh, MeshRenderer)>();
        readonly List<(SkinnedMeshRenderer r, int open, int smile, float openMax, float smileMax)> _morphs = new List<(SkinnedMeshRenderer, int, int, float, float)>();
        Transform _figureHead, _figureMouth;
        float _talk, _attend, _yaw, _pitch = 0.16f, _open, _smile;

        static readonly string[] BoneNames =
        {
            "Hips", "Spine", "Spine1", "Spine2", "Neck", "Head", "LeftArm", "LeftForeArm", "LeftHand", "RightArm", "RightForeArm", "RightHand",
            "LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot",
        };

        /// <summary>
        /// A rotation given as the prototype's three.js Euler (XYZ order, radians), converted to Unity: glTFast mirrors the X axis when
        /// importing, which keeps rotations about X and flips those about Y and Z.
        /// </summary>
        static Quaternion E(float x, float y, float z)
            => Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right) * Quaternion.AngleAxis(-y * Mathf.Rad2Deg, Vector3.up) * Quaternion.AngleAxis(-z * Mathf.Rad2Deg, Vector3.forward);

        public void Build(GameObject model, Material lit)
        {
            var log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            log.name = "Log";
            Destroy(log.GetComponent<Collider>());
            log.transform.SetParent(transform, false);
            log.transform.localPosition = new Vector3(0, SeatTop / 2f, -0.05f);
            log.transform.localRotation = Quaternion.Euler(0, 0, 90);
            log.transform.localScale = new Vector3(SeatTop, 0.85f, SeatTop);
            log.GetComponent<Renderer>().sharedMaterial = new Material(lit) { color = new Color(0.23f, 0.16f, 0.11f) };

            if (model != null) BuildAvatar(model, lit);
            else BuildFigure(lit);
        }

        void BuildAvatar(GameObject prefab, Material lit)
        {
            var m = Instantiate(prefab, transform);
            m.name = "Avatar";
            m.transform.localPosition = Vector3.zero;
            m.transform.localRotation = Quaternion.identity;
            foreach (var t in m.GetComponentsInChildren<Transform>(true))
                if (System.Array.IndexOf(BoneNames, t.name) >= 0 && !_b.ContainsKey(t.name)) _b[t.name] = t;
            foreach (var r in m.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.sharedMaterials = System.Array.ConvertAll(r.sharedMaterials, src => ToStandard(src, lit));
                var mesh = r.sharedMesh;
                if (mesh == null) continue;
                int open = mesh.GetBlendShapeIndex("mouthOpen"), smile = mesh.GetBlendShapeIndex("mouthSmile");
                // a shape's full strength is its last frame's weight (glTFast imports 1, Unity's own importers 100)
                float Full(int i) => i < 0 ? 0f : mesh.GetBlendShapeFrameWeight(i, mesh.GetBlendShapeFrameCount(i) - 1);
                if (open >= 0 || smile >= 0) _morphs.Add((r, open, smile, Full(open), Full(smile)));
                if (!BakeSkinning) continue;
                var go = new GameObject(r.name + " (baked)");
                go.transform.SetParent(r.transform, false);
                var baked = new Mesh { name = r.name + " baked" };
                baked.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = baked;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.enabled = false; // keeps its bones and blend shape weights for baking; just doesn't draw
                _baked.Add((r, baked, mr));
            }

            // Sit: hips just above the log, thighs forward, shins down (the prototype's base pose).
            if (_b.TryGetValue("Hips", out var hips))
            {
                var hipsY = transform.InverseTransformPoint(hips.position).y;
                m.transform.localPosition = new Vector3(0f, SeatTop + 0.09f - hipsY, 0.04f);
            }
            void Pose(string n, float x, float y, float z)
            {
                if (!_b.TryGetValue(n, out var bone)) return;
                bone.localRotation *= E(x, y, z);
                _base[n] = bone.localRotation;
            }
            Pose("LeftUpLeg", -1.5f, 0, 0); Pose("RightUpLeg", -1.5f, 0, 0);
            Pose("LeftLeg", 1.5f, 0, 0); Pose("RightLeg", 1.5f, 0, 0);
            Pose("LeftFoot", -0.05f, 0, 0); Pose("RightFoot", -0.05f, 0, 0);
            Pose("Spine", 0.03f, 0, 0);
            foreach (var n in new[] { "Hips", "Spine1", "Spine2", "Neck", "Head", "LeftArm", "RightArm", "LeftForeArm", "RightForeArm", "LeftHand", "RightHand" })
                if (_b.TryGetValue(n, out var bone) && !_base.ContainsKey(n)) _base[n] = bone.localRotation;

            if (_b.TryGetValue("Head", out var head))
            {
                Mouth = new GameObject("Mouth").transform;
                Mouth.SetParent(head, false);
                Mouth.localPosition = new Vector3(0f, 0.05f, 0.09f);
            }
            else Mouth = transform;
        }

        /// <summary>
        /// glTFast's own glTF/PbrMetallicRoughness shader fails to build on Metal in the headset view ("Vertex shader writes point
        /// size"), so the avatar didn't draw in the simulator at all. Its materials are opaque and textured, so they map cleanly onto the
        /// Standard template the rest of the camp uses.
        /// </summary>
        static Material ToStandard(Material src, Material lit)
        {
            if (src == null || lit == null || !src.shader.name.StartsWith("glTF/")) return src;
            var m = new Material(lit) { name = src.name + " (Standard)" };
            if (src.HasProperty("baseColorTexture")) m.mainTexture = src.GetTexture("baseColorTexture");
            if (src.HasProperty("baseColorFactor")) m.color = src.GetColor("baseColorFactor");
            var textured = src.HasProperty("metallicRoughnessTexture") && src.GetTexture("metallicRoughnessTexture") != null;
            if (src.HasProperty("metallicFactor")) m.SetFloat("_Metallic", textured ? 0f : src.GetFloat("metallicFactor"));
            if (src.HasProperty("roughnessFactor")) m.SetFloat("_Glossiness", (1f - src.GetFloat("roughnessFactor")) * 0.6f);
            return m;
        }

        void BuildFigure(Material lit)
        {
            Debug.Log("Hypa Stories: storyteller model not found (Assets/HypaStories/Local/Models/storyteller.glb); using a stand-in figure.");
            var cloth = new Material(lit) { color = new Color(0.28f, 0.2f, 0.14f) };
            var skin = new Material(lit) { color = new Color(0.45f, 0.3f, 0.22f) };
            GameObject Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
            {
                var g = GameObject.CreatePrimitive(type);
                g.name = name;
                Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(parent, false);
                g.transform.localPosition = pos; g.transform.localScale = scale;
                g.GetComponent<Renderer>().sharedMaterial = mat;
                return g;
            }
            var body = Part(PrimitiveType.Capsule, "Body", transform, new Vector3(0, SeatTop + 0.33f, 0), new Vector3(0.38f, 0.33f, 0.26f), cloth).transform;
            Part(PrimitiveType.Cube, "Thighs", transform, new Vector3(0, SeatTop + 0.06f, 0.2f), new Vector3(0.34f, 0.13f, 0.42f), cloth);
            Part(PrimitiveType.Cube, "Shins", transform, new Vector3(0, SeatTop / 2f, 0.38f), new Vector3(0.3f, SeatTop, 0.12f), cloth);
            var head = new GameObject("Head").transform;
            head.SetParent(transform, false);
            head.localPosition = new Vector3(0, SeatTop + 0.78f, 0);
            Part(PrimitiveType.Sphere, "Skull", head, Vector3.zero, new Vector3(0.2f, 0.24f, 0.21f), skin);
            _figureMouth = Part(PrimitiveType.Cube, "Mouth", head, new Vector3(0, -0.05f, 0.1f), new Vector3(0.05f, 0.005f, 0.01f),
                new Material(lit) { color = new Color(0.08f, 0.03f, 0.03f) }).transform;
            _figureHead = head;
            Mouth = head;
            _ = body;
        }

        void SetBone(string n, float x, float y, float z)
        {
            if (_b.TryGetValue(n, out var bone) && _base.TryGetValue(n, out var q)) bone.localRotation = q * E(x, y, z);
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime, t = Time.time;
            float D(float a, float b, float rate) => a + (b - a) * (1f - Mathf.Exp(-rate * dt));
            _talk = D(_talk, Level > 0.06f ? 1f : 0f, 5f);
            _attend = D(_attend, Attend, 4f);

            // Head: toward the fire while telling (with a slow drift), toward the listener when asked. Computed in world space so it
            // doesn't depend on the model's bone axes.
            var cam = Camera.main != null ? Camera.main.transform : null;
            var target = _attend > 0.5f && cam != null ? cam.position : Fire != null ? Fire.position + Vector3.up * 0.3f : transform.position + transform.forward;
            var from = transform.position + Vector3.up * (SeatTop + 0.75f);
            var local = transform.InverseTransformDirection(target - from);
            var yawTo = Mathf.Clamp(Mathf.Atan2(local.x, local.z), -0.9f, 0.9f) + (_attend > 0.5f ? 0f : Mathf.Sin(t * 0.4f) * 0.2f);
            var pitchTo = Mathf.Clamp(-Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude), -0.3f, 0.45f);
            _yaw = D(_yaw, yawTo, 3f);
            _pitch = D(_pitch, pitchTo, 3f);

            if (_b.Count > 0)
            {
                // breathing and sway; resting hands, which lift and open while talking (left gestures less than right)
                var br = Mathf.Sin(t * 1.6f) * 0.012f;
                SetBone("Spine", 0.03f + br + _attend * 0.08f, 0f, Mathf.Sin(t * 0.5f) * 0.02f);
                SetBone("Spine1", br * 0.6f, 0f, 0f); SetBone("Spine2", br * 0.6f, 0f, 0f);
                Arm("Left", -1f, 0.55f, 1.7f, t);
                Arm("Right", 1f, 1f, 0f, t);
                if (_b.TryGetValue("Neck", out var neck) && _b.TryGetValue("Head", out var head))
                {
                    neck.localRotation = _base["Neck"];
                    head.localRotation = _base["Head"];
                    var up = transform.up; var right = transform.right;
                    neck.rotation = Quaternion.AngleAxis(_yaw * 0.4f * Mathf.Rad2Deg, up) * Quaternion.AngleAxis(_pitch * 0.4f * Mathf.Rad2Deg, right) * neck.rotation;
                    head.rotation = Quaternion.AngleAxis(_yaw * 0.6f * Mathf.Rad2Deg, up)
                                    * Quaternion.AngleAxis((_pitch * 0.6f + Mathf.Sin(t * 5f) * 0.01f * _talk) * Mathf.Rad2Deg, right) * head.rotation;
                }
            }
            else if (_figureHead != null)
            {
                _figureHead.localRotation = Quaternion.Euler(_pitch * Mathf.Rad2Deg + Mathf.Sin(t * 5f) * 0.6f * _talk, _yaw * Mathf.Rad2Deg, 0f);
                _figureHead.localPosition = new Vector3(0, SeatTop + 0.78f + Mathf.Sin(t * 1.6f) * 0.004f, 0);
            }

            // lip sync
            _open = D(_open, Mathf.Min(1f, Level * 1.2f), 22f);
            _smile = D(_smile, 0.12f + (_attend > 0.5f ? 0.15f : 0f), 3f);
            foreach (var (r, open, smile, openMax, smileMax) in _morphs)
            {
                if (open >= 0) r.SetBlendShapeWeight(open, _open * 0.9f * openMax);
                if (smile >= 0) r.SetBlendShapeWeight(smile, _smile * smileMax);
            }
            foreach (var (skin, mesh, draw) in _baked)
            {
                skin.BakeMesh(mesh, false); // in the renderer's local space; the child sits at its origin
                mesh.RecalculateBounds();
            }
            if (_figureMouth != null) _figureMouth.localScale = new Vector3(0.05f, 0.005f + _open * 0.03f, 0.01f);
        }

        void Arm(string side, float s, float g, float ph, float t)
        {
            // small, open-handed gestures in front of the chest (the prototype's full lift raised the hands beside the head here)
            var lift = _talk * g * (0.5f + Mathf.Sin(t * 2.3f + ph) * 0.3f);
            SetBone(side + "Arm", 0.4f - lift * 0.3f, 0f, -s * (0.32f - _talk * g * 0.06f * Mathf.Sin(t * 1.7f + ph)));
            SetBone(side + "ForeArm", 0.7f + lift * 0.22f, 0f, 0f);
            SetBone(side + "Hand", 0f, 0f, s * _talk * g * 0.25f * Mathf.Sin(t * 3.3f + ph));
        }
    }
}
