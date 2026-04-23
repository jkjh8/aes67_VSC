#Requires -Version 5.1
<#
.SYNOPSIS
    AES67 VCS 빌드 스크립트
.DESCRIPTION
    dotnet build → publish → output 폴더에 결과물 정리
#>

param(
    [ValidateSet("Debug","Release")]
    [string]$Configuration = "Release",
    [string]$OutputDir     = "$PSScriptRoot\..\build\output"
)

$ErrorActionPreference = "Stop"
$SolutionPath = "$PSScriptRoot\..\Aes67Vcs.sln"

function Write-Step { param([string]$msg) Write-Host "`n=== $msg ===" -ForegroundColor Cyan }
function Write-OK   { param([string]$msg) Write-Host "  [OK] $msg"   -ForegroundColor Green }
function Write-Fail { param([string]$msg) Write-Host "  [!!] $msg"   -ForegroundColor Red }

# ── .NET SDK 확인 ─────────────────────────────────────────────
Write-Step ".NET SDK 확인"
try {
    $ver = dotnet --version
    Write-OK ".NET SDK $ver"
} catch {
    Write-Fail ".NET SDK를 찾을 수 없습니다. https://dotnet.microsoft.com 에서 설치하세요."
    exit 1
}

# ── 복원 ──────────────────────────────────────────────────────
Write-Step "패키지 복원"
dotnet restore $SolutionPath --verbosity quiet
if ($LASTEXITCODE -ne 0) { Write-Fail "복원 실패"; exit 1 }
Write-OK "복원 완료"

# ── 빌드 ──────────────────────────────────────────────────────
Write-Step "빌드 ($Configuration)"
dotnet build $SolutionPath -c $Configuration --no-restore --verbosity minimal
if ($LASTEXITCODE -ne 0) { Write-Fail "빌드 실패"; exit 1 }
Write-OK "빌드 성공"

# ── 발행 ──────────────────────────────────────────────────────
Write-Step "발행 → $OutputDir"
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item $OutputDir -ItemType Directory | Out-Null

dotnet publish "$PSScriptRoot\..\src\Aes67Vcs.UI\Aes67Vcs.UI.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained false `
    -o $OutputDir `
    --verbosity minimal

if ($LASTEXITCODE -ne 0) { Write-Fail "발행 실패"; exit 1 }
Write-OK "발행 완료: $OutputDir"

Write-Host "`n빌드 성공! 실행 파일: $OutputDir\Aes67Vcs.exe" -ForegroundColor Green
