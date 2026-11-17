using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace HypaStories.Tests
{
    /// <summary>Ticket 3I: personas, the offline brain, the in-character prompt, brain selection and the talk loop.</summary>
    public class LocalTests
    {
        const string TestPersonaPath = "Assets/HypaStories/Locals/Ama.asset";

        static LocalPersona Ama()
        {
            var p = ScriptableObject.CreateInstance<LocalPersona>();
            p.DisplayName = "Ama"; p.Role = "weaver"; p.Home = "a hut by the forest"; p.Knows = "Weaving, the village, Kiko.";
            p.Greeting = "Welcome!"; p.Suggested = new[] { "What are you weaving?", "Do you know Kiko?", "Tell me about the fire." };
            p.Canned = new[]
            {
                new CannedAnswer { Keywords = "weave, loom", Answer = "I weave blankets. My grandmother taught me." },
                new CannedAnswer { Keywords = "kiko", Answer = "Kiko sits by my loom." },
                new CannedAnswer { Keywords = "remembering fire", Answer = "It never goes out." },
                new CannedAnswer { Keywords = "fire", Answer = "Fire keeps us warm." },
            };
            p.Fallback = "I would not know. Ask me about my village.";
            return p;
        }

        // ---------- personas ----------

        [Test] public void TestPersona_LoadsFromItsAsset_AndIsComplete()
        {
            var p = UnityEditor.AssetDatabase.LoadAssetAtPath<LocalPersona>(TestPersonaPath);
            Assert.IsNotNull(p, "Hypa > Stories > Create Test Local makes " + TestPersonaPath);
            Assert.IsNull(p.Validate(), "the test local has everything a local needs");
            Assert.AreEqual(3, p.Suggested.Length);
            Assert.AreEqual("Ama, the weaver", p.Title);
        }

        [Test] public void Persona_FromJson_AndValidate()
        {
            var p = LocalPersona.FromJson("{\"DisplayName\":\"Kofi\",\"Role\":\"fisherman\",\"Knows\":\"The river.\",\"Greeting\":\"Hello!\"," +
                                          "\"Suggested\":[\"a\",\"b\",\"c\"],\"Fallback\":\"Hmm.\"}");
            Assert.AreEqual("Kofi", p.DisplayName);
            Assert.AreEqual("Kofi, the fisherman", p.Title);
            Assert.IsNull(p.Validate());
            var empty = ScriptableObject.CreateInstance<LocalPersona>();
            empty.Knows = ""; empty.Greeting = ""; empty.Suggested = new string[0]; empty.Fallback = "";
            StringAssert.Contains("3 suggested questions", empty.Validate());
            StringAssert.Contains("a greeting", empty.Validate());
        }

        // ---------- the offline brain ----------

        [Test] public void Stub_AnswersFromKeywords_LongestMatchWins_ElseFallback()
        {
            var p = Ama();
            Assert.AreEqual("I weave blankets. My grandmother taught me.", StubLocalBrain.AnswerFor(p, "What do you weave?"));
            Assert.AreEqual("It never goes out.", StubLocalBrain.AnswerFor(p, "What is the Remembering Fire?"), "the more specific keyword wins");
            Assert.AreEqual(p.Fallback, StubLocalBrain.AnswerFor(p, "What is a smartphone?"));
        }

        [Test] public void Stub_SpeaksSentenceBySentence_AndOfflineFails()
        {
            using (new NoContext())
            {
                var said = new List<(string, int, bool)>();
                new StubLocalBrain().AnswerAsync(new LocalRequest { Persona = Ama(), Question = "the loom?" }, (t, i, last) => said.Add((t, i, last)), CancellationToken.None).Wait();
                Assert.AreEqual(2, said.Count);
                Assert.AreEqual(("I weave blankets.", 0, false), said[0]);
                Assert.AreEqual(("My grandmother taught me.", 1, true), said[1]);
                var offline = new StubLocalBrain { Mode = "offline" };
                Assert.Throws<AggregateException>(() => offline.AnswerAsync(new LocalRequest { Persona = Ama(), Question = "x" }, (t, i, l) => { }, CancellationToken.None).Wait());
            }
        }

        // ---------- the prompt and the setting ----------

        [Test] public void Prompt_IsInCharacter_InTheStoryLanguage_Short_WithRecentMemory()
        {
            var fr = StoryLanguages.Find("fr");
            var sys = LocalPrompt.System(Ama(), fr);
            StringAssert.Contains("You are Ama, a weaver", sys);
            StringAssert.Contains("Answer in French", sys);
            StringAssert.Contains("2 or 3 short", sys);
            StringAssert.Contains("Only talk about your own world", sys);
            var history = Enumerable.Range(0, 6).Select(i => new LocalTurn { Question = "q" + i, Answer = "a" + i }).ToArray();
            var m = LocalPrompt.Messages(new LocalRequest { Persona = Ama(), Language = fr, Question = "now?", History = history });
            Assert.AreEqual("system", m[0].Role);
            Assert.AreEqual(1 + 2 * LocalPrompt.MemoryTurns + 1, m.Count, "system + the last 4 exchanges + the question");
            Assert.AreEqual("q2", m[1].Content, "the oldest turns drop off");
            Assert.AreEqual("now?", m[m.Count - 1].Content);
        }

        [Test] public void Sentences_SplitOnEndings_KeepAtMostThree()
        {
            CollectionAssert.AreEqual(new[] { "Welcome to my hut.", "I am weaving!", "Do you like it?" }, Sentences.Split("Welcome to my hut. I am weaving! Do you like it?"));
            CollectionAssert.AreEqual(new[] { "One.", "Two.", "Three. Four." }, Sentences.Split("One. Two. Three. Four."), "no words are dropped");
            CollectionAssert.AreEqual(new[] { "“It never goes out.”", "Truly." }, Sentences.Split("“It never goes out.” Truly."));
            Assert.AreEqual(0, Sentences.Split("  ").Count);
        }

        [Test] public void Setting_PicksTheBrain_WithNoCodeChange()
        {
            Assert.IsTrue(LocalBrains.TryParse("meta/muse-spark-1.3", out var p, out var m));
            Assert.AreEqual(("meta", "muse-spark-1.3"), (p, m));
            Assert.IsTrue(LocalBrains.TryParse("google/google/gemma-4", out p, out m));
            Assert.AreEqual(("google", "google/gemma-4"), (p, m), "the model part keeps its own slashes");
            Assert.IsFalse(LocalBrains.TryParse("muse", out _, out _));

            Assert.IsInstanceOf<StubLocalBrain>(LocalBrains.Create(LocalBrains.Muse, hasKey: false), "no key or proxy: offline answers");
            Assert.IsInstanceOf<StubLocalBrain>(LocalBrains.Create(LocalBrains.Stub, hasKey: true));
            var muse = LocalBrains.Create(LocalBrains.Muse, hasKey: true);
            Assert.AreEqual("meta/muse-spark-1.3", muse.Mode);
            Assert.AreEqual("openai/gpt-5.6-luna", LocalBrains.Create(LocalBrains.OpenAi, hasKey: true).Mode);
            Assert.AreEqual(LocalBrains.Muse, LocalBrains.Choices[0].id, "Muse is the default");
        }

        [Test] public void Fallback_WhenChatFails_TheOfflineAnswersTakeOver()
        {
            using (new NoContext())
            {
                var brain = new FallbackBrain(new FailingBrain(), new StubLocalBrain());
                var said = new List<string>();
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; // it logs a warning when it falls back
                brain.AnswerAsync(new LocalRequest { Persona = Ama(), Question = "Do you know Kiko?" }, (t, i, l) => said.Add(t), CancellationToken.None).Wait();
                CollectionAssert.AreEqual(new[] { "Kiko sits by my loom." }, said);
                Assert.AreEqual("failing→stub", brain.Mode);
            }
        }

        sealed class FailingBrain : ILocalBrain
        {
            public string Mode => "failing";
            public Task AnswerAsync(LocalRequest r, Action<string, int, bool> s, CancellationToken ct) => throw new InvalidOperationException("Hypa is busy");
        }

        // ---------- the talk loop ----------

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
            public event Action Recorded { add { } remove { } }
            public Task<ListenResult> Result { get; set; }
            public void Stop() { }
            public void Cancel() { }
        }

        sealed class FakeListener : IListener
        {
            public readonly Queue<string> Heard = new Queue<string>();
            public IListenSession Listen(StoryLanguage language, Action<float> onLevel)
            {
                if (Heard.Count == 0) return new FakeSession { Result = new TaskCompletionSource<ListenResult>().Task };
                return new FakeSession { Result = Task.FromResult(new ListenResult { Transcript = Heard.Dequeue(), TEnd = 0, TTranscript = 40, Spoke = true }) };
            }
        }

        sealed class InstantBrain : ILocalBrain
        {
            public string Mode => "instant";
            public bool Offline;
            public LocalRequest Last;
            public Task AnswerAsync(LocalRequest r, Action<string, int, bool> onSentence, CancellationToken ct)
            {
                Last = r;
                if (Offline) throw new InvalidOperationException("offline");
                var s = Sentences.Split(StubLocalBrain.AnswerFor(r.Persona, r.Question));
                for (var i = 0; i < s.Count; i++) onSentence(s[i], i, i == s.Count - 1);
                return Task.CompletedTask;
            }
        }

        sealed class FakeView : IAskView
        {
            public readonly List<AskState> States = new List<AskState>();
            public string Question, Answer;
            public void SetState(AskState state, string text) => States.Add(state);
            public void SetLevel(float level01) { }
            public void SetQuestion(string text) => Question = text;
            public void SetAnswer(string text) { if (!string.IsNullOrEmpty(text)) Answer = text; }
            public void SetSuggestions(string[] questions) { }
        }

        sealed class NoContext : IDisposable
        {
            readonly SynchronizationContext _ctx = SynchronizationContext.Current;
            public NoContext() => SynchronizationContext.SetSynchronizationContext(null); // continuations run inline
            public void Dispose() => SynchronizationContext.SetSynchronizationContext(_ctx);
        }

        (LocalTalk talk, FakeSpeech speech, FakeListener mic, InstantBrain brain, FakeView view, List<bool> engaged) Make()
        {
            var speech = new FakeSpeech(); var mic = new FakeListener(); var brain = new InstantBrain(); var view = new FakeView(); var engaged = new List<bool>();
            var persona = Ama();
            return (new LocalTalk(speech, mic, brain, view, () => StoryLanguages.All[0], () => persona, engaged.Add, () => 0), speech, mic, brain, view, engaged);
        }

        [Test] public void Talk_ListensThinksAnswers_InCharacter_AndRemembersTheVisit()
        {
            using (new NoContext())
            {
                var (talk, speech, mic, brain, view, engaged) = Make();
                mic.Heard.Enqueue("What do you weave?");
                talk.BeginAsync().Wait();
                CollectionAssert.AreEqual(new[] { "Hmm, good question…", "I weave blankets.", "My grandmother taught me." }, speech.Spoken, "filler first, then the answer");
                Assert.AreEqual(AskState.Idle, talk.State);
                Assert.AreEqual("My grandmother taught me.", view.Answer, "subtitles show the line being said");
                CollectionAssert.AreEqual(new[] { true, false }, engaged, "turns to you, then back");
                Assert.AreEqual(1, talk.History.Count);
                Assert.IsTrue(talk.Log[0].Ok);
                Assert.AreEqual("instant", talk.Log[0].Mode);

                mic.Heard.Enqueue("Do you know Kiko?");
                talk.BeginAsync().Wait();
                Assert.AreEqual(1, brain.Last.History.Count, "the second question carries the first exchange");
                Assert.AreEqual("What do you weave?", brain.Last.History[0].Question);
            }
        }

        [Test] public void Talk_WhenTheBrainFails_StaysInCharacter_AndLogsTheError()
        {
            using (new NoContext())
            {
                var (talk, speech, mic, brain, _, _) = Make();
                brain.Offline = true;
                mic.Heard.Enqueue("Hello?");
                talk.BeginAsync().Wait();
                Assert.AreEqual("Forgive me, I lost my thread. Ask me again?", speech.Spoken.Last());
                Assert.AreEqual("offline", talk.Log[0].Error);
                Assert.AreEqual(0, talk.History.Count);
                Assert.AreEqual(AskState.Idle, talk.State);
            }
        }

        [Test] public void Talk_PickedSuggestion_WorksWithoutAMicrophone_AndGreetingCanBeTalkedOver()
        {
            using (new NoContext())
            {
                var (talk, speech, mic, _, _, _) = Make();
                talk.GreetAsync().Wait();
                Assert.AreEqual("Welcome!", speech.Spoken[0]);
                var t = talk.BeginAsync(); // nothing heard: still listening
                Assert.AreEqual(AskState.Listening, talk.State);
                talk.SubmitText("Do you know Kiko?");
                t.Wait();
                Assert.AreEqual("Kiko sits by my loom.", speech.Spoken.Last());
                Assert.IsTrue(talk.Log[0].Typed);
            }
        }

        [Test] public void NoWordsHeard_IsNotAFault()
        {
            Assert.IsTrue(Local.IsNoSpeech(new HypaCore.HypaException(HypaCore.HypaErrorKind.BadRequest, "speech_to_text: The provider returned an incomplete response. Please try again.")));
            Assert.IsFalse(Local.IsNoSpeech(new HypaCore.HypaException(HypaCore.HypaErrorKind.Timeout, "Request timed out.")));
        }

        [Test] public void Listening_GivesUpSoonerWhenNoOneSpeaks_ButNotWhileTheyDo()
        {
            var quiet = new SilenceDetector { NoSpeechMs = 7000f };
            var ended = false;
            for (var t = 0; t < 6900 && !ended; t += 100) ended = quiet.Update(0.005f, 100f);
            Assert.IsFalse(ended, "still listening before 7 s of silence");
            Assert.IsTrue(quiet.Update(0.005f, 200f), "nobody spoke for 7 s: stop");

            var talking = new SilenceDetector { NoSpeechMs = 7000f };
            talking.Update(0.2f, 400f);
            ended = false;
            for (var t = 0; t < 7000 && !ended; t += 100) ended = talking.Update(0.2f, 100f);
            Assert.IsFalse(ended, "someone talking is never cut off by the no-voice timeout");
        }

        // ---------- wake word ----------

        [Test] public void WakeWord_HearsTheName_FirstOrAfterAGreeting_WithTheQuestionThatFollows()
        {
            var names = new[] { "Ama", "Auntie Ama" };
            Assert.IsTrue(WakeWord.Match("Ama?", names, out var rest)); Assert.AreEqual("", rest);
            Assert.IsTrue(WakeWord.Match("Hey Ama, what do you weave?", names, out rest)); Assert.AreEqual("what do you weave?", rest);
            Assert.IsTrue(WakeWord.Match("excuse me, ama. Who is Kiko?", names, out rest)); Assert.AreEqual("Who is Kiko?", rest);
            Assert.IsTrue(WakeWord.Match("Ama's loom is lovely", names, out rest), "the transcriber's possessive still calls her");
            Assert.IsTrue(WakeWord.Match("Auntie Ama tell me a story", names, out rest)); Assert.AreEqual("tell me a story", rest);
            Assert.IsTrue(WakeWord.Match("Àma, hello", names, out _), "accents don't matter");
        }

        [Test] public void WakeWord_IgnoresTheNameMidSentence_AndOtherWords()
        {
            var names = new[] { "Ama" };
            Assert.IsFalse(WakeWord.Match("I like Ama", names, out _));
            Assert.IsFalse(WakeWord.Match("and then the fire went out", names, out _));
            Assert.IsFalse(WakeWord.Match("Amazing story", names, out _), "a longer word that starts with the name is not the name");
            Assert.IsFalse(WakeWord.Match("One more thing", names, out _), "everyday words that sound like the name");
            Assert.IsFalse(WakeWord.Match("Any idea where Kiko is?", names, out _));
            Assert.IsFalse(WakeWord.Match("Am I late?", names, out _));
            Assert.IsFalse(WakeWord.Match("Anyone here?", names, out _));
            Assert.IsFalse(WakeWord.Match("Hammer time", names, out _), "an h-word ending in other letters");
            Assert.IsFalse(WakeWord.Match("", names, out _));
            Assert.IsTrue(WakeWord.Match("Kofy, are you there?", new[] { "Kofi" }, out _), "longer names allow one letter off");
        }

        /// <summary>What Wit.ai's dictation actually returned for "Ama" phrases spoken by four voices (2026-11-16).</summary>
        [TestCase("Anna tell me about Kiko", "tell me about Kiko")]
        [TestCase("Hello Anna how are you", "how are you")]
        [TestCase("Hey Anna what do you weave", "what do you weave")]
        [TestCase("Amer. Tell me about Kiko", "Tell me about Kiko")]
        [TestCase("Haluama how are you", "how are you")]
        [TestCase("Hayama", "")]
        [TestCase("Emma tell me about Kiko", "tell me about Kiko")]
        [TestCase("Hello Emma how are you", "how are you")]
        [TestCase("Hey Ama", "")]
        [TestCase("Amma tell me about Kiko", "tell me about Kiko")]
        [TestCase("Hey Emma", "")]
        [TestCase("Hey Weaver what do you make", "what do you make")]
        public void WakeWord_HearsAmaInWhatWitTranscribed(string heard, string question)
        {
            var p = Ama();
            Assert.IsTrue(WakeWord.Match(heard, p.CalledBy(), out var rest), heard);
            Assert.AreEqual(question, rest);
        }

        [Test] public void WakeWord_SoundKey_TreatsTheUsualMishearingsAlike()
        {
            foreach (var w in new[] { "ama", "anna", "emma", "amma", "amer" }) Assert.AreEqual("VM", WakeWord.Sound(w), w);
            Assert.AreNotEqual(WakeWord.Sound("ama"), WakeWord.Sound("anyone"));
            Assert.AreEqual(WakeWord.Sound("kofi"), WakeWord.Sound("kofy"));
        }

        [Test] public void WakeWord_OnlyAQuestionOfTwoWordsOrMore_IsAnsweredStraightAway()
        {
            Assert.IsFalse(WakeWord.IsQuestion(""));
            Assert.IsFalse(WakeWord.IsQuestion("please?"));
            Assert.IsTrue(WakeWord.IsQuestion("what do you weave?"));
        }

        [Test] public void Persona_IsCalledByItsName_AndItsWakeNames()
        {
            var p = Ama();
            p.WakeNames = new[] { "Auntie Ama", " " };
            CollectionAssert.AreEqual(new[] { "Ama", "weaver", "Auntie Ama" }, p.CalledBy().ToArray());
        }

        sealed class FakeWake : IWakeListener
        {
            public bool Available { get; set; } = true;
            public string Problem => "";
            public bool Listening { get; private set; }
            public void Begin() => Listening = true;
            public void End() => Listening = false;
            public event Action<string> Heard { add { } remove { } }
            public event Action<string> HeardSoFar { add { } remove { } }
            public event Action<float> Level { add { } remove { } }
        }

        [Test] public void WakeWord_ListensWhileALocalWantsIt_AndNotWhilePaused()
        {
            var before = WakeWord.Listener; var paused = WakeWord.Paused;
            try
            {
                var l = new FakeWake(); WakeWord.Listener = l; WakeWord.Paused = false;
                object a = new object(), b = new object();
                WakeWord.Want(a, true); WakeWord.Want(b, true);
                Assert.IsTrue(l.Listening);
                WakeWord.Want(a, false);
                Assert.IsTrue(l.Listening, "still wanted by the other local");
                WakeWord.Paused = true; WakeWord.Apply();
                Assert.IsFalse(l.Listening, "the storyteller's question has the microphone");
                WakeWord.Paused = false; WakeWord.Apply();
                Assert.IsTrue(l.Listening);
                WakeWord.Want(b, false);
                Assert.IsFalse(l.Listening);
            }
            finally { WakeWord.Listener = before; WakeWord.Paused = paused; }
        }

        [Test] public void Talk_AQuestionHeardWithTheName_IsAnsweredWithoutListeningFirst()
        {
            using (new NoContext())
            {
                var (talk, speech, mic, _, view, _) = Make();
                talk.AskAsync("what do you weave?").Wait();
                CollectionAssert.DoesNotContain(view.States, AskState.Listening);
                CollectionAssert.AreEqual(new[] { "Hmm, good question…", "I weave blankets.", "My grandmother taught me." }, speech.Spoken);
                Assert.AreEqual(1, talk.History.Count);
                Assert.IsFalse(talk.Log[0].Typed);
                Assert.AreEqual(AskState.Idle, talk.State);
            }
        }

        // ---------- answering sooner ----------

        sealed class SlowSession : IListenSession
        {
            public readonly TaskCompletionSource<ListenResult> Tcs = new TaskCompletionSource<ListenResult>();
            public Task<ListenResult> Result => Tcs.Task;
            public event Action Recorded;
            public void FireRecorded() => Recorded?.Invoke();
            public void Stop() { }
            public void Cancel() { }
        }

        sealed class SlowListener : IListener
        {
            public SlowSession Last;
            public IListenSession Listen(StoryLanguage language, Action<float> onLevel) => Last = new SlowSession();
        }

        [Test] public void Talk_TheFillerStartsWhenTheRecordingStops_NotAfterSpeechToText()
        {
            using (new NoContext())
            {
                var speech = new FakeSpeech(); var mic = new SlowListener(); var view = new FakeView();
                var persona = Ama();
                var talk = new LocalTalk(speech, mic, new InstantBrain(), view, () => StoryLanguages.All[0], () => persona, _ => { }, () => 0);
                var t = talk.BeginAsync();
                Assert.AreEqual(AskState.Listening, talk.State);
                mic.Last.FireRecorded(); // you stopped talking; speech-to-text is still running
                Assert.AreEqual(AskState.Thinking, talk.State, "thinking at once");
                CollectionAssert.AreEqual(new[] { "Hmm, good question…" }, speech.Spoken, "the filler at once");
                mic.Last.Tcs.SetResult(new ListenResult { Transcript = "What do you weave?", Spoke = true });
                t.Wait();
                Assert.AreEqual(1, speech.Spoken.Count(x => x == "Hmm, good question…"), "not said twice");
                Assert.AreEqual("My grandmother taught me.", speech.Spoken.Last());
            }
        }

        [Test] public void Wake_ShowsListeningAtOnce_ThenAnswersTheQuestion()
        {
            using (new NoContext())
            {
                var (talk, speech, _, _, view, engaged) = Make();
                talk.OpenFromWake(); // "Hey Ama…" heard while still talking
                Assert.AreEqual(AskState.Listening, talk.State);
                Assert.IsTrue(talk.WakeOpen);
                talk.AskAsync("what do you weave?").Wait();
                Assert.IsFalse(talk.WakeOpen);
                Assert.AreEqual("My grandmother taught me.", speech.Spoken.Last());
                Assert.AreEqual(AskState.Idle, talk.State);
            }
        }

        [Test] public void Wake_ThatCameToNothing_GoesBackToIdle()
        {
            var (talk, _, _, _, _, _) = Make();
            talk.OpenFromWake();
            talk.CloseWake();
            Assert.AreEqual(AskState.Idle, talk.State);
            Assert.IsFalse(talk.WakeOpen);
        }

        [Test] public void Lines_EveryStoryLanguageHasLines_EnglishUntilTranslated()
        {
            foreach (var l in StoryLanguages.All)
            {
                var lines = LocalLines.For(l.Code);
                Assert.IsNotEmpty(lines.Prompt, l.Code);
                Assert.AreEqual(2, lines.Filler.Length, l.Code);
            }
            Assert.AreEqual("Parler", LocalLines.For("fr").Talk);
            Assert.IsTrue(LocalLines.Ready("en"));
        }
    }
}
