; ============================================================
; AES67 VCS Inno Setup 스크립트
; Inno Setup 6.x 필요
; ============================================================

#define AppName       "AES67 VCS"
#define AppVersion    "1.0.0"
#define AppPublisher  "AES67 VCS Project"
#define AppExeName    "Aes67Vcs.exe"
#define AppGuid       "{A3B2C1D0-E4F5-6789-ABCD-EF0123456789}"

[Setup]
AppId={{A3B2C1D0-E4F5-6789-ABCD-EF0123456789}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
OutputDir=..\build\installer
OutputBaseFilename=AES67Vcs_Setup_v{#AppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 20H1 (19041) / Windows 11 이상만 설치 허용
MinVersion=6.3
UninstallDisplayIcon={app}\{#AppExeName}
UsedUserAreasWarning=no
SetupIconFile=..\src\Aes67Vcs.UI\Resources\aes67vcs.ico
LicenseFile=
; 설치 완료 후 앱 바로 시작
; DisableFinishedPage=no

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
korean.BeveledLabel=AES67 VCS
english.BeveledLabel=AES67 VCS

[Tasks]
Name: "startup";    Description: "Windows 시작 시 자동 실행"; GroupDescription: "추가 작업:"; Flags: unchecked
Name: "desktopicon"; Description: "바탕화면 바로가기 생성";    GroupDescription: "추가 작업:"

[Files]
; ── 앱 바이너리 ────────────────────────────────────────────
Source: "..\build\output\{#AppExeName}";             DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\output\Aes67Vcs.dll";              DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\output\Aes67Vcs.Core.dll";         DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\output\Aes67Vcs.deps.json";        DestDir: "{app}"; Flags: ignoreversion
Source: "..\build\output\Aes67Vcs.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion

; ── Scream 드라이버 (x64) ──────────────────────────────────
Source: "..\tools\scream\Install\driver\x64\Scream.sys"; DestDir: "{app}\driver\x64"; Flags: ignoreversion
Source: "..\tools\scream\Install\driver\x64\Scream.inf"; DestDir: "{app}\driver\x64"; Flags: ignoreversion
Source: "..\tools\scream\Install\driver\x64\scream.cat"; DestDir: "{app}\driver\x64"; Flags: ignoreversion

; ── devcon (드라이버 제거에 사용) ─────────────────────────
Source: "..\tools\scream\Install\helpers\devcon-x64.exe"; DestDir: "{app}\helpers"; Flags: ignoreversion

; ── 설치/제거 헬퍼 스크립트 ───────────────────────────────
Source: "FirewallHelper.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{userappdata}\Aes67Vcs"

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\{#AppName} 제거";      Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}";     Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; ── 시작 프로그램 ──────────────────────────────────────────
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "Aes67Vcs"; \
  ValueData: """{app}\{#AppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startup

; ── Scream 네트워크 기본 설정 (설치 후 앱에서 변경 가능) ──
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Services\Scream\Options"; \
  ValueType: string; ValueName: "UnicastIPv4"; \
  ValueData: "239.255.77.77"; \
  Flags: createvalueifdoesntexist uninsdeletekey
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Services\Scream\Options"; \
  ValueType: dword;  ValueName: "UnicastPort"; \
  ValueData: "4010"; \
  Flags: createvalueifdoesntexist
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Services\Scream\Options"; \
  ValueType: dword;  ValueName: "TTL"; \
  ValueData: "32"; \
  Flags: createvalueifdoesntexist

[Run]
; ── 1. Scream 드라이버 설치 ───────────────────────────────
Filename: "pnputil.exe"; \
  Parameters: "/add-driver ""{app}\driver\x64\Scream.inf"" /install"; \
  WorkingDir: "{app}"; \
  StatusMsg: "Scream 드라이버 설치 중..."; \
  Flags: runhidden waituntilterminated

; ── 2. 방화벽 규칙 추가 ───────────────────────────────────
Filename: "powershell.exe"; \
  Parameters: "-ExecutionPolicy Bypass -File ""{app}\FirewallHelper.ps1"" -Action Add"; \
  StatusMsg: "방화벽 규칙 구성 중..."; \
  Flags: runhidden waituntilterminated

; ── 3. 설치 완료 후 앱 실행 (선택) ───────────────────────
Filename: "{app}\{#AppExeName}"; \
  Description: "{#AppName} 지금 실행"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; ── 앱 프로세스 종료 ──────────────────────────────────────
Filename: "taskkill.exe"; \
  Parameters: "/f /im {#AppExeName}"; \
  RunOnceId: "KillApp"; \
  Flags: runhidden waituntilterminated

; ── 방화벽 규칙 제거 ─────────────────────────────────────
Filename: "powershell.exe"; \
  Parameters: "-ExecutionPolicy Bypass -File ""{app}\FirewallHelper.ps1"" -Action Remove"; \
  RunOnceId: "RemoveFirewall"; \
  Flags: runhidden waituntilterminated

; ── Scream 드라이버 제거 ──────────────────────────────────
Filename: "pnputil.exe"; \
  Parameters: "/remove-device ""ROOT\MEDIA\0000"""; \
  RunOnceId: "RemoveScream"; \
  Flags: runhidden waituntilterminated

[Code]
// ── .NET 8 Desktop Runtime 체크 ────────────────────────────
function IsDotNet8Installed(): Boolean;
var
  KeyPath: String;
begin
  KeyPath := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  Result := RegKeyExists(HKLM, KeyPath);
  // 간단하게 레지스트리 대신 파일 존재 여부로도 확인
  if not Result then
    Result := FileExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.0\wpf\PresentationCore.dll'))
           or DirExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.11'))
           or DirExists(ExpandConstant('{pf}\dotnet\shared\Microsoft.WindowsDesktop.App\8.0.17'));
end;

procedure InitializeWizard;
begin
  // 첫 페이지에 .NET 상태 표시 (선택적)
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not IsDotNet8Installed() then
    Result := '.NET 8 Desktop Runtime이 필요합니다.' + #13#10 +
              'https://dotnet.microsoft.com/download/dotnet/8.0 에서 ' +
              '"Windows Desktop Runtime" 을 설치한 후 다시 실행하세요.';
end;

// 이미 실행 중인 앱 종료
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
