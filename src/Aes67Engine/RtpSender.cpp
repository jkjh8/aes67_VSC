// ============================================================
// RtpSender.cpp
// ============================================================
#include "RtpSender.h"
#include <immintrin.h>   // AVX2
#include <intrin.h>      // __cpuid
#include <iphlpapi.h>    // GetAdaptersInfo
#include <cstring>
#include <cmath>
#include <algorithm>
#include <vector>

#pragma comment(lib, "Ws2_32.lib")

// ── 생성자/소멸자 ────────────────────────────────────────────

RtpSender::RtpSender()
{
    WSADATA wsa{};
    WSAStartup(MAKEWORD(2, 2), &wsa);

    // AVX2 런타임 감지
    int cpuInfo[4] = {};
    __cpuid(cpuInfo, 0);
    if (cpuInfo[0] >= 7) {
        __cpuidex(cpuInfo, 7, 0);
        m_avx2 = (cpuInfo[1] & (1 << 5)) != 0;
    }
}

RtpSender::~RtpSender() { Close(); WSACleanup(); }

// ── Open ─────────────────────────────────────────────────────

bool RtpSender::Open(const std::string& multicastIp, int port,
                     const std::string& srcIp,
                     uint8_t payloadType, int sampleRate, int channels)
{
    m_payloadType   = payloadType;
    m_sampleRate    = sampleRate;
    m_channels      = channels;
    m_ssrc          = GenerateSsrc();
    m_sequence      = (uint16_t)(rand() & 0xFFFF);
    m_samplesPerPkt = (uint32_t)(sampleRate * 4 / 1000); // 4ms = 192 samples

    // UDP 소켓 생성
    m_sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
    if (m_sock == INVALID_SOCKET) return false;

    // 소스 IP 바인드
    sockaddr_in src{};
    src.sin_family      = AF_INET;
    src.sin_port        = 0;
    if (!srcIp.empty())
        inet_pton(AF_INET, srcIp.c_str(), &src.sin_addr);
    else
        src.sin_addr.s_addr = INADDR_ANY;
    bind(m_sock, (sockaddr*)&src, sizeof(src));

    // 멀티캐스트 TTL
    DWORD ttl = 32;
    setsockopt(m_sock, IPPROTO_IP, IP_MULTICAST_TTL,
               (char*)&ttl, sizeof(ttl));

    // 멀티캐스트 루프백 끄기
    BOOL loop = FALSE;
    setsockopt(m_sock, IPPROTO_IP, IP_MULTICAST_LOOP,
               (char*)&loop, sizeof(loop));

    // 목적지 설정
    m_dest.sin_family = AF_INET;
    m_dest.sin_port   = htons((u_short)port);
    inet_pton(AF_INET, multicastIp.c_str(), &m_dest.sin_addr);

    m_statTick = GetTickCount();
    return true;
}

void RtpSender::Close()
{
    if (m_sock != INVALID_SOCKET) {
        closesocket(m_sock);
        m_sock = INVALID_SOCKET;
    }
}

// ── RTP 헤더 작성 (12바이트) ────────────────────────────────

void RtpSender::WriteRtpHeader(uint8_t* dst, bool marker)
{
    dst[0] = 0x80;                                         // V=2
    dst[1] = m_payloadType | (marker ? 0x80 : 0x00);      // M | PT
    dst[2] = (uint8_t)(m_sequence >> 8);
    dst[3] = (uint8_t)(m_sequence & 0xFF);
    m_sequence++;
    dst[4] = (uint8_t)(m_rtpTs >> 24);
    dst[5] = (uint8_t)(m_rtpTs >> 16);
    dst[6] = (uint8_t)(m_rtpTs >>  8);
    dst[7] = (uint8_t)(m_rtpTs      );
    dst[8] = (uint8_t)(m_ssrc >> 24);
    dst[9] = (uint8_t)(m_ssrc >> 16);
    dst[10]= (uint8_t)(m_ssrc >>  8);
    dst[11]= (uint8_t)(m_ssrc      );
    m_rtpTs += m_samplesPerPkt;
}

// ── L24 변환: AVX2 ───────────────────────────────────────────
// float32[-1,1] → 24bit signed big-endian
// AVX2: 8 float → 8×3byte, 처리 단위 8샘플

