using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The V1 stories, both told by the storyteller at the campfire. Ported from the Three.js prototype
    /// (Ideation/src/stories.js, story-kiko.js, story-marlow.js). All text is original placeholder content.
    /// Adding a story = adding an entry here (and, for a new look, a theme in the StoryCard shader).
    /// </summary>
    public static class StoryLibrary
    {
        static Caption C(float at, float until, string text, string fr, string es)
            => new Caption { At = at, Until = until, Text = text, Tr = { ["fr"] = fr, ["es"] = es } };

        static Vector2[] Keys(params float[] tv)
        {
            var k = new Vector2[tv.Length / 2];
            for (var i = 0; i < k.Length; i++) k[i] = new Vector2(tv[i * 2], tv[i * 2 + 1]);
            return k;
        }

        /// <summary>
        /// "Kiko and the Remembering Fire". Caption times come from the pauses in the recorded narration (44.4 s); the three
        /// "why ..." lines share one breath so they are split by word count. If the recording changes, move the times.
        /// It starts to rain at "Not when it rained", builds through the wind and the storm, then eases.
        /// </summary>
        public static readonly StoryTimeline Kiko = new StoryTimeline
        {
            Duration = 48f,
            Flashes = new[] { 34.4f, 35.6f, 36.6f },
            Captions = new[]
            {
                C(0.05f, 3.7f, "Long ago, in a small village surrounded by forests,", "Il y a bien longtemps, dans un petit village entouré de forêts,", "Hace mucho tiempo, en un pequeño pueblo rodeado de bosques,"),
                C(3.95f, 6.6f, "there lived a curious tortoise named Kiko.", "vivait une tortue curieuse nommée Kiko.", "vivía una tortuga curiosa llamada Kiko."),
                C(6.8f, 10.2f, "Kiko was known throughout the village for asking too many questions.", "Kiko était connu dans tout le village pour poser trop de questions.", "Kiko era conocido en todo el pueblo por hacer demasiadas preguntas."),
                C(10.3f, 12.1f, "He wanted to know everything:", "Il voulait tout savoir :", "Quería saberlo todo:"),
                C(12.2f, 14.4f, "why the moon followed travellers,", "pourquoi la lune suivait les voyageurs,", "por qué la luna seguía a los viajeros,"),
                C(14.5f, 16.6f, "why birds sang before sunrise,", "pourquoi les oiseaux chantaient avant le lever du soleil,", "por qué los pájaros cantaban antes del amanecer,"),
                C(16.7f, 20.5f, "and why the old baobab tree never seemed to lose its leaves.", "et pourquoi le vieux baobab ne semblait jamais perdre ses feuilles.", "y por qué el viejo baobab nunca parecía perder sus hojas."),
                C(20.75f, 23.6f, "But there was one mystery nobody could explain.", "Mais il y avait un mystère que personne ne pouvait expliquer.", "Pero había un misterio que nadie podía explicar."),
                C(23.85f, 26.6f, "Every night, deep inside the forest,", "Chaque nuit, au fond de la forêt,", "Cada noche, en lo profundo del bosque,"),
                C(26.8f, 30.2f, "a fire could be seen burning beneath an ancient tree.", "on pouvait voir un feu brûler sous un arbre ancien.", "se veía arder un fuego bajo un árbol antiguo."),
                C(30.5f, 31.9f, "It never went out.", "Il ne s’éteignait jamais.", "Nunca se apagaba."),
                C(32.0f, 33.3f, "Not when it rained.", "Pas même quand il pleuvait.", "Ni cuando llovía."),
                C(33.45f, 35.0f, "Not when the wind blew.", "Pas même quand le vent soufflait.", "Ni cuando soplaba el viento."),
                C(35.2f, 37.7f, "Not even during the heaviest storms.", "Pas même pendant les pires tempêtes.", "Ni siquiera durante las peores tormentas."),
                C(37.8f, 40.9f, "The villagers called it the Remembering Fire.", "Les villageois l’appelaient le Feu du Souvenir.", "Los aldeanos lo llamaban el Fuego del Recuerdo."),
                C(41.0f, 46.0f, "They believed it held the memories of everyone who had ever lived.", "Ils croyaient qu’il gardait les souvenirs de tous ceux qui avaient vécu.", "Creían que guardaba los recuerdos de todos los que habían vivido."),
            },
            Rain = Keys(0, 0, 31.8f, 0, 33.4f, 0.4f, 35.2f, 0.8f, 36.4f, 1, 37.8f, 0.45f, 40, 0.1f, 42, 0),
            Wind = Keys(0, 0, 30, 0.1f, 33, 0.5f, 36, 1, 38, 0.5f, 40.5f, 0.15f, 44, 0),
        };

        static readonly (string text, string fr, string es)[] MarlowLines =
        {
            ("It was raining on Marlow Street, the kind of rain that hides footsteps.", "Il pleuvait sur Marlow Street, le genre de pluie qui cache les pas.", "Llovía en Marlow Street, esa clase de lluvia que esconde las pisadas."),
            ("Detective Nia stood behind the yellow tape, watching the alley breathe red and blue.", "La détective Nia se tenait derrière le ruban jaune, regardant la ruelle respirer en rouge et bleu.", "La detective Nia estaba tras la cinta amarilla, viendo cómo el callejón respiraba en rojo y azul."),
            ("A man had been found at the far end, beside the old brick wall.", "Un homme avait été retrouvé au fond, près du vieux mur de briques.", "Habían encontrado a un hombre al fondo, junto al viejo muro de ladrillo."),
            ("No weapon. No wallet. Only one witness, a girl who swore she had seen nothing.", "Pas d’arme. Pas de portefeuille. Un seul témoin, une fille qui jurait n’avoir rien vu.", "Ni arma. Ni cartera. Solo una testigo, una niña que juraba no haber visto nada."),
            ("But the streetlight above the alley had been flickering all night.", "Mais le lampadaire au-dessus de la ruelle clignotait depuis le début de la nuit.", "Pero la farola sobre el callejón llevaba toda la noche parpadeando."),
            ("Nia noticed it never flickered at random.", "Nia remarqua qu’il ne clignotait jamais au hasard.", "Nia notó que nunca parpadeaba al azar."),
            ("It went dark for three seconds, then flashed twice.", "Il s’éteignait trois secondes, puis clignotait deux fois.", "Se apagaba tres segundos y luego destellaba dos veces."),
            ("Over and over, like someone sending a message.", "Encore et encore, comme quelqu’un qui envoie un message.", "Una y otra vez, como alguien que envía un mensaje."),
            ("She followed the pattern to the lamp post and found a small brass key taped beneath it.", "Elle suivit le rythme jusqu’au lampadaire et trouva une petite clé en laiton collée en dessous.", "Siguió el patrón hasta el poste y encontró una pequeña llave de latón pegada debajo."),
            ("The key fit a door that had been bricked over for twenty years.", "La clé allait à une porte murée depuis vingt ans.", "La llave encajaba en una puerta tapiada hacía veinte años."),
            ("Behind the bricks was a cupboard, and inside it a single photograph.", "Derrière les briques se cachait un placard, et dedans une seule photographie.", "Tras los ladrillos había un armario y dentro una sola fotografía."),
            ("It showed the witness, much younger, standing beside the missing man.", "Elle montrait la témoin, bien plus jeune, à côté de l’homme disparu.", "Mostraba a la testigo, mucho más joven, junto al hombre desaparecido."),
            ("The girl had not seen nothing. She had seen everything, and she had been waiting for someone to ask the right question.", "La fille n’avait pas rien vu. Elle avait tout vu, et elle attendait que quelqu’un pose la bonne question.", "La niña no había visto nada… había visto todo, y esperaba que alguien hiciera la pregunta correcta."),
        };

        /// <summary>
        /// "The Last Light on Marlow Street", told with TTS line by line. Caption times are estimates from word count (2.6 words/s);
        /// the player pins the clock to the line being spoken, so they only need to be roughly right.
        /// </summary>
        public static readonly StoryTimeline Marlow = BuildMarlow();

        static StoryTimeline BuildMarlow()
        {
            var t = 0.4f;
            var caps = new List<Caption>();
            foreach (var (text, fr, es) in MarlowLines)
            {
                var dur = text.Split(' ').Length / 2.6f;
                caps.Add(C(Round2(t), Round2(t + dur), text, fr, es));
                t += dur + 0.45f;
            }
            return new StoryTimeline
            {
                Duration = Mathf.Ceil(t + 1.5f),
                // the streetlight lines flicker the sky
                Flashes = new[] { caps[4].At + 1.2f, caps[6].At + 0.8f, caps[6].At + 3.1f, caps[7].At + 1.4f },
                Captions = caps.ToArray(),
                Rain = Keys(0, 0.55f, 60, 0.55f),
                Wind = Keys(0, 0.25f, 60, 0.25f),
            };
        }

        static float Round2(float v) => (float)Math.Round(v, 2, MidpointRounding.AwayFromZero);

        public static readonly StoryInfo[] All =
        {
            new StoryInfo
            {
                Id = "kiko", Theme = 0, Accent = new Color(1f, 0.70f, 0.36f), Timeline = Kiko,
                Eyebrow = "Folk tale · Prototype", Title = "Kiko and the\nRemembering Fire",
                Desc = "A curious tortoise follows his questions into the forest, toward a fire that has never gone out.",
            },
            new StoryInfo
            {
                Id = "marlow-street", Theme = 1, Accent = new Color(1f, 0.35f, 0.39f), Timeline = Marlow,
                Eyebrow = "Crime story · Prototype", Title = "The Last Light\non Marlow Street",
                Desc = "Rain, a taped-off alley, and one witness who saw nothing, until the streetlight started to flicker.",
            },
        };

        public static StoryInfo Find(string id) => All.FirstOrDefault(s => s.Id == id);
    }
}
