using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Aes67Vcs.Core.Models;

namespace Aes67Vcs.Core.Ptp;

/// <summary>
/// IEEE 1588v2 PTP Slave-only 클라이언트.
/// End-to-End delay 측정 방식.
/// Windows 11 에서 유저스페이스 UDP 소켓으로 동작.
/// </summary>
public sealed class PtpClient : IDisposable
{
    // PTPv2 이벤트 포트 319, 일반 포트 320
    private const int EventPort   = 319;
    private const int GeneralPort = 320;

    private readonly Aes67Config _cfg;
    private UdpClient?  _eventSock;
    private UdpClient?  _generalSock;
    private CancellationTokenSource? _cts;
    private Task?       _recvTask;

    // 동기화 상태
    private PtpState _state = PtpState.Disabled;
    private string   _masterId = "";

    // 타임스탬프 버퍼 (Sync 수신 → FollowUp 대기)
    private long     _t1Ns;          // 마스터 Sync 오리진 타임스탬프 (FollowUp에서 수신)
    private long     _t2Ns;          // 로컬 Sync 수신 시각
    private ushort   _syncSeqId;

    // Delay 측정
    private long     _t3Ns;          // DelayReq 송신 시각
    private long     _t4Ns;          // 마스터가 DelayReq 수신한 시각 (DelayResp)
    private ushort   _delaySeqId;

    private long     _offsetNs;
    private long     _meanPathDelayNs;

    // 저역통과 필터 계수
    private const double Alpha = 0.125;

    public event EventHandler<PtpStatus>? StatusChanged;

    public PtpClient(Aes67Config config) => _cfg = config;

    // ── 시작/중지 ────────────────────────────────────────────

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _eventSock   = CreateMulticastSocket(EventPort,   _cfg.PtpMulticastAddress, _cfg.LocalInterface);
        _generalSock = CreateMulticastSocket(GeneralPort, _cfg.PtpMulticastAddress, _cfg.LocalInterface);

