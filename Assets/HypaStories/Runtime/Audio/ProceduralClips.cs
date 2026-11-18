using System;
using UnityEngine;

namespace HypaStories
{
    /// <summary>
    /// The prototype's synthesised sounds (crickets, rain bed, rumble, thunder, transition chime, UI tap, fallback wind / fire bed),
    /// rendered once into AudioClips. The prototype built them as WebAudio graphs; here the filters run offline. Sample generation is
    /// pure (unit tested); only <see cref="Clip"/> touches Unity.
    /// </summary>
    public static class ProceduralClips
    {
        public const int Rate = 22050;

        /// <summary>RBJ biquad (the same filters WebAudio's BiquadFilterNode uses).</summary>
        public struct Biquad
        {
            float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public static Biquad LowPass(float hz, float q = 0.7071f) => Make(hz, q, 0);
            public static Biquad HighPass(float hz, float q = 0.7071f) => Make(hz, q, 1);
            public static Biquad BandPass(float hz, float q) => Make(hz, q, 2);

            static Biquad Make(float hz, float q, int type)
            {
                var w = 2f * Mathf.PI * Mathf.Clamp(hz, 10f, Rate * 0.45f) / Rate;
                float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * q), a0 = 1f + alpha;
                var f = new Biquad { _a1 = -2f * cos / a0, _a2 = (1f - alpha) / a0 };
                switch (type)
                {
                    case 0: f._b0 = (1f - cos) / 2f / a0; f._b1 = (1f - cos) / a0; f._b2 = f._b0; break;
                    case 1: f._b0 = (1f + cos) / 2f / a0; f._b1 = -(1f + cos) / a0; f._b2 = f._b0; break;
                    default: f._b0 = alpha / a0; f._b1 = 0f; f._b2 = -alpha / a0; break;
                }
                return f;
            }

            /// <summary>New band-pass centre, keeping the filter state (for sweeps).</summary>
            public void RetuneBandPass(float hz, float q)
            {
                var n = BandPass(hz, q);
                _b0 = n._b0; _b1 = n._b1; _b2 = n._b2; _a1 = n._a1; _a2 = n._a2;
            }

            public float Process(float x)
            {
                var y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }
        }

        public static float[] White(int n, int seed)
        {
            var r = new System.Random(seed); var d = new float[n];
            for (var i = 0; i < n; i++) d[i] = (float)(r.NextDouble() * 2.0 - 1.0);
            return d;
        }

        /// <summary>Brown noise, same recipe as the prototype (leaky integrator, gain 3.5).</summary>
        public static float[] Brown(int n, int seed)
        {
            var w = White(n, seed); var d = new float[n]; var b = 0f;
            for (var i = 0; i < n; i++) { b = (b + 0.02f * w[i]) / 1.02f; d[i] = b * 3.5f; }
            return d;
        }

        static float[] Filter(float[] d, params Biquad[] chain)
        {
            var o = new float[d.Length];
            for (var i = 0; i < d.Length; i++)
            {
                var x = d[i];
                for (var k = 0; k < chain.Length; k++) x = chain[k].Process(x);
                o[i] = x;
            }
            return o;
        }

        /// <summary>Crossfades the end into the start so a loop has no click.</summary>
        static float[] Loopable(float[] d, float fadeSeconds = 0.25f)
        {
            var n = Mathf.Min((int)(fadeSeconds * Rate), d.Length / 4);
            var o = new float[d.Length - n];
            Array.Copy(d, o, o.Length);
            for (var i = 0; i < n; i++)
            {
                var k = (float)i / n;
                o[i] = d[i] * k + d[o.Length + i] * (1f - k);
            }
            return o;
        }

        static float Peak(float[] d) { var m = 1e-6f; foreach (var v in d) m = Mathf.Max(m, Mathf.Abs(v)); return m; }
        static float[] Normalize(float[] d, float to) { var k = to / Peak(d); for (var i = 0; i < d.Length; i++) d[i] *= k; return d; }

        // ---------- Beds (looping) ----------

        /// <summary>Rain hiss: white noise band-limited 1.4-9 kHz.</summary>
        public static float[] RainSamples() => Normalize(Loopable(Filter(White(Rate * 4, 11), Biquad.HighPass(1400), Biquad.LowPass(9000))), 0.9f);

        /// <summary>Storm rumble: brown noise under 140 Hz.</summary>
        public static float[] RumbleSamples() => Normalize(Loopable(Filter(Brown(Rate * 4, 12), Biquad.LowPass(140))), 0.9f);

        /// <summary>Fallback when the recorded beds are missing: wind with a slow swell (0.07 Hz).</summary>
        public static float[] WindSamples()
        {
            var n = (int)(Rate / 0.07f);
            var d = Filter(Brown(n, 13), Biquad.LowPass(420));
            for (var i = 0; i < n; i++) d[i] *= 0.16f + 0.09f * Mathf.Sin(2f * Mathf.PI * 0.07f * i / Rate);
            return Normalize(Loopable(d, 1f), 0.5f);
        }

        /// <summary>Fallback fire: a low roar plus random crackle pops (90 ms to ~1.3 s apart).</summary>
        public static float[] FireSamples()
        {
            var n = Rate * 6;
            var roar = Filter(Brown(n, 14), Biquad.LowPass(260));
            Normalize(roar, 0.25f);
            var r = new System.Random(15);
            var white = White(Rate, 16);
            for (var t = 0; t < n - Rate / 4;)
            {
                var bp = Biquad.BandPass(1800 + (float)r.NextDouble() * 3000, 2f);
                var len = (int)((0.03 + r.NextDouble() * 0.05) * Rate);
                var v = 0.05f + (float)r.NextDouble() * 0.12f;
                var off = r.Next(white.Length - len);
                for (var i = 0; i < len; i++)
                    roar[t + i] += bp.Process(white[off + i]) * v * 4f * Mathf.Exp(-6f * i / len);
                t += (int)((0.09 + r.NextDouble() * r.NextDouble() * 1.2) * Rate);
            }
            return Loopable(roar);
        }

        // ---------- One-shots ----------

        /// <summary>A cricket: 3-5 short pulses at 4.3-4.8 kHz, 70 ms apart.</summary>
        public static float[] ChirpSamples(int seed)
        {
            var r = new System.Random(seed);
            var f = 4300f + (float)r.NextDouble() * 500f; var pulses = 3 + r.Next(3);
            var d = new float[(int)(Rate * (pulses * 0.07f + 0.02f))];
            for (var p = 0; p < pulses; p++)
            {
                var start = (int)(p * 0.07f * Rate);
                for (var i = 0; i < (int)(0.055f * Rate) && start + i < d.Length; i++)
                {
                    var t = (float)i / Rate;
                    var env = t < 0.015f ? t / 0.015f : Mathf.Max(0f, 1f - (t - 0.015f) / 0.04f);
                    d[start + i] = Mathf.Sin(2f * Mathf.PI * f * t) * env * 0.5f;
                }
            }
            return d;
        }

