using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HypaStories
{
    /// <summary>
    /// Something that hears speech continuously and reports each finished phrase (the Meta Voice SDK's dictation, see
    /// <c>HypaStories.Voice.VoiceSdkWakeListener</c>). Only runs while asked to: near a local, with nobody else using the microphone.
    /// </summary>
    public interface IWakeListener
    {
        /// <summary>False when it can't run (no Wit token, no microphone permission yet).</summary>
        bool Available { get; }
        /// <summary>Why it isn't available, for the log.</summary>
        string Problem { get; }
        bool Listening { get; }
        /// <summary>Start (or keep) listening.</summary>
        void Begin();
        /// <summary>Stop now and let go of the microphone (a local is about to record the question).</summary>
        void End();
        /// <summary>A finished phrase, on the main thread.</summary>
        event Action<string> Heard;
        /// <summary>The phrase so far, while it is being said ("Hey Emma"), on the main thread: lets a local react at once.</summary>
        event Action<string> HeardSoFar;
        /// <summary>Microphone loudness 0..1 while listening, for the voice meter.</summary>
        event Action<float> Level;
    }

    /// <summary>
    /// The wake word (ticket 3I, stretch): say a local's name ("Ama", "Hey Ama, what do you weave?") to talk to them, with no
    /// hand raised. The listener runs only while some <see cref="Local"/> wants it (you're near them, nobody is talking) and
    /// nothing has <see cref="Paused"/> it (the storyteller's own question uses the microphone). Matching is pure and unit tested.
    /// </summary>
    public static class WakeWord
    {
        /// <summary>Set by the Voice SDK assembly at startup; null = no wake word in this build (raise a hand still works).</summary>
        public static IWakeListener Listener;
        /// <summary>True while something else needs the microphone (the campfire's ask loop).</summary>
        public static bool Paused;

        static readonly HashSet<object> Wanted = new HashSet<object>();

        /// <summary>A local asks for (or releases) the listener. It runs while anyone wants it and nothing paused it.</summary>
        public static void Want(object who, bool want)
        {
            if (want) Wanted.Add(who); else Wanted.Remove(who);
            Apply();
        }

        /// <summary>Starts or stops the listener to match what's wanted. Call after changing <see cref="Paused"/>.</summary>
        public static void Apply()
        {
            var l = Listener;
            if (l == null || !l.Available) return;
            var on = Wanted.Count > 0 && !Paused;
            if (on && !l.Listening) l.Begin();
            else if (!on && l.Listening) l.End();
        }

        /// <summary>Words people put before a name: "hey Ama", "excuse me Ama".</summary>
        static readonly string[] Openers = { "hey", "hi", "hello", "ok", "okay", "excuse me", "please", "oh", "um", "uh", "so", "yo" };

        /// <summary>
        /// Whether <paramref name="phrase"/> calls one of <paramref name="names"/>: the name as the first word, after an optional
        /// opener. Transcription rarely spells an unfamiliar name right (Wit heard "Ama" as Anna, Emma, Amma, Amer, and glued to the
        /// greeting as "Hayama"), so a word that sounds the same counts (<see cref="Sound"/>), as does a one-letter slip on names of four
        /// letters or more, or the name with a short "h…" greeting stuck to its front.
        /// <paramref name="rest"/> is what follows, e.g. the question in "Ama, what do you weave?" (empty if they only said the name).
        /// A name later in a sentence ("I like Ama") doesn't count. Pure.
        /// </summary>
        public static bool Match(string phrase, IEnumerable<string> names, out string rest)
        {
            rest = "";
            if (string.IsNullOrWhiteSpace(phrase) || names == null) return false;
            var words = Words(phrase);
            if (words.Count == 0) return false;
            var keys = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Words(n)).Where(w => w.Count > 0).ToList();
            if (keys.Count == 0) return false;

            var start = 0;
            for (var guard = 0; guard < 3 && start < words.Count; guard++)
            {
                var opener = Openers.Select(o => o.Split(' ')).FirstOrDefault(o => StartsWith(words, start, o, exact: true));
                if (opener == null) break;
                start += opener.Length;
            }
            foreach (var key in keys)
            {
                if (!StartsWith(words, start, key.ToArray(), exact: false)) continue;
                rest = Tail(phrase, start + key.Count);
                return true;
            }
            // "Hayama", "Haluama": the greeting and a one-word name heard as one word.
            if (start == 0)
                foreach (var key in keys)
                {
                    var w = words[0]; var k = key[0];
                    if (key.Count != 1 || k.Length < 3 || w.Length <= k.Length + 1 || w.Length - k.Length > 4 || w[0] != 'h' || !w.EndsWith(k)) continue;
                    rest = Tail(phrase, 1);
                    return true;
                }
            return false;
        }

        static bool StartsWith(List<string> words, int at, string[] key, bool exact)
        {
            if (at + key.Length > words.Count) return false;
            for (var i = 0; i < key.Length; i++)
            {
                var w = words[at + i];
                if (w == key[i]) continue;
                if (exact) return false;
                // "Amas"/"Ama's" (possessive or plural from the transcriber) and one-letter slips on longer names.
                if (w.StartsWith(key[i]) && w.Length - key[i].Length <= 1 && key[i].Length >= 3) continue;
                if (key[i].Length >= 4 && Distance(w, key[i]) <= 1) continue;
                if (key[i].Length >= 3 && w.Length >= 3 && !SoundsLikeNoName.Contains(w) && Sound(w) == Sound(key[i])) continue;
                return false;
            }
            return true;
        }

        /// <summary>Everyday words that sound like a short name by <see cref="Sound"/> ("one" ~ "Ama"): never a call.</summary>
        static readonly HashSet<string> SoundsLikeNoName = new HashSet<string> { "one", "any", "own", "ion", "aim", "anew", "anon" };

        /// <summary>
        /// A rough sound key for names: the first letter's kind (a vowel is a vowel), then the consonants in order, with m and n alike
        /// (nasals swap most in transcription), similar consonants merged, h and doubled letters dropped, and a final r dropped
        /// (non-rhotic "Amer" for "Ama"). Ama, Anna, Emma, Amma, Amer → "VM". Pure.
        /// </summary>
        public static string Sound(string word)
        {
            var sb = new StringBuilder();
            var last = '\0';
            for (var i = 0; i < word.Length; i++)
            {
                var c = word[i];
                if ("aeiouy".IndexOf(c) >= 0) { if (i == 0) sb.Append('V'); last = '\0'; continue; }
                if (c == 'h' || c == 'w') continue;
                var k = c == 'n' || c == 'm' ? 'M' : c == 'b' || c == 'p' ? 'B' : c == 'd' || c == 't' ? 'D' : c == 'g' || c == 'k' || c == 'c' || c == 'q' ? 'K'
                    : c == 's' || c == 'z' || c == 'x' ? 'S' : c == 'f' || c == 'v' ? 'F' : char.ToUpperInvariant(c);
                if (k != last) sb.Append(k);
                last = k;
            }
            if (sb.Length > 2 && sb[sb.Length - 1] == 'R') sb.Length--;
            return sb.ToString();
        }

        /// <summary>Lower case, accents off, apostrophes dropped, split on anything that isn't a letter or digit. Pure.</summary>
        public static List<string> Words(string text)
        {
            var d = (text ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            foreach (var c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (c == '\'' || c == '’') continue;
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return sb.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        /// <summary>The original text after the first <paramref name="skipWords"/> words, without leading punctuation.</summary>
        static string Tail(string phrase, int skipWords)
        {
            var i = 0;
            for (var n = 0; n < skipWords && i < phrase.Length; n++)
            {
                while (i < phrase.Length && !IsWordChar(phrase[i])) i++;
                while (i < phrase.Length && IsWordChar(phrase[i])) i++;
            }
            while (i < phrase.Length && !char.IsLetterOrDigit(phrase[i])) i++;
            return phrase.Substring(i).Trim();
        }

        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '\'' || c == '’' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark;

        /// <summary>A question worth answering straight away: at least two words. ("Ama?" alone means "listen to me".) Pure.</summary>
        public static bool IsQuestion(string rest) => Words(rest).Count >= 2;

        static int Distance(string a, string b)
        {
            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++) prev[j] = j;
            for (var i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (var j = 1; j <= b.Length; j++)
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                var t = prev; prev = cur; cur = t;
            }
            return prev[b.Length];
        }
    }
}
