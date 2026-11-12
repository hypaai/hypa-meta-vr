using System;
using System.IO;

namespace HypaCore
{
    /// <summary>Encodes samples (-1..1) as 16-bit PCM RIFF/WAVE, e.g. microphone audio for speech_to_text. Pure, unit tested.</summary>
    public static class WavEncoder
    {
        public static byte[] Encode16(float[] samples, int count, int channels, int sampleRate)
        {
            count = Math.Min(count, samples?.Length ?? 0);
            using (var ms = new MemoryStream(44 + count * 2))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + count * 2); w.Write(new[] { 'W', 'A', 'V', 'E' });
                w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)channels);
                w.Write(sampleRate); w.Write(sampleRate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(count * 2);
                for (var i = 0; i < count; i++)
                {
                    var v = Math.Max(-1f, Math.Min(1f, samples[i]));
                    w.Write((short)Math.Round(v * (v < 0 ? 32768f : 32767f)));
                }
                return ms.ToArray();
            }
        }
    }
}