        /// <summary>Thunder: low-passed brown noise, 0.12 s attack, ~2.8 s exponential decay.</summary>
        public static float[] ThunderSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = Rate * 3;
            var d = Filter(Brown(n, seed), Biquad.LowPass(160 + (float)r.NextDouble() * 140));
            Normalize(d, 1f);
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var env = t < 0.12f ? t / 0.12f : Mathf.Exp(-(t - 0.12f) * 2.5f);
                d[i] *= env * 0.7f;
            }
            return d;
        }

        /// <summary>Ticket 3L: the portal opening (a soft shimmer of high sines, a low swell) or being stepped through (a rushing whoosh that falls away).</summary>
        public static float[] PortalSamples(bool stepThrough)
        {
            var n = (int)(Rate * (stepThrough ? 1.4f : 1.1f));
            var d = new float[n];
            var w = White(n, stepThrough ? 41 : 43);
            var bp = Biquad.BandPass(300f, 0.9f);
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var u = t / (n / (float)Rate);
                // the noise band sweeps up as you fall in, and down as it shimmers open
                var hz = stepThrough ? 220f * Mathf.Pow(2600f / 220f, Mathf.Sin(u * Mathf.PI * 0.5f)) : 600f + 900f * u;
                if (i % 64 == 0) bp.RetuneBandPass(hz, 0.9f);
                var env = stepThrough ? Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * (1f - 0.35f * u) : Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * 0.45f;
                d[i] = bp.Process(w[i]) * env * 0.5f;
                if (stepThrough) d[i] += Mathf.Sin(2f * Mathf.PI * (70f - 30f * u) * t) * env * 0.22f; // the drop under the feet
                else d[i] += (Mathf.Sin(2f * Mathf.PI * 880f * t) + Mathf.Sin(2f * Mathf.PI * 1318.5f * t) * 0.7f) * 0.05f * Mathf.Exp(-t * 2.6f);
            }
            return Normalize(d, 0.8f);
        }

        /// <summary>The transition cue: an A-major arpeggio of soft sines plus a rising filtered whoosh.</summary>
        public static float[] ChimeSamples()
        {
            var n = (int)(Rate * 3.0f);
            var d = new float[n];
            float[] hz = { 220f, 329.63f, 440f, 554.37f };
            for (var k = 0; k < hz.Length; k++)
            {
                var start = (int)(k * 0.16f * Rate);
                for (var i = 0; start + i < n && i < Rate * 2.5f; i++)
                {
                    var t = (float)i / Rate;
                    var env = t < 0.05f ? t / 0.05f * 0.07f : 0.07f * Mathf.Exp(-(t - 0.05f) * 3.9f);
                    d[start + i] += Mathf.Sin(2f * Mathf.PI * hz[k] * t) * env;
                }
            }
            var w = White(Rate * 2, 17);
            var bp = Biquad.BandPass(250f, 0.8f);
            for (var i = 0; i < w.Length; i++)
            {
                var t = (float)i / Rate;
                if (i % 64 == 0) bp.RetuneBandPass(250f * Mathf.Pow(1400f / 250f, Mathf.Min(t / 1.6f, 1f)), 0.8f);
                var g = t < 0.9f ? t / 0.9f * 0.25f : Mathf.Max(0f, 0.25f * (1f - (t - 0.9f) / 1.1f));
                d[i] += bp.Process(w[i]) * g;
            }
            return d;
        }

        /// <summary>The UI tap: a soft sine with a 30 ms attack and 1.2 s decay.</summary>
        public static float[] TapSamples(float hz)
        {
            var n = (int)(Rate * 1.3f);
            var d = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var env = t < 0.03f ? t / 0.03f * 0.06f : 0.06f * Mathf.Exp(-(t - 0.03f) * 7.5f);
                d[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * env;
            }
            return d;
        }

        // ---------- The trail to the hut (3G): animals and footsteps ----------

        /// <summary>A wolf's howl: a breathy tone that rises, holds with a slow waver and falls away (about 4 s).</summary>
        public static float[] HowlSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 4.2f);
            var d = new float[n];
            var breath = Filter(White(n, seed + 1), Biquad.BandPass(900f, 1.2f));
            float f0 = 330f + (float)r.NextDouble() * 60f, peak = f0 * (1.55f + (float)r.NextDouble() * 0.15f), ph = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                // glide up over 0.9 s, hold with vibrato, sag at the end
                var u = t < 0.9f ? Mathf.SmoothStep(0f, 1f, t / 0.9f) : t > 3.1f ? 1f - 0.18f * (t - 3.1f) / 1.1f : 1f;
                var f = Mathf.Lerp(f0, peak, u) * (1f + 0.012f * Mathf.Sin(2f * Mathf.PI * 5.3f * t));
                ph += 2f * Mathf.PI * f / Rate;
                var env = t < 0.35f ? t / 0.35f : t > 3.2f ? Mathf.Max(0f, 1f - (t - 3.2f) / 1.0f) : 1f;
                d[i] = (Mathf.Sin(ph) * 0.7f + Mathf.Sin(2f * ph) * 0.18f + Mathf.Sin(3f * ph) * 0.07f + breath[i] * 0.12f) * env;
            }
            return Normalize(d, 0.8f);
        }

        /// <summary>A low growl: a rough, throbbing rumble around 110 Hz (about 1.6 s).</summary>
        public static float[] GrowlSamples(int seed)
        {
            var n = (int)(Rate * 1.6f);
            var rough = Filter(Brown(n, seed), Biquad.LowPass(480f));
            Normalize(rough, 1f);
            var d = new float[n];
            var ph = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                ph += 2f * Mathf.PI * (105f + 12f * Mathf.Sin(2f * Mathf.PI * 1.3f * t)) / Rate;
                var saw = (ph / Mathf.PI % 2f) - 1f;
                var throb = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 23f * t);
                var env = t < 0.25f ? t / 0.25f : t > 1.2f ? Mathf.Max(0f, 1f - (t - 1.2f) / 0.4f) : 1f;
                d[i] = (saw * 0.35f + rough[i] * 0.65f) * throb * env;
            }
            return Normalize(Filter(d, Biquad.LowPass(700f)), 0.8f);
        }

        /// <summary>An owl: "hoo... hoo-hoo", soft low tones with a breathy edge (about 2 s).</summary>
        public static float[] HootSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 2.1f);
            var d = new float[n];
            var air = Filter(White(n, seed + 2), Biquad.BandPass(700f, 2f));
            var f = 360f + (float)r.NextDouble() * 40f;
            (float at, float len, float hz)[] notes = { (0f, 0.55f, f), (0.85f, 0.28f, f * 0.94f), (1.25f, 0.6f, f * 0.9f) };
            foreach (var (at, len, hz) in notes)
            {
                var start = (int)(at * Rate);
                for (var i = 0; i < (int)(len * Rate) && start + i < n; i++)
                {
                    var t = (float)i / Rate;
                    var env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / len)); env *= env;
                    var bend = 1f - 0.04f * t / len;
                    d[start + i] += (Mathf.Sin(2f * Mathf.PI * hz * bend * t) + 0.12f * Mathf.Sin(4f * Mathf.PI * hz * bend * t) + air[start + i] * 0.08f) * env;
                }
            }
            return Normalize(d, 0.7f);
        }

        /// <summary>A twig snapping underfoot: a sharp crack and two small splinters.</summary>
        public static float[] SnapSamples(int seed)
        {
            var n = (int)(Rate * 0.35f);
            var w = White(n, seed);
            var d = new float[n];
            var r = new System.Random(seed);
            foreach (var (at, gain, hz) in new[] { (0f, 1f, 2600f), (0.05f + (float)r.NextDouble() * 0.04f, 0.5f, 3400f), (0.13f + (float)r.NextDouble() * 0.05f, 0.3f, 2000f) })
            {
                var bp = Biquad.BandPass(hz, 1.4f);
                var start = (int)(at * Rate);
                for (var i = 0; i < (int)(0.04f * Rate) && start + i < n; i++)
                    d[start + i] += bp.Process(w[start + i]) * gain * Mathf.Exp(-60f * i / Rate);
            }
            return Normalize(d, 0.9f);
        }

        /// <summary>Something running off through the undergrowth: fluttering leafy noise that fades into the distance (about 1.8 s).</summary>
        public static float[] RustleSamples(int seed)
        {
            var n = (int)(Rate * 1.8f);
            var d = Filter(White(n, seed), Biquad.HighPass(1200f), Biquad.LowPass(6500f));
            var r = new System.Random(seed + 3);
            var g = 0.5f;
            for (var i = 0; i < n; i++)
            {
                if (i % 220 == 0) g = 0.2f + (float)r.NextDouble() * 0.8f; // leaves brushing past, in bursts
                var t = (float)i / Rate;
                var stride = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 5.5f * t); // the gallop
                d[i] *= g * stride * Mathf.Exp(-1.6f * t);
            }
            return Normalize(d, 0.8f);
        }

        /// <summary>One footstep on a dirt trail: a soft thud and a crunch of grit and leaves (about 0.2 s).</summary>
        public static float[] StepSamples(int seed)
        {
            var n = (int)(Rate * 0.22f);
            var thud = Filter(Brown(n, seed), Biquad.LowPass(220f));
            var crunch = Filter(White(n, seed + 5), Biquad.BandPass(1500f + seed % 5 * 180f, 0.9f));
            Normalize(thud, 1f); Normalize(crunch, 1f);
            var r = new System.Random(seed);
            var d = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var grit = r.NextDouble() < 0.02 ? 1.6f : 1f;
                d[i] = thud[i] * 0.6f * Mathf.Exp(-28f * t) + crunch[i] * 0.4f * grit * Mathf.Exp(-16f * t) * Mathf.Clamp01(t / 0.01f);
            }
            return Normalize(d, 0.8f);
        }

        // ---------- the village trail and the village (3M) ----------

        /// <summary>A stream over stones: a low wash, a bright gurgle that comes and goes, and little bubbles (loops, about 5 s).</summary>
        public static float[] StreamSamples()
        {
            var n = Rate * 5;
            var wash = Filter(Brown(n, 71), Biquad.LowPass(700f));
            var hiss = Filter(White(n, 72), Biquad.BandPass(1800f, 0.8f));
            Normalize(wash, 0.5f); Normalize(hiss, 0.25f);
            var r = new System.Random(73);
            var d = new float[n];
            var g = 0.5f;
            for (var i = 0; i < n; i++)
            {
                if (i % 400 == 0) g = Mathf.Lerp(g, (float)r.NextDouble(), 0.5f);
                d[i] = wash[i] + hiss[i] * (0.3f + g);
            }
            for (var b = 0; b < 70; b++)
            {
                var at = r.Next(n - Rate / 10);
                var f0 = 350f + (float)r.NextDouble() * 700f;
                var len = (int)((0.02f + (float)r.NextDouble() * 0.03f) * Rate);
                var v = 0.05f + (float)r.NextDouble() * 0.12f;
                var ph = 0f;
                for (var i = 0; i < len; i++)
                {
                    var t = (float)i / len;
                    ph += 2f * Mathf.PI * f0 * (1f + 1.4f * t) / Rate;
                    d[at + i] += Mathf.Sin(ph) * v * Mathf.Sin(Mathf.PI * t);
                }
            }
            return Normalize(Loopable(d, 0.4f), 0.8f);
        }

        /// <summary>A frog: "rrr-ibbit", two trains of nasal pulses (about 0.5 s).</summary>
        public static float[] CroakSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 0.5f);
            var d = new float[n];
            var f0 = 330f + (float)r.NextDouble() * 140f;
            (float at, float len, float rate)[] trains = { (0f, 0.13f, 62f), (0.19f, 0.2f, 48f) };
            foreach (var (at, len, rate) in trains)
            {
                var pulses = Mathf.RoundToInt(len * rate);
                for (var k = 0; k < pulses; k++)
                {
                    var start = (int)((at + k / rate) * Rate);
                    var pl = (int)(0.011f * Rate);
                    for (var i = 0; i < pl && start + i < n; i++)
                    {
                        var t = (float)i / Rate;
                        d[start + i] += (Mathf.Sin(2f * Mathf.PI * f0 * t) + 0.6f * Mathf.Sin(4f * Mathf.PI * f0 * t) + 0.3f * Mathf.Sin(6f * Mathf.PI * f0 * t)) * Mathf.Exp(-260f * t);
                    }
                }
            }
            return Normalize(Filter(d, Biquad.BandPass(f0 * 1.8f, 1.2f)), 0.8f);
        }

        /// <summary>A bat: three or four tiny high chirps sweeping down (about 0.3 s).</summary>
        public static float[] SqueakSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 0.32f);
            var d = new float[n];
            var count = 3 + r.Next(2);
            for (var k = 0; k < count; k++)
            {
                var start = (int)((k * 0.07f + (float)r.NextDouble() * 0.02f) * Rate);
                var len = (int)(0.012f * Rate);
                var ph = 0f;
                for (var i = 0; i < len && start + i < n; i++)
                {
                    var t = (float)i / len;
                    ph += 2f * Mathf.PI * Mathf.Lerp(7200f, 4300f, t) / Rate;
                    d[start + i] += Mathf.Sin(ph) * Mathf.Sin(Mathf.PI * t);
                }
            }
            return Normalize(d, 0.5f);
        }

        /// <summary>A bushbaby: a wailing cry like a small child, "waah... waah" (about 1.6 s).</summary>
        public static float[] GalagoSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 1.6f);
            var d = new float[n];
            var ph = 0f;
            for (var c = 0; c < 2; c++)
            {
                var start = (int)((c * 0.75f) * Rate);
                var len = (int)((0.5f + (float)r.NextDouble() * 0.12f) * Rate);
                var top = 780f + (float)r.NextDouble() * 120f;
                for (var i = 0; i < len && start + i < n; i++)
                {
                    var t = (float)i / len;
                    var f = Mathf.Lerp(520f, top, Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.4f))) * (1f + 0.025f * Mathf.Sin(2f * Mathf.PI * 7f * i / Rate));
                    ph += 2f * Mathf.PI * f / Rate;
                    var env = Mathf.Sin(Mathf.PI * t); env = Mathf.Sqrt(env);
                    d[start + i] += (Mathf.Sin(ph) + 0.5f * Mathf.Sin(2f * ph) + 0.25f * Mathf.Sin(3f * ph) + 0.12f * Mathf.Sin(4f * ph)) * env;
                }
            }
            return Normalize(Filter(d, Biquad.BandPass(1300f, 0.9f)), 0.75f);
        }

        /// <summary>A goat: "meh-eh-eh", a buzzy bleat with a fast quaver (about 0.9 s).</summary>
        public static float[] BleatSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 0.9f);
            var d = new float[n];
            var f0 = 280f + (float)r.NextDouble() * 60f;
            var ph = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / n;
                var quaver = Mathf.Sin(2f * Mathf.PI * 9f * i / Rate);
                ph += 2f * Mathf.PI * f0 * (1f + 0.035f * quaver - 0.08f * t) / Rate;
                var v = 0f;
                for (var h = 1; h <= 8; h++) v += Mathf.Sin(h * ph) / h;
                var env = Mathf.Clamp01(t * 12f) * Mathf.Clamp01((1f - t) * 4f);
                d[i] = v * env * (0.65f + 0.35f * quaver);
            }
            return Normalize(Filter(d, Biquad.BandPass(950f, 1.1f)), 0.7f);
        }

        /// <summary>A fish leaving the water: a soft plop and a short splash (about 0.25 s).</summary>
        public static float[] PlopSamples(int seed)
        {
            var n = (int)(Rate * 0.28f);
            var d = new float[n];
            var noise = White(n, seed);
            var body = Biquad.LowPass(380f);
            var splash = Biquad.BandPass(1800f, 0.8f);
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                d[i] = body.Process(noise[i]) * Mathf.Exp(-t * 18f) * 0.9f
                     + splash.Process(noise[i]) * Mathf.Clamp01(t / 0.02f) * Mathf.Exp(-t * 22f) * 0.45f;
            }
            return Normalize(d, 0.75f);
        }

        /// <summary>A village dog: one short "woof" (about 0.3 s).</summary>
        public static float[] BarkSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 0.32f);
            var d = new float[n];
            var f0 = 180f + (float)r.NextDouble() * 40f;
            var ph = 0f;
            var bp = Biquad.BandPass(700f, 1.2f);
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                ph += 2f * Mathf.PI * f0 * (1f - 0.15f * t) / Rate;
                var v = Mathf.Sin(ph) + 0.45f * Mathf.Sin(2f * ph) + 0.2f * Mathf.Sin(3f * ph);
                var env = Mathf.Clamp01(t / 0.02f) * Mathf.Exp(-t * 11f);
                d[i] = bp.Process(v * env);
            }
            return Normalize(d, 0.8f);
        }

        /// <summary>A village crowd at work, heard from the fence: many low voices, syllables and pauses, no words (loops, 6 s).</summary>
        public static float[] MurmurSamples()
        {
            var n = Rate * 6;
            var d = new float[n];
            var r = new System.Random(81);
            (float f1, float f2)[] vowels = { (800f, 1200f), (500f, 1900f), (320f, 2300f), (520f, 900f), (340f, 800f), (650f, 1700f) };
            for (var v = 0; v < 8; v++)
            {
                var f0 = (v % 2 == 0 ? 110f : 190f) * (0.9f + (float)r.NextDouble() * 0.25f);
                var noise = White(n, 90 + v);
                var a = Biquad.BandPass(500f, 3f); var b = Biquad.BandPass(1500f, 4f);
                var gain = 0.5f + (float)r.NextDouble() * 0.5f;
                var ph = 0f; var syl = 0; var sylLen = 1; var talking = r.NextDouble() < 0.6; var phrase = (int)(Rate * (0.8 + r.NextDouble() * 2.0));
                var pitch = 1f;
                for (var i = 0; i < n; i++)
                {
                    if (--phrase <= 0) { talking = !talking; phrase = (int)(Rate * (talking ? 0.9 + r.NextDouble() * 2.2 : 0.4 + r.NextDouble() * 1.4)); }
                    if (++syl >= sylLen)
                    {
                        syl = 0; sylLen = (int)(Rate * (0.12 + r.NextDouble() * 0.14));
                        var (f1, f2) = vowels[r.Next(vowels.Length)];
                        a.RetuneBandPass(f1, 3f); b.RetuneBandPass(f2, 4f);
                        pitch = 0.9f + (float)r.NextDouble() * 0.25f;
                    }
                    ph += 2f * Mathf.PI * f0 * pitch / Rate;
                    var src = (ph % (2f * Mathf.PI)) / Mathf.PI - 1f + noise[i] * 0.3f; // a buzzy saw with breath
                    var env = talking ? Mathf.Sin(Mathf.PI * syl / sylLen) : 0f;
                    d[i] += (a.Process(src) + 0.6f * b.Process(src)) * env * gain;
                }
            }
            return Normalize(Loopable(Filter(d, Biquad.LowPass(2600f)), 0.5f), 0.7f);
        }

        /// <summary>One drum stroke in the village's drum loop.</summary>
        public struct DrumHit
        {
            /// <summary>Seconds into the loop.</summary>
            public float T;
            /// <summary>0 the djembe, 1 the talking drum, 2 the big barrel drum (dundun).</summary>
            public int Drum;
            /// <summary>Djembe: 0 bass, 1 tone, 2 slap. Talking drum: 0 short, 1 bends up, 2 bends down.</summary>
            public int Kind;
            public float Gain;
        }

        /// <summary>The drum loop's length: four bars at 120 beats a minute.</summary>
        public const float DrumLoopLength = 8f;

        /// <summary>The village drummers' pattern, four bars of sixteenths. The drummers' arms follow it. Pure.</summary>
        public static DrumHit[] DrumPattern()
        {
            const float step = DrumLoopLength / 64f;
            var hits = new System.Collections.Generic.List<DrumHit>();
            void Add(int bar, int s, int drum, int kind, float gain) => hits.Add(new DrumHit { T = (bar * 16 + s) * step, Drum = drum, Kind = kind, Gain = gain });
            for (var bar = 0; bar < 4; bar++)
            {
                // the dundun: the heartbeat, a fill in the last bar
                foreach (var s in bar < 3 ? new[] { 0, 6, 10 } : new[] { 0, 3, 6, 10, 12, 14 }) Add(bar, s, 2, 0, s == 0 ? 1f : 0.8f);
                // the djembe: bass, tones, slaps
                Add(bar, 0, 0, 0, 1f); Add(bar, 8, 0, 0, 0.9f);
                foreach (var s in new[] { 2, 3, 10, 11 }) Add(bar, s, 0, 1, 0.75f);
                foreach (var s in new[] { 6, 7, 14 }) Add(bar, s, 0, 2, s == 14 ? 0.95f : 0.7f);
                if (bar == 3) { Add(bar, 12, 0, 2, 0.8f); Add(bar, 13, 0, 2, 0.85f); Add(bar, 15, 0, 2, 0.9f); }
                // the talking drum: a call that bends, answered
                if (bar % 2 == 0) { Add(bar, 0, 1, 1, 1f); Add(bar, 3, 1, 0, 0.7f); Add(bar, 4, 1, 0, 0.75f); Add(bar, 7, 1, 2, 0.9f); Add(bar, 12, 1, 1, 0.85f); }
                else if (bar == 1) { Add(bar, 0, 1, 0, 0.8f); Add(bar, 2, 1, 0, 0.7f); Add(bar, 6, 1, 1, 1f); Add(bar, 10, 1, 2, 0.8f); }
                else { Add(bar, 0, 1, 0, 0.75f); Add(bar, 1, 1, 0, 0.7f); Add(bar, 2, 1, 0, 0.75f); Add(bar, 3, 1, 0, 0.8f); Add(bar, 8, 1, 1, 1f); Add(bar, 11, 1, 2, 0.9f); }
            }
            hits.Sort((x, y) => x.T.CompareTo(y.T));
            return hits.ToArray();
        }

        /// <summary>The village drums playing <see cref="DrumPattern"/>, with a shaker on the off-beats (loops, <see cref="DrumLoopLength"/> s).</summary>
        public static float[] DrumLoopSamples()
        {
            var n = (int)(Rate * DrumLoopLength);
            var d = new float[n];
            var noise = White(Rate, 101);
            var r = new System.Random(102);
            void Put(int at, float v) { d[((at % n) + n) % n] += v; }
            foreach (var h in DrumPattern())
            {
                var at = (int)(h.T * Rate) + r.Next(-60, 60); // human, not a machine
                var len = (int)(0.7f * Rate);
                var ph = 0f; var ph2 = 0f;
                var slap = Biquad.BandPass(2800f, 1.1f);
                for (var i = 0; i < len; i++)
                {
                    var t = (float)i / Rate;
                    float v;
                    if (h.Drum == 2)
                    {
                        ph += 2f * Mathf.PI * Mathf.Lerp(62f, 95f, Mathf.Exp(-t * 30f)) / Rate;
                        v = Mathf.Sin(ph) * Mathf.Exp(-t * 7f) + noise[i % noise.Length] * 0.25f * Mathf.Exp(-t * 90f);
                    }
                    else if (h.Drum == 0)
                    {
                        if (h.Kind == 0) { ph += 2f * Mathf.PI * Mathf.Lerp(78f, 110f, Mathf.Exp(-t * 40f)) / Rate; v = Mathf.Sin(ph) * Mathf.Exp(-t * 11f) * 0.9f; }
                        else if (h.Kind == 1) { ph += 2f * Mathf.PI * 285f / Rate; ph2 += 2f * Mathf.PI * 440f / Rate; v = (Mathf.Sin(ph) + 0.5f * Mathf.Sin(ph2)) * Mathf.Exp(-t * 20f) * 0.6f; }
                        else { ph += 2f * Mathf.PI * 560f / Rate; v = (slap.Process(noise[i % noise.Length]) * 1.6f * Mathf.Exp(-t * 45f) + Mathf.Sin(ph) * 0.3f * Mathf.Exp(-t * 35f)); }
                        v += noise[(i + 300) % noise.Length] * 0.12f * Mathf.Exp(-t * 120f);
                    }
                    else
                    {
                        float f0 = 150f, f1 = h.Kind == 1 ? 235f : h.Kind == 2 ? 120f : 165f;
                        if (h.Kind == 2) f0 = 220f;
                        ph += 2f * Mathf.PI * (f1 + (f0 - f1) * Mathf.Exp(-t * 9f)) / Rate;
                        v = (Mathf.Sin(ph) + 0.3f * Mathf.Sin(2f * ph) + 0.1f * Mathf.Sin(3f * ph)) * Mathf.Exp(-t * (h.Kind == 0 ? 9f : 5f)) * 0.55f
                            + noise[(i + 700) % noise.Length] * 0.1f * Mathf.Exp(-t * 100f);
                    }
                    Put(at + i, v * h.Gain);
                }
            }
            // the shaker: every eighth, leaning on the off-beats
            var shake = Filter(White(n, 103), Biquad.HighPass(5200f));
            for (var k = 0; k < 64; k += 2)
            {
                var at = (int)(k * DrumLoopLength / 64f * Rate);
                var accent = k % 4 == 2 ? 1f : 0.5f;
                var len = (int)(0.09f * Rate);
                for (var i = 0; i < len; i++)
                {
                    var t = (float)i / Rate;
                    Put(at + i, shake[(at + i) % n] * 0.22f * accent * Mathf.Clamp01(t / 0.012f) * Mathf.Exp(-t * 38f));
                }
            }
            return Normalize(d, 0.85f);
        }

        /// <summary>
        /// One stroke on the king's drums (the drum game). <paramref name="drum"/> 0: the big low drum, a round boom that falls;
        /// 1: the small high drum, a ringing tone. <paramref name="slap"/>: struck at the edge, a sharp crack over a shorter tone.
        /// <paramref name="seed"/> varies the hide's noise, so two takes of one stroke don't sound like a machine. Under a second.
        /// </summary>
        public static float[] DrumStrokeSamples(int drum, bool slap, int seed)
        {
            var low = drum == 0;
            var n = (int)(Rate * (low ? (slap ? 0.5f : 0.85f) : (slap ? 0.35f : 0.55f)));
            var d = new float[n];
            var noise = White(n, seed);
            var crack = Biquad.BandPass(low ? 1500f : 3200f, 1.1f);
            var thump = Biquad.LowPass(low ? 400f : 900f);
            float ph = 0f, ph2 = 0f, ph3 = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                float v;
                if (low)
                {
                    // the big drum: a fundamental that drops as the skin settles, a second mode, the hand's thump
                    var f = slap ? Mathf.Lerp(150f, 190f, Mathf.Exp(-t * 50f)) : Mathf.Lerp(62f, 98f, Mathf.Exp(-t * 22f));
                    ph += 2f * Mathf.PI * f / Rate; ph2 += 2f * Mathf.PI * f * 1.59f / Rate; ph3 += 2f * Mathf.PI * f * 2.14f / Rate;
                    var body = Mathf.Sin(ph) * Mathf.Exp(-t * (slap ? 14f : 5.5f)) + 0.4f * Mathf.Sin(ph2) * Mathf.Exp(-t * 11f) + 0.18f * Mathf.Sin(ph3) * Mathf.Exp(-t * 18f);
                    v = body * (slap ? 0.55f : 1f) + thump.Process(noise[i]) * 0.9f * Mathf.Exp(-t * 60f)
                        + (slap ? crack.Process(noise[i]) * 2.4f * Mathf.Exp(-t * 38f) : 0f);
                }
                else
                {
                    // the small drum: a clear tone with an inharmonic ring, and a bright crack at the edge
                    var f = slap ? 410f : Mathf.Lerp(215f, 238f, Mathf.Exp(-t * 40f));
                    ph += 2f * Mathf.PI * f / Rate; ph2 += 2f * Mathf.PI * f * 1.52f / Rate; ph3 += 2f * Mathf.PI * f * 2.3f / Rate;
                    var body = Mathf.Sin(ph) * Mathf.Exp(-t * (slap ? 26f : 10f)) + 0.5f * Mathf.Sin(ph2) * Mathf.Exp(-t * 16f) + 0.2f * Mathf.Sin(ph3) * Mathf.Exp(-t * 24f);
                    v = body * (slap ? 0.5f : 0.85f) + thump.Process(noise[i]) * 0.5f * Mathf.Exp(-t * 80f)
                        + crack.Process(noise[i]) * (slap ? 2.8f : 0.6f) * Mathf.Exp(-t * (slap ? 45f : 90f));
                }
                d[i] = v * Mathf.Clamp01(t / 0.0015f);
            }
            return Normalize(d, 0.92f);
        }

        /// <summary>
        /// The ogene, the iron bell that keeps time for the drums: a bright clang of inharmonic partials. <paramref name="low"/>:
        /// the bell's lower mouth (the one on the bar line).
        /// </summary>
        public static float[] OgeneSamples(bool low)
        {
            var n = (int)(Rate * 0.6f);
            var d = new float[n];
            var f = low ? 640f : 905f;
            float[] ratio = { 1f, 2.32f, 4.25f, 6.63f }, gain = { 1f, 0.55f, 0.32f, 0.18f }, decay = { 7f, 11f, 17f, 26f };
            var ph = new float[ratio.Length];
            var noise = Filter(White(n, low ? 230 : 231), Biquad.HighPass(4000f));
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var v = 0f;
                for (var k = 0; k < ratio.Length; k++) { ph[k] += 2f * Mathf.PI * f * ratio[k] / Rate; v += Mathf.Sin(ph[k]) * gain[k] * Mathf.Exp(-t * decay[k]); }
                d[i] = (v + noise[i] * 0.5f * Mathf.Exp(-t * 120f)) * Mathf.Clamp01(t / 0.001f);
            }
            return Normalize(d, 0.8f);
        }

        /// <summary>A gourd shaker on the eighths: a short hiss of seeds. <paramref name="accent"/>: the push on the off-beat.</summary>
        public static float[] ShakerSamples(bool accent)
        {
            var n = (int)(Rate * 0.12f);
            var noise = Filter(White(n, accent ? 232 : 233), Biquad.HighPass(5000f), Biquad.LowPass(11000f));
            var d = new float[n];
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                d[i] = noise[i] * Mathf.Clamp01(t / (accent ? 0.018f : 0.008f)) * Mathf.Exp(-t * (accent ? 34f : 50f));
            }
            return Normalize(d, accent ? 0.7f : 0.45f);
        }

        // ---------- story sound cues (Seven Hot Stones, Marlow Street) ----------

        static float Env(float t, float attack, float len) => Mathf.Clamp01(t / Mathf.Max(attack, 1e-4f)) * Mathf.Clamp01((len - t) / Mathf.Max(len * 0.35f, 1e-3f));

        /// <summary>Adds a fainter copy of the sound later on, like a call echoing down the streets.</summary>
        static float[] Echo(float[] d, float delay, float gain)
        {
            var k = (int)(delay * Rate);
            var o = new float[d.Length + k * 2];
            for (var i = 0; i < d.Length; i++) { o[i] += d[i]; o[i + k] += d[i] * gain; o[i + k * 2] += d[i] * gain * gain; }
            return o;
        }

        /// <summary>A huge creature's call: a deep rough voice that swells and falls, echoing (about 3 s). <paramref name="weak"/>: a dying groan.</summary>
        public static float[] RoarSamples(int seed, bool weak = false)
        {
            var r = new System.Random(seed);
            var len = weak ? 2.8f : 2.2f;
            var n = (int)(Rate * len);
            var d = new float[n];
            var noise = Filter(White(n, seed + 1), Biquad.BandPass(650f, 1.2f));
            var ph = 0f;
            var lo = 58f + (float)r.NextDouble() * 10f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate; var u = t / len;
                var f0 = weak ? Mathf.Lerp(lo + 12f, lo - 22f, u) : lo + 38f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 1.3f));
                f0 *= 1f + 0.03f * Mathf.Sin(t * 2f * Mathf.PI * 5.5f);
                ph += 2f * Mathf.PI * f0 / Rate;
                var v = 0f;
                for (var h = 1; h <= 14; h++) v += Mathf.Sin(h * ph) / h;
                var rough = 0.65f + 0.35f * Mathf.Sin(t * 2f * Mathf.PI * 27f);
                d[i] = (v * rough + noise[i] * 1.6f) * Env(t, weak ? 0.5f : 0.22f, len) * (weak ? 0.6f : 1f);
            }
            d = Filter(d, Biquad.LowPass(1600f));
            var f = Filter(d, Biquad.BandPass(420f, 1.1f)); var g = Filter(d, Biquad.BandPass(950f, 1.6f));
            for (var i = 0; i < n; i++) d[i] = d[i] * 0.4f + f[i] * 1.2f + g[i] * 0.8f;
            return Normalize(Echo(d, 0.33f, 0.32f), 0.9f);
        }

        /// <summary>A giant's footstep: a deep thud that shakes the ground, a little debris after (about 1.4 s). <paramref name="big"/>: a body falling.</summary>
        public static float[] StompSamples(int seed, bool big = false)
        {
            var len = big ? 2.6f : 1.4f;
            var n = (int)(Rate * len);
            var rumble = Filter(Brown(n, seed), Biquad.LowPass(110f)); Normalize(rumble, 1f);
            var grit = Filter(White(n, seed + 3), Biquad.BandPass(2400f, 1f)); Normalize(grit, 1f);
            var d = new float[n]; var ph = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                ph += 2f * Mathf.PI * Mathf.Lerp(34f, 62f, Mathf.Exp(-t * 14f)) / Rate;
                d[i] = Mathf.Sin(ph) * Mathf.Exp(-t * (big ? 2.4f : 4.5f)) + rumble[i] * 0.55f * Mathf.Exp(-t * (big ? 1.4f : 3f))
                       + grit[i] * 0.08f * Mathf.Exp(-Mathf.Abs(t - 0.09f) * 25f);
            }
            return Normalize(d, 0.95f);
        }

        /// <summary>A wooden door slammed shut, and its latch rattling (about 0.7 s).</summary>
        public static float[] DoorSamples(int seed)
        {
            var n = (int)(Rate * 0.7f);
            var w = White(n, seed); var r = new System.Random(seed);
            var bp = Biquad.BandPass(480f, 1.4f); var d = new float[n]; var ph = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                ph += 2f * Mathf.PI * 95f / Rate;
                d[i] = bp.Process(w[i]) * 2.2f * Mathf.Exp(-t * 26f) + Mathf.Sin(ph) * Mathf.Exp(-t * 16f);
            }
            for (var k = 0; k < 4; k++)
            {
                var at = (int)((0.09f + k * 0.045f + (float)r.NextDouble() * 0.02f) * Rate);
                var click = Biquad.BandPass(2600f, 3f);
                for (var i = 0; i < 300 && at + i < n; i++) d[at + i] += click.Process(w[(at + i * 3) % n]) * 0.6f * Mathf.Exp(-i / 60f) / (k + 1);
            }
            return Normalize(d, 0.85f);
        }

        /// <summary>A hot stone hissing: steam and crackle that fade (about 1.8 s).</summary>
        public static float[] SizzleSamples(int seed)
        {
            var n = (int)(Rate * 1.8f);
            var d = Filter(White(n, seed), Biquad.HighPass(2600f), Biquad.LowPass(9000f));
            var r = new System.Random(seed + 1); var g = 1f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                if (i % 180 == 0) g = 0.55f + (float)r.NextDouble() * 0.45f;
                d[i] *= g * Mathf.Clamp01(t / 0.03f) * Mathf.Exp(-t * 1.6f);
                if (r.NextDouble() < 0.0015) for (var k = 0; k < 40 && i + k < n; k++) d[i + k] += (k % 2 == 0 ? 1f : -1f) * 0.5f * Mathf.Exp(-k / 8f) * Mathf.Exp(-t);
            }
            return Normalize(d, 0.75f);
        }

        /// <summary>A stone thrown hard: a short whoosh through the air (about 0.45 s).</summary>
        public static float[] ThrowSamples(int seed)
        {
            var n = (int)(Rate * 0.45f);
            var w = White(n, seed); var bp = Biquad.BandPass(500f, 2f); var d = new float[n];
            for (var i = 0; i < n; i++)
            {
                var u = (float)i / n;
                bp.RetuneBandPass(Mathf.Lerp(400f, 1900f, Mathf.Sin(Mathf.PI * u * 0.8f)), 2f);
                d[i] = bp.Process(w[i]) * Mathf.Sin(Mathf.PI * u);
            }
            return Normalize(d, 0.7f);
        }

        /// <summary>A big swallow: two low wet gulps (about 0.6 s).</summary>
        public static float[] GulpSamples(int seed)
        {
            var n = (int)(Rate * 0.6f);
            var d = new float[n];
            foreach (var (at, f0, f1) in new[] { (0f, 190f, 85f), (0.24f, 150f, 65f) })
            {
                var start = (int)(at * Rate); var len = (int)(0.2f * Rate); var ph = 0f;
                for (var i = 0; i < len && start + i < n; i++)
                {
                    var u = (float)i / len;
                    ph += 2f * Mathf.PI * Mathf.Lerp(f0, f1, u) / Rate;
                    d[start + i] += (Mathf.Sin(ph) + 0.3f * Mathf.Sin(2f * ph)) * Mathf.Sin(Mathf.PI * u);
                }
            }
            return Normalize(Filter(d, Biquad.LowPass(900f)), 0.85f);
        }

        /// <summary>A horse whinnying: a high trill falling away, then a snort (about 1.6 s).</summary>
        public static float[] NeighSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 1.6f);
            var d = new float[n]; var ph = 0f;
            var top = 820f + (float)r.NextDouble() * 120f;
            for (var i = 0; i < (int)(Rate * 1.25f); i++)
            {
                var t = (float)i / Rate; var u = t / 1.25f;
                var f0 = Mathf.Lerp(top, 380f, u * u) * (1f + 0.07f * Mathf.Sin(t * 2f * Mathf.PI * 13f));
                ph += 2f * Mathf.PI * f0 / Rate;
                var v = 0f;
                for (var h = 1; h <= 6; h++) v += Mathf.Sin(h * ph) / (h * 0.8f);
                d[i] = v * Env(t, 0.05f, 1.25f);
            }
            var snort = Filter(White(n, seed + 1), Biquad.LowPass(700f));
            for (var i = (int)(Rate * 1.3f); i < n; i++) { var t = (float)i / Rate - 1.3f; d[i] += snort[i] * 2.5f * Mathf.Exp(-t * 14f); }
            var a = Filter(d, Biquad.BandPass(1150f, 1.4f)); var b = Filter(d, Biquad.BandPass(2500f, 2f));
            for (var i = 0; i < n; i++) d[i] = a[i] + b[i] * 0.6f + d[i] * 0.15f;
            return Normalize(d, 0.75f);
        }

        /// <summary>A rooster greeting the dawn: "cock-a-doodle-doo" (about 1.6 s).</summary>
        public static float[] RoosterSamples(int seed)
        {
            var n = (int)(Rate * 1.6f);
            var d = new float[n];
            (float at, float len, float f0, float f1)[] syl = { (0f, 0.13f, 520f, 640f), (0.17f, 0.12f, 600f, 700f), (0.33f, 0.14f, 660f, 740f), (0.52f, 0.85f, 760f, 560f) };
            foreach (var (at, len, f0, f1) in syl)
            {
                var start = (int)(at * Rate); var ph = 0f;
                for (var i = 0; i < (int)(len * Rate) && start + i < n; i++)
                {
                    var t = (float)i / Rate; var u = t / len;
                    var f = (u < 0.3f ? Mathf.Lerp(f0, f1, u / 0.3f) : Mathf.Lerp(f1, f0, (u - 0.3f) / 0.7f)) * (1f + 0.01f * Mathf.Sin(t * 377f));
                    ph += 2f * Mathf.PI * f / Rate;
                    var v = Mathf.Sin(ph) + 0.8f * Mathf.Sin(2f * ph) + 0.6f * Mathf.Sin(3f * ph) + 0.3f * Mathf.Sin(4f * ph);
                    d[start + i] += v * Env(t, 0.02f, len) * (0.75f + 0.25f * Mathf.Sin(t * 2f * Mathf.PI * 70f));
                }
            }
            return Normalize(Filter(d, Biquad.BandPass(1500f, 0.8f)), 0.7f);
        }

        /// <summary>A heartbeat: lub-dub (about 0.8 s).</summary>
        public static float[] HeartbeatSamples()
        {
            var n = (int)(Rate * 0.8f);
            var d = new float[n];
            foreach (var (at, gain) in new[] { (0f, 1f), (0.26f, 0.7f) })
            {
                var start = (int)(at * Rate); var ph = 0f;
                for (var i = 0; i < (int)(0.22f * Rate) && start + i < n; i++)
                {
                    var t = (float)i / Rate;
                    ph += 2f * Mathf.PI * Mathf.Lerp(48f, 72f, Mathf.Exp(-t * 30f)) / Rate;
                    d[start + i] += Mathf.Sin(ph) * gain * Mathf.Exp(-t * 18f) * Mathf.Clamp01(t / 0.008f);
                }
            }
            return Normalize(d, 0.9f);
        }

        /// <summary>Horses coming at a canter: hooves in threes (about 2 s).</summary>
        public static float[] GallopSamples(int seed)
        {
            var n = (int)(Rate * 2f);
            var d = new float[n]; var w = White(n, seed);
            for (var stride = 0; stride < 5; stride++)
                for (var k = 0; k < 3; k++)
                {
                    var start = (int)((stride * 0.38f + k * 0.085f) * Rate);
                    var bp = Biquad.BandPass(700f + k * 150f, 1.5f); var ph = 0f;
                    for (var i = 0; i < (int)(0.08f * Rate) && start + i < n; i++)
                    {
                        var t = (float)i / Rate;
                        ph += 2f * Mathf.PI * 120f / Rate;
                        d[start + i] += (bp.Process(w[start + i]) * 2f + Mathf.Sin(ph)) * Mathf.Exp(-t * 45f) * (0.4f + 0.15f * stride);
                    }
                }
            return Normalize(d, 0.8f);
        }

        /// <summary>A town cheering and clapping (about 3.5 s).</summary>
        public static float[] CheerSamples(int seed)
        {
            var n = (int)(Rate * 3.5f);
            var d = new float[n];
            var r = new System.Random(seed);
            for (var v = 0; v < 12; v++)
            {
                var f0 = (v % 2 == 0 ? 150f : 260f) * (0.9f + (float)r.NextDouble() * 0.3f);
                var start = (int)((float)r.NextDouble() * 0.4f * Rate);
                var len = (int)((1.6f + (float)r.NextDouble() * 1.4f) * Rate);
                var ph = 0f;
                for (var i = 0; i < len && start + i < n; i++)
                {
                    var t = (float)i / Rate; var u = (float)i / len;
                    ph += 2f * Mathf.PI * f0 * (1f + 0.25f * Mathf.Sin(Mathf.PI * Mathf.Min(1f, u * 2f))) / Rate;
                    d[start + i] += ((ph % (2f * Mathf.PI)) / Mathf.PI - 1f) * Env(t, 0.15f, len / (float)Rate) * 0.3f;
                }
            }
            d = Filter(d, Biquad.BandPass(800f, 0.7f));
            var w = White(n, seed + 7);
            for (var c = 0; c < 70; c++)
            {
                var at = (int)((0.5f + (float)r.NextDouble() * 2.8f) * Rate);
                var bp = Biquad.BandPass(1300f + (float)r.NextDouble() * 900f, 1.2f);
                for (var i = 0; i < 260 && at + i < n; i++) d[at + i] += bp.Process(w[at + i]) * 1.4f * Mathf.Exp(-i / 50f);
            }
            for (var i = 0; i < n; i++) d[i] *= Mathf.Clamp01((3.5f - (float)i / Rate) / 0.8f);
            return Normalize(d, 0.75f);
        }

        /// <summary>A police siren far off across the city: a wailing two-tone sweep, muffled by distance (about 6 s).</summary>
        public static float[] SirenSamples()
        {
            var n = Rate * 6;
            var d = new float[n]; var ph = 0f;
            for (var i = 0; i < n; i++)
            {
                var t = (float)i / Rate;
                var f = 950f + 330f * Mathf.Sin(2f * Mathf.PI * t / 2.6f);
                ph += 2f * Mathf.PI * f / Rate;
                d[i] = (Mathf.Sin(ph) + Mathf.Sin(3f * ph) / 3f + Mathf.Sin(5f * ph) / 5f) * Env(t, 1.2f, 6f);
            }
            return Normalize(Filter(d, Biquad.LowPass(1700f)), 0.6f);
        }

        /// <summary>A small brass key: a bright metallic clink and a bounce (about 0.9 s).</summary>
        public static float[] ClinkSamples()
        {
            var n = (int)(Rate * 0.9f);
            var d = new float[n];
            foreach (var (at, gain) in new[] { (0f, 1f), (0.13f, 0.45f) })
            {
                var start = (int)(at * Rate);
                for (var i = 0; start + i < n; i++)
                {
                    var t = (float)i / Rate;
                    d[start + i] += gain * (Mathf.Sin(2f * Mathf.PI * 2350f * t) * Mathf.Exp(-t * 7f) + 0.6f * Mathf.Sin(2f * Mathf.PI * 3870f * t) * Mathf.Exp(-t * 11f)
                                            + 0.4f * Mathf.Sin(2f * Mathf.PI * 5310f * t) * Mathf.Exp(-t * 16f));
                }
            }
            return Normalize(d, 0.55f);
        }

        /// <summary>Old bricks pried loose and crumbling (about 1.4 s).</summary>
        public static float[] BricksSamples(int seed)
        {
            var n = (int)(Rate * 1.4f);
            var d = new float[n]; var r = new System.Random(seed); var w = White(n, seed);
            var scrape = Filter(White(n, seed + 1), Biquad.BandPass(900f, 0.8f));
            for (var i = 0; i < n; i++) d[i] = scrape[i] * 0.3f * Mathf.Clamp01((0.5f - (float)i / Rate) * 3f);
            for (var k = 0; k < 16; k++)
            {
                var at = (int)((0.35f + (float)r.NextDouble() * 0.9f) * Rate);
                var bp = Biquad.BandPass(300f + (float)r.NextDouble() * 900f, 1.5f);
                var g = 0.4f + (float)r.NextDouble() * 0.8f;
                for (var i = 0; i < 900 && at + i < n; i++) d[at + i] += bp.Process(w[at + i]) * g * Mathf.Exp(-i / 160f);
            }
            return Normalize(d, 0.8f);
        }

        /// <summary>Footsteps on a wet street: four steps, each a soft heel and a splash (about 2 s).</summary>
        public static float[] WetStepsSamples(int seed)
        {
            var n = Rate * 2;
            var d = new float[n];
            for (var k = 0; k < 4; k++)
            {
                var step = StepSamples(seed + k);
                var splash = Filter(White(step.Length, seed + 20 + k), Biquad.HighPass(2600f));
                var start = (int)((0.1f + k * 0.48f) * Rate);
                for (var i = 0; i < step.Length && start + i < n; i++)
                    d[start + i] += step[i] * 0.8f + splash[i] * 0.25f * Mathf.Exp(-(float)i / Rate * 20f) * (1f - 0.15f * k);
            }
            return Normalize(d, 0.6f);
        }

        /// <summary>The fire spitting sparks: a quick run of pops (about 0.7 s).</summary>
        public static float[] SparksSamples(int seed)
        {
            var n = (int)(Rate * 0.7f);
            var d = new float[n]; var r = new System.Random(seed); var w = White(n, seed);
            for (var k = 0; k < 9; k++)
            {
                var at = (int)((float)r.NextDouble() * 0.55f * Rate);
                var bp = Biquad.BandPass(1800f + (float)r.NextDouble() * 3000f, 2f);
                for (var i = 0; i < 400 && at + i < n; i++) d[at + i] += bp.Process(w[at + i]) * Mathf.Exp(-i / 70f);
            }
            return Normalize(d, 0.7f);
        }

        /// <summary>A trickster god laughing: a run of "ha"s that tumbles down, warm and a little wild, echoing (about 2.6 s).</summary>
        public static float[] LaughSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 2.2f);
            var d = new float[n];
            var noise = White(n, seed + 1);
            var a = Biquad.BandPass(750f, 2.5f); var b = Biquad.BandPass(1250f, 3f);
            var t0 = 0f; var f0 = 210f; var ph = 0f;
            for (var k = 0; k < 9; k++)
            {
                var len = 0.11f + (float)r.NextDouble() * 0.05f;
                var start = (int)(t0 * Rate);
                for (var i = 0; i < (int)(len * Rate) && start + i < n; i++)
                {
                    var t = (float)i / Rate; var u = t / len;
                    ph += 2f * Mathf.PI * f0 * (1f + 0.08f * (1f - u)) / Rate;
                    var src = (ph % (2f * Mathf.PI)) / Mathf.PI - 1f + noise[start + i] * 0.6f * Mathf.Exp(-t * 40f); // the "h" then the voice
                    d[start + i] += (a.Process(src) + 0.7f * b.Process(src)) * Mathf.Sin(Mathf.PI * u) * (1f - k * 0.06f);
                }
                t0 += len + 0.06f + (float)r.NextDouble() * 0.03f;
                f0 *= 0.965f; // each "ha" a little lower
            }
            return Normalize(Echo(d, 0.27f, 0.3f), 0.8f);
        }

        /// <summary>A shekere: a gourd rattle of beads, shaken in a walking rhythm (about 1.8 s).</summary>
        public static float[] RattleSamples(int seed)
        {
            var n = (int)(Rate * 1.8f);
            var d = new float[n];
            var w = Filter(White(n, seed), Biquad.HighPass(3200f), Biquad.LowPass(9500f));
            for (var k = 0; k < 6; k++)
            {
                var start = (int)((k * 0.28f + (k % 2) * 0.03f) * Rate);
                var accent = k % 2 == 0 ? 1f : 0.6f;
                for (var i = 0; i < (int)(0.16f * Rate) && start + i < n; i++)
                {
                    var t = (float)i / Rate;
                    d[start + i] += w[start + i] * accent * Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-t * 26f);
                }
            }
            return Normalize(d, 0.7f);
        }

        /// <summary>Birds waking at dawn: whistles, trills and chirps from several birds (loops, about 6 s).</summary>
        public static float[] BirdsSamples(int seed)
        {
            var n = Rate * 6;
            var d = new float[n];
            var r = new System.Random(seed);
            for (var call = 0; call < 26; call++)
            {
                var start = (int)((float)r.NextDouble() * 5.4f * Rate);
                var kind = r.Next(3);
                var f0 = 2200f + (float)r.NextDouble() * 2200f;
                var len = kind == 0 ? 0.08f : kind == 1 ? 0.35f : 0.22f;
                var notes = kind == 1 ? 1 : 2 + r.Next(3);
                var gain = 0.3f + (float)r.NextDouble() * 0.7f;
                var ph = 0f;
                for (var k = 0; k < notes; k++)
                {
                    var s0 = start + (int)(k * (len + 0.05f) * Rate);
                    for (var i = 0; i < (int)(len * Rate) && s0 + i < n; i++)
                    {
                        var u = i / (len * Rate);
                        var f = kind == 0 ? f0 * (1.3f - 0.5f * u) : kind == 1 ? f0 * (1f + 0.12f * Mathf.Sin(u * Mathf.PI * 2f * 9f)) : f0 * (0.8f + 0.5f * u);
                        ph += 2f * Mathf.PI * f / Rate;
                        d[s0 + i] += Mathf.Sin(ph) * Mathf.Sin(Mathf.PI * u) * gain;
                    }
                }
            }
            return Normalize(d, 0.5f);
        }

        /// <summary>An angry shout across the road: "Hey! Ha!", a raised voice, no words (about 0.8 s).</summary>
        public static float[] ShoutSamples(int seed)
        {
            var r = new System.Random(seed);
            var n = (int)(Rate * 0.8f);
            var d = new float[n];
            var noise = White(n, seed + 1);
            var a = Biquad.BandPass(820f, 2.4f); var b = Biquad.BandPass(1350f, 3f);
            var t0 = 0f; var ph = 0f;
            var f0 = 190f + (float)r.NextDouble() * 50f;
            foreach (var len in new[] { 0.24f, 0.32f })
            {
                var start = (int)(t0 * Rate);
                for (var i = 0; i < (int)(len * Rate) && start + i < n; i++)
                {
                    var t = (float)i / Rate; var u = t / len;
                    ph += 2f * Mathf.PI * f0 * (1.05f + 0.18f * Mathf.Sin(Mathf.PI * u)) / Rate;
                    var src = (ph % (2f * Mathf.PI)) / Mathf.PI - 1f + noise[start + i] * 0.8f * Mathf.Exp(-t * 30f);
                    var env = Mathf.Clamp01(u * 12f) * Mathf.Clamp01((1f - u) * 5f);
                    d[start + i] += (a.Process(src) + 0.8f * b.Process(src)) * env;
                }
                t0 += len + 0.08f;
                f0 *= 0.92f;
            }
            return Normalize(d, 0.8f);
        }

        public static AudioClip Clip(string name, float[] samples)
        {
            var c = AudioClip.Create(name, samples.Length, 1, Rate, false);
            c.SetData(samples, 0);
            return c;
        }
    }
}
