using System.Runtime.CompilerServices;

namespace Aes67Vcs.Core.Rtp;

/// <summary>
/// WASAPI 오디오 포맷 → AES67 L24 (24bit big-endian) 변환.
/// 핫패스에서 GC 할당 없음. unsafe 블록으로 최대 속도.
/// </summary>
public static class L24Converter
{
    // ── Float32 → L24 big-endian ──────────────────────────────
    // WASAPI shared mode 기본 포맷 (IEEE 754 float, -1.0~+1.0)

    /// <summary>
    /// IEEE 754 float 샘플 배열 → L24 big-endian 바이트 스트림.
    /// dest 크기 = src.Length × 3 이상이어야 함.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Float32ToL24Be(ReadOnlySpan<float> src, Span<byte> dest)
    {
        for (int i = 0; i < src.Length; i++)
        {
            // float → 24bit signed int (클리핑 포함)
            float f = src[i];
            if      (f >  1.0f) f =  1.0f;
            else if (f < -1.0f) f = -1.0f;

            int s = (int)(f * 8_388_607f); // 2^23 - 1

            // big-endian 3바이트 쓰기
            int off = i * 3;
            dest[off]     = (byte)(s >> 16);
            dest[off + 1] = (byte)(s >>  8);
            dest[off + 2] = (byte) s;
        }
    }

    // ── Int32 (24bit in 32bit) → L24 big-endian ───────────────
    // WASAPI exclusive 32bit container, 24bit 유효 데이터

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Int32ToL24Be(ReadOnlySpan<byte> src, Span<byte> dest, int sampleCount)
    {
        // WASAPI Int32: little-endian, 상위 24bit 유효
        for (int i = 0; i < sampleCount; i++)
        {
            int off32 = i * 4;
            // byte[1..3] = 24bit little-endian → big-endian 변환
            dest[i * 3]     = src[off32 + 3];
            dest[i * 3 + 1] = src[off32 + 2];
            dest[i * 3 + 2] = src[off32 + 1];
        }
    }

    // ── Int16 → L24 big-endian ────────────────────────────────
    // 일부 디바이스가 16bit로 제공하는 경우

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Int16ToL24Be(ReadOnlySpan<byte> src, Span<byte> dest, int sampleCount)
    {
        for (int i = 0; i < sampleCount; i++)
        {
            int off16 = i * 2;
            // int16 little-endian
            short s16 = (short)(src[off16] | (src[off16 + 1] << 8));
            // 16→24bit: shift left 8, big-endian
            int s24 = s16 << 8;
            dest[i * 3]     = (byte)(s24 >> 16);
            dest[i * 3 + 1] = (byte)(s24 >>  8);
            dest[i * 3 + 2] = (byte) s24;
        }
    }

    // ── Int24 → L24 big-endian ────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Int24ToL24Be(ReadOnlySpan<byte> src, Span<byte> dest, int sampleCount)
    {
        for (int i = 0; i < sampleCount; i++)
        {
            int off = i * 3;
            // little-endian → big-endian 바이트 순서 뒤집기
            dest[off]     = src[off + 2];
            dest[off + 1] = src[off + 1];
            dest[off + 2] = src[off];
        }
    }

    /// <summary>
    /// Span을 인수로 받는 변환 함수 전용 delegate.
    /// (Span은 Action&lt;&gt; 제네릭에 사용 불가)
    /// </summary>
    public delegate void ConvertFunc(ReadOnlySpan<byte> src, Span<byte> dst, int sampleCount);

    /// <summary>WASAPI WaveFormat에서 적합한 변환 함수 선택</summary>
    public static ConvertFunc GetConverter(int bitsPerSample, bool isFloat)
    {
        return (bitsPerSample, isFloat) switch
        {
            (32, true)  => (src, dst, n) =>
            {
                var floats = System.Runtime.InteropServices.MemoryMarshal
                    .Cast<byte, float>(src[..(n * 4)]);
                Float32ToL24Be(floats, dst);
            },
            (32, false) => (src, dst, n) => Int32ToL24Be(src, dst, n),
            (24, false) => (src, dst, n) => Int24ToL24Be(src, dst, n),
            (16, false) => (src, dst, n) => Int16ToL24Be(src, dst, n),
            _ => throw new NotSupportedException(
                $"지원하지 않는 포맷: {bitsPerSample}bit isFloat={isFloat}")
        };
    }
}
