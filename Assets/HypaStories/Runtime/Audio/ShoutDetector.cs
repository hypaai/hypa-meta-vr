using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace HypaStories
{
    /// <summary>
    /// Hears you shout back at Dodo ("I am!", in any language): the microphone's loudness, no speech-to-text, so it's instant
    /// and never rejects an accent. A voice clearly above the room's own level for a moment counts. In the editor, Space shouts.
    /// </summary>
    public sealed class ShoutDetector : MonoBehaviour
    {
        const int Rate = 16000, Seconds = 2;
        string _device;
        AudioClip _clip;
        int _read;
        float _floor = 0.01f, _loudFor, _rest;
        readonly float[] _buf = new float[Rate / 2];

        /// <summary>Loudness now, 0..1 (for a meter).</summary>
        public float Level { get; private set; }
        /// <summary>True for one frame when a shout is heard.</summary>
        public bool Shouted { get; private set; }
        public bool Listening => _clip != null;

        public void Begin()
        {
            if (_clip != null) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone)) { Permission.RequestUserPermission(Permission.Microphone); return; }
#endif
            if (Microphone.devices.Length == 0) return;
            _device = MicListener.Device(Microphone.devices);
            _clip = Microphone.Start(_device, true, Seconds, Rate);
            _read = 0; _loudFor = 0f;
        }

        public void End()
        {
            if (_clip == null) return;
            Microphone.End(_device);
            _clip = null; Level = 0f;
        }

        void OnDisable() => End();

        /// <summary>Ignore the microphone for a moment (Dodo's own roar must not count as an answer). Editor Space still shouts.</summary>
        public void Rest(float seconds) { _rest = Mathf.Max(_rest, seconds); _loudFor = 0f; }

        /// <summary>Is this frame's loudness a shout over the room's level? Pure, unit tested.</summary>
        public static bool IsShout(float rms, float floor) => rms > 0.06f && rms > floor * 3.5f;

        void Update()
        {
            Shouted = false;
            if (Application.isEditor && Input.GetKeyDown(KeyCode.Space)) { Shouted = true; return; }
            if (_clip == null) { Begin(); return; }
            var pos = Microphone.GetPosition(_device);
            if (pos < 0) return;
            var n = (pos - _read + _clip.samples) % _clip.samples;
            if (n == 0) return;
            n = Mathf.Min(n, _buf.Length);
            var chunk = n == _buf.Length ? _buf : new float[n];
            _clip.GetData(chunk, (pos - n + _clip.samples) % _clip.samples);
            _read = pos;
            var sum = 0f;
            for (var i = 0; i < n; i++) sum += chunk[i] * chunk[i];
            var rms = Mathf.Sqrt(sum / n);
            Level = Mathf.Min(1f, rms * 6f);
            if (_rest > 0f) { _rest -= Time.deltaTime; _loudFor = 0f; return; }
            // the room's own level drifts slowly, so a fan or the fire's crackle doesn't count
            _floor = Mathf.Lerp(_floor, Mathf.Min(rms, 0.05f), 1f - Mathf.Exp(-Time.deltaTime * (rms < _floor ? 2f : 0.3f)));
            _loudFor = IsShout(rms, _floor) ? _loudFor + Time.deltaTime : 0f;
            if (_loudFor > 0.12f) { Shouted = true; _loudFor = -0.6f; } // one shout, then a moment before the next
        }
    }
}
