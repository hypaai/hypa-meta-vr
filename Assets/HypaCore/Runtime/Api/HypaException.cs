using System;
using Newtonsoft.Json.Linq;

namespace HypaCore
{
    public enum HypaErrorKind
    {
        MissingKey,          // no key configured for a paid call
        Unauthorized,        // 401, bad key
        InsufficientBalance, // 400 INSUFFICIENT_BALANCE
        RateLimited,         // 429
        Unavailable,         // 503 (includes the anonymous MCP daily allowance)
        Offline,             // no network / DNS / connection reset
        Timeout,
        BadRequest,          // other 4xx, JSON-RPC errors
        ToolError,           // MCP tool returned isError: true
        Server,              // 5xx
        InvalidResponse,     // could not parse what came back
    }

    public sealed class HypaException : Exception
    {
        public HypaErrorKind Kind { get; }
        public int Status { get; }
        public string Code { get; }   // API error code when present, e.g. INSUFFICIENT_BALANCE

        public HypaException(HypaErrorKind kind, string message, int status = 0, string code = null, Exception inner = null)
            : base(message, inner) { Kind = kind; Status = status; Code = code; }

        public static HypaException FromHttp(int status, string body)
        {
            string msg = null, code = null;
            try
            {
                var o = JObject.Parse(body);
                msg = (string)o["error"]?["message"] ?? (string)o["message"] ?? (o["error"]?.Type == JTokenType.String ? (string)o["error"] : null);
                code = (string)o["code"] ?? (string)o["error"]?["code"];
            }
            catch { /* not JSON */ }
            msg = msg ?? Trim(body);
            if (code == null && body != null && body.Contains("INSUFFICIENT_BALANCE")) code = "INSUFFICIENT_BALANCE";

            HypaErrorKind kind;
            if (status == 401 || status == 403) kind = HypaErrorKind.Unauthorized;
            else if (code == "INSUFFICIENT_BALANCE") kind = HypaErrorKind.InsufficientBalance;
            else if (status == 429) kind = HypaErrorKind.RateLimited;
            else if (status == 503) kind = HypaErrorKind.Unavailable;
            else if (status >= 500) kind = HypaErrorKind.Server;
            else kind = HypaErrorKind.BadRequest;
            return new HypaException(kind, $"HTTP {status}: {msg}", status, code);
        }

        static string Trim(string s) => string.IsNullOrEmpty(s) ? "(empty body)" : (s.Length > 300 ? s.Substring(0, 300) + "..." : s);

        /// <summary>Short message safe to show a user.</summary>
        public string UserMessage()
        {
            switch (Kind)
            {
                case HypaErrorKind.MissingKey: return "No Hypa API key set. Add it to hypa.local.json (see README).";
                case HypaErrorKind.Unauthorized: return "The Hypa API key was rejected. Check hypa.local.json.";
                case HypaErrorKind.InsufficientBalance: return "The Hypa account is out of credit.";
                case HypaErrorKind.RateLimited: return "Too many requests. Wait a moment and try again.";
                case HypaErrorKind.Unavailable: return "Hypa is busy right now. Try again shortly.";
                case HypaErrorKind.Offline: return "You're offline. Reconnect and try again.";
                case HypaErrorKind.Timeout: return "Hypa took too long to answer. Try again.";
                default: return "Hypa couldn't do that: " + Message;
            }
        }
    }
}
