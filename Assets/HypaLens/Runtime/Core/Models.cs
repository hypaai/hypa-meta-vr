using System;
using System.Collections.Generic;
using HypaCore;
using UnityEngine;

namespace HypaLens
{
    /// <summary>Pixel rectangle in camera-frame space. Origin top-left, y grows downward (OCR convention).</summary>
    public readonly struct FrameRect
    {
        public readonly float X, Y, Width, Height;
        public FrameRect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        public Vector2 Center => new Vector2(X + Width * 0.5f, Y + Height * 0.5f);
    }

    public sealed class OcrWord
    {
        public string Text;
        public FrameRect Box;
        public OcrLine Line; // back-reference so a long pinch can grab the whole sentence
    }

    public sealed class OcrLine
    {
        public string Text;
        public FrameRect Box;
        public readonly List<OcrWord> Words = new List<OcrWord>();
    }

    public sealed class OcrResult
    {
        public int FrameWidth, FrameHeight;
        public readonly List<OcrLine> Lines = new List<OcrLine>();
        public bool IsEmpty => Lines.Count == 0;
    }

    /// <summary>Pinhole intrinsics in the captured image's pixels (origin top-left, y down).</summary>
    public readonly struct CameraIntrinsics
    {
        public readonly float Fx, Fy, Cx, Cy;
        public CameraIntrinsics(float fx, float fy, float cx, float cy) { Fx = fx; Fy = fy; Cx = cx; Cy = cy; }

        /// <summary>
        /// Converts sensor intrinsics (Meta Passthrough Camera API: focal length and principal point in sensor
        /// pixels) into intrinsics for an image of <paramref name="image"/> pixels, which the camera produces by
        /// centre-cropping the sensor to the image's aspect ratio and scaling. Mirrors MRUK's
        /// PassthroughCameraAccess.ViewportPointToRay, but in the top-left/y-down pixel space OCR boxes use.
        /// </summary>
        public static CameraIntrinsics FromSensor(Vector2 focal, Vector2 principal, Vector2Int sensor, Vector2Int image)
        {
            var scale = new Vector2((float)image.x / sensor.x, (float)image.y / sensor.y);
            scale /= Mathf.Max(scale.x, scale.y);
            var crop = new Rect(sensor.x * (1f - scale.x) * 0.5f, sensor.y * (1f - scale.y) * 0.5f, sensor.x * scale.x, sensor.y * scale.y);
            var ppUp = new Vector2(principal.x, sensor.y - principal.y); // MRUK's bottom-up principal point
            var sx = image.x / crop.width;
            var sy = image.y / crop.height;
            return new CameraIntrinsics(
                focal.x * sx, focal.y * sy,
                (ppUp.x - crop.x) * sx,
                (crop.y + crop.height - ppUp.y) * sy);
        }
    }

    /// <summary>
    /// One passthrough frame. Lives in memory only: it is disposed right after text recognition and is
    /// never written to disk or sent over the network (privacy requirement in HYP-163).
    /// </summary>
    public sealed class CapturedFrame : IDisposable
    {
        public int Width, Height;
        /// <summary>RGBA pixels in Unity order: bottom row first (as Texture2D/AsyncGPUReadback return them).</summary>
        public Color32[] Pixels;
        public Pose CameraPose;          // world pose of the camera at the moment of capture
        public CameraIntrinsics Intrinsics;
        /// <summary>Set when this is a crop of a larger frame (live mode): where it sits in the full frame, whose size is FullWidth x FullHeight.</summary>
        public int CropX, CropY, FullWidth, FullHeight;
        public bool IsCrop => FullWidth > 0 && (Width != FullWidth || Height != FullHeight || CropX != 0 || CropY != 0);

        public void Dispose() { Pixels = null; }
    }

    public enum SelectionKind { Word, Sentence }

    public sealed class Selection
    {
        public SelectionKind Kind;
        public string Text;
        public OcrWord Word;   // set for Word, and for Sentence it is the word that was pinched
        public OcrLine Line;
    }

    public sealed class LensResult
    {
        public Selection Selection;
        public string SourceLanguage;     // label; null when auto-detect could not tell
        public string TargetLanguage;
        public string Translation;
        public string Meaning;            // null when not applicable or unavailable
        public string MeaningNote;        // human-readable reason when Meaning is null
        public HypaSpeech SourceAudio;    // null when unavailable
        public HypaSpeech TargetAudio;
        public string AudioNote;
    }

    [Serializable]
    public sealed class SavedItem
    {
        public string Id;
        public string Source;
        public string Translation;
        public string SourceLanguage;
        public string TargetLanguage;
        public string Meaning;
        public long SavedAtUnix;
    }
}
