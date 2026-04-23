using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Aes67Vcs.Core.Models;

namespace Aes67Vcs.Core.Scream;

/// <summary>
/// Scream 드라이버 레지스트리 설정 및 서비스 제어.
///
/// Scream 레지스트리 키:
///   HKLM\SYSTEM\CurrentControlSet\Services\Scream\Parameters
///   - UnicastTargets  REG_SZ  "IP:PORT IP:PORT ..."
///   - UnicastPort     REG_DWORD  (사용 안 할 경우)
///   - SilenceThreshold REG_DWORD
///
/// Scream 네트워크 드라이버(WDM)는 재시작하면 설정이 반영됨.
/// </summary>
[SupportedOSPlatform("windows")]
public class ScreamManager
{
    private const string ScreamRegPath =
        @"SYSTEM\CurrentControlSet\Services\Scream\Parameters";

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
    /// Scream 드라이버에 멀티캐스트 주소/포트/채널 설정 기록.
    /// 관리자 권한 필요.
    /// </summary>
    public void ApplyConfig()
    {
        using var key = Registry.LocalMachine.CreateSubKey(ScreamRegPath, writable: true);
        if (key == null)
            throw new InvalidOperationException("Scream 레지스트리 키를 열 수 없습니다. 관리자 권한이 필요합니다.");

        // Scream은 UDP 멀티캐스트로 "IP:PORT" 문자열을 사용
        string target = $"{_cfg.MulticastAddress}:{_cfg.RtpPort}";
        key.SetValue("UnicastTargets", target, RegistryValueKind.String);

        // 채널 수 및 샘플 포맷 (Scream 1.4+ 지원)
        key.SetValue("Channels",       (int)_cfg.Channels,  RegistryValueKind.DWord);
        key.SetValue("SampleRate",     _cfg.SampleRate,      RegistryValueKind.DWord);
        key.SetValue("SampleSize",     _cfg.BitDepth,        RegistryValueKind.DWord);
    }

    /// <summary>현재 레지스트리에서 Scream 설정 읽기</summary>
    public ScreamRegistrySettings? ReadSettings()
    {
        using var key = Registry.LocalMachine.OpenSubKey(ScreamRegPath);
        if (key == null) return null;

        return new ScreamRegistrySettings
        {
            UnicastTargets = key.GetValue("UnicastTargets") as string ?? "",
            Channels       = (int)(key.GetValue("Channels")    ?? 2),
            SampleRate     = (int)(key.GetValue("SampleRate")  ?? 48000),
            SampleSize     = (int)(key.GetValue("SampleSize")  ?? 16),
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
