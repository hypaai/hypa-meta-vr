using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace HypaCore
{
    /// <summary>
    /// Live (streaming) speech to text over Hypa's developer WebSocket, the same one Hypa Lab's Live Transcribe uses
    /// (hypa-v2/apps/hypa-api ws gateway, wss://&lt;api host&gt;/?provider=meta&amp;model=muse-voice-transcribe-1.0).
    /// Authenticates with the API key as the "key-&lt;apiKey&gt;" subprotocol. Send 16 kHz mono WAV chunks as binary frames; it answers
    /// with {"text": "...everything heard so far...", "final": false} as you speak, then final=true after {"type":"stop"}.
    /// Network work runs off the Unity main thread; results are read with <see cref="TryDequeue"/> (main thread, per frame).
    /// </summary>
    public sealed class HypaLiveAsr : IDisposable
    {
        public const string Model = "muse-voice-transcribe-1.0";
        const int SampleRate = 16000, ChunkMs = 400;

        public struct Update { public string Text; public bool Final; public string Error; }

        readonly HypaConfig _cfg;
        readonly ConcurrentQueue<float[]> _audio = new ConcurrentQueue<float[]>();
        readonly ConcurrentQueue<Update> _updates = new ConcurrentQueue<Update>();
        readonly CancellationTokenSource _cts = new CancellationTokenSource();
        ClientWebSocket _ws;
        volatile bool _stopRequested;
        int _closed;

        /// <summary>True once the socket is open.</summary>
        public bool Connected { get; private set; }
        /// <summary>Could not connect, or the connection broke: use the request/response speech_to_text instead.</summary>
        public bool Failed { get; private set; }
        /// <summary>Why it failed (never contains the key).</summary>
        public string FailReason { get; private set; }
        /// <summary>The latest cumulative text.</summary>
        public string Text { get; private set; } = "";
        /// <summary>The server's final answer arrived (after <see cref="Finish"/>) or the socket closed.</summary>
        public bool Done { get; private set; }

        public HypaLiveAsr(HypaConfig cfg = null) { _cfg = cfg ?? HypaConfig.Current; }

        public static string UrlFor(HypaConfig cfg)
        {
            var b = cfg.BaseUrl.TrimEnd('/');
            var host = b.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "wss://" + b.Substring(8)
                : b.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? "ws://" + b.Substring(7) : b;
            // only the host is used: the live gateway sits at the root
            var slash = host.IndexOf('/', host.IndexOf("://", StringComparison.Ordinal) + 3);
            if (slash > 0) host = host.Substring(0, slash);
            return host + "/?provider=meta&model=" + Model;
        }

        /// <summary>Connects and starts the send / receive loops. Never throws: check <see cref="Failed"/>.</summary>
        public async Task StartAsync()
        {
            if (!_cfg.HasKey) { Fail("needs an API key"); return; }
            try
            {
                _ws = new ClientWebSocket();
                _ws.Options.AddSubProtocol("key-" + _cfg.ApiKey);
                using (var connect = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token))
                {
                    connect.CancelAfter(TimeSpan.FromSeconds(8));
                    await _ws.ConnectAsync(new Uri(UrlFor(_cfg)), connect.Token).ConfigureAwait(false);
                }
                await SendTextAsync("{\"type\":\"config\"}").ConfigureAwait(false);
                Connected = true;
                _ = Task.Run(SendLoop);
                _ = Task.Run(ReceiveLoop);
            }
            catch (Exception e) { Fail(e is OperationCanceledException ? "timed out" : e.Message); }
        }

        /// <summary>Queue what the microphone just heard (mono, 16 kHz).</summary>
        public void Send(float[] samples, int count)
        {
            if (Failed || _stopRequested || count <= 0) return;
            var copy = new float[count];
            Array.Copy(samples, copy, count);
            _audio.Enqueue(copy);
        }

        /// <summary>Done speaking: send the rest, then ask for the final text.</summary>
        public void Finish() { _stopRequested = true; }

        public bool TryDequeue(out Update u) => _updates.TryDequeue(out u);

        async Task SendLoop()
        {
            var pending = new System.Collections.Generic.List<float>();
            try
            {
                while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    while (_audio.TryDequeue(out var chunk)) pending.AddRange(chunk);
                    var enough = pending.Count >= SampleRate * ChunkMs / 1000;
                    if (enough || (_stopRequested && pending.Count > 0))
                    {
                        var arr = pending.ToArray(); pending.Clear();
                        var wav = WavEncoder.Encode16(arr, arr.Length, 1, SampleRate);
                        await _ws.SendAsync(new ArraySegment<byte>(wav), WebSocketMessageType.Binary, true, _cts.Token).ConfigureAwait(false);
                        continue;
                    }
                    if (_stopRequested && _audio.IsEmpty)
                    {
                        await SendTextAsync("{\"type\":\"stop\"}").ConfigureAwait(false);
                        return;
                    }
                    await Task.Delay(40, _cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception e) { if (!_cts.IsCancellationRequested) Fail(e.Message); }
        }

        async Task ReceiveLoop()
        {
            var buf = new byte[16 * 1024];
            var sb = new StringBuilder();
            try
            {
                while (!_cts.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    sb.Clear();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), _cts.Token).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close) { Finished(); return; }
                        if (r.MessageType == WebSocketMessageType.Text) sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    } while (!r.EndOfMessage);
                    if (sb.Length > 0) Handle(sb.ToString());
                }
            }
            catch (Exception e) { if (!_cts.IsCancellationRequested) { if (!Done) Fail(e.Message); } }
            Finished();
        }

        void Handle(string json)
        {
            try
            {
                var o = JObject.Parse(json);
                var err = o["error"];
                if (err != null && err.Type != JTokenType.Null) { _updates.Enqueue(new Update { Error = err.ToString() }); return; }
                var text = (string)o["text"] ?? (string)o["transcript"];
                var final = (bool?)o["final"] ?? false;
                if (text == null) return;
                if (text.Length > 0 || final) { if (text.Length > 0) Text = text; _updates.Enqueue(new Update { Text = text, Final = final }); }
                if (final && text.Length > 0) Done = true; // the empty trailing message after it is ignored above
            }
            catch (Exception) { /* not JSON: ignore */ }
        }

        async Task SendTextAsync(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            await _ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true, _cts.Token).ConfigureAwait(false);
        }

        void Fail(string why)
        {
            if (Failed) return;
            Failed = true; FailReason = why;
            Done = true;
        }

        void Finished() { Done = true; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 1) return;
            try { _cts.Cancel(); } catch (Exception) { }
            try { _ws?.Abort(); _ws?.Dispose(); } catch (Exception) { }
            _cts.Dispose();
        }
    }
}
