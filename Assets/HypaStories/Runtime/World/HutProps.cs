using System.Collections.Generic;
using UnityEngine;
using MeshKit = HypaStories.TrailBuilder.MeshKit;

namespace HypaStories
{
    /// <summary>
    /// The shapes of the hut's household things, made here like everything else in the camp (vertex colours on the pines'
    /// shader): a clay pot, a mortar with its pestle, a palm-rib broom. Each is built standing on the origin (y up), with
    /// boxes for its colliders. A new shape = a case in <see cref="Build"/>; a name it doesn't know gets a plain crate.
    /// </summary>
    public static class HutProps
    {
        /// <summary>Adds the shape to <paramref name="mk"/> and its collider boxes to <paramref name="boxes"/>. False = the stand-in crate.</summary>
        public static bool Build(string prop, MeshKit mk, List<Bounds> boxes)
        {
            switch (prop)
            {
                case "clay-pot": ClayPot(mk, boxes); return true;
                case "mortar-pestle": MortarAndPestle(mk, boxes); return true;
                case "broom": Broom(mk, boxes); return true;
                default: Crate(mk, boxes); return false;
            }
        }

        static readonly Quaternion Id = Quaternion.identity;

        /// <summary>A darker or lighter shade of a colour, still opaque.</summary>
        static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        /// <summary>A round-bellied water pot of fired clay, 26 cm tall, with a darker band round the shoulder.</summary>
        static void ClayPot(MeshKit mk, List<Bounds> boxes)
        {
            var clay = new Color(0.62f, 0.33f, 0.2f);
            mk.Lathe(Vector3.zero, Id, new[]
            {
                (0.26f, 0.088f, 0.088f), (0.245f, 0.074f, 0.074f), (0.215f, 0.08f, 0.08f), (0.18f, 0.125f, 0.125f),
                (0.12f, 0.15f, 0.15f), (0.06f, 0.125f, 0.125f), (0.012f, 0.07f, 0.07f), (0f, 0.055f, 0.055f),
            }, clay, 16);
            mk.Lathe(Vector3.zero, Id, new[] { (0.2f, 0.108f, 0.108f), (0.185f, 0.124f, 0.124f), (0.17f, 0.136f, 0.136f) }, Shade(clay, 0.72f), 16); // the band
            mk.Ellipsoid(new Vector3(0f, 0.25f, 0f), new Vector3(0.074f, 0.006f, 0.074f), Id, new Color(0.07f, 0.05f, 0.04f), 12, 4);             // the dark inside
            mk.Ellipsoid(new Vector3(0f, 0.004f, 0f), new Vector3(0.055f, 0.004f, 0.055f), Id, Shade(clay, 0.6f), 12, 4);                       // its foot, seen when you turn it over
            boxes.Add(new Bounds(new Vector3(0f, 0.13f, 0f), new Vector3(0.24f, 0.26f, 0.24f)));
        }

        /// <summary>A waisted hardwood mortar, 32 cm tall, the pestle standing in it.</summary>
        static void MortarAndPestle(MeshKit mk, List<Bounds> boxes)
        {
            var wood = new Color(0.36f, 0.23f, 0.13f);
            mk.Lathe(Vector3.zero, Id, new[]
            {
                (0.32f, 0.125f, 0.125f), (0.3f, 0.135f, 0.135f), (0.22f, 0.115f, 0.115f), (0.13f, 0.075f, 0.075f),
                (0.05f, 0.11f, 0.11f), (0.012f, 0.125f, 0.125f), (0f, 0.12f, 0.12f),
            }, wood, 14);
            mk.Ellipsoid(new Vector3(0f, 0.305f, 0f), new Vector3(0.112f, 0.012f, 0.112f), Id, Shade(wood, 0.45f), 12, 4); // the hollow
            mk.Ellipsoid(new Vector3(0f, 0.004f, 0f), new Vector3(0.12f, 0.004f, 0.12f), Id, Shade(wood, 0.7f), 12, 4);   // its foot
            var pale = new Color(0.55f, 0.4f, 0.24f);
            var foot = new Vector3(0.02f, 0.25f, 0f);
            var top = new Vector3(0.1f, 0.74f, 0.03f);
            mk.Limb(foot, top, 0.036f, 0.03f, pale, 8);                                      // the pestle, leaning on the rim
            mk.Ellipsoid(foot, new Vector3(0.045f, 0.05f, 0.045f), Id, Shade(pale, 0.9f), 8, 5);   // its heavy end
            boxes.Add(new Bounds(new Vector3(0f, 0.16f, 0f), new Vector3(0.25f, 0.32f, 0.25f)));
            boxes.Add(new Bounds(new Vector3(0.06f, 0.53f, 0.015f), new Vector3(0.11f, 0.42f, 0.08f)));
        }

        /// <summary>A short broom of palm-leaf ribs, tied at one end, lying along z (60 cm).</summary>
        static void Broom(MeshKit mk, List<Bounds> boxes)
        {
            var rib = new Color(0.66f, 0.56f, 0.32f);
            var r = new System.Random(5);
            float Rnd() => (float)r.NextDouble();
            for (var i = 0; i < 22; i++)
            {
                var a = Rnd() * Mathf.PI * 2f;
                var spread = Mathf.Sqrt(Rnd());
                var from = new Vector3(Mathf.Cos(a) * 0.012f * spread, 0.03f + Mathf.Sin(a) * 0.012f * spread, -0.3f);
                var to = new Vector3(Mathf.Cos(a) * 0.075f * spread, 0.03f + Mathf.Abs(Mathf.Sin(a)) * 0.03f * spread, 0.26f + Rnd() * 0.05f);
                var c = Shade(rib, 0.82f + Rnd() * 0.3f);
                mk.Beam(from, to, 0.006f, c);
            }
            // the cord that ties it, twice round the handle end
            var cord = new Color(0.3f, 0.2f, 0.12f);
            mk.Limb(new Vector3(0f, 0.03f, -0.27f), new Vector3(0f, 0.03f, -0.22f), 0.022f, 0.024f, cord, 8);
            mk.Limb(new Vector3(0f, 0.03f, -0.14f), new Vector3(0f, 0.03f, -0.11f), 0.028f, 0.03f, cord, 8);
            boxes.Add(new Bounds(new Vector3(0f, 0.032f, 0f), new Vector3(0.15f, 0.064f, 0.62f)));
        }

        /// <summary>The stand-in for a shape that isn't made yet: a small wooden crate.</summary>
        static void Crate(MeshKit mk, List<Bounds> boxes)
        {
            mk.Box(new Vector3(0f, 0.1f, 0f), new Vector3(0.2f, 0.2f, 0.2f), Id, new Color(0.45f, 0.32f, 0.2f));
            boxes.Add(new Bounds(new Vector3(0f, 0.1f, 0f), new Vector3(0.2f, 0.2f, 0.2f)));
        }
    }
}
