// ============================================================
// Aes67EngineInterop.cs — C++ Aes67Engine.dll P/Invoke 브릿지
// ============================================================
using System.Runtime.InteropServices;

namespace Aes67Vcs.Core.Rtp;

/// <summary>통계 콜백 델리게이트 (C++ __stdcall 규약)</summary>
[UnmanagedFunctionPointer(CallingConvention.StdCall)]
public delegate void Aes67StatsCallback(
    int pktsPerSec,
    float avgJitterUs,
    int droppedPkts,
    int channels);

/// <summary>
/// Aes67Engine.dll P/Invoke 래퍼.
/// DLL은 앱 디렉터리 또는 build/native/ 에 위치해야 합니다.
/// </summary>
public static class Aes67EngineInterop
{
    private const string DllName = "Aes67Engine";

    // ── 수명 주기 ────────────────────────────────────────────

    /// <summary>엔진 초기화 (Start 전에 1회 호출)</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall,
               CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Aes67Engine_Init(
        string multicastIp,
        int rtpPort,
        int channels,
        string? srcIp);

    /// <summary>WASAPI 캡처 + RTP 송신 스레드 시작</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Aes67Engine_Start();

    /// <summary>캡처/송신 중지 및 리소스 해제</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void Aes67Engine_Stop();

    /// <summary>현재 실행 중 여부</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Aes67Engine_IsRunning();

    // ── PTP 동기화 ───────────────────────────────────────────

    /// <summary>W32TM에서 얻은 PTP 오프셋(나노초)을 엔진에 전달</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void Aes67Engine_SetPtpOffsetNs(long offsetNs);

    // ── 콜백 / 통계 ──────────────────────────────────────────

    /// <summary>1초 주기 통계 콜백 등록 (null 로 해제)</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    public static extern void Aes67Engine_SetStatsCallback(
        Aes67StatsCallback? callback);

    // ── 디바이스 열거 ────────────────────────────────────────

    /// <summary>
    /// 사용 가능한 렌더 디바이스 목록을 '\n' 구분 UTF-8 문자열로 반환.
    /// 반환값: 전체 문자열 길이 (buf_len 초과 시 잘릴 수 있음).
    /// </summary>
    [DllImport(DllName, CallingConvention = CallingConvention.StdCall,
               CharSet = CharSet.Ansi)]
    public static extern int Aes67Engine_GetDeviceList(
        byte[]? buf, int bufLen);

    /// <summary>디바이스 이름 목록을 string[] 로 반환하는 헬퍼</summary>
    public static string[] GetDeviceNames()
    {
        int needed = Aes67Engine_GetDeviceList(null, 0);
        if (needed <= 0) return Array.Empty<string>();

        var buf = new byte[needed + 1];
        Aes67Engine_GetDeviceList(buf, buf.Length);
        string raw = System.Text.Encoding.UTF8.GetString(buf, 0, needed);
        return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    // ── DLL 존재 확인 ────────────────────────────────────────

    /// <summary>Aes67Engine.dll 이 탐색 경로에 있는지 확인</summary>
    public static bool IsDllAvailable()
    {
        try
        {
            Aes67Engine_IsRunning();
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }
}
