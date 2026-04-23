// ============================================================
// PtpMonitor.cs — W32TM /query /status 기반 PTP 상태 모니터
// ============================================================
// W32TM이 PTP 모드로 동작 중일 때 오프셋/상태를 파싱해서
// Aes67Engine_SetPtpOffsetNs() 로 전달한다.
// W32TM PTP 설정:
//   w32tm /config /manualpeerlist:"<PTP master IP>" /syncfromflags:manual
//         /update
//   w32tm /resync
// ============================================================
using System.Diagnostics;
using System.Text.RegularExpressions;
using Aes67Vcs.Core.Models;
using Aes67Vcs.Core.Rtp;

namespace Aes67Vcs.Core.Ptp;

public sealed class PtpMonitor : IDisposable
{
    // 폴링 간격 (W32TM 쿼리는 무거우므로 1초 간격)
    private const int PollIntervalMs = 1000;

    private CancellationTokenSource? _cts;
    private Task?                    _task;

    // 최근 상태
    private PtpStatus _status = new() { State = PtpState.Disabled };
    private readonly object _lock = new();

    /// <summary>상태가 바뀔 때마다 발생 (UI 스레드가 아닐 수 있음)</summary>
    public event Action<PtpStatus>? StatusChanged;

    // ── 시작/중지 ────────────────────────────────────────────

    public void Start()
    {
        if (_cts != null) return;
        _cts  = new CancellationTokenSource();
        _task = Task.Run(() => PollLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _task?.Wait(2000); } catch { /* ignore */ }
        _cts  = null;
        _task = null;
        UpdateStatus(new PtpStatus { State = PtpState.Disabled });
    }

    public PtpStatus GetStatus()
    {
        lock (_lock) return _status;
    }

    // ── 폴링 루프 ────────────────────────────────────────────

    private async Task PollLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var s = await QueryW32TmAsync(ct);
                UpdateStatus(s);

                // 잠긴 경우 엔진에 오프셋 전달
                if (s.State == PtpState.Locked || s.State == PtpState.Syncing)
                {
                    try { Aes67EngineInterop.Aes67Engine_SetPtpOffsetNs(s.OffsetNs); }
                    catch { /* DLL 미로드 시 무시 */ }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* w32tm 실패 시 재시도 */ }

            await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    // ── W32TM 쿼리 ───────────────────────────────────────────

    private static async Task<PtpStatus> QueryW32TmAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo("w32tm", "/query /status /verbose")
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("w32tm 시작 실패");

        string output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        return ParseW32TmOutput(output);
    }

    // ── 출력 파싱 ────────────────────────────────────────────
    // w32tm /query /status /verbose 출력 예시 (영문 OS):
    //   Leap Indicator: 0(no warning)
    //   Stratum: 2 (secondary reference - syncd by (S)NTP)
    //   Precision: -23 (119.209ns per tick)
    //   Root Delay: 0.0156250s
    //   Root Dispersion: 7.8005719s
    //   ReferenceId: 0xC0A80101 (source IP: 192.168.1.1)
    //   Last Successful Sync Time: ...
    //   Source: 192.168.1.1
    //   Poll Interval: 6 (64s)
    //   Phase Offset: +0.0000032s   ← 핵심
    //   ClockRate: 0.0156250s
    //   State: Synchronized

    private static readonly Regex _offsetRx = new(
        @"(?:Phase Offset|Clock Offset)[:\s]+([+-]?\d+\.\d+)s",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _stateRx = new(
        @"State[:\s]+(\w[\w\s]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _sourceRx = new(
        @"Source[:\s]+([\d\.a-fA-F:]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _refIdRx = new(
        @"ReferenceId[:\s]+0x[0-9A-Fa-f]+\s+\(source IP:\s*([\d\.]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static PtpStatus ParseW32TmOutput(string output)
    {
        var result = new PtpStatus();

        // 오프셋 파싱 (초 → 나노초)
        var offMatch = _offsetRx.Match(output);
        if (offMatch.Success &&
            double.TryParse(offMatch.Groups[1].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double offsetSec))
        {
            result.OffsetNs = (long)(offsetSec * 1_000_000_000.0);
        }

        // 상태 파싱
        var stMatch = _stateRx.Match(output);
        string stateStr = stMatch.Success
            ? stMatch.Groups[1].Value.Trim().ToLowerInvariant()
            : "";

        result.State = stateStr switch
        {
            var s when s.Contains("sync")     => PtpState.Locked,
            var s when s.Contains("hold")     => PtpState.Syncing,
            var s when s.Contains("discover") => PtpState.Listening,
            var s when s.Contains("listen")   => PtpState.Listening,
            var s when s.Contains("error")    => PtpState.Error,
            _                                  => PtpState.Listening,
        };

        // 마스터 IP
        var refIdM = _refIdRx.Match(output);
        if (refIdM.Success)
            result.MasterClockId = refIdM.Groups[1].Value;
        else
        {
            var srcM = _sourceRx.Match(output);
            if (srcM.Success)
                result.MasterClockId = srcM.Groups[1].Value;
        }

        // 오프셋 임계 판단: |offset| < 1ms 이면 Locked
        if (result.State == PtpState.Locked &&
            Math.Abs(result.OffsetNs) > 1_000_000L)
            result.State = PtpState.Syncing;

        return result;
    }

    // ── 이벤트 발행 ──────────────────────────────────────────

    private void UpdateStatus(PtpStatus s)
    {
        lock (_lock) _status = s;
        StatusChanged?.Invoke(s);
    }

    public void Dispose() => Stop();
}
