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

        static readonly Intent[] SevenStones =
        {
            I("hausa|town|where|ville|où|pueblo|ciudad|dónde|donde", 0,
                new[] { "It all happens long ago, in a town in Hausaland.", "By day, the town is full of life." },
                new[] { "Tout se passe il y a longtemps, dans une ville du pays haoussa.", "Le jour, la ville déborde de vie." },
                new[] { "Todo ocurre hace mucho tiempo, en un pueblo del país hausa.", "De día, el pueblo rebosa de vida." }),
            I("dodo|creature|monster|créature|monstre|criatura|monstruo", 3,
                new[] { "Dodo is a creature the whole town fears.", "Nobody dares to challenge it." },
                new[] { "Dodo est une créature que toute la ville craint.", "Personne n’ose la défier." },
                new[] { "Dodo es una criatura que todo el pueblo teme.", "Nadie se atreve a desafiarla." }),
            I("fight|answer|afraid|scared|hide|combat|répond|peur|pelea|respond|miedo", 7,
                new[] { "Dodo calls out, asking who in the town can fight it.", "Everyone is too frightened to answer." },
                new[] { "Dodo crie pour demander qui, dans la ville, peut se battre contre lui.", "Tout le monde a trop peur pour répondre." },
                new[] { "Dodo grita preguntando quién en el pueblo puede pelear con él.", "Todos tienen demasiado miedo para responder." }),
            I("auta|boy|garçon|niño|nino|old woman|vieille|anciana", 16,
                new[] { "Auta is a young boy, a stranger in the town.", "He is staying with an old woman." },
                new[] { "Auta est un jeune garçon, un étranger dans la ville.", "Il loge chez une vieille femme." },
                new[] { "Auta es un niño, un forastero en el pueblo.", "Vive con una anciana." }),
            I("stone|hot|fire|pierre|feu|piedra|fuego", 26,
                new[] { "Auta heats seven stones in the fire until they are very hot.", "He has a plan for them." },
                new[] { "Auta chauffe sept pierres dans le feu jusqu’à ce qu’elles soient brûlantes.", "Il a un plan." },
                new[] { "Auta calienta siete piedras en el fuego hasta que arden.", "Tiene un plan." }),
            I("mouth|swallow|throw|bouche|gueule|avale|boca|traga", 31,
                new[] { "When Dodo bends down at the doorway, Auta throws a hot stone into its mouth.", "Dodo swallows it." },
                new[] { "Quand Dodo se baisse à la porte, Auta lui lance une pierre brûlante dans la gueule.", "Dodo l’avale." },
                new[] { "Cuando Dodo se agacha en la puerta, Auta le lanza una piedra caliente a la boca.", "Dodo se la traga." }),
            I("dead|die|defeat|kill|mort|vaincu|tué|muerto|vencido|mató", 48,
                new[] { "After swallowing all seven hot stones, Dodo could not go on.", "By morning, it was dead." },
                new[] { "Après avoir avalé les sept pierres brûlantes, Dodo ne put plus continuer.", "Au matin, il était mort." },
                new[] { "Después de tragarse las siete piedras calientes, Dodo ya no pudo seguir.", "Por la mañana, estaba muerto." }),
            I("horse|cheval|chevaux|caballo", 54,
                new[] { "The horses refuse to go near Dodo's body.", "Even dead, it frightens them." },
                new[] { "Les chevaux refusent de s’approcher du corps de Dodo.", "Même mort, il les effraie." },
                new[] { "Los caballos se niegan a acercarse al cuerpo de Dodo.", "Incluso muerto, los asusta." }),
            I("tail|proof|queue|preuve|cola|prueba", 58,
                new[] { "Auta took Dodo's tail in the night.", "He shows it as proof that he killed the creature." },
                new[] { "Auta a pris la queue de Dodo pendant la nuit.", "Il la montre comme preuve qu’il a tué la créature." },
                new[] { "Auta le quitó la cola a Dodo durante la noche.", "La muestra como prueba de que él mató a la criatura." }),
            I("king|reward|roi|récompense|rey|premio|recompensa", 61,
                new[] { "The king rewards Auta for his bravery." },
                new[] { "Le roi récompense Auta pour son courage." },
                new[] { "El rey recompensa a Auta por su valentía." }),
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
        /// The most advanced matching topic wins, so "how did the stones defeat Dodo?" hits the spoiler guard until the story
        /// has said it. Pure.
        /// </summary>
        public static string[] AnswerFor(string question, int lineIndex, string lang, string storyId)
        {
            var intents = storyId == "marlow-street" ? Marlow : SevenStones;
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
