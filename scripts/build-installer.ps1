#Requires -Version 5.1
<#
.SYNOPSIS
    AES67 VCS 설치 패키지 빌드 (Inno Setup)
.DESCRIPTION
    1. dotnet publish → build/output
    2. Inno Setup 없으면 자동 다운로드
    3. ISCC.exe 로 컴파일 → build/installer/AES67Vcs_Setup_v1.0.0.exe
#>

param(
    [string]$IsccPath = ""   # 직접 지정 시 자동 탐색 생략
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent

function Write-Step { param([string]$m) Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-OK   { param([string]$m) Write-Host "  [OK] $m"   -ForegroundColor Green }
function Write-Fail { param([string]$m) Write-Host "  [!!] $m"   -ForegroundColor Red; exit 1 }

# ── STEP 1: dotnet publish ────────────────────────────────────
Write-Step "앱 발행"
$OutDir = "$Root\build\output"
dotnet publish "$Root\src\Aes67Vcs.UI\Aes67Vcs.UI.csproj" `
    -c Release -r win-x64 --self-contained false `
    -o $OutDir --verbosity minimal
if ($LASTEXITCODE -ne 0) { Write-Fail "dotnet publish 실패" }
Write-OK "발행 완료 → $OutDir"

# ── STEP 2: Inno Setup 탐색/설치 ─────────────────────────────
Write-Step "Inno Setup 확인"

$IsccCandidates = @(
    $IsccPath,
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$Iscc = $IsccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $Iscc) {
    Write-Host "  Inno Setup 없음 → 자동 다운로드 중..." -ForegroundColor Yellow

    $TmpDir     = "$env:TEMP\InnoSetupInstall"
    $TmpExe     = "$TmpDir\is.exe"
    New-Item $TmpDir -ItemType Directory -Force | Out-Null

    # GitHub 미러에서 Inno Setup 6 최신 버전 다운로드
    $ApiUrl  = "https://api.github.com/repos/jrsoftware/issrc/releases/latest"
    $Release = Invoke-RestMethod $ApiUrl -Headers @{"User-Agent"="Aes67VcsBuild"}
    $Asset   = $Release.assets | Where-Object { $_.name -like "innosetup-*.exe" } | Select-Object -First 1

    if (-not $Asset) {
        # 직접 다운로드 폴백
        $DownloadUrl = "https://files.jrsoftware.org/is/6/innosetup-6.3.3.exe"
        Write-Host "  직접 URL 사용: $DownloadUrl"
    } else {
        $DownloadUrl = $Asset.browser_download_url
        Write-Host "  다운로드: $($Asset.name)"
    }

    Invoke-WebRequest $DownloadUrl -OutFile $TmpExe
    Write-Host "  자동 설치 중 (조용히)..."
    Start-Process $TmpExe -ArgumentList "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES" -Wait
    Remove-Item $TmpDir -Recurse -Force

    $Iscc = $IsccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $Iscc) { Write-Fail "Inno Setup 설치 후에도 ISCC.exe를 찾을 수 없습니다." }
}

Write-OK "ISCC: $Iscc"

# ── STEP 3: installer 출력 폴더 ──────────────────────────────
$InstallerOutDir = "$Root\build\installer"
New-Item $InstallerOutDir -ItemType Directory -Force | Out-Null

# ── STEP 4: ISS 컴파일 ───────────────────────────────────────
Write-Step "설치 패키지 컴파일"
$IssFile = "$Root\installer\AES67Vcs.iss"

& $Iscc $IssFile
if ($LASTEXITCODE -ne 0) { Write-Fail "ISCC 컴파일 실패 (코드 $LASTEXITCODE)" }

# ── STEP 5: 결과 확인 ────────────────────────────────────────
$Output = Get-ChildItem $InstallerOutDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $Output) { Write-Fail "출력 파일을 찾을 수 없습니다." }

Write-OK "설치 패키지 생성 완료:"
Write-Host ""
Write-Host "  $($Output.FullName)" -ForegroundColor White
Write-Host "  크기: $([math]::Round($Output.Length/1MB, 1)) MB" -ForegroundColor Gray
Write-Host ""
