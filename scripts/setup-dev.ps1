#Requires -Version 5.1
<#
.SYNOPSIS
    AES67 VCS 개발 환경 셋업 스크립트
.DESCRIPTION
    새 PC에서 한 번만 실행하면 개발에 필요한 모든 도구를 설치하고
    리포지토리를 클론합니다.

    필요 도구:
      - Git
      - Visual Studio 2022 Build Tools (C++ 워크로드 포함)
      - .NET 8 SDK
      - Inno Setup 6 (설치 패키지 빌드용, 선택)

.EXAMPLE
    # 관리자 PowerShell에서 실행
    Set-ExecutionPolicy Bypass -Scope Process -Force
    .\setup-dev.ps1

    # 클론 경로를 직접 지정할 경우
    .\setup-dev.ps1 -TargetDir "D:\dev\aes67_VSC"
#>

param(
    [string]$TargetDir = "$env:USERPROFILE\dev\aes67_VSC",
    [switch]$SkipClone,          # 이미 클론되어 있을 때
    [switch]$SkipVS,             # VS Build Tools 설치 건너뜀
    [switch]$SkipDotnet,         # .NET SDK 설치 건너뜀
    [switch]$SkipInnoSetup       # Inno Setup 설치 건너뜀
)

$ErrorActionPreference = "Stop"
$RepoUrl = "https://github.com/jkjh8/aes67_VSC.git"

function Write-Step  { param([string]$m) Write-Host "`n=== $m ===" -ForegroundColor Cyan }
function Write-OK    { param([string]$m) Write-Host "  [OK] $m"   -ForegroundColor Green }
function Write-Skip  { param([string]$m) Write-Host "  [--] $m"   -ForegroundColor DarkGray }
function Write-Warn  { param([string]$m) Write-Host "  [!!] $m"   -ForegroundColor Yellow }
function Write-Fail  { param([string]$m) Write-Host "  [XX] $m"   -ForegroundColor Red; exit 1 }

# ── 관리자 권한 확인 ──────────────────────────────────────────
if (-not ([Security.Principal.WindowsPrincipal]
          [Security.Principal.WindowsIdentity]::GetCurrent()
         ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))
{
    Write-Warn "관리자 권한으로 다시 실행합니다..."
    Start-Process pwsh -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" $($MyInvocation.BoundParameters.GetEnumerator() | ForEach-Object { "-$($_.Key) $($_.Value)" })" -Verb RunAs
    exit
}

Write-Host @"

  ╔══════════════════════════════════════════╗
  ║   AES67 VCS  —  개발 환경 셋업           ║
  ║   GitHub : jkjh8/aes67_VSC              ║
  ╚══════════════════════════════════════════╝

"@ -ForegroundColor Cyan

# ── 1. winget 확인 ────────────────────────────────────────────
Write-Step "winget 확인"
$winget = Get-Command winget -ErrorAction SilentlyContinue
if (-not $winget) {
    Write-Warn "winget이 없습니다. Microsoft Store에서 'App Installer'를 설치하거나"
    Write-Warn "https://aka.ms/getwinget 에서 직접 설치하세요."
    Write-Fail "winget 필요"
}
Write-OK "winget $(& winget --version)"

# ── 2. Git ────────────────────────────────────────────────────
Write-Step "Git 확인 / 설치"
$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) {
    Write-OK "Git $(& git --version)"
} else {
    Write-Host "  Git 설치 중..." -ForegroundColor Yellow
    winget install --id Git.Git -e --silent --accept-package-agreements --accept-source-agreements
    # PATH 갱신
    $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("PATH","User")
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) { Write-OK "Git 설치 완료" }
    else       { Write-Fail "Git 설치 실패 — 수동으로 https://git-scm.com 에서 설치하세요." }
}

# ── 3. 리포지토리 클론 ────────────────────────────────────────
Write-Step "리포지토리 클론"
if ($SkipClone) {
    Write-Skip "클론 건너뜀 (-SkipClone)"
} elseif (Test-Path "$TargetDir\.git") {
    Write-Skip "이미 클론됨: $TargetDir"
    Write-Host "  최신 변경 pull 중..." -ForegroundColor Yellow
    & git -C $TargetDir pull
    Write-OK "Pull 완료"
} else {
    $parent = Split-Path $TargetDir -Parent
    if (-not (Test-Path $parent)) { New-Item $parent -ItemType Directory | Out-Null }
    Write-Host "  클론 중: $RepoUrl → $TargetDir" -ForegroundColor Yellow
    & git clone $RepoUrl $TargetDir
    if ($LASTEXITCODE -ne 0) { Write-Fail "클론 실패" }
    Write-OK "클론 완료: $TargetDir"
}

# ── 4. .NET 8 SDK ─────────────────────────────────────────────
Write-Step ".NET 8 SDK 확인 / 설치"
if ($SkipDotnet) {
    Write-Skip ".NET SDK 건너뜀 (-SkipDotnet)"
} else {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    $hasNet8 = $false
    if ($dotnet) {
        $sdks = & dotnet --list-sdks 2>$null
        $hasNet8 = $sdks -match "^8\."
    }
    if ($hasNet8) {
        Write-OK ".NET 8 SDK 이미 설치됨"
    } else {
        Write-Host "  .NET 8 SDK 설치 중..." -ForegroundColor Yellow
        winget install --id Microsoft.DotNet.SDK.8 -e --silent --accept-package-agreements --accept-source-agreements
        $env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" +
                    [System.Environment]::GetEnvironmentVariable("PATH","User")
        Write-OK ".NET 8 SDK 설치 완료"
    }
}

