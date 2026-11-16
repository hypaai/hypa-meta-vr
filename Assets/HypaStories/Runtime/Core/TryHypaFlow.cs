using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using UnityEngine;

namespace HypaStories
{
    public enum TryStatus { Idle, Listening, Transcribing, Working }
    public enum DictTab { Source, Target }

    /// <summary>What "Try Hypa" needs from Hypa, behind a seam so the flow is unit tested without a network.</summary>
    public interface ITryApi
    {
        Task<IReadOnlyList<HypaLanguage>> LanguagesAsync(CancellationToken ct);
        /// <summary>sourceLang null = auto-detect.</summary>
        Task<HypaTranslation> TranslateAsync(string text, string targetLang, string sourceLang, CancellationToken ct);
        Task<HypaDictionaryEntry> LookUpAsync(string word, string language, CancellationToken ct);
        /// <summary>Resolves when the speech has been fetched and queued. voice null = Auto.</summary>
        Task SpeakAsync(string text, string language, string voice, CancellationToken ct);
        /// <summary>Why the text translates as it does (the Thinking button).</summary>
        Task<string> ExplainAsync(string sourceText, string targetText, string sourceLang, string targetLang, CancellationToken ct);
        bool IsPlaying { get; }
        void StopSpeaking();
    }

    /// <summary>Hypa's MCP tools (translate_message, look_up_term, text_to_speech) and the shared audio player.</summary>
    public sealed class HypaTryApi : ITryApi
    {
        readonly HypaMcpClient _mcp = new HypaMcpClient();
        readonly HypaLanguages _languages;
        public HypaTryApi() { _languages = new HypaLanguages(_mcp); }

        public Task<IReadOnlyList<HypaLanguage>> LanguagesAsync(CancellationToken ct) => _languages.GetAsync(ct);
        public Task<HypaTranslation> TranslateAsync(string text, string targetLang, string sourceLang, CancellationToken ct) => _mcp.TranslateAsync(text, targetLang, sourceLang, ct);
        public Task<HypaDictionaryEntry> LookUpAsync(string word, string language, CancellationToken ct) => _mcp.LookUpTermAsync(word, language, ct);

        public async Task SpeakAsync(string text, string language, string voice, CancellationToken ct)
        {
            var speech = await _mcp.TextToSpeechAsync(text, language, ct, voice);
            await HypaAudioPlayer.Instance.PlayNowAsync(speech, ct);
        }

        public Task<string> ExplainAsync(string sourceText, string targetText, string sourceLang, string targetLang, CancellationToken ct) => _mcp.ExplainTranslationAsync(sourceText, targetText, sourceLang, targetLang, ct);

        public bool IsPlaying => HypaAudioPlayer.Instance.IsPlaying || HypaAudioPlayer.Instance.Queued > 0;
        public void StopSpeaking() => HypaAudioPlayer.Instance.Stop(0.1f);
    }

    /// <summary>One tab of the dictionary: the word, its language, and what the lookup returned (Hypa Translate's source / target tab).</summary>
    public sealed class DictSide
    {
        public string Word = "", Language = "";
        public HypaDictionaryCard Card;
        public bool Loading, Failed;
    }

    /// <summary>
    /// "Try Hypa", Hypa Translate (hypa-v2/apps/hypa-translate) at the end of a story, voice only. Same behaviour as the app:
    ///  - pick a source (or Auto detect) and a target language, swap them; speak into the input (it is typed in) and Translate;
    ///  - the speaker plays the input or the translation (the voice menu is Auto or one of the target language's voices);
    ///  - when the source is a single word, a dictionary icon appears next to the translation. It opens a panel with two tabs, the
    ///    source word and the translated word, each looked up (look_up_term) the first time its tab is shown, with a Retry on failure.
    /// No keyboard, no image translation. Pure logic over <see cref="ITryApi"/> and <see cref="IListener"/>; <see cref="TryHypaView"/>
    /// draws it. Results of a superseded request are dropped.
    /// </summary>
    public sealed class TryHypaFlow
    {
        /// <summary>Shown when the language list can't be fetched (offline): enough to try it.</summary>
        static readonly string[] Fallback = { "English", "French", "Spanish", "Yoruba", "Igbo", "Hausa", "Swahili", "Portuguese", "Arabic", "German" };
        /// <summary>Listed first in the picker (Hypa's home languages, then the biggest).</summary>
        static readonly string[] Pinned =
            { "English", "Yoruba", "Igbo", "Hausa", "Swahili", "Nigerian Pidgin", "French", "Spanish", "Portuguese", "Arabic", "German", "Chinese" };

