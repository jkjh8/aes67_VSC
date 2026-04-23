# AES67 VCS

Windows 11용 AES67 가상 사운드카드.  
Scream 가상 오디오 드라이버를 통해 WASAPI Loopback으로 오디오를 캡처하고,  
IEEE 1588v2(PTP) 타임스탬프와 함께 AES67 RTP 멀티캐스트로 송출합니다.

---

## 구조

```
Aes67Vcs.sln
├── src/
│   ├── Aes67Engine/          C++ DLL — WASAPI 캡처 + AVX2 L24 변환 + RTP/UDP 송신
│   ├── Aes67Vcs.Core/        C# 라이브러리 — P/Invoke, PTP 모니터, SAP, 설정
│   └── Aes67Vcs.UI/          C# WinForms — 트레이 UI
├── tools/scream/             Scream 4.0 드라이버 바이너리
├── installer/                Inno Setup 6 스크립트
└── scripts/                  빌드·설치 PowerShell 스크립트
```

---

## 새 PC에서 개발 환경 셋업

### 필수 도구

| 도구 | 버전 | 용도 |
|---|---|---|
| Git | 최신 | 소스 관리 |
| Visual Studio 2022 (또는 Build Tools) | 17.x | C++ DLL 빌드 (MSVC v143) |
| — C++ 워크로드 | | `Microsoft.VisualStudio.Workload.VCTools` |
| — Windows 11 SDK | 10.0.26100+ | |
| .NET 8 SDK | 8.x | C# UI/Core 빌드 |
| Inno Setup 6 | 6.x | 설치 패키지 빌드 (선택) |

### 1. 도구 설치 (winget)

관리자 PowerShell에서 한 줄씩 실행합니다.

```powershell
# Git
winget install --id Git.Git -e

# .NET 8 SDK
winget install --id Microsoft.DotNet.SDK.8 -e

# VS 2022 Build Tools — C++ 워크로드 포함
winget install --id Microsoft.VisualStudio.2022.BuildTools -e `
  --override "--quiet --wait --add Microsoft.VisualStudio.Workload.VCTools --add Microsoft.VisualStudio.Component.Windows11SDK.26100 --includeRecommended"

# Inno Setup 6 (선택)
winget install --id JRSoftware.InnoSetup -e
```

> **VS 2022 Community/Professional/Enterprise** 가 이미 설치되어 있다면  
> VS Installer → 수정 → **"C++를 사용한 데스크톱 개발"** 워크로드 체크 후 설치.

또는 아래 스크립트로 한 번에 처리:

```powershell
# 관리자 PowerShell
Set-ExecutionPolicy Bypass -Scope Process -Force
.\scripts\setup-dev.ps1
```

### 2. 리포지토리 클론

```powershell
git clone https://github.com/jkjh8/aes67_VSC.git
cd aes67_VSC
```

### 3. 빌드

#### 전체 빌드 (스크립트)

```powershell
.\scripts\build.ps1
```

- `build/native/Aes67Engine.dll` — C++ 엔진 DLL
- `build/output/Aes67Vcs.exe` — C# UI 실행 파일

#### 수동 빌드

**C++ DLL** (Visual Studio 또는 Developer PowerShell)

```powershell
# Developer PowerShell for VS 2022 에서
msbuild src\Aes67Engine\Aes67Engine.vcxproj /p:Configuration=Release /p:Platform=x64
```

또는 Visual Studio에서 `Aes67Vcs.sln` 열기 → `Aes67Engine` 프로젝트 → **빌드**.

**C# UI**

```powershell
dotnet publish src\Aes67Vcs.UI\Aes67Vcs.UI.csproj -c Release -r win-x64 --self-contained false -o build\output
```

#### 설치 패키지 빌드

```powershell
.\scripts\build-installer.ps1
# → build/installer/AES67Vcs_Setup_v1.0.0.exe
```

---

## 실행 방법

### 사전 조건

1. **Scream 4.0 드라이버** 설치  
   `tools/scream/Install/driver/x64/Scream.inf` → 우클릭 → 설치  
   (또는 앱 내 "드라이버 설치" 버튼 사용)

2. **PTP 마스터** (선택)  
   네트워크에 IEEE 1588v2 PTP 그랜드마스터가 있다면,  
   앱 UI의 **마스터 IP** 칸에 입력하면 W32TM 설정이 자동 적용됩니다.

3. **방화벽** (설치 패키지 사용 시 자동)  
   수동 설치 시 `installer/FirewallHelper.ps1 -Action Add` 실행.

### 실행

```
build\output\Aes67Vcs.exe
```

트레이 아이콘 → 더블클릭 → UI 설정 후 **시작**.

---

## 설정 항목

| 항목 | 기본값 | 설명 |
|---|---|---|
| 채널 | 2ch | 2ch 또는 8ch |
| 멀티캐스트 IP | `239.255.77.77` | AES67 RTP 목적지 |
| RTP 포트 | `4010` | UDP 포트 |
| PTP 마스터 IP | (없음) | W32TM 자동 설정 — 비우면 기존 설정 유지 |
| PTP 도메인 | `0` | IEEE 1588 도메인 번호 |

설정 파일: `%APPDATA%\Aes67Vcs\config.json`

---

## 기술 스택

- **C++ DLL** (`Aes67Engine.dll`)  
  WASAPI Loopback → AVX2 float32→L24 변환 → RTP 패킷 조립 → UDP 멀티캐스트  
  4ms ptime / `QueryPerformanceCounter` 타이밍 / `THREAD_PRIORITY_TIME_CRITICAL`

- **C# P/Invoke** (`Aes67EngineInterop.cs`)  
  `__stdcall` 규약 / `Aes67StatsCallback` 델리게이트 (GC-safe)

- **W32TM PTP 모니터** (`PtpMonitor.cs`)  
  `w32tm /query /status` 폴링 → 오프셋 파싱 → `Aes67Engine_SetPtpOffsetNs()` 전달

- **SAP/SDP 어나운서** (`SapAnnouncer.cs`)  
  RFC 2974 / 30초 주기 / 224.2.127.254:9875

---

## 개발 노트

- `src/Aes67Engine/` 는 **MSVC v143** 전용. `dotnet build` 로 빌드 불가 (vcxproj).  
  반드시 MSBuild 또는 Visual Studio로 빌드.
- `build/native/Aes67Engine.dll` 이 `Aes67Vcs.exe` 와 **같은 폴더**에 있어야 실행됩니다.
- Scream 레지스트리 경로: `HKLM\SYSTEM\CurrentControlSet\Services\Scream\Options`
