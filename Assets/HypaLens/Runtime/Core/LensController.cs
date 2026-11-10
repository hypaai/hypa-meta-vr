using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;

namespace HypaLens
{
    /// <summary>
    /// Orchestrates capture -> on-device recognition -> (select) -> translate / define / speak.
    /// No scene access here, so it is testable, and nothing heavy runs on the main thread: recognition runs in
    /// Task.Run and API calls are network I/O through HypaCore. Only the selected text is sent; the frame's pixels are
    /// dropped straight after recognition.
    ///
    /// Latency plan for the 3-second target: translation, the dictionary lookup and the source audio start together
    /// when the source language is known; the translation's audio starts as soon as the translation arrives. Results
    /// are cached per (text, source, target) for the session, and pair checks per (source, target).
    /// </summary>
    public sealed class LensController
    {
        readonly IFrameSource _frames;
        readonly ITextRecognizer _recognizer;
        readonly IHypaLensApi _api;
        readonly Func<bool> _isOnline;
        readonly Dictionary<string, LensResult> _results = new Dictionary<string, LensResult>();
        readonly Dictionary<string, bool> _pairs = new Dictionary<string, bool>();

        /// <summary>Hypa language label, or null = auto-detect.</summary>
        public string SourceLanguage { get; set; }
        public string TargetLanguage { get; set; } = "English";

        /// <summary>Pose and intrinsics of the last capture, for placing labels. Pixels are already gone.</summary>
        public CapturedFrame LastFrameMeta { get; private set; }
        public OcrResult LastOcr { get; private set; }

        public LensController(IFrameSource frames, ITextRecognizer recognizer, IHypaLensApi api, Func<bool> isOnline = null)
        {
            _frames = frames; _recognizer = recognizer; _api = api;
            _isOnline = isOnline ?? (() => true);
        }

        /// <summary>Capture one frame and recognise text. Throws LensException (NoTextFound, PermissionDenied, ...).</summary>
        public async Task<OcrResult> CaptureAsync(CancellationToken ct)
        {
            CapturedFrame frame = null;
            try
            {
                frame = await _frames.CaptureAsync(ct);
                var ocr = await Task.Run(() => _recognizer.RecognizeAsync(frame, ct), ct);
                if (ocr == null || ocr.IsEmpty) throw new LensException(LensErrorKind.NoTextFound);

                LastFrameMeta = new CapturedFrame
                {
                    Width = frame.Width, Height = frame.Height,
                    CameraPose = frame.CameraPose, Intrinsics = frame.Intrinsics, Pixels = null,
                };
                LastOcr = ocr;
                return ocr;
            }
            catch (OperationCanceledException) { throw; }
            catch (LensException) { throw; }
            catch (Exception e) { throw new LensException(LensErrorKind.Recognition, e.Message, e); }
            finally { frame?.Dispose(); }
        }

        public static Selection SelectWord(OcrWord w)
            => new Selection { Kind = SelectionKind.Word, Text = w.Text, Word = w, Line = w.Line };

        public static Selection SelectSentence(OcrWord w)
            => new Selection { Kind = SelectionKind.Sentence, Text = w.Line?.Text ?? w.Text, Word = w, Line = w.Line };

        /// <summary>Free check, cached. Null source = auto-detect.</summary>
        public async Task<bool> IsPairSupportedAsync(string source, string target, CancellationToken ct)
        {
            var key = (source ?? "") + "|" + target;
            if (_pairs.TryGetValue(key, out var ok)) return ok;
            ok = await _api.CheckPairAsync(source, target, ct);
            _pairs[key] = ok;
            return ok;
        }

        /// <summary>
        /// Translation is required; meaning and audio are best-effort, so the card is never blank.
        /// <paramref name="onTranslation"/> fires as soon as the translation is in (meaning/audio still loading).
        /// Throws LensException (Offline, UnsupportedPair, Api) for the required part.
        /// </summary>
        public async Task<LensResult> ResolveAsync(Selection sel, CancellationToken ct, Action<LensResult> onTranslation = null)
        {
            if (!_isOnline()) throw new LensException(LensErrorKind.Offline);

            var src = SourceLanguage; var tgt = TargetLanguage;
            var cacheKey = sel.Text + "|" + src + "|" + tgt;
            if (_results.TryGetValue(cacheKey, out var cached)) return cached;
            var isWord = sel.Kind == SelectionKind.Word;

            try
            {
                if (!await IsPairSupportedAsync(src, tgt, ct))
                    throw new LensException(LensErrorKind.UnsupportedPair, $"{src ?? "auto"}->{tgt}");

                var translationTask = _api.TranslateAsync(sel.Text, src, tgt, ct);
                var meaningTask = src != null ? Meaning(sel, src, ct) : null;
                var sourceAudioTask = src != null ? SoftAudio(() => _api.SpeakAsync(sel.Text, src, ct)) : null;

                var tr = await translationTask;
                if (tr == null || string.IsNullOrWhiteSpace(tr.Text))
                    throw new LensException(LensErrorKind.Api, "Hypa returned an empty translation.");

                var detected = src ?? tr.DetectedLanguage; // may still be null (D4: user picks the source)
                var result = new LensResult
                {
                    Selection = sel, SourceLanguage = detected, TargetLanguage = tgt, Translation = tr.Text,
                    MeaningNote = "Looking up meaning...",
                };
                onTranslation?.Invoke(result);

                var targetAudioTask = SoftAudio(() => _api.SpeakAsync(tr.Text, tgt, ct));
                meaningTask = meaningTask ?? (detected != null
                    ? Meaning(sel, detected, ct)
                    : Task.FromResult<(string, string)>((null, "Couldn't tell the source language. Pick it with \"From\" to see a meaning.")));
                sourceAudioTask = sourceAudioTask ?? (detected != null
                    ? SoftAudio(() => _api.SpeakAsync(sel.Text, detected, ct))
                    : Task.FromResult<(HypaSpeech, string)>((null, null)));

                await Task.WhenAll(meaningTask, sourceAudioTask, targetAudioTask);

                (result.Meaning, result.MeaningNote) = meaningTask.Result;
                if (result.Meaning == null && result.MeaningNote == null) result.MeaningNote = "No dictionary entry found.";
                result.SourceAudio = sourceAudioTask.Result.Item1;
                result.TargetAudio = targetAudioTask.Result.Item1;
                if (result.SourceAudio == null && result.TargetAudio == null)
                    result.AudioNote = targetAudioTask.Result.Item2 ?? "Audio isn't available right now.";

                _results[cacheKey] = result;
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { throw LensException.From(e); }
        }

        Task<(string, string)> Meaning(Selection sel, string language, CancellationToken ct)
            => sel.Kind == SelectionKind.Word
                ? Soft(() => _api.LookUpAsync(sel.Text, language, ct))
                : Task.FromResult<(string, string)>((null, "Dictionary meanings are for single words."));

        static async Task<(string, string)> Soft(Func<Task<string>> f)
        {
            try { var s = await f(); return string.IsNullOrWhiteSpace(s) ? (null, "No dictionary entry found.") : (s, null); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return (null, "Dictionary is unavailable right now."); }
        }

        static async Task<(HypaSpeech, string)> SoftAudio(Func<Task<HypaSpeech>> f)
        {
            try { return (await f(), null); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return (null, "Audio is unavailable right now."); }
        }
    }
}
