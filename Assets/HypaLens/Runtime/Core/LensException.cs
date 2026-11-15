using System;
using HypaCore;

namespace HypaLens
{
    public enum LensErrorKind
    {
        NoTextFound,
        UnsupportedPair,
        Offline,
        PermissionDenied,
        CameraUnavailable,
        Recognition,
        Api,
    }

    public sealed class LensException : Exception
    {
        public LensErrorKind Kind { get; }
        /// <summary>Set when the error came from HypaCore; its message is more specific (missing key, out of credit...).</summary>
        public HypaException Hypa { get; }

        public LensException(LensErrorKind kind, string message = null, Exception inner = null)
            : base(message ?? kind.ToString(), inner)
        {
            Kind = kind;
            Hypa = inner as HypaException;
        }

        /// <summary>Wraps any exception into a LensException so the UI always has a typed, displayable error.</summary>
        public static LensException From(Exception e)
        {
            if (e is AggregateException ae && ae.InnerExceptions.Count == 1) e = ae.InnerException;
            switch (e)
            {
                case LensException le: return le;
                case HypaException he:
                    // Only a real network failure is "offline". A timeout means Hypa was slow ("took too long, try again").
                    var kind = he.Kind == HypaErrorKind.Offline ? LensErrorKind.Offline : LensErrorKind.Api;
                    return new LensException(kind, he.Message, he);
                case System.Net.Http.HttpRequestException _:
                case System.IO.IOException _:
                case System.Net.Sockets.SocketException _:
                    return new LensException(LensErrorKind.Offline, null, e);
                default:
                    return new LensException(LensErrorKind.Api, e.Message, e);
            }
        }

        /// <summary>Plain-language message with a next step. Never empty.</summary>
        public string UserMessage(string source = null, string target = null)
        {
            switch (Kind)
            {
                case LensErrorKind.NoTextFound:
                    return "No text found. Move closer, add light, and face the text straight on, then capture again.";
                case LensErrorKind.UnsupportedPair:
                    return $"Hypa can't translate {source ?? "this language"} to {target ?? "that language"} yet. Pick a different language.";
                case LensErrorKind.Offline:
                    return "You're offline. Reconnect to Wi-Fi to translate. Saved words still work.";
                case LensErrorKind.PermissionDenied:
                    return "Camera permission is off. Hypa Lens only reads the text you pinch and never saves photos. Allow camera access in Settings > Privacy to continue.";
                case LensErrorKind.CameraUnavailable:
                    return "The headset camera isn't available right now. " + (Message != Kind.ToString() ? Message : "Try again in a moment.");
                case LensErrorKind.Recognition:
                    return "Couldn't read the text in that capture. " + (Message != Kind.ToString() ? Message : "Try again with better light.");
                default:
                    if (Hypa != null) return Hypa.UserMessage();
                    return string.IsNullOrEmpty(Message) ? "Something went wrong. Try again." : "Something went wrong: " + Message;
            }
        }
    }
}
