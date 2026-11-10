using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaLens
{
    /// <summary>Where camera frames come from: the Passthrough Camera API on device, a synthetic frame in the simulator.</summary>
    public interface IFrameSource
    {
        Task<CapturedFrame> CaptureAsync(CancellationToken ct);
    }

    /// <summary>On-device text recognition. Must return word + line boxes. Called off the main thread.</summary>
    public interface ITextRecognizer
    {
        Task<OcrResult> RecognizeAsync(CapturedFrame frame, CancellationToken ct);
    }

    /// <summary>
    /// What the Lens needs from Hypa. The production implementation (<see cref="HypaCoreLensApi"/>) goes through
    /// HypaCore only; the simulator uses canned data. Languages are Hypa labels such as "Yoruba".
    /// Implementations may throw HypaException or LensException; the controller maps both.
    /// </summary>
    public interface IHypaLensApi
    {
        Task<IReadOnlyList<HypaLanguage>> ListLanguagesAsync(CancellationToken ct);
        /// <summary>sourceLanguage null = auto-detect.</summary>
        Task<bool> CheckPairAsync(string sourceLanguage, string targetLanguage, CancellationToken ct);
        /// <summary>sourceLanguage null = auto-detect; the detected label is returned when the API reports it.</summary>
        Task<HypaTranslation> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken ct);
        /// <summary>Display text for the meaning, or null when there is none.</summary>
        Task<string> LookUpAsync(string term, string language, CancellationToken ct);
        Task<HypaSpeech> SpeakAsync(string text, string language, CancellationToken ct);
    }

    /// <summary>Pins a point from a camera ray onto a real surface (MRUK on device, a plane in the simulator).</summary>
    public interface ISurfacePlacer
    {
        bool TryPlace(Ray ray, out Vector3 position, out Vector3 normal);
    }
}