        public const string VoiceAuto = "Auto";

        readonly ITryApi _api;
        readonly IListener _listener;
        /// <summary>
        /// Show words as they are said (the microphone runs until you tap stop, streaming to live transcription). Off, it
        /// records the way the storyteller's Ask does: until you stop talking (or tap stop), then transcribes once.
        /// </summary>
        public bool LiveText = true;
        CancellationTokenSource _cts = new CancellationTokenSource();
        IListenSession _session;
        int _version, _dictVersionS, _dictVersionT;
        Task _loading;
        string _translatedFrom = "", _hint = "", _copied = "", _base = "";
        string _flash;
        float _flashLeft;
        int _thinkVersion;

        public TryStatus Status { get; private set; }
        /// <summary>Why the last attempt failed, or a hint. Empty when all is well.</summary>
        public string Message { get; private set; } = "";
        /// <summary>What was said. Typed into the input box.</summary>
        public string Input { get; private set; } = "";
        public string Output { get; private set; } = "";
        public string DetectedLanguage { get; private set; }
        public float Level { get; private set; }
        /// <summary>A neutral note (copied, test phrase). Errors are in <see cref="Message"/>.</summary>
        public string Info => string.IsNullOrEmpty(_copied) ? _hint : _copied;
        /// <summary>The Thinking button: the explanation of the translation, shown under it while on.</summary>
        public string Thinking { get; private set; } = "";
        public bool ThinkingOpen { get; private set; }
        public bool ThinkingLoading { get; private set; }

        /// <summary>null = auto-detect.</summary>
        public HypaLanguage Source { get; private set; }
        public HypaLanguage Target { get; private set; }
        /// <summary>The voice for the translation: <see cref="VoiceAuto"/> or one of the target language's voices.</summary>
        public string Voice { get; private set; } = VoiceAuto;
        public IReadOnlyList<HypaLanguage> Languages { get; private set; } = new HypaLanguage[0];
        /// <summary>Words from the story offered as chips, in story order.</summary>
        public string[] Suggestions { get; private set; } = new string[0];
        string[] _cycle = new string[0];

        // speech: which button is speaking ("input", "output", "word", "definition", "usage", "synonyms", "antonyms", "cultural", "fun")
        public string ActiveAudio { get; private set; }
        public bool AudioLoading { get; private set; }

        // dictionary
        public bool DictionaryOpen { get; private set; }
        public DictTab Tab { get; private set; } = DictTab.Source;
        public DictSide DictSource { get; } = new DictSide();
        public DictSide DictTarget { get; } = new DictSide();

        public event Action Changed;

        public TryHypaFlow(ITryApi api, IListener listener, string targetLanguage = null)
        {
            _api = api; _listener = listener;
            SetLanguages(Fallback.Select(n => new HypaLanguage { Name = n, Code = n.ToLowerInvariant().Substring(0, 2), Voices = new[] { "default" } }).ToList());
            var want = string.IsNullOrEmpty(targetLanguage) || targetLanguage == "English" ? "Yoruba" : targetLanguage;
            Target = Find(want) ?? Languages[1];
        }

        void Notify() => Changed?.Invoke();

        void SetLanguages(IReadOnlyList<HypaLanguage> list)
        {
            Languages = list;
            if (Source != null) Source = Find(Source.Name) ?? Source;
            if (Target != null) Target = Find(Target.Name) ?? Target;
            Voice = DefaultVoice();
        }

        public HypaLanguage Find(string nameOrCode) => HypaLanguages.Find(Languages, nameOrCode);

        /// <summary>The language list for the picker: pinned ones first, then the rest A to Z.</summary>
        public IReadOnlyList<HypaLanguage> Ordered()
        {
            var pinned = Pinned.Select(Find).Where(l => l != null).ToList();
            var rest = Languages.Where(l => !pinned.Contains(l)).OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase);
            return pinned.Concat(rest).ToList();
        }

