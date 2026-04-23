#pragma once
// ============================================================
// Aes67Engine.h  — AES67 RTP 스트리머 C++ DLL 공개 API
// C# P/Invoke 호환: __stdcall, blittable 타입만 사용
// ============================================================
#include <stdint.h>
#include <stdbool.h>

#ifdef AES67ENGINE_EXPORTS
#  define AES67API __declspec(dllexport)
#else
#  define AES67API __declspec(dllimport)
#endif

extern "C" {

// ── 통계 콜백 ──────────────────────────────────────────────
typedef void (__stdcall *Aes67StatsCallback)(
    int     pkts_per_sec,
    float   avg_jitter_us,   // 패킷 간격 지터 (마이크로초)
    int     dropped_pkts,    // 링버퍼 오버플로우로 버린 패킷 수
    int     channels
);

// ── 초기화/시작/중지 ────────────────────────────────────────
// multicast_ip : "239.255.77.77"
// rtp_port     : 4010
// channels     : 2 or 8
// src_ip       : 소스 IP (NULL = 자동)
AES67API bool __stdcall Aes67Engine_Init(
    const char* multicast_ip,
    int         rtp_port,
    int         channels,
    const char* src_ip);

AES67API bool __stdcall Aes67Engine_Start();
AES67API void __stdcall Aes67Engine_Stop();
AES67API bool __stdcall Aes67Engine_IsRunning();

// ── PTP 오프셋 주입 (C# W32TM 모니터에서 호출) ───────────────
// offset_ns: 로컬 클록과 PTP 마스터의 오프셋(나노초)
AES67API void __stdcall Aes67Engine_SetPtpOffsetNs(int64_t offset_ns);

// ── 통계 콜백 등록 ──────────────────────────────────────────
AES67API void __stdcall Aes67Engine_SetStatsCallback(Aes67StatsCallback cb);

// ── 디바이스 목록 (UI용) ────────────────────────────────────
// buf: 디바이스 이름을 '\n'으로 구분해 채움
// return: 실제 길이
AES67API int __stdcall Aes67Engine_GetDeviceList(char* buf, int buf_len);

} // extern "C"
