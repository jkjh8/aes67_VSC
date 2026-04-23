#pragma once
// ============================================================
// RtpSender.h — AES67 RTP 패킷 조립 + UDP 멀티캐스트 송신
// ============================================================
#include <winsock2.h>
#include <ws2tcpip.h>
#include <stdint.h>
#include <string>
#include <atomic>

class RtpSender {
public:
    RtpSender();
    ~RtpSender();

    bool Open(const std::string& multicastIp, int port,
              const std::string& srcIp,
              uint8_t payloadType, int sampleRate, int channels);
    void Close();

    // float32 PCM → L24 big-endian 변환 후 RTP 헤더 붙여 UDP 송신
    // 반환: 송신 바이트 수 (실패 시 -1)
    int Send(const float* pcm, int frames, int64_t ptpTimeNs);

    // 통계
    int   GetPktsPerSec();
    float GetAvgJitterUs();
    int   GetDropped() const { return m_dropped.load(); }

    static uint32_t GenerateSsrc();

private:
    void WriteRtpHeader(uint8_t* dst, bool marker);
    void ConvertL24Be(const float* src, uint8_t* dst, int sampleCount);
    void ConvertL24Be_AVX2(const float* src, uint8_t* dst, int sampleCount);
    void ConvertL24Be_Scalar(const float* src, uint8_t* dst, int sampleCount);

    SOCKET   m_sock       = INVALID_SOCKET;
    sockaddr_in m_dest    = {};

    uint8_t  m_payloadType = 96;
    int      m_sampleRate  = 48000;
    int      m_channels    = 2;
    uint32_t m_ssrc        = 0;
    uint16_t m_sequence    = 0;
    uint32_t m_rtpTs       = 0;
    uint32_t m_samplesPerPkt = 0;

    bool     m_avx2 = false;  // 런타임 AVX2 지원 여부

    // 송신 버퍼 (최대 8ch × 192샘플 × 3byte + 12 header = 4632+12)
    static constexpr int MAX_PKT = 4644;
    uint8_t  m_pktBuf[MAX_PKT] = {};

    // 통계
    std::atomic<int>   m_dropped{0};
    int   m_pktCount    = 0;
    int   m_pktPerSec   = 0;
    float m_jitterAccum = 0.f;
    float m_avgJitter   = 0.f;
    DWORD m_statTick    = 0;
    int64_t m_lastSendTick = 0;
};
