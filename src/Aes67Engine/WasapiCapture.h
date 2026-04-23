#pragma once
// ============================================================
// WasapiCapture.h — WASAPI Loopback 캡처
// Scream 가상 디바이스에서 오디오 스트림을 읽어
// 콜백으로 float32 PCM을 전달
// ============================================================
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <winsock2.h>   // winsock.h 보다 먼저 포함 필수
#include <windows.h>
#include <mmdeviceapi.h>
#include <audioclient.h>
#include <audiopolicy.h>
#include <functional>
#include <string>
#include <vector>

// WASAPI 콜백: (버퍼 포인터, 프레임 수, 채널 수, 샘플레이트)
using CaptureCallback = std::function<
    void(const float* data, uint32_t frames, int channels, int sampleRate)>;

class WasapiCapture {
public:
    WasapiCapture();
    ~WasapiCapture();

    // Scream 디바이스 찾아서 열기 (없으면 기본 렌더 디바이스)
    bool Open(const std::string& preferredDevice = "Scream");
    void Close();
    bool Start(CaptureCallback cb);
    void Stop();

    int  GetChannels()   const { return m_channels; }
    int  GetSampleRate() const { return m_sampleRate; }
    bool IsOpen()        const { return m_audioClient != nullptr; }

    // 디바이스 이름 목록
    static std::vector<std::string> EnumerateDevices();

private:
    static DWORD WINAPI CaptureThread(LPVOID param);
    void RunCapture();

    IMMDeviceEnumerator* m_enumerator = nullptr;
    IMMDevice*           m_device     = nullptr;
    IAudioClient*        m_audioClient = nullptr;
    IAudioCaptureClient* m_captureClient = nullptr;

    HANDLE   m_captureEvent = nullptr;
    HANDLE   m_stopEvent    = nullptr;
    HANDLE   m_thread       = nullptr;

    WAVEFORMATEX* m_wfx      = nullptr;
    int           m_channels = 0;
    int           m_sampleRate = 0;

    CaptureCallback m_callback;

    // float 변환 버퍼 (열당 재사용)
    std::vector<float> m_convertBuf;
};
