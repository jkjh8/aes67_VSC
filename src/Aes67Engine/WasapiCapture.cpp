// ============================================================
// WasapiCapture.cpp
// ============================================================
#include "WasapiCapture.h"
#include <avrt.h>
#include <functiondiscoverykeys_devpkey.h>
#include <cstring>
#include <stdexcept>
#include <algorithm>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "mmdevapi.lib")
#pragma comment(lib, "avrt.lib")

// ── 생성자/소멸자 ────────────────────────────────────────────

WasapiCapture::WasapiCapture()
{
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr,
        CLSCTX_ALL, IID_PPV_ARGS(&m_enumerator));
}

WasapiCapture::~WasapiCapture()
{
    Close();
    if (m_enumerator) { m_enumerator->Release(); m_enumerator = nullptr; }
    CoUninitialize();
}

// ── 디바이스 열거 ────────────────────────────────────────────

std::vector<std::string> WasapiCapture::EnumerateDevices()
{
    std::vector<std::string> result;
    IMMDeviceEnumerator* enumerator = nullptr;
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr,
        CLSCTX_ALL, IID_PPV_ARGS(&enumerator));
    if (!enumerator) return result;

    IMMDeviceCollection* col = nullptr;
    enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &col);
    if (col) {
        UINT count = 0;
        col->GetCount(&count);
        for (UINT i = 0; i < count; i++) {
            IMMDevice* dev = nullptr;
            if (FAILED(col->Item(i, &dev))) continue;
            IPropertyStore* props = nullptr;
            dev->OpenPropertyStore(STGM_READ, &props);
            if (props) {
                PROPVARIANT pv;
                PropVariantInit(&pv);
                props->GetValue(PKEY_Device_FriendlyName, &pv);
                if (pv.vt == VT_LPWSTR) {
                    int len = WideCharToMultiByte(CP_UTF8, 0,
                        pv.pwszVal, -1, nullptr, 0, nullptr, nullptr);
                    std::string s(len - 1, 0);
                    WideCharToMultiByte(CP_UTF8, 0,
                        pv.pwszVal, -1, s.data(), len, nullptr, nullptr);
                    result.push_back(s);
                }
                PropVariantClear(&pv);
                props->Release();
            }
            dev->Release();
        }
        col->Release();
    }
    enumerator->Release();
    return result;
}

// ── Open ─────────────────────────────────────────────────────

bool WasapiCapture::Open(const std::string& preferredDevice)
{
    if (!m_enumerator) return false;

    // 렌더 엔드포인트 목록에서 Scream 찾기
    IMMDeviceCollection* col = nullptr;
    m_enumerator->EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, &col);
    if (!col) return false;

    UINT count = 0;
    col->GetCount(&count);
    for (UINT i = 0; i < count && !m_device; i++) {
        IMMDevice* dev = nullptr;
        if (FAILED(col->Item(i, &dev))) continue;
        IPropertyStore* props = nullptr;
        dev->OpenPropertyStore(STGM_READ, &props);
        if (props) {
            PROPVARIANT pv;
            PropVariantInit(&pv);
            props->GetValue(PKEY_Device_FriendlyName, &pv);
            if (pv.vt == VT_LPWSTR) {
                // 디바이스 이름에 "Scream" 포함 여부 확인
                std::wstring name(pv.pwszVal);
                std::wstring target(preferredDevice.begin(),
                                    preferredDevice.end());
                std::wstring nameLower = name;
                std::wstring targetLower = target;
                std::transform(nameLower.begin(), nameLower.end(),
                               nameLower.begin(), ::towlower);
                std::transform(targetLower.begin(), targetLower.end(),
                               targetLower.begin(), ::towlower);
                if (nameLower.find(targetLower) != std::wstring::npos) {
                    m_device = dev;
                    dev = nullptr;
                }
            }
            PropVariantClear(&pv);
            props->Release();
        }
        if (dev) dev->Release();
    }
    col->Release();

    // Scream 없으면 기본 렌더 디바이스 사용
    if (!m_device)
        m_enumerator->GetDefaultAudioEndpoint(eRender, eConsole, &m_device);
    if (!m_device) return false;

    // IAudioClient 활성화
    if (FAILED(m_device->Activate(__uuidof(IAudioClient), CLSCTX_ALL,
                                  nullptr, (void**)&m_audioClient)))
        return false;

    // 믹스 포맷 가져오기
    m_audioClient->GetMixFormat(&m_wfx);
    m_channels   = m_wfx->nChannels;
    m_sampleRate = m_wfx->nSamplesPerSec;

    // Loopback + 이벤트 구동 초기화
    // REFERENCE_TIME: 100ns 단위, 4ms = 40000
    REFERENCE_TIME bufDuration = 40000; // 4ms
    HRESULT hr = m_audioClient->Initialize(
        AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
        bufDuration, 0, m_wfx, nullptr);
    if (FAILED(hr)) return false;

    m_captureEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
    m_audioClient->SetEventHandle(m_captureEvent);

    m_audioClient->GetService(__uuidof(IAudioCaptureClient),
                              (void**)&m_captureClient);
    if (!m_captureClient) return false;

    m_stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    return true;
}

