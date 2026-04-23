namespace Aes67Vcs.Core.Models;

public class PtpStatus
{
    public PtpState State { get; set; } = PtpState.Disabled;
    /// <summary>현재 마스터 클록 ID (8바이트 hex)</summary>
    public string MasterClockId { get; set; } = "";
    /// <summary>오프셋 (나노초)</summary>
    public long OffsetNs { get; set; }
    /// <summary>Mean Path Delay (나노초)</summary>
    public long MeanPathDelayNs { get; set; }
    /// <summary>마지막 동기화 시각</summary>
    public DateTime LastSyncTime { get; set; }
    /// <summary>잠금 상태 여부</summary>
    public bool IsLocked => State == PtpState.Locked;

    public string StateDescription => State switch
    {
        PtpState.Disabled  => "비활성",
        PtpState.Listening => "마스터 탐색 중...",
        PtpState.Syncing   => $"동기화 중 (오프셋: {OffsetNs:+#;-#;0} ns)",
        PtpState.Locked    => $"잠금 (오프셋: {OffsetNs:+#;-#;0} ns)",
        PtpState.Error     => "오류",
        _                  => "알 수 없음"
    };
}