void RtpSender::ConvertL24Be_AVX2(
    const float* src, uint8_t* dst, int n)
{
    const __m256 scale  = _mm256_set1_ps(8388607.0f);
    const __m256 clip_p = _mm256_set1_ps( 1.0f);
    const __m256 clip_n = _mm256_set1_ps(-1.0f);

    int i = 0;
    for (; i <= n - 8; i += 8, dst += 24) {
        __m256 v = _mm256_loadu_ps(src + i);
        v = _mm256_min_ps(_mm256_max_ps(v, clip_n), clip_p);
        __m256i s = _mm256_cvtps_epi32(_mm256_mul_ps(v, scale));

        // 8개 int32 → 8×3byte big-endian (스칼라로 패킹)
        int32_t tmp[8];
        _mm256_storeu_si256((__m256i*)tmp, s);
        for (int j = 0; j < 8; j++) {
            int32_t val = tmp[j];
            dst[j*3]   = (uint8_t)(val >> 16);
            dst[j*3+1] = (uint8_t)(val >>  8);
            dst[j*3+2] = (uint8_t)(val      );
        }
    }
    // 나머지 (8 미만) 스칼라 처리
    for (; i < n; i++, dst += 3) {
        float f = std::clamp(src[i], -1.0f, 1.0f);
        int32_t val = (int32_t)(f * 8388607.0f);
        dst[0] = (uint8_t)(val >> 16);
        dst[1] = (uint8_t)(val >>  8);
        dst[2] = (uint8_t)(val      );
    }
}

void RtpSender::ConvertL24Be_Scalar(
    const float* src, uint8_t* dst, int n)
{
    for (int i = 0; i < n; i++, dst += 3) {
        float f = std::clamp(src[i], -1.0f, 1.0f);
        int32_t val = (int32_t)(f * 8388607.0f);
        dst[0] = (uint8_t)(val >> 16);
        dst[1] = (uint8_t)(val >>  8);
        dst[2] = (uint8_t)(val      );
    }
}

void RtpSender::ConvertL24Be(const float* src, uint8_t* dst, int n)
{
    if (m_avx2) ConvertL24Be_AVX2(src, dst, n);
    else        ConvertL24Be_Scalar(src, dst, n);
}

// ── 송신 ─────────────────────────────────────────────────────

int RtpSender::Send(const float* pcm, int frames, int64_t ptpTimeNs)
{
    if (m_sock == INVALID_SOCKET) return -1;

    // PTP 타임스탬프 동기화 (초기 또는 주기적 보정)
    // ptpTimeNs → RTP timestamp (단위: 샘플)
    if (ptpTimeNs > 0) {
        uint32_t ptpTs = (uint32_t)(ptpTimeNs /
            (1'000'000'000LL / m_sampleRate));
        // 큰 오차(>1패킷)가 있을 때만 보정
        int32_t diff = (int32_t)(ptpTs - m_rtpTs);
        if (diff < -(int32_t)m_samplesPerPkt ||
            diff >  (int32_t)m_samplesPerPkt * 2)
            m_rtpTs = ptpTs;
    }

    int sampleCount = frames * m_channels;
    int payloadBytes = sampleCount * 3;
    int pktLen = 12 + payloadBytes;
    if (pktLen > MAX_PKT) return -1;

    WriteRtpHeader(m_pktBuf, false);
    ConvertL24Be(pcm, m_pktBuf + 12, sampleCount);

    int sent = sendto(m_sock, (char*)m_pktBuf, pktLen, 0,
                      (sockaddr*)&m_dest, sizeof(m_dest));

    // ── 지터 측정 ──────────────────────────────────────────
    LARGE_INTEGER li;
    QueryPerformanceCounter(&li);
    int64_t now = li.QuadPart;
    if (m_lastSendTick > 0) {
        LARGE_INTEGER freq;
        QueryPerformanceFrequency(&freq);
        float intervalUs = (float)(now - m_lastSendTick) /
                           (float)freq.QuadPart * 1e6f;
        float expectedUs = (float)frames / m_sampleRate * 1e6f;
        float jitter = fabsf(intervalUs - expectedUs);
        m_jitterAccum += jitter;
        m_pktCount++;
    }
    m_lastSendTick = now;

    // 1초 통계 갱신
    DWORD tick = GetTickCount();
    if (tick - m_statTick >= 1000) {
        m_pktPerSec  = m_pktCount;
        m_avgJitter  = m_pktCount > 0 ? m_jitterAccum / m_pktCount : 0.f;
        m_pktCount   = 0;
        m_jitterAccum= 0.f;
        m_statTick   = tick;
    }

    return sent;
}

// ── 통계 ─────────────────────────────────────────────────────

int   RtpSender::GetPktsPerSec()   { return m_pktPerSec; }
float RtpSender::GetAvgJitterUs()  { return m_avgJitter; }

// ── SSRC 생성 ─────────────────────────────────────────────────

uint32_t RtpSender::GenerateSsrc()
{
    // MAC 기반 SSRC (RFC 3550)
    ULONG size = 0;
    GetAdaptersInfo(nullptr, &size);
    std::vector<uint8_t> buf(size);
    auto* info = reinterpret_cast<IP_ADAPTER_INFO*>(buf.data());
    if (GetAdaptersInfo(info, &size) == NO_ERROR && info) {
        uint32_t ssrc = 0;
        for (int i = 0; i < 4 && i < (int)info->AddressLength; i++)
            ssrc = (ssrc << 8) | info->Address[i];
        return ssrc ? ssrc : rand();
    }
    return (uint32_t)rand();
}
