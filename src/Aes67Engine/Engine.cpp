// ============================================================
// Engine.cpp — DLL 진입점 + 공개 C API 구현
// ============================================================
// winsock2.h 는 반드시 windows.h 보다 먼저 포함되어야 함
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>
#include <ws2tcpip.h>

#include "Aes67Engine.h"
#include "WasapiCapture.h"
#include "RtpSender.h"

#include <windows.h>
#include <avrt.h>       // AvSetMmThreadCharacteristics
#include <mmsystem.h>
#include <atomic>
#include <mutex>
#include <vector>
#include <string>
#include <cstring>

#pragma comment(lib, "winmm.lib")
#pragma comment(lib, "iphlpapi.lib")

// ── 전역 엔진 상태 ────────────────────────────────────────────

namespace {
    WasapiCapture       g_capture;
    RtpSender           g_sender;
    Aes67StatsCallback  g_statsCallback = nullptr;
    std::atomic<bool>   g_running{false};
    std::atomic<int64_t>g_ptpOffsetNs{0};

    // 링버퍼: WASAPI 콜백 → 송신
    static constexpr int RING_FRAMES = 48000 / 5; // 200ms
    static constexpr int MAX_CH      = 8;
    float   g_ring[RING_FRAMES * MAX_CH] = {};
    int     g_ringCh      = 2;
    int     g_ringRate    = 48000;
    int     g_ringWrite   = 0;
    int     g_ringRead    = 0;
    int     g_ringFill    = 0;     // 단위: 프레임
    std::mutex g_ringMtx;

    // 송신 스레드
    HANDLE  g_sendThread  = nullptr;
    HANDLE  g_stopEvent   = nullptr;

    std::string g_multicastIp;
    int         g_rtpPort   = 4010;
    int         g_channels  = 2;
    std::string g_srcIp;

    int g_targetCh = 2;            // 목표 채널 수 (다운믹스/업믹스용)
}

// ── 링버퍼 ───────────────────────────────────────────────────

static void RingWrite(const float* data, int frames, int srcCh)
{
    std::lock_guard<std::mutex> lk(g_ringMtx);
    int avail = RING_FRAMES - g_ringFill;
    int write = std::min(frames, avail);

    for (int f = 0; f < write; f++) {
        int dst = ((g_ringWrite + f) % RING_FRAMES) * g_targetCh;
        // 채널 수 변환 (다운믹스/업믹스/패스스루)
        if (srcCh == g_targetCh) {
            memcpy(&g_ring[dst], &data[f * srcCh],
                   g_targetCh * sizeof(float));
        } else {
            // 간단한 채널 매핑
            for (int c = 0; c < g_targetCh; c++)
                g_ring[dst + c] = (c < srcCh)
                    ? data[f * srcCh + c]
                    : data[f * srcCh + (srcCh - 1)];
        }
    }
    g_ringWrite = (g_ringWrite + write) % RING_FRAMES;
    g_ringFill += write;
}

static bool RingRead(float* dst, int frames)
{
    std::lock_guard<std::mutex> lk(g_ringMtx);
    if (g_ringFill < frames) return false;
    for (int f = 0; f < frames; f++) {
        int src = ((g_ringRead + f) % RING_FRAMES) * g_targetCh;
        memcpy(&dst[f * g_targetCh], &g_ring[src],
               g_targetCh * sizeof(float));
    }
    g_ringRead = (g_ringRead + frames) % RING_FRAMES;
    g_ringFill -= frames;
    return true;
}

// ── 송신 스레드 ──────────────────────────────────────────────

