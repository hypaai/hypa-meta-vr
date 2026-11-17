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

        // Eshu at the Crossroads: English, French and Spanish answers; other languages fall back to English.
        static readonly Intent[] Eshu =
        {
            I("crossroad|where|où|carrefour|dónde|donde|encrucijada", 0,
                new[] { "The road runs between two compounds and meets at a crossroads.", "Everyone who uses it has to pass that place." },
                new[] { "La route passe entre deux concessions et rejoint un carrefour.", "Tous ceux qui l’empruntent passent par cet endroit." },
                new[] { "El camino pasa entre dos recintos familiares y llega a una encrucijada.", "Todos los que lo usan tienen que pasar por ese lugar." }),
            I("friend|ami|amigo", 2,
                new[] { "They are two friends who farm side by side.", "They go to market as one." },
                new[] { "Ce sont deux amis qui cultivent leurs champs côte à côte.", "Ils vont au marché d’un même pas." },
                new[] { "Son dos amigos que cultivan campos vecinos.", "Van juntos al mercado, como uno solo." }),
            I("swear|oath|swore|jur", 6,
                new[] { "They swore that nothing could come between them.", "Not a quarrel, and not even a god." },
                new[] { "Ils ont juré que rien ne pourrait les séparer.", "Ni une querelle, ni même un dieu." },
                new[] { "Juraron que nada podría separarlos.", "Ni una pelea, ni siquiera un dios." }),
            I("eshu|èṣù|esù|god|dieu|dios|deity|divinité|deidad", 17,
                new[] { "Eshu is a small god of the crossroads.", "He carries messages and mischief between heaven and earth." },
                new[] { "Eshu est un petit dieu des carrefours.", "Il porte les messages et la malice entre le ciel et la terre." },
                new[] { "Eshu es un pequeño dios de las encrucijadas.", "Lleva mensajes y travesuras entre el cielo y la tierra." }),
            I("hat|chapeau|sombrero|colour|color|couleur|red|white|rouge|blanc|rojo|blanco", 19,
                new[] { "His hat is red on one side and white on the other.", "Each friend can see only one side." },
                new[] { "Son chapeau est rouge d’un côté et blanc de l’autre.", "Chaque ami n’en voit qu’un seul côté." },
                new[] { "Su sombrero es rojo por un lado y blanco por el otro.", "Cada amigo solo puede ver un lado." }),
            I("disagree|argue|quarrel|blind|désaccord|disput|querelle|aveugle|discut|pelea|ciego", 27,
                new[] { "Each friend saw a different side of the same hat.", "One saw red, and the other saw white." },
                new[] { "Chaque ami a vu un côté différent du même chapeau.", "L’un a vu du rouge, l’autre du blanc." },
                new[] { "Cada amigo vio un lado distinto del mismo sombrero.", "Uno vio rojo y el otro vio blanco." }),
            I("fight|market|marché|bagarre|mercado|shout", 36,
                new[] { "The quarrel follows them to the market and becomes a fight." },
                new[] { "La querelle les suit jusqu’au marché et tourne à la bagarre." },
                new[] { "La disputa los sigue hasta el mercado y acaba en pelea." }),
            I("laugh|rire|rit|reír|ríe|rie", 43,
                new[] { "Eshu is at the shrine, laughing, because he planned the trick." },
                new[] { "Eshu est au sanctuaire et il rit, parce que c’est lui qui a monté le tour." },
                new[] { "Eshu está en el santuario, riéndose, porque él planeó el engaño." }),
            I("both|truth|teach|same road|vérité|enseign|leçon|verdad|enseñ|lección", 52,
                new[] { "He walked between them so each saw only his own side.", "They forgot they were still on the same road." },
                new[] { "Il est passé entre eux, et chacun n’a vu que son côté.", "Ils avaient oublié qu’ils étaient toujours sur la même route." },
                new[] { "Caminó entre ellos, y cada uno vio solo su propio lado.", "Olvidaron que seguían en el mismo camino." }),
            I("leave|offering|calabash|wine|laiss|offrande|ofrenda|vino", 62,
                new[] { "People leave a little something for Eshu at a crossroads." },
                new[] { "Les gens laissent une petite offrande pour Eshu aux carrefours." },
                new[] { "La gente deja una pequeña ofrenda para Eshu en las encrucijadas." }),
        };

        // The King's Magic Drum: English, French and Spanish answers; other languages fall back to English.
        static readonly Intent[] Drum =
        {
            I("calabar|where|où|dónde|donde", 0,
                new[] { "Calabar is a town by the river in the south of Nigeria.", "King Efriam Duke ruled there." },
                new[] { "Calabar est une ville au bord du fleuve, dans le sud du Nigeria.", "C’est là que régnait le roi Efriam Duke." },
                new[] { "Calabar es una ciudad junto al río, en el sur de Nigeria.", "Allí reinaba el rey Efriam Duke." }),
            I("drum|tambour|tambor|feast|festin|banquete", 1,
                new[] { "When the king beat his drum, a feast appeared.", "There was always enough for everyone." },
                new[] { "Quand le roi battait son tambour, un festin apparaissait.", "Il y en avait toujours assez pour tout le monde." },
                new[] { "Cuando el rey tocaba su tambor, aparecía un banquete.", "Siempre había suficiente para todos." }),
            I("enem|ennemi|war|guerre|guerra", 3,
                new[] { "The king did not love war.", "He would rather feed his enemies than fight them." },
                new[] { "Le roi n’aimait pas la guerre.", "Il préférait nourrir ses ennemis que les combattre." },
                new[] { "Al rey no le gustaba la guerra.", "Prefería dar de comer a sus enemigos que luchar contra ellos." }),
            I("rule|règle|regla|stick|bâton|palo", 6,
                new[] { "The drum's owner must never step over a stick lying across the road.", "If he does, the drum gives no more food." },
                new[] { "Le propriétaire du tambour ne doit jamais enjamber un bâton en travers de la route.", "Sinon, le tambour ne donne plus de nourriture." },
                new[] { "El dueño del tambor nunca debe pasar por encima de un palo atravesado en el camino.", "Si lo hace, el tambor ya no da comida." }),
            I(@"egbo|whip|fouet|látigo", 7,
                new[] { "The Egbo men come when the drum's rule is broken.", "Three hundred of them, with sticks and whips." },
                new[] { "Les hommes Egbo viennent quand on enfreint la règle du tambour.", "Ils sont trois cents, avec des bâtons et des fouets." },
                new[] { "Los hombres Egbo llegan cuando se rompe la regla del tambor.", "Son trescientos, con palos y látigos." }),
            I("tortoise|tortue|tortuga", 9,
                new[] { "Tortoise is clever, and he wants the king's drum more than anyone." },
                new[] { "Tortue est malin, et il veut le tambour du roi plus que personne." },
                new[] { "Tortuga es astuto, y quiere el tambor del rey más que nadie." }),
            I(@"\bnut|noix|nuez|upset|angry|fâché|enfadado", 14,
                new[] { "A palm nut fell from his tree, and the king's wife gave it to her little daughter.", "Tortoise says the nut was his." },
                new[] { "Une noix de palme est tombée de son arbre, et l’épouse du roi l’a donnée à sa fillette.", "Tortue dit que la noix était à lui." },
                new[] { "Una nuez de palma cayó de su árbol, y la esposa del rey se la dio a su hijita.", "Tortuga dice que la nuez era suya." }),
            I("choose|chose|choisi|eligió|elige", 20,
                new[] { "Tortoise chose the drum.", "It was what he had wanted all along." },
                new[] { "Tortue a choisi le tambour.", "C’est ce qu’il voulait depuis le début." },
                new[] { "Tortuga eligió el tambor.", "Era lo que había querido desde el principio." }),
            I(@"wrong|\bmal\b|\bstep|enjamb|por encima", 32,
                new[] { "Tortoise stepped over a stick lying across the path.", "He broke the drum's rule without noticing." },
                new[] { "Tortue a enjambé un bâton en travers du chemin.", "Il a enfreint la règle du tambour sans s’en rendre compte." },
                new[] { "Tortuga pasó por encima de un palo atravesado en el sendero.", "Rompió la regla del tambor sin darse cuenta." }),
            I("king stay|stay away|pas venu|no fue|second feast", 42,
                new[] { "The king and his wives stayed at home.", "Perhaps the king guessed what the drum would do." },
                new[] { "Le roi et ses épouses sont restés chez eux.", "Peut-être que le roi avait deviné ce que ferait le tambour." },
                new[] { "El rey y sus esposas se quedaron en casa.", "Quizás el rey adivinó lo que haría el tambor." }),
            I("leopard|léopard|leopardo|escape|échapp|escap", 47,
                new[] { "Leopard leapt clean over the compound wall.", "He was the only one to get away." },
                new[] { "Léopard a sauté d’un bond par-dessus le mur de la concession.", "C’est le seul qui a pu s’enfuir." },
                new[] { "Leopardo saltó por encima del muro del recinto.", "Fue el único que logró escapar." }),
            I("fufu|tree|arbre|árbol", 52,
                new[] { "The king gave Tortoise a magic fufu tree.", "It gave fresh fufu and soup, but only once a day." },
                new[] { "Le roi a donné à Tortue un arbre à foufou magique.", "Il donnait du foufou frais et de la soupe, mais une seule fois par jour." },
                new[] { "El rey le dio a Tortuga un árbol de fufú mágico.", "Daba fufú fresco y sopa, pero solo una vez al día." }),
            I(@"trick|ruse|truco|\bash|cendre|ceniza", 57,
                new[] { "The boy hid a calabash of ashes in his father's bag, over a small hole.", "The ashes left a trail he could follow." },
                new[] { "Le garçon a caché une calebasse de cendres dans le sac de son père, au-dessus d’un petit trou.", "Les cendres ont laissé une traînée à suivre." },
                new[] { "El niño escondió una calabaza de ceniza en la bolsa de su padre, encima de un agujerito.", "La ceniza dejó un rastro que podía seguir." }),
            I("boys|garçons|niños|twice|deux fois|dos veces", 60,
                new[] { "The boys took food from the tree a second time in one day.", "That broke the tree's rule." },
                new[] { "Les garçons ont pris de la nourriture à l’arbre une deuxième fois dans la même journée.", "Ils ont enfreint la règle de l’arbre." },
                new[] { "Los niños tomaron comida del árbol por segunda vez en el mismo día.", "Así rompieron la regla del árbol." }),
            I(@"palms|palmiers|palmeras|\blive|vivent|viven", 65,
                new[] { "Because the rule was broken, Tortoise's family had to live among the prickly tie-tie palms.", "Tortoises have lived there ever since." },
                new[] { "Parce que la règle a été enfreinte, la famille de Tortue a dû vivre parmi les palmiers rotins épineux.", "Depuis, les tortues vivent là." },
                new[] { "Como se rompió la regla, la familia de Tortuga tuvo que vivir entre las palmeras de ratán espinosas.", "Desde entonces, las tortugas viven allí." }),
        };

        // Marlow Street: English answers; other languages fall back to English until the translation step covers answers.
        static readonly Intent[] Marlow =
        {
            I(@"\bnia\b|detective", 2, new[] { "Nia is the detective on the case.", "She is standing behind the tape, watching the alley." }),
            I(@"bello|officer|policeman", 3, new[] { "Officer Bello is a young policeman working with Nia.", "He is soaked, and trying not to look nervous." }),
            I("man|body|found|dead|victim", 7, new[] { "A man was found at the far end of the alley, beside the old brick wall." }),
            I(@"button|his hand|in his hand", 9, new[] { "In the man's hand, Nia found a single brass button.", "It did not come from his own coat." }),
            I(@"witness|girl|saw|\bama\b", 11, new[] { "The only witness is a girl called Ama.", "She says she saw nothing, because she was asleep." }),
            I(@"asleep|window|lying|\bliar\b", 16, new[] { "Ama says she was asleep.", "But the light in her window, right over the alley, was still on." }),
            I("light|lamp|flicker|streetlight", 18, new[] { "The streetlight above the alley has been flickering all night.", "Nia thinks it is not random." }),
            I("pattern|message|code|three seconds", 23, new[] { "The light goes dark for three seconds, then flashes twice.", "It repeats, like someone sending a message." }),
            I("key|brass", 25, new[] { "Under the lamp post, Nia finds a small brass key." }),
            I("door|brick|cupboard", 28, new[] { "The key opens a door that was bricked over twenty years ago." }),
            I("photo|picture|photograph", 32, new[] { "Inside the cupboard is a single photograph.", "It shows the witness, much younger, beside the missing man." }),
            I(@"blackout|lights went out|power cut|twenty years|vanish|missing", 34, new[] { "Twenty years ago, the whole street lost its power for a night.", "That same night, a man from Marlow Street vanished and was never found." }),
            I(@"came back|come back|ran away|run away|same face", 37, new[] { "The man in the alley is the man who vanished twenty years ago.", "He had run away, and tonight he came back." }),
            I(@"second man|torn|killer|who did it|who killed", 38, new[] { "In the photograph, a second man stands half out of the frame, with brass buttons on his coat.", "His face has been torn away." }),
            I(@"wire|switch|signal|who is sending", 40, new[] { "A thin wire runs from the lamp post to Ama's lit window.", "Someone up there has been talking to Nia all night, in light." }),
            I(@"daughter|father|related", 42, new[] { "In the photograph, the little girl holds the man's hand the way a daughter does." }),
            I("truth|why|really|waiting|question", 43, new[] { "The girl saw everything.", "She was waiting for someone to ask the right question." }),
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
            var intents = storyId == "marlow-street" ? Marlow : storyId == "eshu-crossroads" ? Eshu : storyId == "kings-drum" ? Drum : SevenStones;
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
