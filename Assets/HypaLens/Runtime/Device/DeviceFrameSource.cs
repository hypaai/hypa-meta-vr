using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace HypaLens
{
    /// <summary>
    /// Grabs ONE passthrough camera frame via the Passthrough Camera API (WebCamTexture on Quest 3/3S, Horizon OS v74+).
    /// Asks for camera permission first; the frame stays in memory (see CapturedFrame).
    ///
    /// STATUS: permission + frame grab are written but UNTESTED on a headset. Camera intrinsics and the camera's world
    /// pose must come from the Meta SDK (PassthroughCameraUtils in Meta's passthrough camera samples); wire them in via
    /// <see cref="IntrinsicsProvider"/> / <see cref="PoseProvider"/> from HypaLens.Quest. Until then capture fails with a
    /// clear message rather than anchoring labels with guessed numbers.
    /// </summary>
    public sealed class DeviceFrameSource : IFrameSource
    {
        public const string HeadsetCameraPermission = "horizonos.permission.HEADSET_CAMERA";

        public Func<CameraIntrinsics> IntrinsicsProvider;
        public Func<Pose> PoseProvider;

        public async Task<CapturedFrame> CaptureAsync(CancellationToken ct)
        {
            await EnsurePermission();
            if (IntrinsicsProvider == null || PoseProvider == null)
                throw new LensException(LensErrorKind.CameraUnavailable,
                    "Camera intrinsics/pose aren't wired yet (HypaLens.Quest needs the Meta Passthrough Camera API providers).");

            var devices = WebCamTexture.devices;
            if (devices.Length == 0) throw new LensException(LensErrorKind.CameraUnavailable);

            var cam = new WebCamTexture(devices[0].name, 1280, 960, 30);
            try
            {
                cam.Play();
                var timeout = Time.realtimeSinceStartup + 3f;
                while (!cam.didUpdateThisFrame || cam.width < 16)
                {
                    ct.ThrowIfCancellationRequested();
                    if (Time.realtimeSinceStartup > timeout) throw new LensException(LensErrorKind.CameraUnavailable, "Camera timed out.");
                    await Task.Yield();
                }
                var pose = PoseProvider();        // pose at the moment of capture
                var k = IntrinsicsProvider();
                return new CapturedFrame { Width = cam.width, Height = cam.height, Pixels = cam.GetPixels32(), CameraPose = pose, Intrinsics = k };
            }
            finally { cam.Stop(); UnityEngine.Object.Destroy(cam); }
        }

        /// <summary>Asks for the headset camera permission once; throws PermissionDenied if refused. No-op off Android.</summary>
        public static async Task EnsurePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (Permission.HasUserAuthorizedPermission(HeadsetCameraPermission)) return;
            var tcs = new TaskCompletionSource<bool>();
            var cb = new PermissionCallbacks();
            cb.PermissionGranted += _ => tcs.TrySetResult(true);
            cb.PermissionDenied += _ => tcs.TrySetResult(false);
            cb.PermissionDeniedAndDontAskAgain += _ => tcs.TrySetResult(false);
            Permission.RequestUserPermission(HeadsetCameraPermission, cb);
            if (!await tcs.Task) throw new LensException(LensErrorKind.PermissionDenied);
#else
            await Task.CompletedTask;
#endif
        }
    }
}