        /// <summary>Fetches the full language list (free tool, once). Keeps the short fallback list when offline.</summary>
        public Task LoadLanguagesAsync()
        {
            if (_loading != null && !_loading.IsFaulted) return _loading;
            return _loading = Load();

            async Task Load()
            {
                try
                {
                    var all = await _api.LanguagesAsync(_cts.Token);
                    if (all != null && all.Count > 0) { SetLanguages(all.Where(l => !string.IsNullOrEmpty(l.Name)).ToList()); Notify(); }
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { Debug.LogWarning("Hypa Stories: couldn't load the language list: " + e.Message); }
            }
        }

        // ---------- choices ----------

        public static bool IsSingleWord(string text) => !string.IsNullOrWhiteSpace(text) && !text.Trim().Any(char.IsWhiteSpace);

        /// <summary>The dictionary icon: a translation of a single word.</summary>
        public bool CanShowDictionary => !string.IsNullOrEmpty(Output) && IsSingleWord(_translatedFrom);

        /// <summary>Source language for a dictionary lookup and the tab label: the one picked, else the one detected, else English.</summary>
        public string SourceName => Source?.Name ?? (string.IsNullOrEmpty(DetectedLanguage) ? "English" : DetectedLanguage);

        /// <summary>Source language, or null for auto-detect.</summary>
        public void SetSource(HypaLanguage l) { if (l?.Name == Source?.Name) return; Source = l; ResetTranslation(); Notify(); }

        /// <summary>The story's language is the source. The target stays whatever you last chose, unless that is the same language.</summary>
        public void BindStory(string storyLanguage, IReadOnlyList<string> targets)
        {
            _cycle = (targets ?? new string[0]).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToArray();
            var src = Find(storyLanguage) ?? Bare(storyLanguage);
            SetSource(src);
            if (Target == null || string.Equals(Target.Name, src?.Name, StringComparison.OrdinalIgnoreCase))
            {
                var other = _cycle.FirstOrDefault(n => !string.Equals(n, src?.Name, StringComparison.OrdinalIgnoreCase)) ?? "English";
                SetTarget(Find(other) ?? Bare(other));
            }
        }

        /// <summary>The next story language, skipping the one the story is being told in.</summary>
        public void CycleTarget()
        {
            if (_cycle.Length == 0) return;
            var i = Array.FindIndex(_cycle, n => string.Equals(n, Target?.Name, StringComparison.OrdinalIgnoreCase));
            for (var k = 1; k <= _cycle.Length; k++)
            {
                var name = _cycle[(i + k) % _cycle.Length];
                if (string.Equals(name, Source?.Name, StringComparison.OrdinalIgnoreCase)) continue;
                SetTarget(Find(name) ?? Bare(name));
                return;
            }
        }

        public void SetSuggestions(string[] words) { Suggestions = words ?? new string[0]; Notify(); }

        static HypaLanguage Bare(string name) => new HypaLanguage { Name = name ?? "", Code = name ?? "", Voices = new[] { "auto" } };

        /// <summary>A suggested word: translate it, then say the translation.</summary>
        public async Task UseWordAsync(string word)
        {
            if (string.IsNullOrWhiteSpace(word) || Listening) return;
            Input = word.Trim();
            Notify();
            await RunAsync();
            if (Status == TryStatus.Idle && !string.IsNullOrWhiteSpace(Output) && string.IsNullOrEmpty(Message))
                await SpeakOutputAsync();
        }

        /// <summary>Tap to speak. Tap again to stop. The words are translated and then spoken back.</summary>
        public async Task DictateAsync()
        {
            if (Listening) { await ToggleMicAsync(); return; }
            await ToggleMicAsync();
            if (Status != TryStatus.Idle || string.IsNullOrWhiteSpace(Input) || !string.IsNullOrEmpty(Message)) return;
            await RunAsync();
            if (Status == TryStatus.Idle && !string.IsNullOrWhiteSpace(Output) && string.IsNullOrEmpty(Message))
                await SpeakOutputAsync();
        }

        public void SetTarget(HypaLanguage l)
        {
            if (l == null || l.Name == Target?.Name) return;
            Target = l; Voice = DefaultVoice();
            HypaSettings.TargetLanguage = l.Name;
            ResetTranslation(); Notify();
            // Hypa Translate's default setting: changing the target translates again what is already in the input
            if (!string.IsNullOrWhiteSpace(Input)) _ = RunAsync();
        }

        /// <summary>The voices to choose from for the translation: Auto, then the target language's own.</summary>
        public IReadOnlyList<string> VoiceOptions() => new[] { VoiceAuto }.Concat(Target?.Voices ?? new string[0]).Distinct().ToList();

        readonly Dictionary<string, string> _voiceByLanguage = new Dictionary<string, string>();

        /// <summary>As Hypa Translate: Auto (Hypa's router picks the voice) until you pick one for this language; then that one.</summary>
        string DefaultVoice()
        {
            if (Target != null && _voiceByLanguage.TryGetValue(Target.Name, out var v) && VoiceOptions().Contains(v)) return v;
            return VoiceAuto;
        }

        public void SetVoice(string voice)
        {
            Voice = string.IsNullOrEmpty(voice) ? VoiceAuto : voice;
            if (Target != null) _voiceByLanguage[Target.Name] = Voice;
            Notify();
        }

        /// <summary>Changing a language or the text clears the translation (Hypa Translate does the same); Translate runs it again.</summary>
        void ResetTranslation()
        {
            _version++;
            StopSpeaking();
            Output = ""; DetectedLanguage = null; _translatedFrom = ""; Message = ""; _copied = "";
            Thinking = ""; ThinkingOpen = false; ThinkingLoading = false; _thinkVersion++;
            if (Status == TryStatus.Working) Status = TryStatus.Idle;
            ResetDictionary();
        }

        /// <summary>Swap the two languages and the two texts. Not from auto-detect, as in Hypa Translate.</summary>
        public void Swap()
        {
            if (Source == null) { Message = "Pick the first language to swap them."; Notify(); return; }
            Cancel();
            var s = Source; Source = Target; Target = s;
            Voice = DefaultVoice();
            if (!string.IsNullOrWhiteSpace(Output)) { var t = Input; Input = Output; Output = t; }
            HypaSettings.TargetLanguage = Target.Name;
            ResetTranslation();
            Notify();
        }

        public void Clear()
        {
            Cancel();
            Input = Output = Message = ""; DetectedLanguage = null; _translatedFrom = ""; _hint = _copied = "";
            Status = TryStatus.Idle;
            ResetDictionary();
            Notify();
        }

        // ---------- speaking into the input ----------

        public bool Listening => Status == TryStatus.Listening;

        /// <summary>Tap the mic: start listening; tap again to stop. When the transcript arrives it becomes the input and is translated.</summary>
        public async Task ToggleMicAsync()
        {
            if (Status == TryStatus.Listening) { _session?.Stop(); Status = TryStatus.Transcribing; Level = 0f; Notify(); return; }
            if (Status == TryStatus.Transcribing) return;
            StopSpeaking();
            var version = ++_version;
            Message = ""; _hint = ""; _copied = "";
            // dictation adds to what is already there, so you can speak a sentence at a time
            _base = Input?.Trim() ?? "";
            if (!string.IsNullOrEmpty(Output)) { Output = ""; _translatedFrom = ""; ResetDictionary(); }
            Status = TryStatus.Listening; Level = 0f;
            Notify();
            var live = LiveText ? _listener as IPartialListener : null;
            try
            {
                // a handler also switches the microphone to "until you tap stop" and live partial results
                live?.SetPartialHandler(t => { if (version == _version && Status == TryStatus.Listening) { Input = Join(_base, t); Notify(); } });
                _session = _listener.Listen(ListenLanguage(), l => { Level = l; });
                var heard = await _session.Result;
                live?.SetPartialHandler(null);
                if (version != _version) return;
                _session = null; Level = 0f;
                Debug.Log($"Hypa Stories: translate mic spoke={heard.Spoke} peak={heard.Peak:0.000} live={live != null} heard \"{heard.Transcript}\"");
                if (!heard.Spoke || string.IsNullOrWhiteSpace(heard.Transcript))
                {
                    Status = TryStatus.Idle;
                    Input = _base;
                    Message = heard.Peak < 0.002f && !heard.Spoke
                        ? NoSound
                        : "I didn't catch that. Tap the mic and try again.";
                    Notify();
                    return;
                }
                Input = Join(_base, heard.Transcript.Trim());
                if (Input.Length > MaxChars) Input = Input.Substring(0, MaxChars);
                _hint = heard.Typed ? "Test phrase: this Mac won't give Unity its microphone. Start Unity from Terminal to use your voice." : "";
                Status = TryStatus.Idle;
                Notify(); // as on the web, Translate is pressed by you
            }
            catch (OperationCanceledException) { live?.SetPartialHandler(null); }
            catch (Exception e) when (version == _version)
            {
                live?.SetPartialHandler(null);
                _session = null; Level = 0f;
                Input = _base;
                Fail(e, e is InvalidOperationException ? (e.Message.StartsWith("Allow the microphone") ? e.Message : "The microphone isn't available.") : null);
            }
        }

        public const int MaxChars = 500;

        /// <summary>What to check when the microphone gave only silence, for this device.</summary>
        static string NoSound => Application.platform == RuntimePlatform.Android
            ? "No sound is reaching the microphone. Check that the headset's microphone is on, and that the app may use it (Settings > Apps > Permissions)."
            : "No sound is reaching the microphone. Check that Unity is allowed to use it (System Settings > Privacy > Microphone).";

        static string Join(string a, string b) => string.IsNullOrEmpty(a) ? b : string.IsNullOrEmpty(b) ? a : a + " " + b;

        StoryLanguage ListenLanguage() => Source == null ? new StoryLanguage("", "Auto", null) : new StoryLanguage(Source.Code, Source.Name, Source.Name);

        // ---------- translate ----------

        /// <summary>Translate the current input (the Translate button; a finished recording calls it too).</summary>
        public async Task RunAsync()
        {
            if (string.IsNullOrWhiteSpace(Input) || Status == TryStatus.Listening || Status == TryStatus.Transcribing) return;
            var version = ++_version;
            var ct = _cts.Token;
            var text = Input.Trim();
            Message = ""; _copied = ""; Status = TryStatus.Working; Output = ""; _translatedFrom = ""; Thinking = ""; ThinkingOpen = false;
            ResetDictionary();
            Notify();
            try
            {
                var r = await _api.TranslateAsync(text, Target.Name, Source?.Name, ct);
                if (version != _version) return;
                Output = r?.Text ?? "";
                DetectedLanguage = r?.DetectedLanguage;
                _translatedFrom = text;
                Voice = DefaultVoice();
                if (string.IsNullOrWhiteSpace(Output)) Message = "Hypa couldn't translate that. Try saying it again.";
                Status = TryStatus.Idle;
                Notify();
            }
            catch (OperationCanceledException) { }
            catch (Exception e) when (version == _version) { Fail(e, null); }
        }

        // ---------- the dictionary ----------

        void ResetDictionary()
        {
            _dictVersionS++; _dictVersionT++;
            DictionaryOpen = false; Tab = DictTab.Source;
            foreach (var d in new[] { DictSource, DictTarget }) { d.Word = d.Language = ""; d.Card = null; d.Loading = d.Failed = false; }
        }

        /// <summary>The dictionary icon: opens on the source word's tab and looks it up.</summary>
        public void OpenDictionary()
        {
            if (!CanShowDictionary) return;
            DictionaryOpen = true; Tab = DictTab.Source;
            Notify();
            _ = FetchDictionaryAsync(DictTab.Source);
        }

        public void CloseDictionary() { if (!DictionaryOpen) return; StopSpeaking(); DictionaryOpen = false; Notify(); }

        public void SetTab(DictTab tab)
        {
            if (!DictionaryOpen) return;
            Tab = tab;
            StopSpeaking();
            Notify();
            _ = FetchDictionaryAsync(tab);
        }

        public DictSide Side(DictTab tab) => tab == DictTab.Source ? DictSource : DictTarget;
        public string TabLabel(DictTab tab) => tab == DictTab.Source ? SourceName : Target?.Name ?? "";

        /// <summary>Looks the tab's word up the first time it is shown (not again once loaded), or again after a failure (Retry).</summary>
        public async Task FetchDictionaryAsync(DictTab tab)
        {
            var side = Side(tab);
            var word = (tab == DictTab.Source ? Input : Output)?.Trim();
            var language = tab == DictTab.Source ? SourceName : Target?.Name;
            if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(language)) return;
            if (side.Loading || (side.Card != null && side.Word == word)) return;
            var version = tab == DictTab.Source ? ++_dictVersionS : ++_dictVersionT;
            side.Word = word; side.Language = language; side.Card = null; side.Failed = false; side.Loading = true;
            Notify();
            try
            {
                var entry = await _api.LookUpAsync(word, language, _cts.Token);
                if (version != (tab == DictTab.Source ? _dictVersionS : _dictVersionT)) return;
                var card = entry?.ToCard(word);
                side.Card = card != null && !card.IsEmpty ? card : null; // empty: "No definition found"
                side.Loading = false;
                Notify();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (version != (tab == DictTab.Source ? _dictVersionS : _dictVersionT)) return;
                side.Loading = false; side.Failed = true;
                if (!(e is HypaException)) Debug.LogWarning("Hypa Stories: dictionary lookup failed: " + e.Message);
                Notify();
            }
        }

