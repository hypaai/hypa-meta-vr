using System.Collections.Generic;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Makes the scanned props (Poly Haven rocks, logs, ferns, grass) affordable on Quest. Measured on a Quest Pro: the camp drew
    /// 2.2 million triangles with the glTF PBR shader and ran at 2-3 fps (GPU-bound, ~540 ms a frame); one boulder alone is 66k
    /// triangles and the camp places it 31 times. Each spawned prop gets:
    ///  - cheap materials: <c>HypaStories/Prop</c> (Lambert, base colour only) or <c>HypaStories/PropCutout</c> for cards, which also
    ///    turns the alpha-blended grass into alpha-tested grass; shared per source material, instanced.
    ///  - lighter meshes: any mesh over <see cref="MaxTris"/> triangles is replaced by a vertex-clustered copy of about
    ///    <see cref="TargetTris"/> (made once per source mesh, then shared). Cards are small meshes and are left alone.
    /// </summary>
    public static class PropDiet
    {
        public const int MaxTris = 6000, TargetTris = 2500;

        static readonly Dictionary<Material, Material> Materials = new Dictionary<Material, Material>();
        static readonly Dictionary<Mesh, Mesh> Meshes = new Dictionary<Mesh, Mesh>();
        static readonly int BaseTex = Shader.PropertyToID("baseColorTexture"), BaseColor = Shader.PropertyToID("baseColorFactor"),
            Cutoff = Shader.PropertyToID("alphaCutoff");

        /// <summary>Applies both to every renderer under <paramref name="root"/>. Null shaders = materials left as they are.</summary>
        public static void Apply(GameObject root, Shader opaque, Shader cutout)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var card = false; // alpha-tested foliage: clustering would wreck the blades, so its mesh stays as it is
                foreach (var m in r.sharedMaterials)
                    if (m != null && (m.IsKeywordEnabled("_ALPHATEST_ON") || m.IsKeywordEnabled("_ALPHABLEND_ON") || m.renderQueue >= 2450 || m.shader == cutout)) card = true;
                if (opaque != null && cutout != null)
                {
                    var mats = r.sharedMaterials;
                    for (var i = 0; i < mats.Length; i++) mats[i] = Cheap(mats[i], opaque, cutout);
                    r.sharedMaterials = mats;
                }
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                if (r is MeshRenderer && !card)
                {
                    var f = r.GetComponent<MeshFilter>();
                    if (f != null && f.sharedMesh != null) f.sharedMesh = Lighter(f.sharedMesh);
                }
            }
        }

        static Material Cheap(Material m, Shader opaque, Shader cutout)
        {
            if (m == null || m.shader == opaque || m.shader == cutout) return m;
            if (Materials.TryGetValue(m, out var cached)) return cached;
            var cut = m.IsKeywordEnabled("_ALPHATEST_ON") || m.IsKeywordEnabled("_ALPHABLEND_ON") || m.renderQueue >= 2450;
            var c = new Material(cut ? cutout : opaque) { name = m.name + " (Quest)", enableInstancing = true };
            var tex = m.HasProperty(BaseTex) ? m.GetTexture(BaseTex) : m.HasProperty("_MainTex") ? m.mainTexture : null;
            c.mainTexture = tex;
            if (tex != null && m.HasProperty(BaseTex)) { c.mainTextureScale = m.GetTextureScale(BaseTex); c.mainTextureOffset = m.GetTextureOffset(BaseTex); }
            c.color = m.HasProperty(BaseColor) ? m.GetColor(BaseColor) : m.HasProperty("_Color") ? m.color : Color.white;
            if (cut) c.SetFloat("_Cutoff", m.HasProperty(Cutoff) ? Mathf.Clamp(m.GetFloat(Cutoff), 0.3f, 0.6f) : 0.5f);
            return Materials[m] = c;
        }

        /// <summary>The mesh, or a lighter copy if it has more than <see cref="MaxTris"/> triangles.</summary>
        public static Mesh Lighter(Mesh src)
        {
            if (src == null || Tris(src) <= MaxTris) return src;
            if (Meshes.TryGetValue(src, out var cached)) return cached;
            if (!src.isReadable) return Meshes[src] = src; // can't read it: keep it (import with Read/Write to slim it)
            Mesh best = null;
            // Grid cells per side for ~TargetTris on a closed surface (verts ≈ 6·r², tris ≈ 2·verts); shrink until it fits.
            for (var r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(TargetTris / 12f) * 1.4f), 8, 64); r >= 6; r -= 3)
            {
                best = Cluster(src, r);
                if (Tris(best) <= TargetTris * 1.3f) break;
            }
            Debug.Log($"Hypa Stories: slimmed {src.name} from {Tris(src) / 1000}k to {Tris(best) / 1000f:0.0}k triangles for Quest.");
            return Meshes[src] = best;
        }

        public static long Tris(Mesh m)
        {
            long t = 0;
            for (var i = 0; i < m.subMeshCount; i++) t += (long)m.GetIndexCount(i) / 3;
            return t;
        }

        /// <summary>
        /// Vertex clustering: snap vertices to an r×r×r grid over the bounds and merge each cell (averaged position and normal, the
        /// first UV). Vertices on different UV islands stay apart (the UV is part of the key), so textures don't smear across seams.
        /// Triangles that collapse are dropped. Pure apart from Unity's Mesh, unit tested.
        /// </summary>
        public static Mesh Cluster(Mesh src, int r)
        {
            var pos = src.vertices; var nrm = src.normals; var uv = src.uv;
            var hasN = nrm != null && nrm.Length == pos.Length; var hasUv = uv != null && uv.Length == pos.Length;
            var b = src.bounds; var size = Vector3.Max(b.size, Vector3.one * 1e-5f);
            var cell = new Dictionary<long, int>();
            var map = new int[pos.Length];
            var outPos = new List<Vector3>(); var outN = new List<Vector3>(); var outUv = new List<Vector2>(); var count = new List<int>();
            for (var i = 0; i < pos.Length; i++)
            {
                var g = Vector3.Scale(pos[i] - b.min, new Vector3(r / size.x, r / size.y, r / size.z));
                long x = Mathf.Clamp((int)g.x, 0, r), y = Mathf.Clamp((int)g.y, 0, r), z = Mathf.Clamp((int)g.z, 0, r);
                long u = hasUv ? Mathf.FloorToInt(uv[i].x * 6f) & 63 : 0, v = hasUv ? Mathf.FloorToInt(uv[i].y * 6f) & 63 : 0;
                var key = (((x * 128 + y) * 128 + z) * 64 + u) * 64 + v;
                if (!cell.TryGetValue(key, out var k))
                {
                    k = outPos.Count; cell[key] = k;
                    outPos.Add(Vector3.zero); outN.Add(Vector3.zero); outUv.Add(hasUv ? uv[i] : Vector2.zero); count.Add(0);
                }
                outPos[k] += pos[i]; if (hasN) outN[k] += nrm[i]; count[k]++;
                map[i] = k;
            }
            for (var k = 0; k < outPos.Count; k++) { outPos[k] /= count[k]; outN[k] = outN[k].normalized; }

            var mesh = new Mesh { name = src.name + " (slim)" };
            if (outPos.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(outPos);
            if (hasUv) mesh.SetUVs(0, outUv);
            mesh.subMeshCount = src.subMeshCount;
            var seen = new HashSet<long>();
            for (var s = 0; s < src.subMeshCount; s++)
            {
                var tri = src.GetTriangles(s);
                var list = new List<int>(tri.Length / 4);
                seen.Clear();
                for (var t = 0; t + 2 < tri.Length; t += 3)
                {
                    int a = map[tri[t]], c = map[tri[t + 1]], d = map[tri[t + 2]];
                    if (a == c || c == d || a == d) continue;
                    // the same triangle twice (two source triangles collapsing onto one)
                    int lo = Mathf.Min(a, Mathf.Min(c, d)), hi = Mathf.Max(a, Mathf.Max(c, d)), mid = a + c + d - lo - hi;
                    if (!seen.Add(((long)lo * 1000003 + mid) * 1000003 + hi)) continue;
                    list.Add(a); list.Add(c); list.Add(d);
                }
                mesh.SetTriangles(list, s, false);
            }
            if (hasN) mesh.SetNormals(outN); else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }
    }
}
