using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;
using NUnit.Framework;

namespace HypaStories.Tests
{
    /// <summary>"Try Hypa": speak into the input, translate, say it back, look a word up. No network, no microphone.</summary>
    public class TryHypaTests
    {
        sealed class FakeApi : ITryApi
        {
            public readonly List<string> Calls = new List<string>();
            public Exception Fail;
            public string Translation = "Bonjour";
            public readonly List<string> Spoken = new List<string>();
            public readonly Dictionary<string, HypaDictionaryEntry> Dict = new Dictionary<string, HypaDictionaryEntry>();
            public Task<IReadOnlyList<HypaLanguage>> LanguagesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<HypaLanguage>>(new List<HypaLanguage>
            {
                new HypaLanguage { Name = "English", Code = "en", Voices = new[] { "a" } },
                new HypaLanguage { Name = "French", Code = "fr", Voices = new[] { "a" } },
                new HypaLanguage { Name = "Yoruba", Code = "yo", Voices = new[] { "a" } },
                new HypaLanguage { Name = "Igbo", Code = "ig" }, // no voice
                new HypaLanguage { Name = "Zulu", Code = "zu", Voices = new[] { "a" } },
            });
            public Task<HypaTranslation> TranslateAsync(string text, string targetLang, string sourceLang, CancellationToken ct)
            {
                Calls.Add($"mt {text}|{targetLang}|{sourceLang ?? "auto"}");
                if (Fail != null) throw Fail;
                return Task.FromResult(new HypaTranslation { Text = Translation, DetectedLanguage = "English" });
            }
            public Task<HypaDictionaryEntry> LookUpAsync(string word, string language, CancellationToken ct)
            {
                Calls.Add($"dict {word}|{language}");
                if (Dict.TryGetValue(word, out var e)) return Task.FromResult(e);
                return Task.FromResult(new HypaDictionaryEntry()); // nothing known
            }
            public Exception SpeakFail;
            public Task SpeakAsync(string text, string language, string voice, CancellationToken ct)
            {
                if (SpeakFail != null) throw SpeakFail;
                Spoken.Add(language + ":" + (voice ?? "auto") + ":" + text); return Task.CompletedTask;
            }
            public Task<string> ExplainAsync(string st, string tt, string sl, string tl, CancellationToken ct) { Calls.Add($"think {st}|{tt}"); return Task.FromResult("because"); }
            public bool IsPlaying => false;
            public void StopSpeaking() { }
        }

        sealed class FakeSession : IListenSession
        {
            public Task<ListenResult> Result { get; set; }
            public void Stop() { }
            public void Cancel() { }
        }

        sealed class FakeListener : IListener, IPartialListener
        {
            public int LiveSwitchedOn;
            public void SetPartialHandler(Action<string> handler) { if (handler != null) LiveSwitchedOn++; }
            public string Heard;
            public float Peak = 0.1f;
            public bool NoMic;
            public StoryLanguage Asked;
            public IListenSession Listen(StoryLanguage language, Action<float> onLevel)
            {
                Asked = language;
                if (NoMic) throw new InvalidOperationException("No microphone.");
                return new FakeSession { Result = Task.FromResult(new ListenResult { Transcript = Heard ?? "", Spoke = Heard != null, Peak = Peak }) };
            }
        }

        FakeApi _api; FakeListener _mic; TryHypaFlow _f;
        SynchronizationContext _ctx;
        string _savedTarget;

        [SetUp] public void SetUp()
        {
            _ctx = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null); // continuations run inline
            _savedTarget = HypaSettings.TargetLanguage;
            _api = new FakeApi(); _mic = new FakeListener();
            _f = new TryHypaFlow(_api, _mic, "Yoruba");
            _f.LoadLanguagesAsync();
            _f.SetTarget(_f.Find("French"));
            _api.Calls.Clear();
        }

        [TearDown] public void TearDown()
        {
            SynchronizationContext.SetSynchronizationContext(_ctx);
            HypaSettings.TargetLanguage = _savedTarget;
        }

        [Test] public void TheStoryLanguage_IsTheSource_AndNotTheTarget()
        {
            _f.SetTarget(_f.Find("English"));
            _f.BindStory("English", new[] { "English", "Yoruba", "French" });
            Assert.AreEqual("English", _f.Source.Name);
            Assert.AreEqual("Yoruba", _f.Target.Name);
            _f.CycleTarget();
            Assert.AreEqual("French", _f.Target.Name);
            _f.CycleTarget();
            Assert.AreEqual("Yoruba", _f.Target.Name); // English, the story, is skipped
        }

