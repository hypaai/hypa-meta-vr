using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HypaCore
{
    [Flags]
    public enum HypaFeature { None = 0, Mt = 1, Tts = 2, Asr = 4 }

    /// <summary>
    /// Language list from the API (list_languages, free), cached for the session, with a feature filter.
    /// What the API exposes today: list_languages gives each language's TTS voices, so Tts is filtered from the list.
    /// Mt is checked per pair with check_language_pair (free). The API does not expose per-language ASR support in
    /// list_languages, so Asr is not filtered here: Stories must confirm ASR support with the API team (see README).
    /// </summary>
    public sealed class HypaLanguages
    {
        readonly HypaMcpClient _mcp;
        IReadOnlyList<HypaLanguage> _cache;

        public HypaLanguages(HypaMcpClient mcp) { _mcp = mcp; }

        public async Task<IReadOnlyList<HypaLanguage>> GetAsync(CancellationToken ct)
            => _cache ?? (_cache = await _mcp.ListLanguagesAsync(ct));

        public async Task<IReadOnlyList<HypaLanguage>> GetAsync(HypaFeature required, CancellationToken ct)
            => Filter(await GetAsync(ct), required);

        /// <summary>Pure filter. Unknown capabilities (Mt, Asr) don't exclude a language; see class remarks.</summary>
        public static IReadOnlyList<HypaLanguage> Filter(IReadOnlyList<HypaLanguage> all, HypaFeature required)
            => all.Where(l => (required & HypaFeature.Tts) == 0 || l.HasTts).ToList();

        public static HypaLanguage Find(IReadOnlyList<HypaLanguage> all, string nameOrCode)
            => all?.FirstOrDefault(l => string.Equals(l.Name, nameOrCode, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(l.Code, nameOrCode, StringComparison.OrdinalIgnoreCase));
    }
}
