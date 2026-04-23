#Requires -RunAsAdministrator
param(
    [string]$InstallDir = "$env:ProgramFiles\Aes67Vcs"
)

$ErrorActionPreference = "Stop"

# 방화벽 규칙 제거
@("AES67-VCS-PTP-Event","AES67-VCS-PTP-General","AES67-VCS-RTP",
  "AES67-VCS-SAP","AES67-VCS-RTP-Out","AES67-VCS-SAP-Out") | ForEach-Object {
    Remove-NetFirewallRule -DisplayName $_ -ErrorAction SilentlyContinue
}

# 시작 프로그램 제거
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" `
    -Name "Aes67Vcs" -ErrorAction SilentlyContinue

# 앱 폴더 제거
if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }

Write-Host "AES67 VCS 제거 완료" -ForegroundColor Green
Write-Host "(Scream 드라이버는 별도로 제거하세요: pnputil /remove-device ROOT\MEDIA\0000)" -ForegroundColor Yellow