        [Test] public void AFailedReading_ShowsForAMoment_ThenTheWordsReturn()
        {
            _api.SpeakFail = new Exception("timeout");
            _f.UseWordAsync("calabash");
            Assert.AreEqual("Bonjour", _f.Output);
            StringAssert.Contains("Try again", _f.Message);
            _f.Tick(3.9f);
            StringAssert.Contains("Try again", _f.Message);
            _f.Tick(0.2f);
            Assert.AreEqual("", _f.Message, "the red note goes, and the translation stays");
            Assert.AreEqual("Bonjour", _f.Output);
        }

        [Test] public void AWordFromTheStory_IsTranslated_AndSpoken()
        {
            _f.UseWordAsync("calabash");
            Assert.AreEqual("calabash", _f.Input);
            Assert.AreEqual("Bonjour", _f.Output);
            CollectionAssert.AreEqual(new[] { "mt calabash|French|auto" }, _api.Calls);
            CollectionAssert.AreEqual(new[] { "French:auto:Bonjour" }, _api.Spoken);
        }

        [Test] public void EveryStory_HasThreeWordsToTranslate_InEveryLanguage()
        {
            foreach (var st in StoryLibrary.All)
                foreach (var lang in StoryLanguages.All)
                {
                    var w = st.WordsFor(lang.Code);
                    Assert.AreEqual(3, w.Length, $"{st.Id} {lang.Code}");
                    Assert.IsTrue(w.All(x => x.Length < 18), $"{st.Id} {lang.Code}: short enough for a chip");
                }
            Assert.AreEqual(new[] { "friends", "hat", "calabash" }, StoryLibrary.Find("eshu-crossroads").WordsFor("en"));
            Assert.AreEqual("fìlà", StoryLibrary.Find("eshu-crossroads").WordsFor("yo")[1], "in the story's own wording");
        }

        [Test] public void SuggestedWords_ComeFromTheStory_InOrder()
        {
            var words = TryHypaText.Suggest(new[] { "The calabash of palm wine.", "He set the hat down.", "A friend shouted." }, 3);
            CollectionAssert.AreEqual(new[] { "calabash", "palm", "wine" }, words);
        }

        [Test] public void WithoutLiveText_TheMicRecordsLikeAsk()
        {
            _f.LiveText = false;
            _mic.Heard = "hat";
            Assert.IsTrue(_f.ToggleMicAsync().IsCompleted);
            Assert.AreEqual(0, _mic.LiveSwitchedOn, "never the stream-until-Done mode");
            Assert.AreEqual("hat", _f.Input);
            _f.LiveText = true;
            _f.ToggleMicAsync();
            Assert.AreEqual(1, _mic.LiveSwitchedOn, "with live text on, it streams");
        }

        [Test] public void Speaking_IsTypedIntoTheInput_ThenTranslated()
        {
            _mic.Heard = "  Good morning ";
            Assert.IsTrue(_f.ToggleMicAsync().IsCompleted);
            Assert.AreEqual("", _f.Output);       // speaking only types; Translate is pressed by you
            Assert.IsEmpty(_api.Calls);
            _f.RunAsync();
            Assert.AreEqual("Good morning", _f.Input);
            Assert.AreEqual("Bonjour", _f.Output);
            CollectionAssert.AreEqual(new[] { "mt Good morning|French|auto" }, _api.Calls); // source left on auto-detect
            Assert.AreEqual("English", _f.DetectedLanguage);
            Assert.AreEqual(TryStatus.Idle, _f.Status);
            Assert.AreEqual("", _f.Message);
        }

        [Test] public void Dictation_AddsToWhatIsAlreadyThere()
        {
            _mic.Heard = "Good evening"; _f.ToggleMicAsync();
            _mic.Heard = "how are you"; _f.ToggleMicAsync();
            Assert.AreEqual("Good evening how are you", _f.Input);
        }

        [Test] public void Thinking_FetchesOnce_ShowsAndHides()
        {
            _mic.Heard = "Hello"; _f.ToggleMicAsync(); _f.RunAsync();
            _api.Calls.Clear();
            _f.ToggleThinkingAsync();
            Assert.IsTrue(_f.ThinkingOpen); Assert.AreEqual("because", _f.Thinking);
            _f.ToggleThinkingAsync(); Assert.IsFalse(_f.ThinkingOpen);
            _f.ToggleThinkingAsync(); Assert.IsTrue(_f.ThinkingOpen);
            CollectionAssert.AreEqual(new[] { "think Hello|Bonjour" }, _api.Calls); // asked once
        }

