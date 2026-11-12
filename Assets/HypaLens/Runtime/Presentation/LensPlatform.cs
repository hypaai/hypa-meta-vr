using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HypaLens
{
    /// <summary>
    /// Device-side services for the Lens. HypaLens.Quest registers a factory at startup (passthrough camera frames,
    /// ML Kit, MRUK); the editor-only simulation uses Sim* classes. Pieces left null get an honest stand-in so the
    /// app shows a clear message, never a crash.
    /// Input is not here: HypaCore's PinchPointer handles hands/mouse for every mode.
    /// </summary>
    public sealed class LensPlatform
    {
        public IFrameSource Frames;
        public ITextRecognizer Recognizer;
        public ISurfacePlacer Placer;

        public static Func<LensPlatform> Factory;
    }

    /// <summary>Used where ML Kit can't run (the editor with the simulated camera off): a clear message, not a crash.</summary>
    sealed class UnavailableRecognizer : ITextRecognizer
    {
        public Task<OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken ct)
            => throw new LensException(LensErrorKind.Recognition,
                "Text is read on the headset with ML Kit, which runs only in a Quest build. In the editor, turn on Use Simulated Camera.");
    }

    sealed class UnwiredPlacer : ISurfacePlacer
    {
        public bool TryPlace(Ray ray, out Vector3 position, out Vector3 normal) { position = default; normal = default; return false; }
    }
}
