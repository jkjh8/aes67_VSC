#Requires -RunAsAdministrator
<#
.SYNOPSIS
    AES67 VCS 설치 스크립트 (Scream 드라이버 + 앱)
.DESCRIPTION
    1. Scream 드라이버 다운로드 및 설치 (pnputil)
    2. AES67 VCS 앱을 Program Files에 복사
    3. 시작 프로그램 등록
    4. Windows 방화벽 규칙 추가 (UDP 319, 320, 5004, 9875)
#>

param(
    [string]$ScreamInfPath  = "",   # 미지정 시 자동 다운로드
    [string]$AppSourceDir   = "$PSScriptRoot\..\build\output",
    [string]$InstallDir     = "$env:ProgramFiles\Aes67Vcs"
)

$ErrorActionPreference = "Stop"

function Write-Step { param([string]$msg) Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-OK   { param([string]$msg) Write-Host "  [OK] $msg"   -ForegroundColor Green }
function Write-Fail { param([string]$msg) Write-Host "  [!!] $msg"   -ForegroundColor Red; exit 1 }

# ── 1. Scream 드라이버 설치 ───────────────────────────────────
Write-Step "Scream 드라이버 설치"

if ([string]::IsNullOrEmpty($ScreamInfPath)) {
    # GitHub 릴리즈에서 최신 버전 다운로드
    $ScreamDir  = "$env:TEMP\ScreamInstall"
    $ScreamZip  = "$ScreamDir\scream.zip"
    New-Item $ScreamDir -ItemType Directory -Force | Out-Null

    Write-Host "  Scream 최신 버전 다운로드 중..."
    # Scream GitHub: duncanthrax/scream
    $ApiUrl = "https://api.github.com/repos/duncanthrax/scream/releases/latest"
    try {
        $release    = Invoke-RestMethod $ApiUrl -Headers @{ "User-Agent" = "Aes67Vcs-Installer" }
        $asset      = $release.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
        if (-not $asset) { Write-Fail "Scream 릴리즈를 찾을 수 없습니다." }
        Invoke-WebRequest $asset.browser_download_url -OutFile $ScreamZip
        Expand-Archive $ScreamZip -DestinationPath $ScreamDir -Force
        $ScreamInfPath = (Get-ChildItem $ScreamDir -Filter "*.inf" -Recurse | Select-Object -First 1).FullName
        Write-OK "다운로드 완료: $($asset.name)"
    } catch {
        Write-Host "  [경고] 자동 다운로드 실패. 수동으로 -ScreamInfPath 를 지정하세요." -ForegroundColor Yellow
        Write-Host "  Scream 다운로드: https://github.com/duncanthrax/scream/releases" -ForegroundColor Yellow
    }
}

if (-not [string]::IsNullOrEmpty($ScreamInfPath) -and (Test-Path $ScreamInfPath)) {
    Write-Host "  INF: $ScreamInfPath"
    pnputil /add-driver "$ScreamInfPath" /install
    if ($LASTEXITCODE -eq 0) { Write-OK "Scream 드라이버 설치됨" }
    else { Write-Host "  [경고] pnputil 실패 (이미 설치되어 있을 수 있음)" -ForegroundColor Yellow }
} else {
    Write-Host "  [스킵] Scream INF 없음 — 수동 설치 필요" -ForegroundColor Yellow
}

# ── 2. AES67 VCS 앱 복사 ─────────────────────────────────────
Write-Step "AES67 VCS 앱 설치"

if (-not (Test-Path $AppSourceDir)) {
    Write-Host "  빌드 결과물 없음. 먼저 build.ps1 을 실행하세요." -ForegroundColor Yellow
} else {
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    Copy-Item $AppSourceDir $InstallDir -Recurse
    Write-OK "설치 완료: $InstallDir"
}

# ── 3. 시작 프로그램 등록 ─────────────────────────────────────
Write-Step "시작 프로그램 등록"
$StartupKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$ExePath    = "$InstallDir\Aes67Vcs.exe"
if (Test-Path $ExePath) {
    Set-ItemProperty $StartupKey -Name "Aes67Vcs" -Value "`"$ExePath`""
    Write-OK "시작 프로그램 등록됨"
} else {
    Write-Host "  [스킵] 실행 파일 없음" -ForegroundColor Yellow
}

# ── 4. 방화벽 규칙 추가 ───────────────────────────────────────
Write-Step "방화벽 규칙 추가"

$rules = @(
    @{ Name="AES67-VCS-PTP-Event";   Port=319;  Proto="UDP"; Dir="Inbound"  },
    @{ Name="AES67-VCS-PTP-General"; Port=320;  Proto="UDP"; Dir="Inbound"  },
    @{ Name="AES67-VCS-RTP";         Port=5004; Proto="UDP"; Dir="Inbound"  },
    @{ Name="AES67-VCS-SAP";         Port=9875; Proto="UDP"; Dir="Inbound"  },
    @{ Name="AES67-VCS-RTP-Out";     Port=5004; Proto="UDP"; Dir="Outbound" },
    @{ Name="AES67-VCS-SAP-Out";     Port=9875; Proto="UDP"; Dir="Outbound" }
)

foreach ($r in $rules) {
    $existing = Get-NetFirewallRule -DisplayName $r.Name -ErrorAction SilentlyContinue
    if ($existing) { Remove-NetFirewallRule -DisplayName $r.Name }

    New-NetFirewallRule `
        -DisplayName $r.Name `
        -Direction   $r.Dir `
        -Protocol    $r.Proto `
        -LocalPort   $r.Port `
        -Action      Allow `
        -Profile     Any | Out-Null
    Write-OK "방화벽: $($r.Name) ($($r.Dir) $($r.Proto):$($r.Port))"
}

# ── 5. W32tm PTP 활성화 (Windows 11) ─────────────────────────
Write-Step "Windows Time Service PTP 설정"
Write-Host "  Windows 11 에서 PTP 하드웨어 타임스탬핑을 사용하려면:" -ForegroundColor Yellow
Write-Host "  w32tm /config /manualpeerlist:`"ptpmaster.local`" /syncfromflags:manual /update" -ForegroundColor Yellow
Write-Host "  (AES67 VCS는 자체 PTP 클라이언트를 사용하므로 선택 사항)" -ForegroundColor Yellow

Write-Host "`n설치 완료!" -ForegroundColor Green
Write-Host "실행: $InstallDir\Aes67Vcs.exe" -ForegroundColor Cyan
