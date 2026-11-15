using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    public class StoryTests
    {
        // ---------- Timeline / director ----------

        [Test] public void Track_EasesBetweenKeys_AndHoldsAtTheEnds()
        {
            var k = new[] { new Vector2(0, 0), new Vector2(10, 1) };
            Assert.AreEqual(0f, StoryDirector.Track(k, -1f));
            Assert.AreEqual(0.5f, StoryDirector.Track(k, 5f), 1e-5);
            Assert.Less(StoryDirector.Track(k, 2f), 0.2f); // smoothstep: slow start
            Assert.AreEqual(1f, StoryDirector.Track(k, 99f));
        }

        static StoryTimeline Stones => StoryLibrary.Find("seven-hot-stones").Timeline;
        static Caption Line(StoryTimeline tl, string id) => tl.Captions.First(c => c.Id == id);

        [Test] public void Director_SevenStones_WindRisesWhenDodoComes()
        {
            var d = new StoryDirector(Stones);
            Assert.AreEqual(-1, d.Sample(0f).Index);
            Assert.AreEqual(0.1f, d.Sample(Line(Stones, "came").At).Wind, 1e-4);
            Assert.AreEqual(0.45f, d.Sample(Line(Stones, "came").Until).Wind, 1e-4);
            var s = d.Sample(Line(Stones, "approached").Until);
            Assert.AreEqual(0.85f, s.Wind, 1e-4);
            Assert.AreEqual("Before long, Dodo approached the house.", Stones.Captions[s.Index].Text);
            Assert.AreEqual(0f, d.Sample(Line(Stones, "dawn").Until).Wind, 1e-4);
            Assert.AreEqual(0f, d.Sample(Stones.Duration).Rain);
            var flash = Line(Stones, "challenge").At + 0.5f;
            Assert.AreEqual(0f, d.Sample(flash - 0.4f).Flash);
            Assert.Greater(d.Sample(flash + 0.05f).Flash, 0.3f);
        }

        [Test] public void Director_ThunderFollowsEachFlash_Once()
        {
            var d = new StoryDirector(Stones);
            var flash = Line(Stones, "challenge").At + 0.5f;
            Assert.AreEqual(0, d.ThunderBetween(flash - 0.4f, flash + 0.3f));
            Assert.AreEqual(1, d.ThunderBetween(flash + 0.3f, flash + 0.4f));
            Assert.AreEqual(4, d.ThunderBetween(0f, Stones.Duration));
        }

        [Test] public void WordsShown_LightUpWithTheVoice()
        {
            var c = new Caption { At = 10f, Until = 14.25f, Text = "a b c d" };
            Assert.AreEqual(0, StoryDirector.WordsShown(c, 10f, 4));
            Assert.AreEqual(2, StoryDirector.WordsShown(c, 12f, 4));
            Assert.AreEqual(4, StoryDirector.WordsShown(c, 20f, 4));
        }

        [Test] public void Stories_TimesFromWordCount_InOrder()
        {
            var marlow = StoryLibrary.Find("marlow-street").Timeline;
            var caps = marlow.Captions;
            Assert.AreEqual(13, caps.Length);
            Assert.AreEqual(0.4f, caps[0].At, 1e-4);
            for (var i = 1; i < caps.Length; i++) Assert.Greater(caps[i].At, caps[i - 1].Until);
            Assert.GreaterOrEqual(marlow.Duration, caps[caps.Length - 1].Until + 1.5f);
            Assert.AreEqual(4, marlow.Flashes.Length);
        }

        [Test] public void Parse_TranslatesById_FallsBackToEnglish_AndEasesCuesOverTheirLine()
        {
            const string story = @"{ ""title"": ""T"", ""rain"": 0.2, ""lines"": [
                { ""id"": ""a"", ""text"": ""one two three"" },
                { ""id"": ""b"", ""text"": ""four five"", ""rain"": 1, ""flashes"": [0.5] } ] }";
            var st = StoryLibrary.Parse("t", story, new Dictionary<string, string> { ["fr"] = @"{ ""a"": ""un deux trois"" }" });
            var caps = st.Timeline.Captions;
            Assert.AreEqual("un deux trois", caps[0].TextFor("fr"));
            Assert.AreEqual("four five", caps[1].TextFor("fr"));
            var d = new StoryDirector(st.Timeline);
            Assert.AreEqual(0.2f, d.Sample(caps[1].At).Rain, 1e-4);
            Assert.AreEqual(1f, d.Sample(caps[1].Until).Rain, 1e-4);
            Assert.AreEqual(caps[1].At + 0.5f, st.Timeline.Flashes[0], 1e-4);
            Assert.AreEqual(StoryLibrary.DefaultModel, st.Model);
            Assert.IsEmpty(st.VoicesFor("en")); // no voices: Auto
        }

        [Test] public void Parse_Voices_PerLanguage_FirstThatHasOneWins()
        {
            const string story = @"{ ""voices"": [
                { ""model"": ""hypaai/orpheus"", ""voice"": { ""fr"": ""Alloy"", ""yo"": ""Toyin"" } },
                { ""model"": ""x/y"" },
                { ""model"": ""openai/gpt-4o-mini-tts"", ""voice"": ""fable"" } ], ""lines"": [ { ""id"": ""a"", ""text"": ""one"" } ] }";
            var st = StoryLibrary.Parse("t", story, new Dictionary<string, string>());
            Assert.AreEqual(2, st.Voices.Length); // the one without a voice is dropped
            var yo = st.VoicesFor("yo");
            Assert.AreEqual("hypaai/orpheus Toyin", yo[0].ToString());
            Assert.AreEqual("openai/gpt-4o-mini-tts fable", yo[1].ToString()); // the fallback
            Assert.AreEqual("openai/gpt-4o-mini-tts fable", st.VoicesFor("en").Single().ToString());
        }

        [Test] public void Suggestions_AreFromLinesToldSoFar_NewestFirst_InTheLanguage()
        {
            const string story = @"{ ""model"": ""meta/m"", ""lines"": [
                { ""id"": ""a"", ""text"": ""one"", ""ask"": [""A?""] },
                { ""id"": ""b"", ""text"": ""two"" },
                { ""id"": ""c"", ""text"": ""three"", ""ask"": [""C?"", ""C2?""] },
                { ""id"": ""d"", ""text"": ""four"", ""ask"": [""D?""] } ] }";
            var st = StoryLibrary.Parse("t", story, new Dictionary<string, string> { ["fr"] = @"{ ""c.ask"": [""C fr ?""] }" });
            Assert.AreEqual("meta/m", st.Model);
            CollectionAssert.AreEqual(new[] { "A?" }, st.Suggestions(1, "en"));
            CollectionAssert.AreEqual(new[] { "C?", "C2?", "A?" }, st.Suggestions(2, "en"));
            CollectionAssert.AreEqual(new[] { "D?", "C?", "C2?" }, st.Suggestions(3, "en"));
            CollectionAssert.AreEqual(new[] { "C fr ?", "A?" }, st.Suggestions(2, "fr"));
        }

        [Test] public void EveryStory_SuggestsFromTheStart_InEveryLanguage()
        {
            foreach (var st in StoryLibrary.All)
            {
                StringAssert.Contains("/", st.Model, st.Id);
                foreach (var lang in StoryLanguages.All)
                    Assert.IsNotEmpty(st.Suggestions(0, lang.Code), $"{st.Id} {lang.Code}");
                foreach (var c in st.Timeline.Captions.Where(c => c.Ask.Length > 0))
                    foreach (var lang in Translated)
                        Assert.AreEqual(c.Ask.Length, c.AskFor(lang).Length, $"{st.Id} {c.Id} {lang}");
                foreach (var c in st.Timeline.Captions.Where(c => c.Ask.Length > 0))
                    foreach (var lang in Translated)
                        CollectionAssert.AreNotEqual(c.Ask, c.AskFor(lang), $"{st.Id} {c.Id} {lang}: untranslated question");
            }
        }

        static readonly string[] Translated = StoryLanguages.All.Where(l => l.Code != "en").Select(l => l.Code).ToArray();

        [Test] public void EveryLine_IsTranslated_AndFallsBackToEnglish()
        {
            foreach (var st in StoryLibrary.All)
                foreach (var c in st.Timeline.Captions)
                    foreach (var lang in Translated)
                        Assert.IsFalse(string.IsNullOrEmpty(c.TextFor(lang)) || c.TextFor(lang) == c.Text, $"{st.Id} {lang}: {c.Text}");
            Assert.AreEqual("One stone.", Line(Stones, "one-stone").TextFor("sw")); // not offered: English
        }

        [Test] public void Languages_FindByCodeOrHypaLabel()
        {
            Assert.AreEqual("fr", StoryLanguages.Find("French").Code);
            Assert.AreEqual("Español", StoryLanguages.Find("ES").Name);
            Assert.AreEqual("yo", StoryLanguages.Find("Yoruba").Code);
            Assert.AreEqual("pcm", StoryLanguages.Find("Pidgin").Code);
            Assert.IsNull(StoryLanguages.Find("Swahili"));
            foreach (var l in StoryLanguages.All.Where(l => l.Code != "en"))
                Assert.AreNotEqual(StoryLanguages.Lines("en").Again, StoryLanguages.Lines(l.Code).Again, l.Code);
        }

        // ---------- Q&A stand-in ----------

        [Test] public void Qa_NoSpoilers_PastTheInterruptedLine()
        {
            var early = StubStoryQa.AnswerFor("How did the stones defeat Dodo?", 3, "en", "seven-hot-stones");
            StringAssert.StartsWith("Hmm, I cannot tell you that just yet.", early[0]);
            var late = StubStoryQa.AnswerFor("How did the stones defeat Dodo?", 50, "en", "seven-hot-stones");
            Assert.AreEqual("After swallowing all seven hot stones, Dodo could not go on.", late[0]);
        }

        [Test] public void Qa_AnswersInTheLanguage_AndDefaults()
        {
            Assert.AreEqual("Dodo est une créature que toute la ville craint.", StubStoryQa.AnswerFor("qui est la créature ?", 4, "fr", "seven-hot-stones")[0]);
            Assert.AreEqual("That is a good question.", StubStoryQa.AnswerFor("What colour is the sky?", 5, "en", "seven-hot-stones")[0]);
            // Marlow has English answers only for now
            Assert.AreEqual("Nia is the detective on the case.", StubStoryQa.AnswerFor("who is nia", 3, "fr", "marlow-street")[0]);
        }

        [Test] public void Chat_PromptHasTheStorySoFar_TheQuestion_AndTheLanguage()
        {
            var m = ChatStoryQa.Messages(new QaRequest
            {
                Question = "qui est Auta ?", StorySoFar = "Il y a bien longtemps…", Language = StoryLanguages.Find("fr"),
            }, "Seven Hot Stones:\nThe Night Dodo Came");
            Assert.AreEqual("system", (string)m[0]["role"]);
            StringAssert.Contains("“Seven Hot Stones: The Night Dodo Came”", (string)m[0]["content"]);
            StringAssert.Contains("Answer in French", (string)m[0]["content"]);
            StringAssert.Contains("Il y a bien longtemps…", (string)m[1]["content"]);
            StringAssert.EndsWith("qui est Auta ?", (string)m[1]["content"]);
        }

        [Test] public void Chat_SplitsTheReplyIntoSpokenSentences()
        {
            CollectionAssert.AreEqual(new[] { "Well, Auta is brave.", "“Why?” he asks!", "¿Y tú?" },
                ChatStoryQa.Sentences("**Well**, Auta is brave. “Why?” he asks!\n¿Y tú?"));
            Assert.AreEqual(4, ChatStoryQa.Sentences("A. B. C. D. E.").Count);
            Assert.IsEmpty(ChatStoryQa.Sentences("  "));
        }

        // ---------- UI ----------

        [Test] public void Fade_RefreshWhileFadedOut_KeepsTheTextsOwnAlpha()
        {
            var root = new GameObject("group");
            try
            {
                var text = new GameObject("text").AddComponent<TMPro.TextMeshPro>();
                text.transform.SetParent(root.transform, false);
                text.alpha = 0.5f;
                var g = root.AddComponent<KitGroup>();
                g.Refresh();
                g.Set(0f);                 // leave a story
                g.Refresh();               // load the next one while hidden
                g.Set(1f);                 // fade back in
                Assert.AreEqual(0.5f, text.alpha, 1e-4);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        // ---------- Listening / gestures ----------

        [Test] public void Mic_SkipsVirtualInputs()
        {
            Assert.AreEqual("MacBook Pro Microphone",
                MicListener.Device(new[] { "AlteredVirtualAudio Device", "BlackHole 2ch", "MacBook Pro Microphone", "ZoomAudioDevice" }));
            Assert.AreEqual("Android audio input", MicListener.Device(new[] { "Android audio input" }));
            Assert.IsNull(MicListener.Device(new[] { "BlackHole 2ch" }));
        }

        [Test] public void Silence_EndsAfterSpeechThenQuiet()
        {
            var d = new SilenceDetector();
            Assert.IsFalse(d.Update(0.001f, 2000f));      // quiet before speaking never ends it (until the max)
            Assert.IsFalse(d.Update(0.1f, 400f));
            Assert.IsFalse(d.Update(0.001f, 1000f));
            Assert.IsTrue(d.Update(0.001f, 400f));
            Assert.IsTrue(d.Spoke);
        }

        [Test] public void Silence_EndsAtTheMax()
        {
            var d = new SilenceDetector();
            Assert.IsFalse(d.Update(0f, 11000f));
            Assert.IsTrue(d.Update(0f, 1500f));
            Assert.IsFalse(d.Spoke);
        }

        [Test] public void RaiseHand_FiresOnceAfterHold_AndReArms()
        {
            var g = new RaiseHandGesture { HoldSeconds = 0.3f };
            Assert.IsFalse(g.Update(true, 0.2f));
            Assert.IsTrue(g.Update(true, 0.2f));
            Assert.IsFalse(g.Update(true, 1f));
            Assert.IsFalse(g.Update(false, 0.1f));
            Assert.IsFalse(g.Update(true, 0.1f));
            Assert.IsTrue(g.Update(true, 0.25f));
        }

        // ---------- UI text ----------

        [Test] public void Reveal_LightsTheFirstWords_AndWraps()
        {
            var s = StoryUi.Reveal("one two three", 2, 7, Color.white, Color.black);
            Assert.AreEqual("<color=#FFFFFFFF>one two</color><color=#000000FF>\nthree</color>", s);
            Assert.AreEqual("<color=#000000FF>one</color>", StoryUi.Reveal("one", 0, 30, Color.white, Color.black));
        }

        // ---------- Audio synthesis ----------

        [Test] public void Procedural_ClipsAreFiniteAndInRange()
        {
            foreach (var d in new[] { ProceduralClips.ChimeSamples(), ProceduralClips.TapSamples(262f), ProceduralClips.ChirpSamples(1), ProceduralClips.ThunderSamples(2) })
            {
                Assert.Greater(d.Length, 1000);
                Assert.IsTrue(d.All(v => !float.IsNaN(v) && Mathf.Abs(v) <= 1.0001f));
            }
        }

        // ---------- The ask loop (tickets 3B / 3C) ----------

        sealed class FakeSpeech : IStorySpeech
        {
            public readonly List<string> Spoken = new List<string>();
            public Task<SpeechLine> PrepareAsync(string text, StoryLanguage language, CancellationToken ct) => Task.FromResult(new SpeechLine { Text = text });
            public Task PlayAsync(SpeechLine line, Action<double> onStart, CancellationToken ct) { Spoken.Add(line.Text); onStart?.Invoke(100); return Task.CompletedTask; }
            public void SkipCurrent() { }
            public void StopAll() { }
        }

        sealed class FakeSession : IListenSession
        {
            public Task<ListenResult> Result { get; set; }
            public void Stop() { }
            public void Cancel() { }
        }

        sealed class FakeListener : IListener
        {
            public readonly Queue<string> Heard = new Queue<string>();
            public bool NoMic;
            public int Starts;
            public IListenSession Listen(StoryLanguage language, Action<float> onLevel)
            {
                Starts++;
                if (NoMic) throw new InvalidOperationException("No microphone.");
                if (Heard.Count == 0) return new FakeSession { Result = new TaskCompletionSource<ListenResult>().Task }; // still listening
                return new FakeSession { Result = Task.FromResult(new ListenResult { Transcript = Heard.Dequeue(), TEnd = 0, TTranscript = 40, Spoke = true }) };
            }
        }

        sealed class FakeQa : IStoryQa
        {
            public string Mode => "fake";
            public bool Offline;
            public QaRequest Last;
            public Task AnswerAsync(QaRequest req, Action<string, int, bool> onSentence, CancellationToken ct)
            {
                Last = req;
                if (Offline) throw new InvalidOperationException("offline");
                var a = StubStoryQa.AnswerFor(req.Question, req.LineIndex, req.Language.Code, req.StoryId);
                for (var i = 0; i < a.Length; i++) onSentence(a[i], i, i == a.Length - 1);
                return Task.CompletedTask;
            }
        }

        sealed class FakeNarration : INarration
        {
            public bool Interrupted;
            public int ResumedAt = -1;
            public bool CanAsk => true;
            public int LineIndex => 12;
            public string StoryId => "seven-hot-stones";
            public string StorySoFar => "Long ago…";
            public string[] Suggestions => new[] { "What is Dodo?" };
            public void Interrupt() => Interrupted = true;
            public void Resume(int lineIndex) => ResumedAt = lineIndex;
        }

        sealed class FakeView : IAskView
        {
            public readonly List<AskState> States = new List<AskState>();
            public string Text, Question, Answer;
            public void SetState(AskState state, string text) { States.Add(state); Text = text; }
            public void SetLevel(float level01) { }
            public void SetQuestion(string text) => Question = text;
            public void SetAnswer(string text) { if (!string.IsNullOrEmpty(text)) Answer = text; }
            public string[] Suggestions;
            public void SetSuggestions(string[] questions) => Suggestions = questions;
        }

        FakeSpeech _speech; FakeListener _mic; FakeQa _qa; FakeNarration _story; FakeView _view; AskFlow _ask;
        readonly List<bool> _asking = new List<bool>();
        SynchronizationContext _ctx;

        [SetUp] public void SetUp()
        {
            // run continuations inline so the whole loop completes inside the test
            _ctx = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            _speech = new FakeSpeech(); _mic = new FakeListener(); _qa = new FakeQa(); _story = new FakeNarration(); _view = new FakeView();
            _asking.Clear();
            _ask = new AskFlow(_story, _speech, _mic, _qa, _view, () => StoryLanguages.All[0], _asking.Add, () => 0);
        }

        [TearDown] public void TearDown() => SynchronizationContext.SetSynchronizationContext(_ctx);

        [Test] public void Ask_InterruptsAnswersAndResumesTheSameLine()
        {
            _mic.Heard.Enqueue("Why did nobody answer?");
            var t = _ask.BeginAsync();
            Assert.IsTrue(t.IsCompleted);
            Assert.IsTrue(_story.Interrupted);
            CollectionAssert.AreEqual(new[] { "Hmm, good question…", "Dodo calls out, asking who in the town can fight it.", "Everyone is too frightened to answer.", "Now, where was I…" }, _speech.Spoken);
            Assert.AreEqual(12, _story.ResumedAt);
            Assert.AreEqual(AskState.Idle, _ask.State);
            CollectionAssert.AreEqual(new[] { true, false }, _asking);
            Assert.AreEqual("Long ago…", _qa.Last.StorySoFar);
            CollectionAssert.AreEqual(new[] { "What is Dodo?" }, _view.Suggestions);
            var log = _ask.Log.Single();
            Assert.IsTrue(log.Ok);
            Assert.AreEqual(100, log.Filler);
            Assert.AreEqual(40, log.Transcript);
            Assert.AreEqual(100, log.Playback);
        }

        [Test] public void Ask_UnclearThreeTimes_GoesBackToTheStory()
        {
            _mic.Heard.Enqueue(""); _mic.Heard.Enqueue(" "); _mic.Heard.Enqueue("");
            _ask.BeginAsync();
            Assert.AreEqual(3, _mic.Starts);
            CollectionAssert.AreEqual(new[] { "Sorry, could you say that again?", "Sorry, could you say that again?", "Let's keep going with the story." }, _speech.Spoken);
            Assert.AreEqual(12, _story.ResumedAt);
            Assert.IsEmpty(_ask.Log);
        }

        [Test] public void Ask_ServiceOffline_KeepsGoingAndLogsTheError()
        {
            _qa.Offline = true;
            _mic.Heard.Enqueue("who is auta");
            _ask.BeginAsync();
            CollectionAssert.AreEqual(new[] { "Hmm, good question…", "Let's keep going with the story." }, _speech.Spoken);
            Assert.AreEqual(12, _story.ResumedAt);
            Assert.AreEqual("offline", _ask.Log.Single().Error);
        }

        [Test] public void Ask_NoMicrophone_APickedQuestionStillWorks()
        {
            _mic.NoMic = true;
            var t = _ask.BeginAsync();
            Assert.IsFalse(t.IsCompleted);
            Assert.AreEqual(AskState.Listening, _ask.State);
            StringAssert.Contains("pick a question", _view.Text);
            _ask.SubmitText("What is Dodo?");
            Assert.IsTrue(t.IsCompleted);
            Assert.AreEqual("Dodo is a creature the whole town fears.", _speech.Spoken[1]);
            Assert.IsTrue(_ask.Log.Single().Typed);
        }

        [Test] public void Ask_Cancel_StopsWithoutResuming()
        {
            var t = _ask.BeginAsync();               // nothing heard yet: still listening
            Assert.AreEqual(AskState.Listening, _ask.State);
            _ask.Cancel();
            Assert.AreEqual(AskState.Idle, _ask.State);
            Assert.AreEqual(-1, _story.ResumedAt);
            CollectionAssert.AreEqual(new[] { true, false }, _asking);
            Assert.IsTrue(t.IsCompleted);
        }
    }
}
