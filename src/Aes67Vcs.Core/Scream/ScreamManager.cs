using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Aes67Vcs.Core.Models;

namespace Aes67Vcs.Core.Scream;

/// <summary>
/// Scream 4.0 드라이버 레지스트리 설정 및 서비스 제어.
///
/// Scream 4.0 실제 레지스트리 키 (소스: adapter.cpp):
///   HKLM\SYSTEM\CurrentControlSet\Services\Scream\Options
///   - UnicastIPv4     REG_SZ    대상 IP (기본: "239.255.77.77", 멀티캐스트 가능)
///   - UnicastPort     REG_DWORD 대상 포트 (기본: 4010)
///   - UnicastSrcIPv4  REG_SZ    송신 소스 IP (선택)
///   - UnicastSrcPort  REG_DWORD 송신 소스 포트 (기본: 0 = OS 자동)
///   - TTL             REG_DWORD 멀티캐스트 TTL
///   - DSCP            REG_DWORD DSCP 마킹 값
///   - SilenceThreshold REG_DWORD
///
/// 채널/샘플레이트/비트뎁스는 WASAPI 레벨에서 설정됨 (레지스트리 불필요).
/// 드라이버 재시작(디바이스 비활성화→활성화) 시 설정이 반영됨.
/// </summary>
[SupportedOSPlatform("windows")]
public class ScreamManager
{
    private const string ScreamRegPath =
        @"SYSTEM\CurrentControlSet\Services\Scream\Options";

    private readonly Aes67Config _cfg;

    public ScreamManager(Aes67Config config) => _cfg = config;

    // ── 설치 확인 ────────────────────────────────────────────

    public bool IsDriverInstalled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\Scream");
        return key != null;
    }

    public bool IsDevicePresent()
    {
        using var key = Registry.LocalMachine.OpenSubKey(
            @"SYSTEM\CurrentControlSet\Services\Scream\Parameters");
        return key != null;
    }

    // ── 설정 적용 ────────────────────────────────────────────

    /// <summary>
    /// Scream 4.0 드라이버에 네트워크 설정 기록.
    /// 관리자 권한 필요. 드라이버 재시작 후 반영.
    /// </summary>
    public void ApplyConfig()
    {
        using var key = Registry.LocalMachine.CreateSubKey(ScreamRegPath, writable: true);
        if (key == null)
            throw new InvalidOperationException("Scream 레지스트리 키를 열 수 없습니다. 관리자 권한이 필요합니다.");

        // UnicastIPv4: 멀티캐스트 주소 (예: 239.255.77.77)
        key.SetValue("UnicastIPv4", _cfg.MulticastAddress, RegistryValueKind.String);
        // UnicastPort: RTP 포트 (AES67 기본 5004, Scream 기본 4010)
        key.SetValue("UnicastPort",  _cfg.RtpPort,  RegistryValueKind.DWord);
        // TTL: 멀티캐스트 TTL (로컬 서브넷 = 1, 라우팅 필요 시 증가)
        key.SetValue("TTL",          32,             RegistryValueKind.DWord);

        if (!string.IsNullOrEmpty(_cfg.LocalInterface))
            key.SetValue("UnicastSrcIPv4", _cfg.LocalInterface, RegistryValueKind.String);
    }

    /// <summary>현재 레지스트리에서 Scream 설정 읽기</summary>
    public ScreamRegistrySettings? ReadSettings()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ScreamRegPath);
        if (key == null) return null;

        return new ScreamRegistrySettings
        {
            UnicastTargets = key.GetValue("UnicastIPv4") as string ?? "239.255.77.77",
            Channels       = 2,    // WASAPI 레벨 설정 — 레지스트리 없음
            SampleRate     = 48000,
            SampleSize     = 24,
        };
    }

    // ── 서비스 재시작 ────────────────────────────────────────

    /// <summary>
    /// Scream 오디오 디바이스를 비활성화/활성화하여 설정 재로드.
    /// devcon 또는 pnputil 사용.
    /// </summary>
    public async Task RestartDeviceAsync()
    {
        // Windows 11: pnputil로 Scream 가상 디바이스 재시작
        await RunProcessAsync("pnputil", "/restart-device \"ROOT\\MEDIA\\0000\"");
    }

    /// <summary>Scream 드라이버 설치 (관리자 권한)</summary>
    public async Task InstallDriverAsync(string infPath)
    {
        if (!File.Exists(infPath))
            throw new FileNotFoundException("INF 파일을 찾을 수 없습니다", infPath);
        await RunProcessAsync("pnputil", $"/add-driver \"{infPath}\" /install");
    }

    /// <summary>Scream 드라이버 제거</summary>
    public async Task UninstallDriverAsync()
    {
        await RunProcessAsync("pnputil", "/remove-device \"ROOT\\MEDIA\\0000\"");
    }

    // ── 유틸리티 ─────────────────────────────────────────────

    private static async Task<string> RunProcessAsync(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            CreateNoWindow         = true,
            Verb                   = "runas",
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"프로세스 시작 실패: {exe}");
        string output = await proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        if (proc.ExitCode != 0)
        {
            string err = await proc.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"{exe} 실패 (코드 {proc.ExitCode}): {err}");
        }
        return output;
    }
}

public class ScreamRegistrySettings
{
    public string UnicastTargets { get; init; } = "";
    public int    Channels       { get; init; }
    public int    SampleRate     { get; init; }
    public int    SampleSize     { get; init; }
}
