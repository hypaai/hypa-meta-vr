using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HypaCore;

namespace HypaLens
{
    /// <summary>
    /// The Lens's only route to Hypa: HypaCore's MCP client (translate_message, text_to_speech, look_up_term,
    /// check_language_pair, list_languages). Only the selected text is ever sent.
    /// </summary>
    public sealed class HypaCoreLensApi : IHypaLensApi
    {
        readonly HypaMcpClient _mcp;
        readonly HypaLanguages _languages;

        public HypaCoreLensApi(HypaMcpClient mcp = null)
        {
            _mcp = mcp ?? new HypaMcpClient();
            _languages = new HypaLanguages(_mcp);
        }

        public Task<IReadOnlyList<HypaLanguage>> ListLanguagesAsync(CancellationToken ct) => _languages.GetAsync(ct);

        public async Task<bool> CheckPairAsync(string source, string target, CancellationToken ct)
            => (await _mcp.CheckLanguagePairAsync(target, source, ct)).Supported;

        public Task<HypaTranslation> TranslateAsync(string text, string source, string target, CancellationToken ct)
            => _mcp.TranslateAsync(text, target, source, ct);

        public async Task<string> LookUpAsync(string term, string language, CancellationToken ct)
        {
            // look_up_term accepts 1..120 chars and runs on a small model: words and short phrases only.
            if (term.Length > 120) return null;
            return (await _mcp.LookUpTermAsync(term, language, ct)).ToDisplayText();
        }

        public Task<HypaSpeech> SpeakAsync(string text, string language, CancellationToken ct)
            => _mcp.TextToSpeechAsync(text, language, ct);

        public Task<HypaImageTranslation> TranslateImageAsync(byte[] jpeg, string source, string target, CancellationToken ct)
            => _mcp.TranslateImageAsync(jpeg, target, source, ct);
    }
}
