using System;
using System.Threading.Tasks;

namespace HypaStories
{
    /// <summary>What was heard. Times are in ms (realtime clock): TEnd = end of the question, TTranscript = transcript ready.</summary>
    public sealed class ListenResult
    {
        public string Transcript = "";
        public double TEnd, TTranscript;
        public bool Spoke, Typed;
    }

    /// <summary>One open microphone: resolves when the user goes quiet, or on <see cref="Stop"/> (release of a held Ask).</summary>
    public interface IListenSession
    {
        Task<ListenResult> Result { get; }
        /// <summary>Stop recording and transcribe what was heard.</summary>
        void Stop();
        /// <summary>Stop recording and drop it (a suggested question was picked instead): no transcription call.</summary>
        void Cancel();
    }

    /// <summary>Microphone + speech-to-text. Start throws when there is no microphone (or no permission).</summary>
    public interface IListener
    {
        IListenSession Start(StoryLanguage language, Action<float> onLevel);
    }

    /// <summary>
    /// Ends a recording when the speaker goes quiet: after some speech (over <see cref="MinSpeechMs"/>) followed by
    /// <see cref="SilenceMs"/> of quiet, or at <see cref="MaxMs"/>. Same thresholds as the prototype. Pure, unit tested.
    /// </summary>
    public sealed class SilenceDetector
    {
        public float SilenceMs = 1300f, MinSpeechMs = 350f, MaxMs = 12000f, Threshold = 0.02f;
        public bool Spoke { get; private set; }
        float _speechMs, _quietMs, _elapsedMs;

        /// <summary>Feed the loudness (RMS) of the last <paramref name="dtMs"/>. Returns true when the recording should end.</summary>
        public bool Update(float rms, float dtMs)
        {
            _elapsedMs += dtMs;
            if (rms > Threshold) { Spoke = true; _speechMs += dtMs; _quietMs = 0f; }
            else if (Spoke) _quietMs += dtMs;
            return (Spoke && _speechMs > MinSpeechMs && _quietMs > SilenceMs) || _elapsedMs > MaxMs;
        }
    }
}
