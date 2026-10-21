using System.Buffers.Binary;

namespace MusicSync.Audio;

internal readonly record struct WaveFormatInfo(
    bool IsFloat, int Channels, int SampleRate, int BlockAlign, int BitsPerSample, int ValidBitsPerSample)
{
    private static readonly Guid PcmSubtype = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    public static WaveFormatInfo Parse(ReadOnlySpan<byte> format)
    {
        if (format.Length < 18)
            throw new NotSupportedException("播放裝置回傳的音訊格式不完整。");

        var tag = BinaryPrimitives.ReadUInt16LittleEndian(format);
        var channels = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(2));
        var sampleRate = BinaryPrimitives.ReadInt32LittleEndian(format.Slice(4));
        var blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(12));
        var bits = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(14));
        var validBits = (int)bits;
        var isFloat = tag == 3;
        if (tag == 0xfffe)
        {
            var extraSize = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(16));
            if (extraSize < 22 || format.Length < 40)
                throw new NotSupportedException("播放裝置的延伸音訊格式不完整。");
            validBits = BinaryPrimitives.ReadUInt16LittleEndian(format.Slice(18));
            if (validBits == 0)
                validBits = bits;
            var subtype = new Guid(format.Slice(24, 16));
            if (subtype == FloatSubtype)
                isFloat = true;
            else if (subtype == PcmSubtype)
                isFloat = false;
            else
                throw new NotSupportedException("播放裝置使用不支援的音訊子格式。");
        }
        else if (tag != 1 && tag != 3)
        {
            throw new NotSupportedException("播放裝置須使用 PCM 或 IEEE 浮點混音格式。");
        }

        if (channels == 0 || channels > 64 || sampleRate < 8000 || sampleRate > 768000 ||
            validBits <= 0 || validBits > bits || blockAlign < channels * (bits / 8) ||
            (isFloat ? bits != 32 : bits != 8 && bits != 16 && bits != 24 && bits != 32))
            throw new NotSupportedException("播放裝置使用不支援的取樣格式。");

        return new WaveFormatInfo(isFloat, channels, sampleRate, blockAlign, bits, validBits);
    }
}

internal readonly record struct AudioBlockStatistics(double SquareSum, long SampleCount, double Peak);

internal static class PcmSampleDecoder
{
    /// <summary>
    /// Measures the interleaved channels independently, avoiding cancellation between stereo channels.
    /// WAVEFORMATEXTENSIBLE integer valid bits are left aligned in their sample containers.
    /// </summary>
    public static AudioBlockStatistics Analyze(ReadOnlySpan<byte> audio, int frames, WaveFormatInfo format,
        AudioFeatureAnalyzer? features = null)
    {
        if (frames < 0 || (long)frames * format.BlockAlign > audio.Length)
            throw new ArgumentException("音訊封包長度與格式不符。", nameof(audio));

        double squareSum = 0;
        double peak = 0;
        var bytesPerSample = format.BitsPerSample / 8;
        for (var frame = 0; frame < frames; frame++)
        {
            var offset = frame * format.BlockAlign;
            for (var channel = 0; channel < format.Channels; channel++, offset += bytesPerSample)
            {
                var sample = audio.Slice(offset, bytesPerSample);
                double value;
                if (format.IsFloat)
                {
                    value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(sample));
                    if (!double.IsFinite(value))
                        value = 0;
                }
                else
                {
                    value = format.BitsPerSample switch
                    {
                        8 => (sample[0] - 128) / 128.0,
                        16 => BinaryPrimitives.ReadInt16LittleEndian(sample) / 32768.0,
                        24 => ReadInt24(sample) / 8388608.0,
                        32 => BinaryPrimitives.ReadInt32LittleEndian(sample) / 2147483648.0,
                        _ => throw new NotSupportedException("不支援的 PCM 位元深度。")
                    };
                }

                value = Math.Clamp(value, -1.0, 1.0);
                squareSum += value * value;
                peak = Math.Max(peak, Math.Abs(value));
                features?.AddSample(value, channel);
            }
            features?.FinishFrame();
        }

        return new AudioBlockStatistics(squareSum, (long)frames * format.Channels, peak);
    }

    private static int ReadInt24(ReadOnlySpan<byte> sample) =>
        (sample[0] | sample[1] << 8 | sample[2] << 16) << 8 >> 8;
}