        // ---------- hearing it ----------

        /// <summary>
        /// Speak <paramref name="text"/> in <paramref name="language"/>; <paramref name="key"/> names the button, so it can show
        /// loading and stop. Tapping the button that is speaking stops it. The voice menu only applies to the translation.
        /// </summary>
        public async Task SpeakAsync(string key, string text, HypaLanguage language)
        {
            if (ActiveAudio == key) { StopSpeaking(); return; }
            if (string.IsNullOrWhiteSpace(text)) return;
            if (language == null) { Message = "Pick the language first, then I can say it."; Notify(); return; }
            if (!language.HasTts) { Message = "There's no voice for " + language.Name + " yet."; Notify(); return; }
            _api.StopSpeaking();
            var ct = _cts.Token;
            ActiveAudio = key; AudioLoading = true; Message = "";
            Notify();
            try
            {
                await _api.SpeakAsync(text, language.Name, key == "output" && Voice != VoiceAuto ? Voice : null, ct);
                if (ActiveAudio == key) { AudioLoading = false; Notify(); }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (ActiveAudio == key) { ActiveAudio = null; AudioLoading = false; }
                Fail(e, null);
                // the translation is already on screen: the red note only needs a moment, then the words come back
                _flash = Message;
                _flashLeft = 4f;
            }
        }