        UpdateState(PtpState.Listening);
        _recvTask = Task.WhenAll(
            ReceiveLoopAsync(_eventSock,   _cts.Token),
            ReceiveLoopAsync(_generalSock, _cts.Token)
        );
    }

    public void Stop()
    {
        _cts?.Cancel();
        _eventSock?.Close();
        _generalSock?.Close();
        _recvTask?.Wait(2000);
        _cts?.Dispose();
        _cts = null;
        UpdateState(PtpState.Disabled);
    }

    // ── 수신 루프 ────────────────────────────────────────────

    private async Task ReceiveLoopAsync(UdpClient sock, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await sock.ReceiveAsync(ct);
                ProcessPacket(result.Buffer, result.RemoteEndPoint);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) { }
    }

    private void ProcessPacket(byte[] data, IPEndPoint remote)
    {
        if (data.Length < PtpHeader.HeaderSize) return;

        PtpHeader hdr;
        try { hdr = PtpHeader.Parse(data); }
        catch { return; }

        // 도메인 필터
        if (hdr.DomainNumber != _cfg.PtpDomain) return;

        var payload = data.AsSpan(PtpHeader.HeaderSize);

        switch (hdr.MessageType)
        {
            case PtpMessageType.Sync:
                HandleSync(hdr, payload);
                break;
            case PtpMessageType.FollowUp:
                HandleFollowUp(hdr, payload);
                break;
            case PtpMessageType.DelayResp:
                HandleDelayResp(hdr, payload);
                break;
            case PtpMessageType.Announce:
                HandleAnnounce(hdr, payload);
                break;
        }
    }

    // ── PTP 메시지 핸들러 ────────────────────────────────────

    private void HandleSync(PtpHeader hdr, ReadOnlySpan<byte> payload)
    {
        _t2Ns     = GetLocalTimeNs();
        _syncSeqId = hdr.SequenceId;

        bool twoStep = (hdr.FlagField & 0x0200) != 0;
        if (!twoStep && payload.Length >= 10)
        {
            // one-step: OriginTimestamp 바로 포함
            _t1Ns = new PtpSyncMessage(hdr, payload).OriginTimestamp.ToNanoseconds()
                    + hdr.CorrectionNanoseconds;
            ComputeOffset();
            SendDelayReq();
        }
        // two-step: FollowUp 대기
    }

    private void HandleFollowUp(PtpHeader hdr, ReadOnlySpan<byte> payload)
    {
        if (hdr.SequenceId != _syncSeqId || payload.Length < 10) return;
        _t1Ns = PtpTimestamp.Parse(payload).ToNanoseconds() + hdr.CorrectionNanoseconds;
        ComputeOffset();
        SendDelayReq();
    }

    private void HandleDelayResp(PtpHeader hdr, ReadOnlySpan<byte> payload)
    {
        if (hdr.SequenceId != _delaySeqId || payload.Length < 20) return;

        var msg = new PtpDelayRespMessage(hdr, payload);
        if (!OurPortId(msg.RequestingPortIdentity)) return;

        _t4Ns = msg.ReceiveTimestamp.ToNanoseconds() + hdr.CorrectionNanoseconds;

        // Mean Path Delay = ((t2-t1) + (t4-t3)) / 2
        long raw = ((_t2Ns - _t1Ns) + (_t4Ns - _t3Ns)) / 2;
        _meanPathDelayNs = (long)(_meanPathDelayNs * (1 - Alpha) + raw * Alpha);

        UpdateState(PtpState.Locked);
        PublishStatus();
    }

    private void HandleAnnounce(PtpHeader hdr, ReadOnlySpan<byte> payload)
    {
        // 마스터 클록 ID 갱신
        string id = BitConverter.ToString(hdr.SourcePortIdentity, 0, 8).Replace("-", "");
        if (_masterId != id)
        {
            _masterId = id;
            UpdateState(PtpState.Syncing);
        }
    }

    // ── DelayReq 송신 ────────────────────────────────────────

    private ushort _delayReqSeq;

    private void SendDelayReq()
    {
        if (_eventSock == null) return;
        try
        {
            _delaySeqId = _delayReqSeq++;
            var pkt = BuildDelayReqPacket(_delaySeqId);
            _t3Ns = GetLocalTimeNs();
            var ep = new IPEndPoint(IPAddress.Parse(_cfg.PtpMulticastAddress), EventPort);
            _eventSock.Send(pkt, pkt.Length, ep);
        }
        catch { /* 전송 실패 무시 */ }
    }

    private byte[] BuildDelayReqPacket(ushort seqId)
    {
        // 최소 44바이트 헤더 + 10바이트 timestamp
        var buf = new byte[44];
        buf[0] = 0x11; // messageType=DelayReq(1) | transportSpec
        buf[1] = 0x02; // versionPTP = 2
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(2), 44);
        buf[4] = (byte)_cfg.PtpDomain;
        // sourcePortIdentity: MAC + 0xFF + 0xFF + portNum
        GetLocalClockId().CopyTo(buf, 20);
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(28), 1); // portNum
        BinaryPrimitives.WriteUInt16BigEndian(buf.AsSpan(30), seqId);
        buf[32] = 0x01; // control = DelayReq
        buf[33] = 0x7F; // logMessageInterval
        // originTimestamp 모두 0 (slave)
        return buf;
    }

    // ── 오프셋 계산 ──────────────────────────────────────────

    private void ComputeOffset()
    {
        // offset = ((t2-t1) - (t4-t3)) / 2 — 단, 아직 t4 없으면 단순 계산
        _offsetNs = (long)((_t2Ns - _t1Ns) * (1 - Alpha) + _offsetNs * Alpha);
        UpdateState(PtpState.Syncing);
    }

    // ── 유틸리티 ─────────────────────────────────────────────

    private static long GetLocalTimeNs()
    {
        // Windows 고해상도 타이머
        return (long)(DateTime.UtcNow.Subtract(new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc))
                                     .TotalMilliseconds * 1_000_000);
    }

    private static byte[] _localClockId = Array.Empty<byte>();
    private static byte[] GetLocalClockId()
    {
        if (_localClockId.Length == 10) return _localClockId;
        var buf = new byte[10];
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var mac = nic.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length == 6 && mac.Any(b => b != 0))
            {
                // EUI-64: MAC[0..3] + 0xFF 0xFE + MAC[3..6]
                buf[0] = mac[0]; buf[1] = mac[1]; buf[2] = mac[2]; buf[3] = 0xFF;
                buf[4] = 0xFE;   buf[5] = mac[3]; buf[6] = mac[4]; buf[7] = mac[5];
                buf[8] = 0; buf[9] = 1; // portNum = 1
                break;
            }
        }
        _localClockId = buf;
        return buf;
    }

    private bool OurPortId(byte[] reqId)
    {
        var mine = GetLocalClockId();
        return reqId.AsSpan(0, 10).SequenceEqual(mine);
    }

    private static UdpClient CreateMulticastSocket(int port, string group, string localIp)
    {
        var sock = new UdpClient();
        sock.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        sock.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        var mcastAddr = IPAddress.Parse(group);
        var localAddr = string.IsNullOrEmpty(localIp) ? IPAddress.Any : IPAddress.Parse(localIp);
        sock.JoinMulticastGroup(mcastAddr, localAddr);
        sock.MulticastLoopback = false;
        return sock;
    }

    private void UpdateState(PtpState state)
    {
        _state = state;
        PublishStatus();
    }

    private void PublishStatus()
    {
        StatusChanged?.Invoke(this, new PtpStatus
        {
            State            = _state,
            MasterClockId    = _masterId,
            OffsetNs         = _offsetNs,
            MeanPathDelayNs  = _meanPathDelayNs,
            LastSyncTime     = DateTime.Now,
        });
    }

    public void Dispose() => Stop();
}
