using System.Buffers.Binary;

namespace Aes67Vcs.Core.Ptp;

/// <summary>PTPv2 메시지 타입 (IEEE 1588-2008 Table 19)</summary>
public enum PtpMessageType : byte
{
    Sync             = 0x0,
    DelayReq         = 0x1,
    PDelayReq        = 0x2,
    PDelayResp       = 0x3,
    FollowUp         = 0x8,
    DelayResp        = 0x9,
    PDelayRespFollowUp = 0xA,
    Announce         = 0xB,
    Signaling        = 0xC,
    Management       = 0xD,
}

/// <summary>PTPv2 공통 헤더 44바이트 파서</summary>
public class PtpHeader
{
    public const int HeaderSize = 44;

    public PtpMessageType MessageType { get; private set; }
    public byte VersionPtp { get; private set; }   // 2
    public ushort MessageLength { get; private set; }
    public byte DomainNumber { get; private set; }
    public ushort FlagField { get; private set; }
    public long CorrectionNs { get; private set; } // 서브나노초 포함, 상위 48bit = ns
    public byte[] SourcePortIdentity { get; private set; } = new byte[10]; // clockId(8) + portNum(2)
    public ushort SequenceId { get; private set; }
    public byte ControlField { get; private set; }
    public sbyte LogMessageInterval { get; private set; }

    public static PtpHeader Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new ArgumentException("PTP 헤더 크기 부족");

        var h = new PtpHeader
        {
            MessageType        = (PtpMessageType)(data[0] & 0x0F),
            VersionPtp         = (byte)(data[1] & 0x0F),
            MessageLength      = BinaryPrimitives.ReadUInt16BigEndian(data[2..]),
            DomainNumber       = data[4],
            FlagField          = BinaryPrimitives.ReadUInt16BigEndian(data[6..]),
            CorrectionNs       = BinaryPrimitives.ReadInt64BigEndian(data[8..]),
            SequenceId         = BinaryPrimitives.ReadUInt16BigEndian(data[30..]),
            ControlField       = data[32],
            LogMessageInterval = (sbyte)data[33],
        };
        data.Slice(20, 10).CopyTo(h.SourcePortIdentity);
        return h;
    }

    /// <summary>correction 필드 → 나노초 (하위 16bit 은 서브나노초)</summary>
    public long CorrectionNanoseconds => CorrectionNs >> 16;
}

/// <summary>PTP Timestamp (80비트: 48bit 초 + 32bit 나노초)</summary>
public readonly struct PtpTimestamp
{
    public readonly ulong Seconds;
    public readonly uint  Nanoseconds;

    public PtpTimestamp(ulong seconds, uint nanoseconds)
    {
        Seconds     = seconds;
        Nanoseconds = nanoseconds;
    }

    public static PtpTimestamp Parse(ReadOnlySpan<byte> data)
    {
        // 48bit big-endian seconds
        ulong sec = ((ulong)data[0] << 40) | ((ulong)data[1] << 32) |
                    ((ulong)data[2] << 24) | ((ulong)data[3] << 16) |
                    ((ulong)data[4] <<  8) |  (ulong)data[5];
        uint ns = BinaryPrimitives.ReadUInt32BigEndian(data[6..]);
        return new PtpTimestamp(sec, ns);
    }

    /// <summary>DateTime UTC로 변환 (정밀도 1ns)</summary>
    public DateTime ToUtc()
    {
        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return epoch.AddSeconds(Seconds).AddTicks((long)(Nanoseconds / 100));
    }

    public long ToNanoseconds() => (long)Seconds * 1_000_000_000L + Nanoseconds;
}

/// <summary>Sync / FollowUp 메시지 페이로드</summary>
public class PtpSyncMessage
{
    public PtpHeader Header { get; }
    public PtpTimestamp OriginTimestamp { get; }

    public PtpSyncMessage(PtpHeader header, ReadOnlySpan<byte> payload)
    {
        Header          = header;
        OriginTimestamp = PtpTimestamp.Parse(payload);
    }
}

/// <summary>DelayResp 메시지 페이로드</summary>
public class PtpDelayRespMessage
{
    public PtpHeader Header { get; }
    public PtpTimestamp ReceiveTimestamp { get; }
    public byte[] RequestingPortIdentity { get; } = new byte[10];

    public PtpDelayRespMessage(PtpHeader header, ReadOnlySpan<byte> payload)
    {
        Header             = header;
        ReceiveTimestamp   = PtpTimestamp.Parse(payload);
        payload.Slice(10, 10).CopyTo(RequestingPortIdentity);
    }
}
