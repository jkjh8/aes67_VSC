#Requires -Version 5.1
<#
.SYNOPSIS
    AES67 VCS 빌드 스크립트
.DESCRIPTION
    1) MSBuild → Aes67Engine.dll (C++, x64 Release)
    2) dotnet publish → Aes67Vcs.UI (C#, net8.0-windows)
    3) Aes67Engine.dll → 출력 폴더로 복사
#>

param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release",
    [string]$OutputDir     = "$PSScriptRoot\..\build\output"
)

$ErrorActionPreference = "Stop"
$Root          = Resolve-Path "$PSScriptRoot\.."
$SolutionPath  = "$Root\Aes67Vcs.sln"
$CppVcxproj    = "$Root\src\Aes67Engine\Aes67Engine.vcxproj"
$NativeBinDir  = "$Root\build\native"
$DllName       = "Aes67Engine.dll"

function Write-Step { param([string]$msg) Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-OK   { param([string]$msg) Write-Host "  [OK] $msg"   -ForegroundColor Green }
function Write-Fail { param([string]$msg) Write-Host "  [!!] $msg"   -ForegroundColor Red; exit 1 }

# ── 1. MSBuild / VS 탐색 ──────────────────────────────────────
Write-Step "C++ 엔진 빌드 (MSBuild)"

# vswhere 로 MSBuild 경로 자동 탐색
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) {
    $vswhere = "${env:ProgramFiles}\Microsoft Visual Studio\Installer\vswhere.exe"
}

$msbuild = $null
if (Test-Path $vswhere) {
    $vsPath = & $vswhere -latest -requires Microsoft.Component.MSBuild -property installationPath 2>$null
    if ($vsPath) {
        $candidate = Join-Path $vsPath "MSBuild\Current\Bin\MSBuild.exe"
        if (Test-Path $candidate) { $msbuild = $candidate }
    }
}

# 환경 변수 fallback
if (-not $msbuild) {
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { $msbuild = $cmd.Source }
}

if (-not $msbuild) {
    Write-Host "  [!!] MSBuild를 찾을 수 없습니다." -ForegroundColor Yellow
    Write-Host "       Visual Studio 2022 빌드 도구를 설치하거나 Developer PowerShell에서 실행하세요." -ForegroundColor Yellow
    Write-Host "       C++ DLL 빌드를 건너뜁니다." -ForegroundColor Yellow
} else {
    Write-OK "MSBuild: $msbuild"

    New-Item -ItemType Directory -Force $NativeBinDir | Out-Null

    & $msbuild $CppVcxproj `
        /p:Configuration=$Configuration `
        /p:Platform=x64 `
        /p:SolutionDir="$Root\" `
        /m /nologo /verbosity:minimal

    if ($LASTEXITCODE -ne 0) { Write-Fail "C++ DLL 빌드 실패" }

    $dllPath = "$NativeBinDir\$DllName"
    if (Test-Path $dllPath) {
        Write-OK "DLL 생성: $dllPath"
    } else {
        Write-Fail "빌드 후 DLL 파일을 찾을 수 없습니다: $dllPath"
    }
}

# ── 2. .NET SDK 확인 ─────────────────────────────────────────
Write-Step ".NET SDK 확인"
try {
    $ver = dotnet --version
    Write-OK ".NET SDK $ver"
} catch {
    Write-Fail ".NET SDK를 찾을 수 없습니다. https://dotnet.microsoft.com 에서 설치하세요."
}

# ── 3. 패키지 복원 ────────────────────────────────────────────
Write-Step "패키지 복원"
dotnet restore $SolutionPath --verbosity quiet
if ($LASTEXITCODE -ne 0) { Write-Fail "복원 실패" }
Write-OK "복원 완료"

# ── 4. C# 빌드 ────────────────────────────────────────────────
Write-Step "C# 빌드 ($Configuration)"
dotnet build $SolutionPath -c $Configuration --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { Write-Fail "빌드 실패" }
Write-OK "빌드 성공"

# ── 5. 발행 ───────────────────────────────────────────────────
Write-Step "발행 → $OutputDir"
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item $OutputDir -ItemType Directory | Out-Null

dotnet publish "$Root\src\Aes67Vcs.UI\Aes67Vcs.UI.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $OutputDir `
    --verbosity minimal

if ($LASTEXITCODE -ne 0) { Write-Fail "발행 실패" }
Write-OK "발행 완료"

# ── 6. DLL → 출력 폴더 복사 ────────────────────────────────────
Write-Step "Aes67Engine.dll 복사"
$dllSrc = "$NativeBinDir\$DllName"
if (Test-Path $dllSrc) {
    Copy-Item $dllSrc "$OutputDir\$DllName" -Force
    Write-OK "복사 완료: $OutputDir\$DllName"
} else {
    Write-Host "  [경고] $dllSrc 없음 — DLL을 수동으로 복사하세요." -ForegroundColor Yellow
}

Write-Host "`n빌드 성공! 실행 파일: $OutputDir\Aes67Vcs.exe" -ForegroundColor Green