# ── 5. Visual Studio 2022 Build Tools (C++ 워크로드) ─────────
Write-Step "Visual Studio 2022 Build Tools 확인 / 설치"
if ($SkipVS) {
    Write-Skip "VS Build Tools 건너뜀 (-SkipVS)"
} else {
    $vswhere = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path $vswhere)) {
        $vswhere = "C:\Program Files\Microsoft Visual Studio\Installer\vswhere.exe"
    }

    $vsHasCpp = $false
    if (Test-Path $vswhere) {
        $vsPath = & $vswhere -latest -requires Microsoft.VisualCpp.Tools.HostX64.TargetX64 -property installationPath 2>$null
        $vsHasCpp = -not [string]::IsNullOrEmpty($vsPath)
    }

    if ($vsHasCpp) {
        Write-OK "Visual Studio C++ 도구 이미 설치됨"
    } else {
        Write-Host "  VS 2022 Build Tools 설치 중 (C++, CMake 포함)..." -ForegroundColor Yellow
        Write-Host "  ※ 수백 MB 다운로드 — 시간이 걸립니다." -ForegroundColor DarkYellow
        winget install --id Microsoft.VisualStudio.2022.BuildTools -e --silent --accept-package-agreements --accept-source-agreements `
            --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.VC.Tools.x86.x64 --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --includeRecommended"
        if ($LASTEXITCODE -ne 0) {
            Write-Warn "winget 설치가 완전히 끝나지 않았을 수 있습니다."
            Write-Warn "VS Installer가 백그라운드에서 계속 실행 중일 수 있으니 잠시 후 확인하세요."
        } else {
            Write-OK "VS 2022 Build Tools 설치 완료"
        }
    }
}

# ── 6. Inno Setup 6 ───────────────────────────────────────────
Write-Step "Inno Setup 6 확인 / 설치 (선택)"
if ($SkipInnoSetup) {
    Write-Skip "Inno Setup 건너뜀 (-SkipInnoSetup)"
} else {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $iscc) {
        $iscc = Get-Item "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" -ErrorAction SilentlyContinue
    }
    if ($iscc) {
        Write-OK "Inno Setup 이미 설치됨"
    } else {
        Write-Host "  Inno Setup 6 설치 중..." -ForegroundColor Yellow
        winget install --id JRSoftware.InnoSetup -e --silent --accept-package-agreements --accept-source-agreements
        Write-OK "Inno Setup 6 설치 완료"
    }
}

# ── 7. 환경 최종 확인 ────────────────────────────────────────
Write-Step "환경 확인"

$env:PATH = [System.Environment]::GetEnvironmentVariable("PATH","Machine") + ";" +
            [System.Environment]::GetEnvironmentVariable("PATH","User")

# Git
$g = Get-Command git -ErrorAction SilentlyContinue
if ($g) { Write-OK "git : $(& git --version)" } else { Write-Warn "git : 미설치" }

# .NET
$d = Get-Command dotnet -ErrorAction SilentlyContinue
if ($d) { Write-OK "dotnet : $(& dotnet --version)" } else { Write-Warn "dotnet : 미설치" }

# MSBuild
$vswhere2 = "C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere2)) { $vswhere2 = "C:\Program Files\Microsoft Visual Studio\Installer\vswhere.exe" }
if (Test-Path $vswhere2) {
    $vsInst = & $vswhere2 -latest -property installationPath 2>$null
    if ($vsInst) {
        $msbuild = Join-Path $vsInst "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $msbuild) { Write-OK "MSBuild : $msbuild" }
        else { Write-Warn "MSBuild : VS 있으나 MSBuild 없음" }
    } else { Write-Warn "MSBuild : VS 없음 — C++ 빌드 불가" }
} else { Write-Warn "MSBuild : vswhere 없음" }

# Inno Setup
$iscc2 = Get-Item "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" -ErrorAction SilentlyContinue
if ($iscc2) { Write-OK "ISCC   : $($iscc2.FullName)" } else { Write-Skip "ISCC   : 미설치 (installer 빌드 불가)" }

# ── 8. 완료 메시지 ───────────────────────────────────────────
Write-Host @"

  ══════════════════════════════════════════════
   셋업 완료!

   프로젝트 경로 : $TargetDir

   다음 단계:
     1. Visual Studio 2022에서 솔루션 열기
        $TargetDir\Aes67Vcs.sln

     2. C++ DLL 빌드 (Release|x64)
        → Aes67Engine 프로젝트 우클릭 → 빌드

     3. 전체 빌드 스크립트
        cd '$TargetDir'
        .\scripts\build.ps1

     4. 설치 패키지 빌드
        .\scripts\build-installer.ps1
  ══════════════════════════════════════════════
"@ -ForegroundColor Green
