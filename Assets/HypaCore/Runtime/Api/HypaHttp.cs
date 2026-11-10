using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HypaCore
{
    /// <summary>
    /// The only code in the app that touches the network. Uses HttpClient so requests and response parsing run off
    /// the Unity main thread (continuations use ConfigureAwait(false)); callers awaiting from the main thread resume there.
    /// </summary>
    public static class HypaHttp
    {
        static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>POST JSON to the Hypa API. Sends x-api-key when one is configured. Returns the raw body (JSON or SSE).</summary>
        public static async Task<string> PostAsync(HypaConfig cfg, string path, string json, CancellationToken ct)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Post, cfg.BaseUrl + path))
            {
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                req.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
                if (cfg.HasKey) req.Headers.TryAddWithoutValidation("x-api-key", cfg.ApiKey);
                using (var res = await Send(req, ct).ConfigureAwait(false))
                {
                    var body = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) throw HypaException.FromHttp((int)res.StatusCode, body);
                    return body;
                }
            }
        }

        /// <summary>GET bytes from a signed URL (TTS audio). Never sends the API key to it.</summary>
        public static async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
        {
            using (var req = new HttpRequestMessage(HttpMethod.Get, url))
            using (var res = await Send(req, ct).ConfigureAwait(false))
            {
                if (!res.IsSuccessStatusCode)
                    throw new HypaException(HypaErrorKind.BadRequest, $"Audio link returned HTTP {(int)res.StatusCode} (links expire after about an hour).", (int)res.StatusCode);
                return await res.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            }
        }

        static async Task<HttpResponseMessage> Send(HttpRequestMessage req, CancellationToken ct)
        {
            try { return await Client.SendAsync(req, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TaskCanceledException e) { throw new HypaException(HypaErrorKind.Timeout, "Request timed out.", 0, null, e); }
            catch (HttpRequestException e) { throw new HypaException(HypaErrorKind.Offline, e.Message, 0, null, e); }
        }

        /// <summary>
        /// Hypa answers either with a JSON body or with server-sent events ("event: message\ndata: {...}\n\n").
        /// Returns each JSON payload in order. "[DONE]" markers are skipped. Pure, unit tested.
        /// </summary>
        public static List<string> JsonPayloads(string body)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(body)) return list;
            var trimmed = body.TrimStart();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) { list.Add(trimmed); return list; }

            var data = new StringBuilder();
            void Flush()
            {
                if (data.Length == 0) return;
                var s = data.ToString();
                if (s != "[DONE]") list.Add(s);
                data.Clear();
            }
            foreach (var raw in body.Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.Length == 0) { Flush(); continue; }
                if (!raw.StartsWith("data:")) continue;
                var v = raw.Substring(5);
                if (v.StartsWith(" ")) v = v.Substring(1);
                if (data.Length > 0) data.Append('\n');
                data.Append(v);
            }
            Flush();
            return list;
        }
    }
}
