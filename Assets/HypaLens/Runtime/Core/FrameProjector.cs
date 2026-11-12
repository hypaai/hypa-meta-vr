using UnityEngine;

namespace HypaLens
{
    /// <summary>
    /// Pixel (camera frame) -> world ray, using the camera pose and intrinsics captured with the frame.
    /// Because the pose is stored at capture time, labels stay pinned to the real surface when the head moves.
    /// </summary>
    public static class FrameProjector
    {
        public static Ray PixelToWorldRay(CapturedFrame frame, Vector2 pixel)
            => PixelToWorldRay(frame.Intrinsics, frame.CameraPose, pixel);

        public static Ray PixelToWorldRay(CameraIntrinsics k, Pose pose, Vector2 pixel)
        {
            // Camera space: +x right, +y up, +z forward. Image space: y grows downward.
            var dirCam = new Vector3((pixel.x - k.Cx) / k.Fx, -(pixel.y - k.Cy) / k.Fy, 1f).normalized;
            return new Ray(pose.position, pose.rotation * dirCam);
        }

        /// <summary>Approximate world size of a pixel rect seen face-on at <paramref name="distance"/> metres.</summary>
        public static Vector2 PixelSizeToWorld(CameraIntrinsics k, FrameRect box, float distance)
            => new Vector2(box.Width * distance / k.Fx, box.Height * distance / k.Fy);

        /// <summary>Rotation that makes a TextMesh/quad readable to the viewer on a surface with this normal.</summary>
        public static Quaternion FaceViewer(Vector3 surfaceNormalTowardViewer, Vector3 worldUp)
            => Quaternion.LookRotation(-surfaceNormalTowardViewer, worldUp);
    }
}