// ── Close ────────────────────────────────────────────────────

void WasapiCapture::Close()
{
    Stop();
    if (m_captureClient) { m_captureClient->Release(); m_captureClient = nullptr; }
    if (m_audioClient)   { m_audioClient->Release();   m_audioClient   = nullptr; }
    if (m_device)        { m_device->Release();        m_device        = nullptr; }
    if (m_wfx)           { CoTaskMemFree(m_wfx);       m_wfx           = nullptr; }
    if (m_captureEvent)  { CloseHandle(m_captureEvent);m_captureEvent  = nullptr; }
    if (m_stopEvent)     { CloseHandle(m_stopEvent);   m_stopEvent     = nullptr; }
}

// ── Start/Stop ───────────────────────────────────────────────

bool WasapiCapture::Start(CaptureCallback cb)
{
    if (!m_captureClient) return false;
    m_callback = cb;
    ResetEvent(m_stopEvent);
    if (FAILED(m_audioClient->Start())) return false;
    m_thread = CreateThread(nullptr, 0, CaptureThread, this, 0, nullptr);
    return m_thread != nullptr;
}

void WasapiCapture::Stop()
{
    if (m_stopEvent)   SetEvent(m_stopEvent);
    if (m_thread)      { WaitForSingleObject(m_thread, 3000); CloseHandle(m_thread); m_thread = nullptr; }
    if (m_audioClient) m_audioClient->Stop();
}

// ── 캡처 스레드 ──────────────────────────────────────────────

DWORD WINAPI WasapiCapture::CaptureThread(LPVOID param)
{
    auto* self = static_cast<WasapiCapture*>(param);
    self->RunCapture();
    return 0;
}

void WasapiCapture::RunCapture()
{
    // MMCSS Pro Audio 등록
    DWORD taskIdx = 0;
    HANDLE mmHandle = AvSetMmThreadCharacteristics(L"Pro Audio", &taskIdx);

    const HANDLE waitHandles[2] = { m_captureEvent, m_stopEvent };
    bool isFloat  = (m_wfx->wFormatTag == WAVE_FORMAT_IEEE_FLOAT) ||
                    (m_wfx->wFormatTag == WAVE_FORMAT_EXTENSIBLE &&
                     reinterpret_cast<WAVEFORMATEXTENSIBLE*>(m_wfx)
                         ->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT);
    int bps = m_wfx->wBitsPerSample;

    while (true) {
        DWORD wait = WaitForMultipleObjects(2, waitHandles, FALSE, 200);
        if (wait == WAIT_OBJECT_0 + 1) break; // stop 이벤트
        if (wait != WAIT_OBJECT_0)     continue;

        UINT32 packetSize = 0;
        while (SUCCEEDED(m_captureClient->GetNextPacketSize(&packetSize))
               && packetSize > 0)
        {
            BYTE*  pData   = nullptr;
            UINT32 frames  = 0;
            DWORD  flags   = 0;
            if (FAILED(m_captureClient->GetBuffer(
                    &pData, &frames, &flags, nullptr, nullptr)))
                break;

            // silence 플래그면 0으로 채움
            bool isSilence = (flags & AUDCLNT_BUFFERFLAGS_SILENT) != 0;

            // float32로 변환 (필요 시)
            int totalSamples = (int)(frames * m_channels);
            m_convertBuf.resize(totalSamples);

            if (isSilence) {
                memset(m_convertBuf.data(), 0, totalSamples * sizeof(float));
            } else if (isFloat) {
                memcpy(m_convertBuf.data(), pData, totalSamples * sizeof(float));
            } else if (bps == 16) {
                const int16_t* s16 = reinterpret_cast<const int16_t*>(pData);
                for (int i = 0; i < totalSamples; i++)
                    m_convertBuf[i] = s16[i] / 32768.0f;
            } else if (bps == 24) {
                // 24bit little-endian → float
                const uint8_t* p = pData;
                for (int i = 0; i < totalSamples; i++, p += 3) {
                    int32_t s = (int32_t)(p[0] | (p[1] << 8) | (p[2] << 16));
                    if (s & 0x800000) s |= 0xFF000000; // sign extend
                    m_convertBuf[i] = s / 8388608.0f;
                }
            } else if (bps == 32) {
                // 32bit int → float
                const int32_t* s32 = reinterpret_cast<const int32_t*>(pData);
                for (int i = 0; i < totalSamples; i++)
                    m_convertBuf[i] = s32[i] / 2147483648.0f;
            }

            m_captureClient->ReleaseBuffer(frames);

            if (m_callback)
                m_callback(m_convertBuf.data(), frames,
                           m_channels, m_sampleRate);
        }
    }

    if (mmHandle) AvRevertMmThreadCharacteristics(mmHandle);
}
