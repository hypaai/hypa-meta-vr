using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>Quest performance: heavy scanned meshes are slimmed, light ones are left alone.</summary>
    public class PropDietTests
    {
        static Mesh Sphere()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var m = Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(go);
            return m;
        }

        [Test] public void Cluster_CutsTriangles_KeepsTheShape()
        {
            var src = Sphere(); // 768 triangles
            var slim = PropDiet.Cluster(src, 6);
            Assert.Less(PropDiet.Tris(slim), PropDiet.Tris(src) * 0.6f, "fewer triangles");
            Assert.Greater(PropDiet.Tris(slim), 20, "still a closed shape");
            Assert.AreEqual(src.bounds.size.x, slim.bounds.size.x, 0.25f, "about the same size");
        }

        [Test] public void Lighter_LeavesSmallMeshesAlone()
        {
            var src = Sphere();
            Assert.AreSame(src, PropDiet.Lighter(src), "under the limit: the same mesh");
        }
    }
}