        [Test] public void Speaking_ListensInTheSourceLanguage()
        {
            _f.SetSource(_f.Find("Yoruba"));
            _mic.Heard = "e kaaro";
            _f.ToggleMicAsync(); _f.RunAsync();
            Assert.AreEqual("Yoruba", _mic.Asked.HypaLabel);
            StringAssert.Contains("|Yoruba", _api.Calls.Single());
        }

        [Test] public void Nothing_Heard_AsksAgain_AndMakesNoCall()
        {
            _mic.Heard = null;
            _f.ToggleMicAsync();
            StringAssert.Contains("didn't catch", _f.Message);
            Assert.IsEmpty(_api.Calls);
            Assert.AreEqual("", _f.Input);
        }

        [Test] public void Silence_OnTheMicrophone_PointsAtThePermission()
        {
            _mic.Heard = null; _mic.Peak = 0f;
            _f.ToggleMicAsync();
            StringAssert.Contains("No sound is reaching the microphone", _f.Message);
        }

        [Test] public void NoMicrophone_SaysSo()
        {
            _mic.NoMic = true;
            _f.ToggleMicAsync();
            StringAssert.Contains("microphone", _f.Message);
            Assert.AreEqual(TryStatus.Idle, _f.Status);
        }

        [Test] public void ApiError_ShowsTheUserMessage()
        {
            _api.Fail = new HypaException(HypaErrorKind.InsufficientBalance, "no credit");
            _mic.Heard = "Hello";
            _f.ToggleMicAsync(); _f.RunAsync();
            Assert.AreEqual(new HypaException(HypaErrorKind.InsufficientBalance, "").UserMessage(), _f.Message);
            Assert.AreEqual("", _f.Output);
            Assert.AreEqual("Hello", _f.Input); // what was said is kept, so Translate can be tried again
        }

        [Test] public void ChangingTheTarget_TranslatesTheInputAgain_AsTheAppDoes()
        {
            _mic.Heard = "Hello"; _f.ToggleMicAsync(); _f.RunAsync();
            _api.Calls.Clear();
            _f.SetTarget(_f.Find("Zulu"));
            CollectionAssert.AreEqual(new[] { "mt Hello|Zulu|auto" }, _api.Calls);
            Assert.AreEqual("Bonjour", _f.Output);
        }

        [Test] public void ChangingTheTarget_WithNothingSaid_MakesNoCall()
        {
            _f.SetTarget(_f.Find("Zulu"));
            Assert.IsEmpty(_api.Calls);
        }

        [Test] public void Swap_ExchangesLanguagesAndTexts_ButNotFromAutoDetect()
        {
            _f.Swap();
            StringAssert.Contains("Pick the first language", _f.Message);
            _f.SetSource(_f.Find("English"));
            _mic.Heard = "Hello"; _f.ToggleMicAsync(); _f.RunAsync();
            _f.Swap();
            Assert.AreEqual("French", _f.Source.Name);
            Assert.AreEqual("English", _f.Target.Name);
            Assert.AreEqual("Bonjour", _f.Input);
            Assert.AreEqual("", _f.Output); // translate again, as in the app
        }

        static HypaDictionaryEntry Entry(string json) => new HypaDictionaryEntry { Raw = Newtonsoft.Json.Linq.JObject.Parse(json) };

        [Test] public void Dictionary_OnlyForASingleWord()
        {
            _mic.Heard = "Good morning"; _f.ToggleMicAsync(); _f.RunAsync();
            Assert.IsFalse(_f.CanShowDictionary);
            _f.OpenDictionary();
            Assert.IsFalse(_f.DictionaryOpen);
            _f.Clear();
            _mic.Heard = "Hello"; _f.ToggleMicAsync(); _f.RunAsync();
            Assert.IsTrue(_f.CanShowDictionary);
        }

        [Test] public void Dictionary_OpensOnTheSourceTab_AndLooksUpEachTabOnce()
        {
            _api.Dict["Hello"] = Entry("{\"definitions\":[\"a greeting\"]}");
            _api.Dict["Bonjour"] = Entry("{\"definitions\":[\"salutation\"]}");
            _mic.Heard = "Hello"; _f.ToggleMicAsync(); _f.RunAsync();
            _api.Calls.Clear();
            _f.OpenDictionary();
            Assert.IsTrue(_f.DictionaryOpen);
            Assert.AreEqual(DictTab.Source, _f.Tab);
            CollectionAssert.AreEqual(new[] { "dict Hello|English" }, _api.Calls); // the detected language: the source was Auto
            Assert.AreEqual("a greeting", _f.DictSource.Card.Definitions[0].Text);
            _f.SetTab(DictTab.Target);
            CollectionAssert.AreEqual(new[] { "dict Hello|English", "dict Bonjour|French" }, _api.Calls);
            Assert.AreEqual("salutation", _f.DictTarget.Card.Definitions[0].Text);
            _f.SetTab(DictTab.Source); _f.SetTab(DictTab.Target);
            Assert.AreEqual(2, _api.Calls.Count); // not fetched again
        }

