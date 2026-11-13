using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// Editor-only head stand-in on the scene's own camera: hold the right mouse button and move to look around (the story list and
    /// player sit to the right of the fire, as they would in a headset). Does nothing once an XR device is running.
    /// </summary>
    public sealed class EditorMouseLook : MonoBehaviour
    {
        public float Sensitivity = 2.5f;
        float _yaw, _pitch;

        void Start()
        {
            var e = transform.eulerAngles;
            _yaw = e.y; _pitch = e.x > 180f ? e.x - 360f : e.x;
        }

        void Update()
        {
            if (!Application.isEditor || UnityEngine.XR.XRSettings.isDeviceActive || !Input.GetMouseButton(1)) return;
            _yaw += Input.GetAxis("Mouse X") * Sensitivity;
            _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * Sensitivity, -70f, 70f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }
    }
}