        /// <summary>The speaker under the input (what was said) or under the translation.</summary>
        public Task SpeakInputAsync() => SpeakAsync("input", Input, Source ?? Find(DetectedLanguage));
        public Task SpeakOutputAsync() => SpeakAsync("output", Output, Target);

        public void StopSpeaking()
        {
            if (ActiveAudio == null) return;
            ActiveAudio = null; AudioLoading = false;
            _api.StopSpeaking();
            Notify();
        }

        /// <summary>Per frame: clears the speaker's state when the audio has finished playing, and lets a failed reading's note fade.</summary>
        public void Tick(float dt)
        {
            if (ActiveAudio != null && !AudioLoading && !_api.IsPlaying) { ActiveAudio = null; Notify(); }
            if (_flash == null) return;
            _flashLeft -= dt;
            if (_flashLeft > 0f) return;
            var gone = _flash;
            _flash = null;
            if (Message == gone) { Message = ""; Notify(); }
        }

        // ---------- copy, thinking ----------

        /// <summary>The copy buttons.</summary>
        public void Copy(bool output)
        {
            var text = output ? Output : Input;
            if (string.IsNullOrWhiteSpace(text)) return;
            GUIUtility.systemCopyBuffer = text;
            _copied = "Copied to clipboard";
            Notify();
        }