        [Test] public void Dictionary_NothingKnown_IsNoDefinition_AndClosesWithTheTranslation()
        {
            _mic.Heard = "Hello"; _f.ToggleMicAsync(); _f.RunAsync();
            _f.OpenDictionary();
            Assert.IsNull(_f.DictSource.Card);
            Assert.IsFalse(_f.DictSource.Loading); Assert.IsFalse(_f.DictSource.Failed);
            _f.SetTarget(_f.Find("Zulu"));
            Assert.IsFalse(_f.DictionaryOpen);
        }

        [Test] public void Speak_UsesTheTranslationsLanguage_AndVoice_AndExplainsMissingVoices()
        {
            _mic.Heard = "Hello"; _f.ToggleMicAsync(); _f.RunAsync();
            _f.SpeakOutputAsync();
            CollectionAssert.AreEqual(new[] { "French:auto:Bonjour" }, _api.Spoken);
            _f.SetVoice("a");
            _f.StopSpeaking();
            _f.SpeakOutputAsync();
            Assert.AreEqual("French:a:Bonjour", _api.Spoken[1]);
            _mic.Heard = "Hello"; _f.SetTarget(_f.Find("Igbo")); _f.RunAsync();
            _f.StopSpeaking();
            _f.SpeakOutputAsync();
            Assert.AreEqual(2, _api.Spoken.Count); // Igbo has no voice yet
            StringAssert.Contains("no voice for Igbo", _f.Message);
        }

        [Test] public void VoiceOptions_AreAutoThenTheLanguagesVoices()
        {
            CollectionAssert.AreEqual(new[] { "Auto", "a" }, _f.VoiceOptions());
        }

        [Test] public void Voice_DefaultsToAuto_AndAPickedVoiceIsRememberedPerLanguage()
        {
            Assert.AreEqual("Auto", _f.Voice);
            _f.SetVoice("a");
            _f.SetTarget(_f.Find("Yoruba")); Assert.AreEqual("Auto", _f.Voice);
            _f.SetTarget(_f.Find("French")); Assert.AreEqual("a", _f.Voice);
        }

        [Test] public void Clear_EmptiesEverything()
        {
            _mic.Heard = "Hello"; _f.ToggleMicAsync(); _f.RunAsync();
            _f.Clear();
            Assert.AreEqual("", _f.Input); Assert.AreEqual("", _f.Output); Assert.AreEqual("", _f.Message);
        }

        [Test] public void Picker_ListsHomeLanguagesFirst_ThenAToZ()
        {
            var names = _f.Ordered().Select(l => l.Name).ToList();
            CollectionAssert.AreEqual(new[] { "English", "Yoruba", "Igbo", "French", "Zulu" }, names); // the pinned order, then the rest
        }

        // ---------- text helpers ----------

        [Test] public void Clip_AddsAnEllipsis_OnlyWhenCut()
        {
            Assert.AreEqual("Nigerian…", TryHypaText.Clip("Nigerian Pidgin", 9));
            Assert.AreEqual("Yoruba", TryHypaText.Clip("Yoruba", 9));
        }

        [Test] public void Typewriter_TypesIn_KeepsGoingOnAContinuation_RestartsOnANewText()
        {
            var t = new Typewriter { CharsPerSecond = 10f };
            t.Set("Hello");
            Assert.AreEqual("", t.Current);
            t.Tick(0.25f); Assert.AreEqual("He", t.Current);
            t.Set("Hello there"); Assert.AreEqual("He", t.Current); // continued, not restarted
            t.Tick(10f); Assert.AreEqual("Hello there", t.Current); Assert.IsTrue(t.Done);
            t.Set("Bye"); Assert.AreEqual("", t.Current);
            t.Set("Bye", true); t.Set("Hi", true); Assert.AreEqual("Hi", t.Current);
        }

        [Test] public void Typewriter_NeverSplitsASurrogatePair()
        {
            var t = new Typewriter { CharsPerSecond = 10f };
            t.Set("a😀b");
            t.Tick(0.2f); // 2 chars: "a" + the high surrogate
            Assert.AreEqual("a😀", t.Current);
        }
    }
}
