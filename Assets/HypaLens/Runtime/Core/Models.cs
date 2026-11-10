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

    public readonly struct CameraIntrinsics
    {
        public readonly float Fx, Fy, Cx, Cy;
        public CameraIntrinsics(float fx, float fy, float cx, float cy) { Fx = fx; Fy = fy; Cx = cx; Cy = cy; }
    }

    /// <summary>
    /// One passthrough frame. Lives in memory only: it is disposed right after text recognition and is
    /// never written to disk or sent over the network (privacy requirement in HYP-163).
    /// </summary>
    public sealed class CapturedFrame : IDisposable
    {
        public int Width, Height;
        public Color32[] Pixels;
        public Pose CameraPose;          // world pose of the camera at the moment of capture
        public CameraIntrinsics Intrinsics;

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
