using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The stories, loaded from Resources/Stories/{id}/: story.json (the English script, card text, answer model, voices, suggested
    /// questions and weather cues) and one {language code}.json per translation ({ line id: text, "line id.ask": [questions] }).
    /// Adding a language = adding a file per story; a line it doesn't translate is told in English. See README > Stories.
    ///
    /// Lines carry no times: the storyteller's TTS paces the story, so each line gets an estimate from its English word count
    /// (<see cref="WordsPerSecond"/>), and weather cues reach their value by the end of their line.
    /// </summary>
    public static class StoryLibrary
    {
        /// <summary>Card order. Each id is a folder under Resources/Stories.</summary>
        static readonly string[] Ids = { "kiko", "marlow-street" };

        const float WordsPerSecond = 2.6f, Start = 0.4f, Gap = 0.45f, Tail = 1.5f;

        /// <summary>Answers questions when a story.json names no model.</summary>
        public const string DefaultModel = "meta/muse-wonder-1.0";

        public static readonly StoryInfo[] All = Ids.Select(Load).ToArray();

        public static StoryInfo Find(string id) => All.FirstOrDefault(s => s.Id == id);

        static StoryInfo Load(string id)
        {
            var story = Resources.Load<TextAsset>($"Stories/{id}/story");
            if (story == null) throw new InvalidOperationException($"Hypa Stories: no Resources/Stories/{id}/story.json.");
            var translations = new Dictionary<string, string>();
            foreach (var lang in StoryLanguages.All)
            {
                var file = Resources.Load<TextAsset>($"Stories/{id}/{lang.Code}");
                if (file != null) translations[lang.Code] = file.text;
            }
            return Parse(id, story.text, translations);
        }

        /// <summary>A story from its story.json and its translation files (language code → file text). Pure.</summary>
        public static StoryInfo Parse(string id, string storyJson, IReadOnlyDictionary<string, string> translations)
        {
            var o = JObject.Parse(storyJson);
            ColorUtility.TryParseHtmlString((string)o["accent"] ?? "#FFFFFF", out var accent);
            var tr = translations.ToDictionary(t => t.Key, t => JObject.Parse(t.Value));

            var caps = new List<Caption>();
            var flashes = new List<float>();
            var rain = Track("rain", o);
            var wind = Track("wind", o);
            var t = Start;
            foreach (var line in (JArray)o["lines"])
            {
                var lineId = (string)line["id"];
                var text = (string)line["text"];
                var c = new Caption
                {
                    Id = lineId, At = Round2(t), Until = Round2(t + text.Split(' ').Length / WordsPerSecond), Text = text,
                    Ask = Strings(line["ask"]),
                };
                foreach (var kv in tr)
                {
                    if (kv.Value[lineId] is JValue v && !string.IsNullOrEmpty((string)v)) c.Tr[kv.Key] = (string)v;
                    var ask = Strings(kv.Value[lineId + ".ask"]);
                    if (ask.Length > 0) c.AskTr[kv.Key] = ask;
                }
                caps.Add(c);
                rain.Cue(line, c);
                wind.Cue(line, c);
                foreach (var f in (line["flashes"] as JArray) ?? new JArray()) flashes.Add(c.At + (float)f);
                t = c.Until + Gap;
            }

            return new StoryInfo
            {
                Id = id, Theme = (int?)o["theme"] ?? 0, Accent = accent,
                Eyebrow = (string)o["eyebrow"] ?? "", Title = (string)o["title"] ?? id, Desc = (string)o["description"] ?? "",
                Model = (string)o["model"] ?? DefaultModel,
                Voices = ((o["voices"] as JArray) ?? new JArray()).OfType<JObject>()
                    .Select(v => new VoiceOption { Model = (string)v["model"], Voice = (string)v["voice"] })
                    .Where(v => !string.IsNullOrEmpty(v.Model) && !string.IsNullOrEmpty(v.Voice)).ToArray(),
                Timeline = new StoryTimeline
                {
                    Duration = Mathf.Ceil(t - Gap + Tail),
                    Captions = caps.ToArray(),
                    Flashes = flashes.ToArray(),
                    Rain = rain.Keys.ToArray(),
                    Wind = wind.Keys.ToArray(),
                },
            };
        }

        /// <summary>A weather track: the story's starting value, then each line's cue eased in over that line.</summary>
        sealed class CueTrack
        {
            readonly string _name;
            float _value;
            public readonly List<Vector2> Keys = new List<Vector2>();

            public CueTrack(string name, float start) { _name = name; _value = start; Keys.Add(new Vector2(0f, start)); }

            public void Cue(JToken line, Caption c)
            {
                var v = (float?)line[_name];
                if (v == null) return;
                Keys.Add(new Vector2(c.At, _value));
                Keys.Add(new Vector2(c.Until, v.Value));
                _value = v.Value;
            }
        }

        static string[] Strings(JToken t)
            => (t as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray() ?? new string[0];

        static CueTrack Track(string name, JObject story) => new CueTrack(name, (float?)story[name] ?? 0f);

        static float Round2(float v) => (float)Math.Round(v, 2, MidpointRounding.AwayFromZero);
    }
}
