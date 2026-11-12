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

        public static AudioClip Clip(string name, float[] samples)
        {
            var c = AudioClip.Create(name, samples.Length, 1, Rate, false);
            c.SetData(samples, 0);
            return c;
        }
    }
}
