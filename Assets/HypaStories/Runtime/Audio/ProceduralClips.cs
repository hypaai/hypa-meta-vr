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

        public static AudioClip Clip(string name, float[] samples)
        {
            var c = AudioClip.Create(name, samples.Length, 1, Rate, false);
            c.SetData(samples, 0);
            return c;
        }
    }
}