        /// <summary>The brain button: explain the translation (fetched the first time), show or hide it.</summary>
        public async Task ToggleThinkingAsync()
        {
            if (string.IsNullOrEmpty(Output) || ThinkingLoading) return;
            if (ThinkingOpen) { ThinkingOpen = false; Notify(); return; }
            if (!string.IsNullOrEmpty(Thinking)) { ThinkingOpen = true; Notify(); return; }
            var v = ++_thinkVersion;
            ThinkingLoading = true; ThinkingOpen = true; Message = "";
            Notify();
            try
            {
                var t = await _api.ExplainAsync(_translatedFrom, Output, Source?.Name ?? DetectedLanguage, Target.Name, _cts.Token);
                if (v != _thinkVersion) return;
                Thinking = t ?? "";
                ThinkingLoading = false;
                if (string.IsNullOrWhiteSpace(Thinking)) { ThinkingOpen = false; Message = "No explanation was available for this translation."; }
                Notify();
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                if (v != _thinkVersion) return;
                ThinkingLoading = false; ThinkingOpen = false;
                Fail(e, null);
            }
        }

        // ---------- lifecycle ----------

        /// <summary>Stops the microphone, speech and any request in flight (closing the panel). Texts stay for next time.</summary>
        public void Cancel()
        {
            _version++; _dictVersionS++; _dictVersionT++;
            _session?.Cancel(); _session = null;
            (_listener as IPartialListener)?.SetPartialHandler(null);
            _cts.Cancel(); _cts.Dispose(); _cts = new CancellationTokenSource();
            _loading = null;
            if (ActiveAudio != null) { ActiveAudio = null; AudioLoading = false; _api.StopSpeaking(); }
            DictSource.Loading = DictTarget.Loading = false;
            Level = 0f;
            Status = TryStatus.Idle;
        }

        void Fail(Exception e, string fallback)
        {
            Status = TryStatus.Idle;
            Message = e is HypaException h ? h.UserMessage() : fallback ?? "Something went wrong. Try again.";
            Debug.LogWarning("Hypa Stories: Try Hypa failed: " + e.Message);
            Notify();
        }
    }
}
