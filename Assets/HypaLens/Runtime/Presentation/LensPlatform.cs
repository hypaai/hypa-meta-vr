using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace HypaLens
{
    /// <summary>
    /// Device-side services for the Lens. HypaLens.Quest registers a factory at startup; the simulator uses Sim*
    /// classes. Pieces left null get an honest "not wired" stand-in so the app shows a clear message, never a crash.
    /// Input is not here: HypaCore's PinchPointer handles hands/mouse for every mode.
    /// </summary>
    public sealed class LensPlatform
    {
        public IFrameSource Frames;
        public ITextRecognizer Recognizer;
        public ISurfacePlacer Placer;

        public static Func<LensPlatform> Factory;
    }

    sealed class UnwiredRecognizer : ITextRecognizer
    {
        public Task<OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken ct)
            => throw new LensException(LensErrorKind.Recognition,
                "On-headset text recognition isn't wired yet (decision D2: pick the engine).");
    }

    sealed class UnwiredPlacer : ISurfacePlacer
    {
        public bool TryPlace(Ray ray, out Vector3 position, out Vector3 normal) { position = default; normal = default; return false; }
    }
}