static DWORD WINAPI SendThread(LPVOID)
{
    // MMCSS Pro Audio 등록
    DWORD taskIdx = 0;
    HANDLE mmHnd = AvSetMmThreadCharacteristics(L"Pro Audio", &taskIdx);

    // 타이머 해상도 1ms
    timeBeginPeriod(1);

    const int sampleRate    = g_ringRate;
    const int ptimeMs       = 4;
    const int framesPerPkt  = sampleRate * ptimeMs / 1000; // 192
    const int maxCh         = g_targetCh;

    std::vector<float> frameBuf(framesPerPkt * maxCh);
    LONGLONG nextTick = 0;

    // 고해상도 타이머 기반 간격
    LARGE_INTEGER freq, now;
    QueryPerformanceFrequency(&freq);
    QueryPerformanceCounter(&now);
    nextTick = now.QuadPart + freq.QuadPart * ptimeMs / 1000;

    while (WaitForSingleObject(g_stopEvent, 0) != WAIT_OBJECT_0)
    {
        // 정확한 4ms 대기
        QueryPerformanceCounter(&now);
        LONGLONG waitNs = (nextTick - now.QuadPart)
                          * 1'000'000'000LL / freq.QuadPart;
        if (waitNs > 500'000LL)  // 0.5ms 이상이면 Sleep
            Sleep((DWORD)(waitNs / 1'000'000LL));
        nextTick += freq.QuadPart * ptimeMs / 1000;

        if (!RingRead(frameBuf.data(), framesPerPkt)) continue;

        // PTP 오프셋 적용한 현재 시각
        FILETIME ft;
        GetSystemTimePreciseAsFileTime(&ft);
        int64_t fileTime  = ((int64_t)ft.dwHighDateTime << 32) | ft.dwLowDateTime;
        int64_t unixNs    = (fileTime - 116444736000000000LL) * 100LL;
        int64_t ptpNs     = unixNs + g_ptpOffsetNs.load();

        g_sender.Send(frameBuf.data(), framesPerPkt, ptpNs);

        // 통계 콜백
        if (g_statsCallback) {
            g_statsCallback(
                g_sender.GetPktsPerSec(),
                g_sender.GetAvgJitterUs(),
                g_sender.GetDropped(),
                g_targetCh);
        }
    }

    timeEndPeriod(1);
    if (mmHnd) AvRevertMmThreadCharacteristics(mmHnd);
    return 0;
}

// ── C API 구현 ────────────────────────────────────────────────

extern "C" {

bool __stdcall Aes67Engine_Init(
    const char* multicast_ip,
    int rtp_port, int channels, const char* src_ip)
{
    if (g_running) return false;

    g_multicastIp = multicast_ip ? multicast_ip : "239.255.77.77";
    g_rtpPort     = rtp_port;
    g_targetCh    = (channels == 8) ? 8 : 2;
    g_srcIp       = src_ip ? src_ip : "";
    return true;
}

bool __stdcall Aes67Engine_Start()
{
    if (g_running) return false;

    // WASAPI 캡처 열기
    if (!g_capture.Open("Scream")) return false;

    g_ringCh   = g_capture.GetChannels();
    g_ringRate = g_capture.GetSampleRate();

    // RTP 송신 초기화
    if (!g_sender.Open(g_multicastIp, g_rtpPort, g_srcIp,
                       96, g_ringRate, g_targetCh))
        return false;

    g_stopEvent  = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    g_sendThread = CreateThread(nullptr, 0, SendThread, nullptr, 0, nullptr);
    SetThreadPriority(g_sendThread, THREAD_PRIORITY_TIME_CRITICAL);

    // WASAPI 캡처 콜백 → 링버퍼
    g_capture.Start([](const float* data, uint32_t frames,
                        int ch, int /*sr*/) {
        RingWrite(data, (int)frames, ch);
    });

    g_running = true;
    return true;
}

void __stdcall Aes67Engine_Stop()
{
    if (!g_running) return;
    g_running = false;
    g_capture.Stop();
    if (g_stopEvent)  { SetEvent(g_stopEvent); }
    if (g_sendThread) {
        WaitForSingleObject(g_sendThread, 3000);
        CloseHandle(g_sendThread);
        g_sendThread = nullptr;
    }
    if (g_stopEvent) { CloseHandle(g_stopEvent); g_stopEvent = nullptr; }
    g_capture.Close();
    g_sender.Close();
}

bool __stdcall Aes67Engine_IsRunning()
{
    return g_running.load();
}

void __stdcall Aes67Engine_SetPtpOffsetNs(int64_t offset_ns)
{
    g_ptpOffsetNs.store(offset_ns);
}

void __stdcall Aes67Engine_SetStatsCallback(Aes67StatsCallback cb)
{
    g_statsCallback = cb;
}

int __stdcall Aes67Engine_GetDeviceList(char* buf, int buf_len)
{
    auto devs = WasapiCapture::EnumerateDevices();
    std::string joined;
    for (auto& d : devs) { joined += d; joined += '\n'; }
    int copy = std::min((int)joined.size(), buf_len - 1);
    if (buf && buf_len > 0) {
        memcpy(buf, joined.data(), copy);
        buf[copy] = '\0';
    }
    return (int)joined.size();
}

} // extern "C"

// DLL 진입점
BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_DETACH && g_running)
        Aes67Engine_Stop();
    return TRUE;
}
