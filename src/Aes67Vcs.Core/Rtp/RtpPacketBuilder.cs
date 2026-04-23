using System.Buffers.Binary;

namespace Aes67Vcs.Core.Rtp;

/// <summary>
/// AES67 RTP 패킷 빌더.
/// RFC 3550 헤더 (12바이트) + L24 big-endian PCM 페이로드.
/// GC 압박 없이 동작하도록 ArrayPool 버퍼를 직접 채움.
/// </summary>
public sealed class RtpPacketBuilder
{
    private const byte RtpVersion = 0x80; // V=2, P=0, X=0, CC=0
    private const byte MarkerBit  = 0x80;

    private readonly byte _payloadType;   // 96 (dynamic L24)
    private readonly uint _ssrc;
    private ushort        _sequence;
    private uint          _timestamp;
    private readonly uint _samplesPerPacket;

    /// <summary>
    /// RTP 헤더 고정 크기 = 12 bytes
    /// </summary>
    public const int HeaderSize = 12;

    public RtpPacketBuilder(byte payloadType, uint ssrc, uint samplesPerPacket)
    {
        _payloadType      = payloadType;
        _ssrc             = ssrc;
        _samplesPerPacket = samplesPerPacket;
        _sequence         = (ushort)Random.Shared.Next(0, 0xFFFF);
        _timestamp        = (uint)Random.Shared.Next();
    }

    /// <summary>
    /// 12바이트 RTP 헤더를 <paramref name="dest"/> 앞부분에 씁니다.
    /// 첫 패킷에만 마커 비트를 세웁니다.
    /// </summary>
    public void WriteHeader(Span<byte> dest, bool marker = false)
    {
        dest[0] = RtpVersion;
        dest[1] = (byte)(_payloadType | (marker ? MarkerBit : 0));
        BinaryPrimitives.WriteUInt16BigEndian(dest[2..], _sequence++);
        BinaryPrimitives.WriteUInt32BigEndian(dest[4..], _timestamp);
        BinaryPrimitives.WriteUInt32BigEndian(dest[8..], _ssrc);
    }

    /// <summary>
    /// 패킷 전송 후 타임스탬프를 한 패킷만큼 전진.
    /// </summary>
    public void AdvanceTimestamp() => _timestamp += _samplesPerPacket;

    /// <summary>
    /// PTP 오프셋을 적용해 타임스탬프를 초기화.
    /// ptpNs: PTP 현재 시각(나노초), sampleRate: 48000
    /// </summary>
    public void SyncToPtp(long ptpNs, int sampleRate = 48000)
    {
        // RTP 타임스탬프 = PTP 시각을 샘플 단위로 변환 (32bit wrap)
        _timestamp = (uint)(ptpNs / (1_000_000_000L / sampleRate));
    }
}
