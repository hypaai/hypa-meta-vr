using System.Threading;
using System.Threading.Tasks;
using Meta.XR; // PassthroughCameraAccess (MRUK v209)
using UnityEngine;
using UnityEngine.Rendering;

namespace HypaLens.Quest
{
    /// <summary>
    /// One passthrough camera frame from MRUK's <see cref="PassthroughCameraAccess"/> (Quest 3/3S, Horizon OS v74+;
    /// also Link and the Meta XR Simulator v85+). Asks for the camera permission on the first capture (after
    /// onboarding has explained why), starts the camera then and keeps it running for later captures.
    /// The frame is read back from the GPU without stalling, with the camera pose and intrinsics converted to the
    /// image's pixels so <see cref="FrameProjector"/> can pin labels.
    /// </summary>
    public sealed class PcaFrameSource : IFrameSource
    {
        const float StartTimeoutSeconds = 5f;
        PassthroughCameraAccess _pca;

        public async Task<CapturedFrame> CaptureAsync(CancellationToken ct)
        {
            if (!PassthroughCameraAccess.IsSupported)
                throw new LensException(LensErrorKind.CameraUnavailable,
                    "This headset doesn't support passthrough camera access (needs Quest 3 or 3S on Horizon OS v74 or newer).");

            await DeviceFrameSource.EnsurePermission(); // explains-then-asks: first capture comes after onboarding

            var pca = GetCameraAccess();
            if (!pca.enabled) pca.enabled = true;
            var timeout = Time.realtimeSinceStartup + StartTimeoutSeconds;
            while (!pca.IsPlaying || pca.CurrentResolution.x < 16)
            {
                ct.ThrowIfCancellationRequested();
                if (Time.realtimeSinceStartup > timeout)
                    throw new LensException(LensErrorKind.CameraUnavailable, "The camera didn't start. Try again in a moment.");
                await Task.Yield(); // next frame (Unity synchronization context)
            }

            var tex = pca.GetTexture();
            if (tex == null) throw new LensException(LensErrorKind.CameraUnavailable, "No camera image yet. Try again.");

            // Pose and intrinsics belong to the image being read back, so take them now, before awaiting.
            var pose = pca.GetCameraPose();
            var k = pca.Intrinsics;
            var size = new Vector2Int(tex.width, tex.height);
            var intrinsics = CameraIntrinsics.FromSensor(k.FocalLength, k.PrincipalPoint, k.SensorResolution, size);

            var pixels = await Readback(tex);
            ct.ThrowIfCancellationRequested();
            return new CapturedFrame { Width = size.x, Height = size.y, Pixels = pixels, CameraPose = pose, Intrinsics = intrinsics };
        }

        PassthroughCameraAccess GetCameraAccess()
        {
            if (_pca != null) return _pca;
            // The scene's "Passthrough Camera Access" building block, if present (kept disabled until first capture).
            _pca = Object.FindAnyObjectByType<PassthroughCameraAccess>(FindObjectsInactive.Include);
            if (_pca == null)
            {
                var go = new GameObject("Hypa Passthrough Camera");
                go.SetActive(false);
                _pca = go.AddComponent<PassthroughCameraAccess>(); // defaults: left camera, 1280x960
                _pca.enabled = false;
                go.SetActive(true);
            }
            if (!_pca.gameObject.activeInHierarchy) _pca.gameObject.SetActive(true);
            return _pca;
        }

        /// <summary>Non-blocking GPU readback (the blocking GetColors() would drop frames). Result is Unity order.</summary>
        static Task<Color32[]> Readback(Texture tex)
        {
            var tcs = new TaskCompletionSource<Color32[]>();
            AsyncGPUReadback.Request(tex, 0, TextureFormat.RGBA32, req =>
            {
                if (req.hasError) tcs.TrySetException(new LensException(LensErrorKind.CameraUnavailable, "Couldn't read the camera image."));
                else tcs.TrySetResult(req.GetData<Color32>().ToArray());
            });
            return tcs.Task;
        }
    }
}
