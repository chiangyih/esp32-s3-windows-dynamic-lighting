using System.Buffers.Binary;
using MusicSync.Audio;

internal static class AudioChecks
{
    public static int Run()
    {
        var checks = 0;

        void Near(double actual, double expected, string message, double tolerance = 1e-9)
        {
            if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
                throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}");
            checks++;
        }

        void True(bool value, string message)
        {
            if (!value)
                throw new InvalidOperationException(message);
            checks++;
        }

        void Reject<T>(Action action, string message) where T : Exception
        {
            try { action(); }
            catch (T) { checks++; return; }
            throw new InvalidOperationException(message);
        }

        var floatStereo = WaveFormatInfo.Parse(Format(3, 2, 32));
        var stereo = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(stereo, 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(stereo.AsSpan(4), -0.5f);
        var antiphase = PcmSampleDecoder.Analyze(stereo, 1, floatStereo);
        Near(Math.Sqrt(antiphase.SquareSum / antiphase.SampleCount), 0.5, "Opposite stereo channels must not cancel");
        Near(antiphase.Peak, 0.5, "Float stereo peak");
        True(antiphase.SampleCount == 2, "Float stereo channel count");

        var floatMono = WaveFormatInfo.Parse(Format(3, 1, 32));
        var exceptional = new byte[16];
        BinaryPrimitives.WriteSingleLittleEndian(exceptional, float.NaN);
        BinaryPrimitives.WriteSingleLittleEndian(exceptional.AsSpan(4), float.PositiveInfinity);
        BinaryPrimitives.WriteSingleLittleEndian(exceptional.AsSpan(8), float.NegativeInfinity);
        BinaryPrimitives.WriteSingleLittleEndian(exceptional.AsSpan(12), 2.0f);
        var finite = PcmSampleDecoder.Analyze(exceptional, 4, floatMono);
        Near(finite.SquareSum, 1.0, "NaN and infinities must become zero; excessive float samples clamp");
        Near(finite.Peak, 1.0, "Clamped float peak");

        var pcm16 = WaveFormatInfo.Parse(Format(1, 1, 16));
        var pcm16Bytes = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(pcm16Bytes, short.MinValue);
        BinaryPrimitives.WriteInt16LittleEndian(pcm16Bytes.AsSpan(2), short.MaxValue);
        var result16 = PcmSampleDecoder.Analyze(pcm16Bytes, 2, pcm16);
        Near(result16.SquareSum, 1 + Math.Pow(32767 / 32768.0, 2), "PCM16 signed endpoints");
        Near(result16.Peak, 1, "PCM16 minimum must normalize to negative one");

        var pcm24 = WaveFormatInfo.Parse(Format(1, 1, 24));
        var result24 = PcmSampleDecoder.Analyze(new byte[] { 0, 0, 0x80, 0xff, 0xff, 0x7f }, 2, pcm24);
        Near(result24.SquareSum, 1 + Math.Pow(8388607 / 8388608.0, 2), "PCM24 sign extension");
        Near(result24.Peak, 1, "PCM24 negative full scale");

        var pcm32 = WaveFormatInfo.Parse(Format(1, 1, 32));
        var pcm32Bytes = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(pcm32Bytes, int.MinValue);
        BinaryPrimitives.WriteInt32LittleEndian(pcm32Bytes.AsSpan(4), int.MaxValue);
        var result32 = PcmSampleDecoder.Analyze(pcm32Bytes, 2, pcm32);
        Near(result32.SquareSum, 1 + Math.Pow(int.MaxValue / 2147483648.0, 2), "PCM32 signed endpoints");
        Near(result32.Peak, 1, "PCM32 negative full scale");

        var extendedPcm = Format(0xfffe, 2, 32, 24,
            new Guid("00000001-0000-0010-8000-00aa00389b71"));
        var pcm24In32 = WaveFormatInfo.Parse(extendedPcm);
        True(!pcm24In32.IsFloat && pcm24In32.ValidBitsPerSample == 24 && pcm24In32.BitsPerSample == 32,
            "WAVEFORMATEXTENSIBLE PCM valid bits");
        var leftAligned = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(leftAligned, 0x40000000);
        BinaryPrimitives.WriteInt32LittleEndian(leftAligned.AsSpan(4), -0x40000000);
        var alignedResult = PcmSampleDecoder.Analyze(leftAligned, 1, pcm24In32);
        Near(Math.Sqrt(alignedResult.SquareSum / alignedResult.SampleCount), 0.5,
            "24 valid bits in 32-bit containers must remain left aligned");

        var extendedFloat = WaveFormatInfo.Parse(Format(0xfffe, 2, 32, 32,
            new Guid("00000003-0000-0010-8000-00aa00389b71")));
        True(extendedFloat.IsFloat && extendedFloat.SampleRate == 48000 && extendedFloat.BlockAlign == 8,
            "WAVEFORMATEXTENSIBLE IEEE float subtype");
        var zeroValidBits = WaveFormatInfo.Parse(Format(0xfffe, 1, 16, 0,
            new Guid("00000001-0000-0010-8000-00aa00389b71")));
        True(zeroValidBits.ValidBitsPerSample == 16, "Zero valid-bit field falls back to container bits");

        var silence = PcmSampleDecoder.Analyze(new byte[384], 96, WaveFormatInfo.Parse(Format(1, 2, 16)));
        Near(silence.SquareSum, 0, "Silent PCM block RMS");
        Near(silence.Peak, 0, "Silent PCM block peak");
        True(silence.SampleCount == 192, "Silent block still contributes sample count");
        var empty = PcmSampleDecoder.Analyze(Array.Empty<byte>(), 0, floatMono);
        True(empty.SampleCount == 0 && empty.SquareSum == 0 && empty.Peak == 0, "Empty block");

        Reject<NotSupportedException>(() => WaveFormatInfo.Parse(new byte[17]), "Reject truncated WAVEFORMATEX");
        Reject<NotSupportedException>(() => WaveFormatInfo.Parse(Format(6, 1, 16)), "Reject compressed formats");
        Reject<NotSupportedException>(() => WaveFormatInfo.Parse(Format(0xfffe, 1, 16, 16, Guid.Empty)),
            "Reject unknown extensible subtype");
        Reject<NotSupportedException>(() => WaveFormatInfo.Parse(Format(0xfffe, 1, 16, 17,
            new Guid("00000001-0000-0010-8000-00aa00389b71"))), "Reject valid bits beyond container");
        Reject<ArgumentException>(() => PcmSampleDecoder.Analyze(new byte[3], 1, floatStereo),
            "Reject truncated audio packet before reading");

        // Padded frames must not count padding bytes as audio channels.
        var paddedFormat = Format(1, 1, 16);
        BinaryPrimitives.WriteUInt16LittleEndian(paddedFormat.AsSpan(12), 4);
        var padded = PcmSampleDecoder.Analyze(new byte[] { 0, 0x40, 0xff, 0x7f }, 1,
            WaveFormatInfo.Parse(paddedFormat));
        Near(padded.SquareSum, 0.25, "Frame padding ignored");
        True(padded.SampleCount == 1, "Padding not counted as a sample");

        return checks;
    }

    private static byte[] Format(ushort tag, ushort channels, ushort bits, ushort validBits = 0,
        Guid subtype = default)
    {
        var bytes = new byte[tag == 0xfffe ? 40 : 18];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 48000);
        var alignment = (ushort)(channels * bits / 8);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 48000 * alignment);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), alignment);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), bits);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), tag == 0xfffe ? (ushort)22 : (ushort)0);
        if (tag == 0xfffe)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), validBits);
            subtype.TryWriteBytes(bytes.AsSpan(24));
        }
        return bytes;
    }
}
