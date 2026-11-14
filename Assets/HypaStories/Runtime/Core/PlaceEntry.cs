using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Where you stand when you arrive at a place (ticket 3F): this transform's position, on the floor, facing its forward
    /// (blue arrow). One per place scene. <see cref="Places.GoTo"/> moves the place so this lands under your head, facing
    /// the way you face, and never moves the camera.
    /// </summary>
    public sealed class PlaceEntry : MonoBehaviour
    {
        [Tooltip("What you see beyond the place (the camera's clear colour) while you're here. The camp's comes back when you return.")]
        public Color Background = new Color(0.05f, 0.04f, 0.035f);

        /// <summary>The way you face on arrival, flattened to the ground.</summary>
        public Vector3 Forward
        {
            get
            {
                var f = transform.forward; f.y = 0f;
                return f.sqrMagnitude < 1e-6f ? Vector3.forward : f.normalized;
            }
        }

        void OnDrawGizmos()
        {
            var p = transform.position;
            Gizmos.color = new Color(1f, 0.66f, 0.32f);
            Gizmos.DrawWireSphere(p, 0.25f);
            Gizmos.DrawLine(p + Vector3.up * 1.6f, p);
            Gizmos.color = new Color(0.3f, 0.6f, 1f);
            Gizmos.DrawLine(p + Vector3.up * 0.05f, p + Vector3.up * 0.05f + Forward * 0.8f);
        }
    }
}
