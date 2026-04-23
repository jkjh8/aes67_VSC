namespace Aes67Vcs.Core.Models;

/// <summary>오디오 채널 수</summary>
public enum ChannelCount { Two = 2, Eight = 8 }

/// <summary>PTP 동기화 상태</summary>
public enum PtpState
{
    Disabled,
    Listening,
    Syncing,
    Locked,
    Error
}

/// <summary>AES67 VCS 전체 설정</summary>
public class Aes67Config
{
    // ── 오디오 ──────────────────────────────────────────────
    public ChannelCount Channels { get; set; } = ChannelCount.Two;
    /// <summary>샘플레이트 고정 48000 Hz</summary>
    public int SampleRate => 48000;
    /// <summary>비트뎁스 고정 24bit (L24)</summary>
    public int BitDepth => 24;

    // ── 네트워크 ─────────────────────────────────────────────
    /// <summary>
    /// RTP 멀티캐스트 주소.
    /// Scream 기본: 239.255.77.77 / AES67 권장: 239.69.x.x
    /// </summary>
    public string MulticastAddress { get; set; } = "239.255.77.77";
    /// <summary>RTP 포트 (Scream 기본 4010 / AES67 기본 5004)</summary>
    public int RtpPort { get; set; } = 4010;
    /// <summary>송신에 사용할 로컬 네트워크 인터페이스 IP</summary>
    public string LocalInterface { get; set; } = "";
    /// <summary>스트림 이름 (SAP 어나운스용)</summary>
    public string StreamName { get; set; } = "AES67-VCS";

    // ── PTP ─────────────────────────────────────────────────
    public bool PtpEnabled { get; set; } = true;
    /// <summary>PTP 도메인 번호 (AES67 기본 0)</summary>
    public int PtpDomain { get; set; } = 0;
    /// <summary>PTP 멀티캐스트 주소 (기본 224.0.1.129)</summary>
    public string PtpMulticastAddress { get; set; } = "224.0.1.129";
    /// <summary>
    /// W32TM이 동기화할 PTP 그랜드마스터 IP.
    /// 비어 있으면 W32TM 자동 설정을 건너뜀.
    /// </summary>
    public string PtpMasterIp { get; set; } = "";

    // ── Scream ───────────────────────────────────────────────
    public bool ScreamEnabled { get; set; } = true;
    /// <summary>Scream 드라이버 설치 경로</summary>
    public string ScreamInstallPath { get; set; } =
        @"C:\Program Files\Scream";
}
