using System;
using System.Text;

namespace HypaCore
{
    /// <summary>Decodes RIFF/WAVE PCM (8/16/24/32-bit int) and 32-bit float to normalised samples. Pure, unit tested.</summary>
    public static class WavDecoder
    {
        public static bool TryDecode(byte[] b, out float[] samples, out int channels, out int sampleRate)
        {
            samples = null; channels = 0; sampleRate = 0;
            if (b == null || b.Length < 44 || Ascii(b, 0) != "RIFF" || Ascii(b, 8) != "WAVE") return false;

            int format = 0, bits = 0, pos = 12;
            int dataOff = -1, dataLen = 0;
            while (pos + 8 <= b.Length)
            {
                var id = Ascii(b, pos);
                var len = BitConverter.ToInt32(b, pos + 4);
                var body = pos + 8;
                if (len < 0 || body > b.Length) return false;
                if (id == "fmt ")
                {
                    format = BitConverter.ToInt16(b, body);
                    channels = BitConverter.ToInt16(b, body + 2);
                    sampleRate = BitConverter.ToInt32(b, body + 4);
                    bits = BitConverter.ToInt16(b, body + 14);
                    if (format == unchecked((short)0xFFFE) && len >= 26) format = BitConverter.ToInt16(b, body + 24); // WAVE_FORMAT_EXTENSIBLE
                }
                else if (id == "data")
                {
                    dataOff = body;
                    dataLen = Math.Min(len, b.Length - body); // tolerate streamed headers with a bogus length
                    break;
                }
                pos = body + len + (len & 1);
            }
            if (dataOff < 0 || channels <= 0 || sampleRate <= 0 || bits == 0) return false;
            if (format != 1 && format != 3) return false;

            var bytesPer = bits / 8;
            var n = dataLen / bytesPer;
            samples = new float[n];
            for (int i = 0, o = dataOff; i < n; i++, o += bytesPer)
            {
                if (format == 3 && bits == 32) samples[i] = BitConverter.ToSingle(b, o);
                else if (bits == 16) samples[i] = BitConverter.ToInt16(b, o) / 32768f;
                else if (bits == 8) samples[i] = (b[o] - 128) / 128f;
                else if (bits == 24) samples[i] = ((b[o] | (b[o + 1] << 8) | ((sbyte)b[o + 2] << 16))) / 8388608f;
                else if (bits == 32) samples[i] = BitConverter.ToInt32(b, o) / 2147483648f;
                else return false;
            }
            return true;
        }

        static string Ascii(byte[] b, int o) => o + 4 <= b.Length ? Encoding.ASCII.GetString(b, o, 4) : "";
    }
}
