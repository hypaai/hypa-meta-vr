using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HypaStories
{
    /// <summary>A question asked mid-story. LineIndex is the interrupted line: nothing after it may be told (no spoilers).</summary>
    public sealed class QaRequest
    {
        public string Question, StoryId, StorySoFar;
        public StoryLanguage Language;
        public int LineIndex;
    }

    /// <summary>
    /// The Q&amp;A service: question + story so far + language → answer sentences, each delivered as soon as it is ready so it can
    /// be spoken while the next one is still coming. 2-3 short sentences, never past the interrupted line.
    /// </summary>
    public interface IStoryQa
    {
        string Mode { get; }
        /// <summary>Calls <paramref name="onSentence"/>(text, index, isLast) per sentence on the caller's thread. Throws when offline.</summary>
        Task AnswerAsync(QaRequest request, Action<string, int, bool> onSentence, CancellationToken ct);
    }

    /// <summary>
    /// Stand-in for <see cref="ChatStoryQa"/> (canned, spoiler-guarded answers with simulated latency), ported from the prototype's
    /// qa.js: tests the interaction loop without the network. Pick it with StoryApp's qaMode: "stub", "slow" (about 3 s to the first
    /// sentence: exercises the second filler) or "offline" (every call fails).
    /// </summary>
    public sealed class StubStoryQa : IStoryQa
    {
        public string Mode { get; set; } = "stub";
        readonly Random _rng = new Random();

        sealed class Intent
        {
            public Regex Re;
            /// <summary>First line from which the story has said enough to answer; earlier is a spoiler.</summary>
            public int Min;
            public Dictionary<string, string[]> A;
        }

        static Intent I(string pattern, int min, string[] en, string[] fr = null, string[] es = null)
        {
            var a = new Dictionary<string, string[]> { ["en"] = en };
            if (fr != null) a["fr"] = fr;
            if (es != null) a["es"] = es;
            return new Intent { Re = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), Min = min, A = a };
        }

        static readonly Intent[] Kiko =
        {
            I("kiko|tortoise|tortue|tortuga|who is", 1,
                new[] { "Kiko is a curious tortoise from a small village.", "He is known for asking too many questions." },
                new[] { "Kiko est une tortue curieuse d’un petit village.", "Il est connu pour poser trop de questions." },
                new[] { "Kiko es una tortuga curiosa de un pequeño pueblo.", "Es famoso por hacer demasiadas preguntas." }),
            I("village|where|where does|où|dónde|donde", 0,
                new[] { "It all happens long ago, in a small village.", "The village is surrounded by forests." },
                new[] { "Tout se passe il y a longtemps, dans un petit village.", "Le village est entouré de forêts." },
                new[] { "Todo ocurre hace mucho tiempo, en un pequeño pueblo.", "El pueblo está rodeado de bosques." }),
            I("moon|lune|luna", 4,
                new[] { "Kiko wonders why the moon seems to follow travellers.", "It is one of the many things he wants to understand." },
                new[] { "Kiko se demande pourquoi la lune semble suivre les voyageurs.", "C’est l’une des nombreuses choses qu’il veut comprendre." },
                new[] { "Kiko se pregunta por qué la luna parece seguir a los viajeros.", "Es una de las muchas cosas que quiere entender." }),
            I("bird|oiseau|pájaro|pajaro|sunrise", 5,
                new[] { "Kiko also wonders why birds sing before sunrise." },
                new[] { "Kiko se demande aussi pourquoi les oiseaux chantent avant le lever du soleil." },
                new[] { "Kiko también se pregunta por qué los pájaros cantan antes del amanecer." }),
            I("baobab|tree|arbre|árbol|arbol|leaves", 6,
                new[] { "The old baobab tree never seems to lose its leaves.", "Kiko cannot work out why." },
                new[] { "Le vieux baobab ne perd jamais ses feuilles.", "Kiko ne comprend pas pourquoi." },
                new[] { "El viejo baobab nunca parece perder sus hojas.", "Kiko no entiende por qué." }),
            I("fire|feu|fuego|burning", 9,
                new[] { "Deep in the forest, a fire burns beneath an ancient tree.", "Nobody in the village can explain it." },
                new[] { "Au fond de la forêt, un feu brûle sous un arbre ancien.", "Personne au village ne peut l’expliquer." },
                new[] { "En lo profundo del bosque, un fuego arde bajo un árbol antiguo.", "Nadie en el pueblo puede explicarlo." }),
            I("rain|wind|storm|pluie|vent|lluvia|viento|tormenta|go out|never", 11,
                new[] { "That fire never goes out.", "Not in the rain, not in the wind, not even in the heaviest storms." },
                new[] { "Ce feu ne s’éteint jamais.", "Ni sous la pluie, ni dans le vent, pas même dans les pires tempêtes." },
                new[] { "Ese fuego nunca se apaga.", "Ni con lluvia, ni con viento, ni siquiera en las peores tormentas." }),
            I("remember|memor|mémoire|souvenir|recuerdo|name|called", 14,
                new[] { "The villagers call it the Remembering Fire.", "They believe it holds the memories of everyone who ever lived." },
                new[] { "Les villageois l’appellent le Feu du Souvenir.", "Ils croient qu’il garde les souvenirs de tous ceux qui ont vécu." },
                new[] { "Los aldeanos lo llaman el Fuego del Recuerdo.", "Creen que guarda los recuerdos de todos los que han vivido." }),
        };

        // Marlow Street: English answers; other languages fall back to English until the translation step covers answers.
        static readonly Intent[] Marlow =
        {
            I("nia|detective|who is", 1, new[] { "Nia is the detective on the case.", "She is standing behind the tape, watching the alley." }),
            I("man|body|found|dead|victim", 2, new[] { "A man was found at the far end of the alley, beside the old brick wall." }),
            I("witness|girl|saw", 3, new[] { "The only witness is a girl.", "She swears she saw nothing." }),
            I("light|lamp|flicker|streetlight", 4, new[] { "The streetlight above the alley has been flickering all night.", "Nia thinks it is not random." }),
            I("pattern|message|code|three seconds", 7, new[] { "The light goes dark for three seconds, then flashes twice.", "It repeats, like someone sending a message." }),
            I("key|brass", 8, new[] { "Under the lamp post, Nia finds a small brass key." }),
            I("door|brick|cupboard", 9, new[] { "The key opens a door that was bricked over twenty years ago." }),
            I("photo|picture|photograph", 11, new[] { "Inside the cupboard is a single photograph.", "It shows the witness, much younger, beside the missing man." }),
            I("truth|why|really|waiting|question", 12, new[] { "The girl saw everything.", "She was waiting for someone to ask the right question." }),
        };

        static readonly Dictionary<string, string[]> Spoiler = new Dictionary<string, string[]>
        {
            ["en"] = new[] { "Hmm, I cannot tell you that just yet.", "Keep listening and you will find out." },
            ["fr"] = new[] { "Hmm, je ne peux pas encore te le dire.", "Continue d’écouter et tu le découvriras." },
            ["es"] = new[] { "Mmm, todavía no puedo contártelo.", "Sigue escuchando y lo descubrirás." },
        };

        static readonly Dictionary<string, string[]> Default = new Dictionary<string, string[]>
        {
            ["en"] = new[] { "That is a good question.", "Let us see what the story tells us." },
            ["fr"] = new[] { "C’est une bonne question.", "Voyons ce que l’histoire nous dit." },
            ["es"] = new[] { "Es una buena pregunta.", "Veamos qué nos cuenta la historia." },
        };

        static string[] For(Dictionary<string, string[]> d, string lang) => d.TryGetValue(lang ?? "en", out var a) ? a : d["en"];

        /// <summary>
        /// The most advanced matching topic wins, so "why does the fire never go out?" hits the spoiler guard until the story
        /// has said it. Pure.
        /// </summary>
        public static string[] AnswerFor(string question, int lineIndex, string lang, string storyId)
        {
            var intents = storyId == "marlow-street" ? Marlow : Kiko;
            var hit = intents.Where(i => i.Re.IsMatch(question ?? "")).OrderByDescending(i => i.Min).FirstOrDefault();
            if (hit == null) return For(Default, lang);
            if (hit.Min > lineIndex) return For(Spoiler, lang);
            return For(hit.A, lang);
        }

        public async Task AnswerAsync(QaRequest req, Action<string, int, bool> onSentence, CancellationToken ct)
        {
            if (Mode == "offline") throw new InvalidOperationException("offline");
            await Task.Delay(Mode == "slow" ? 3200 : 450 + _rng.Next(250), ct); // chat: time to the first sentence
            var sentences = AnswerFor(req.Question, req.LineIndex, req.Language?.Code, req.StoryId);
            for (var i = 0; i < sentences.Length; i++)
            {
                if (i > 0) await Task.Delay(260, ct);
                onSentence(sentences[i], i, i == sentences.Length - 1);
            }
        }
    }
}
