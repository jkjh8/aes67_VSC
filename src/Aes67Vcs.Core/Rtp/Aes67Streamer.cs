using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Aes67Vcs.Core.Models;

namespace Aes67Vcs.Core.Rtp;

/// <summary>
/// AES67 RTP 스트리머.
///
/// 흐름:
///   Scream 가상 디바이스 (WASAPI Loopback)
///     → 링버퍼 (스레드 분리)
///       → 4ms 청크 단위 L24 변환 + RTP 헤더
///         → UDP 멀티캐스트 송신
///
/// 성능 최적화:
///   - MMCSS "Pro Audio" 스레드 우선순위
///   - timeBeginPeriod(1) 타이머 해상도
///   - ArrayPool 로 GC 압박 제거
///   - 핫패스 무할당 설계
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class Aes67Streamer : IDisposable
{
    // ── P/Invoke ─────────────────────────────────────────────
    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint AvSetMmThreadCharacteristics(string TaskName, ref uint TaskIndex);
    [DllImport("avrt.dll", SetLastError = true)]
    private static extern bool AvRevertMmThreadCharacteristics(nint handle);
    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint u);
    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint u);

    // ── 상수 ─────────────────────────────────────────────────
    private const int SampleRate      = 48_000;
    private const int Channels2       = 2;
    private const int Channels8       = 8;
    private const int PtimeMs         = 4;           // 4ms ptime (AES67 호환)
    private const int SamplesPerPkt   = SampleRate * PtimeMs / 1000; // 192
    private const byte PayloadType    = 96;

    // 링버퍼 크기: 100ms 분량 (지터 흡수용)
    private const int RingBufferMs    = 100;

    // ── 멤버 ─────────────────────────────────────────────────
    private readonly Aes67Config       _cfg;
    private WasapiCapture?             _capture;
    private UdpClient?                 _udp;
    private Thread?                    _sendThread;
    private CancellationTokenSource?   _cts;

    // 링버퍼: WASAPI 콜백 → 송신 스레드
    private byte[]  _ring = Array.Empty<byte>();
    private int     _ringWrite;
    private int     _ringRead;
    private int     _ringFill;          // 현재 채워진 바이트 수
    private readonly object _ringLock = new();

    // 오디오 포맷 변환기 (WASAPI → L24)
    private L24Converter.ConvertFunc? _converter;
    private int    _bytesPerSample;     // WASAPI 소스 바이트/샘플
    private int    _channels;

    // 상태
    public bool    IsRunning => _cts != null && !_cts.IsCancellationRequested;
    public event EventHandler<StreamerStats>? StatsUpdated;

    // PTP 연동
    private long   _ptpOffsetNs;
    public void SetPtpOffset(long offsetNs) => _ptpOffsetNs = offsetNs;

    public Aes67Streamer(Aes67Config cfg) => _cfg = cfg;

    // ── 시작 ─────────────────────────────────────────────────

    public void Start()
    {
        if (IsRunning) return;

        _channels = (int)_cfg.Channels;
        int l24BytesPerPkt = SamplesPerPkt * _channels * 3;
        int ringBytes = SampleRate * RingBufferMs / 1000 * _channels * 4; // float32 기준
        _ring      = new byte[ringBytes];
        _ringWrite = _ringRead = _ringFill = 0;

        // Scream 디바이스 찾기
        var device = FindScreamDevice()
            ?? throw new InvalidOperationException(
                "Scream 오디오 디바이스를 찾을 수 없습니다. 드라이버가 설치되어 있는지 확인하세요.");

        // WASAPI Loopback 캡처 설정
        _capture = new WasapiLoopbackCapture(device);
        SetupConverter(_capture.WaveFormat);

        _capture.DataAvailable  += OnDataAvailable;
        _capture.RecordingStopped += (_, _) => { };

        // UDP 소켓
        _udp = new UdpClient();
        _udp.Client.SetSocketOption(SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress, true);
        _udp.MulticastLoopback = false;
        _udp.Ttl = 32;

        _cts = new CancellationTokenSource();

        // 타이머 해상도 1ms
        timeBeginPeriod(1);

        // 캡처 시작
        _capture.StartRecording();

        // 송신 스레드 (MMCSS 적용)
        _sendThread = new Thread(() => SendLoop(l24BytesPerPkt))
        {
            Name         = "AES67-Send",
            IsBackground = true,
            Priority     = ThreadPriority.Highest,
        };
        _sendThread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        _sendThread?.Join(2000);
        _udp?.Close();
        _udp = null;
        timeEndPeriod(1);
        _cts?.Dispose();
        _cts = null;
    }

    // ── WASAPI 콜백 → 링버퍼 ────────────────────────────────

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0) return;
        lock (_ringLock)
        {
            int avail = _ring.Length - _ringFill;
            int bytes = Math.Min(e.BytesRecorded, avail);
            if (bytes <= 0) return; // 오버플로우 보호

            int end = (_ringWrite + bytes);
            if (end <= _ring.Length)
            {
                e.Buffer.AsSpan(0, bytes).CopyTo(_ring.AsSpan(_ringWrite));
            }
            else
            {
                int first = _ring.Length - _ringWrite;
                e.Buffer.AsSpan(0, first).CopyTo(_ring.AsSpan(_ringWrite));
                e.Buffer.AsSpan(first, bytes - first).CopyTo(_ring);
            }
            _ringWrite = (_ringWrite + bytes) % _ring.Length;
            _ringFill += bytes;
        }
    }

    // ── 송신 루프 ────────────────────────────────────────────

    private void SendLoop(int l24BytesPerPkt)
    {
        // MMCSS Pro Audio 등록
        uint taskIndex = 0;
        nint mmHandle  = AvSetMmThreadCharacteristics("Pro Audio", ref taskIndex);

        var rtp     = new RtpPacketBuilder(PayloadType,
                          GenerateSsrc(), (uint)SamplesPerPkt);
        var ep      = new IPEndPoint(
                          IPAddress.Parse(_cfg.MulticastAddress), _cfg.RtpPort);

        // 패킷 버퍼: 헤더 + L24 페이로드 (ArrayPool)
        int pktSize   = RtpPacketBuilder.HeaderSize + l24BytesPerPkt;
        // 소스 청크 크기 (WASAPI bytes/sample × samples × channels)
        int srcChunk  = SamplesPerPkt * _channels * _bytesPerSample;

        byte[] pktBuf = ArrayPool<byte>.Shared.Rent(pktSize);
        byte[] srcBuf = ArrayPool<byte>.Shared.Rent(srcChunk);

        bool firstPacket = true;
        long nextSendTick = Environment.TickCount64;
        int  statPackets  = 0;
        long statTimer    = Environment.TickCount64;

        try
        {
            while (!_cts!.IsCancellationRequested)
            {
                // 4ms 간격 대기
                long now = Environment.TickCount64;
                long wait = nextSendTick - now;
                if (wait > 0) Thread.Sleep((int)wait);
                nextSendTick += PtimeMs;

                // 링버퍼에서 한 패킷 분량 읽기
                if (!TryReadRing(srcBuf, srcChunk)) continue;

                // L24 변환
                var l24Span = pktBuf.AsSpan(
                    RtpPacketBuilder.HeaderSize, l24BytesPerPkt);
                _converter!(srcBuf.AsSpan(0, srcChunk), l24Span,
                    SamplesPerPkt * _channels);

                // PTP 타임스탬프 동기화 (첫 패킷 또는 주기적)
                if (firstPacket)
                {
                    rtp.SyncToPtp(GetPtpTimeNs(), SampleRate);
                    firstPacket = false;
                }

                // RTP 헤더 쓰기
                rtp.WriteHeader(pktBuf.AsSpan(0, RtpPacketBuilder.HeaderSize),
                    marker: firstPacket);
                rtp.AdvanceTimestamp();

                // UDP 송신
                try
                {
                    _udp?.Send(pktBuf, pktSize, ep);
                    statPackets++;
                }
                catch (SocketException) { /* 일시적 네트워크 오류 무시 */ }

                // 1초마다 통계 이벤트
                if (Environment.TickCount64 - statTimer >= 1000)
                {
                    StatsUpdated?.Invoke(this, new StreamerStats
                    {
                        PacketsPerSec = statPackets,
                        PtimeMs       = PtimeMs,
                        Channels      = _channels,
                        Destination   = $"{_cfg.MulticastAddress}:{_cfg.RtpPort}",
                    });
                    statPackets = 0;
                    statTimer   = Environment.TickCount64;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(pktBuf);
            ArrayPool<byte>.Shared.Return(srcBuf);
            if (mmHandle != nint.Zero)
                AvRevertMmThreadCharacteristics(mmHandle);
        }
    }

    // ── 링버퍼 읽기 ──────────────────────────────────────────

    private bool TryReadRing(byte[] dest, int count)
    {
        lock (_ringLock)
        {
            if (_ringFill < count) return false;

            int end = _ringRead + count;
            if (end <= _ring.Length)
            {
                _ring.AsSpan(_ringRead, count).CopyTo(dest);
            }
            else
            {
                int first = _ring.Length - _ringRead;
                _ring.AsSpan(_ringRead, first).CopyTo(dest);
                _ring.AsSpan(0, count - first).CopyTo(dest.AsSpan(first));
            }
            _ringRead = (_ringRead + count) % _ring.Length;
            _ringFill -= count;
            return true;
        }
    }

    // ── 유틸리티 ─────────────────────────────────────────────

    private static MMDevice? FindScreamDevice()
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (var dev in enumerator.EnumerateAudioEndPoints(
            DataFlow.Render, DeviceState.Active))
        {
            if (dev.FriendlyName.Contains("Scream", StringComparison.OrdinalIgnoreCase))
                return dev;
        }
        return null;
    }

    /// <summary>설치된 모든 WASAPI 렌더 디바이스 이름 목록</summary>
    public static List<string> GetRenderDeviceNames()
    {
        var list = new List<string>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var dev in enumerator.EnumerateAudioEndPoints(
            DataFlow.Render, DeviceState.Active))
            list.Add(dev.FriendlyName);
        return list;
    }

    private void SetupConverter(WaveFormat fmt)
    {
        _bytesPerSample = fmt.BitsPerSample / 8;
        bool isFloat    = fmt is WaveFormatExtensible wfe
                          && wfe.SubFormat == NAudio.MediaFoundation.AudioSubtypes.MFAudioFormat_Float
                       || fmt.Encoding == WaveFormatEncoding.IeeeFloat;
        _converter = L24Converter.GetConverter(fmt.BitsPerSample, isFloat);
    }

    private long GetPtpTimeNs()
    {
        long utcNs = (long)(DateTime.UtcNow
            .Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .TotalMilliseconds * 1_000_000L);
        return utcNs + _ptpOffsetNs;
    }

    private static uint GenerateSsrc()
    {
        // MAC 기반 SSRC (RFC 3550 §8)
        foreach (var nic in System.Net.NetworkInformation
            .NetworkInterface.GetAllNetworkInterfaces())
        {
            var mac = nic.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length >= 4 && mac.Any(b => b != 0))
                return BitConverter.ToUInt32(mac, 0);
        }
        return (uint)Random.Shared.Next();
    }

    public void Dispose() => Stop();
}

public class StreamerStats
{
    public int    PacketsPerSec { get; init; }
    public int    PtimeMs       { get; init; }
    public int    Channels      { get; init; }
    public string Destination   { get; init; } = "";
}
