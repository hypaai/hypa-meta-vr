using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace HypaCore
{
    /// <summary>
    /// What the app asked Hypa, for the debug panels (DEBUG=true, see <see cref="HypaConfig.DebugMode"/>): each call's API, what it
    /// sent (text, language, model, voice), what came back (provider, model, voice), how long it took and how it ended, plus notes
    /// the app adds (a line skipped, a line played silently). Off unless a panel turns it on: until then <see cref="Begin"/> returns
    /// null and nothing is kept. Calls finish on background threads, so everything goes through one lock.
    /// </summary>
    public static class HypaTrace
    {
        public sealed class Call
        {
            public int Id;
            public string Api, Sent, Got, Status;
            public double Start, End = -1;
            public bool Running => End < 0;
            public double Seconds(double now) => (Running ? now : End) - Start;
        }

        public sealed class Note
        {
            public double At;
            public string Area, Text;
        }

        const int KeepCalls = 60, KeepNotes = 40, ClipAt = 90;
        static readonly object Gate = new object();
        static readonly List<Call> Calls = new List<Call>();
        static readonly List<Note> Notes = new List<Note>();
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        static int _id, _total, _failed, _version;

        /// <summary>Set by a debug panel. Off: nothing is recorded.</summary>
        public static bool Enabled { get; set; }
        /// <summary>Seconds on the trace's clock (any thread).</summary>
        public static double Now => Clock.Elapsed.TotalSeconds;
        /// <summary>Bumps on every change.</summary>
        public static int Version => Volatile.Read(ref _version);

        /// <summary>A call is starting. Null (and nothing kept) while tracing is off.</summary>
        public static Call Begin(string api, string sent)
        {
            if (!Enabled) return null;
            var c = new Call { Id = Interlocked.Increment(ref _id), Api = api, Sent = sent, Status = "running", Start = Now };
            lock (Gate)
            {
                Calls.Add(c);
                _total++;
                // the oldest finished calls go first; a call still running is never dropped
                for (var i = 0; i < Calls.Count && Calls.Count > KeepCalls; )
                    if (!Calls[i].Running) Calls.RemoveAt(i); else i++;
            }
            Interlocked.Increment(ref _version);
            return c;
        }

        /// <summary>A call ended: "ok", "cancelled", or the error kind, with what came back (or the error).</summary>
        public static void End(Call c, string status, string got = null)
        {
            if (c == null) return;
            lock (Gate)
            {
                c.Status = status; c.Got = got; c.End = Now;
                if (status != "ok" && status != "cancelled") _failed++;
            }
            Interlocked.Increment(ref _version);
        }

        /// <summary>Something the app did or decided (a skip, a timeout, a line played silently).</summary>
        public static void Add(string area, string text)
        {
            if (!Enabled) return;
            lock (Gate)
            {
                Notes.Add(new Note { At = Now, Area = area, Text = text });
                if (Notes.Count > KeepNotes) Notes.RemoveAt(0);
            }
            Interlocked.Increment(ref _version);
        }

        /// <summary>Copies of the calls and notes, oldest first, to draw on the main thread.</summary>
        public static void Snapshot(List<Call> calls, List<Note> notes, out int total, out int failed)
        {
            calls.Clear(); notes.Clear();
            lock (Gate)
            {
                foreach (var c in Calls)
                    calls.Add(new Call { Id = c.Id, Api = c.Api, Sent = c.Sent, Got = c.Got, Status = c.Status, Start = c.Start, End = c.End });
                notes.AddRange(Notes);
                total = _total; failed = _failed;
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Calls.RemoveAll(c => !c.Running);
                Notes.Clear();
                _total = Calls.Count; _failed = 0;
            }
            Interlocked.Increment(ref _version);
        }

        /// <summary>
        /// A JSON object as short "key: value" pairs. Long text is cut; pictures, audio and links are only named (a signed audio
        /// link is a credential for an hour, and base64 is just noise).
        /// </summary>
        public static string Describe(JObject o)
        {
            if (o == null) return "";
            var sb = new StringBuilder();
            foreach (var p in o.Properties())
            {
                var v = p.Value;
                string s;
                if (p.Name == "image" || p.Name == "audio" || p.Name == "audioBase64")
                    s = v.Type == JTokenType.String ? $"<{((string)v).Length * 3 / 4 / 1024} KB>" : "<data>";
                else if (p.Name.EndsWith("Url") || p.Name.EndsWith("url")) s = v.Type == JTokenType.Null ? "none" : "<link>";
                else if (v.Type == JTokenType.String) s = "\"" + Clip((string)v, ClipAt) + "\"";
                else if (v.Type == JTokenType.Object || v.Type == JTokenType.Array) s = Clip(v.ToString(Newtonsoft.Json.Formatting.None), ClipAt);
                else s = v.ToString();
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(p.Name).Append(": ").Append(s);
            }
            return sb.ToString();
        }

        public static string Clip(string s, int max)
            => string.IsNullOrEmpty(s) ? s : (s = s.Replace('\n', ' ')).Length <= max ? s : s.Substring(0, max - 1) + "…";
    }
}
