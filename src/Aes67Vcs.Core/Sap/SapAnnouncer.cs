using System.Net;
using System.Net.Sockets;
using System.Text;
using Aes67Vcs.Core.Models;

namespace Aes67Vcs.Core.Sap;

/// <summary>
/// SAP (Session Announcement Protocol, RFC 2974) 어나운서.
/// AES67 스트림을 SAP 멀티캐스트(224.2.127.254)로 주기적으로 광고.
/// 간격: 30초 (RFC 2974 권장)
/// </summary>
public sealed class SapAnnouncer : IDisposable
{
    private const string SapMulticast = "224.2.127.254";
    private const int    SapPort      = 9875;
    private const int    AnnounceIntervalMs = 30_000;

    private readonly Aes67Config _cfg;
    private UdpClient?   _sock;
    private Timer?       _timer;
    private ushort       _msgIdHash;
    private string       _currentSdp = "";

    public SapAnnouncer(Aes67Config config)
    {
        _cfg        = config;
        _msgIdHash  = (ushort)Random.Shared.Next(0, 0xFFFF);
    }

    public void Start()
    {
        if (_sock != null) return;
        _sock = new UdpClient();
        _sock.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _sock.MulticastLoopback = false;
        _sock.Ttl = 32;

        _currentSdp = SdpBuilder.Build(_cfg);
        SendAnnounce(isDelete: false);

        _timer = new Timer(_ => SendAnnounce(false), null,
                           AnnounceIntervalMs, AnnounceIntervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;

        // Delete 패킷 전송
        if (_sock != null)
        {
            try { SendAnnounce(isDelete: true); } catch { }
            _sock.Close();
            _sock = null;
        }
    }

    /// <summary>설정 변경 시 호출 → SDP 재생성 + 즉시 재광고</summary>
    public void Refresh()
    {
        _currentSdp = SdpBuilder.Build(_cfg);
        SendAnnounce(false);
    }

    // ── SAP 패킷 구조 (RFC 2974 §3) ─────────────────────────
    //  0                   1                   2                   3
    //  0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
    // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    // | V=1 |A|R|T|E|C|   auth len  |         msg id hash           |
    // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    // |                    originating source                        |
    // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    // | optional: auth data, payload type, ...                       |
    // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+
    // | payload (payload-type CRNL + SDP)                            |
    // +-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+-+

    private void SendAnnounce(bool isDelete)
    {
        if (_sock == null) return;

        var sdpBytes    = Encoding.ASCII.GetBytes(_currentSdp);
        var mimeType    = "application/sdp\0"u8.ToArray();

        // 로컬 IP를 originating source로
        byte[] srcIp = GetSourceIpBytes();

        // SAP 헤더 (8바이트)
        byte flags = (byte)(0x20                     // V=1
                    | (isDelete ? 0x04 : 0x00));     // T=1 → delete
        var header = new byte[8];
        header[0] = flags;
        header[1] = 0; // auth len = 0
        header[2] = (byte)(_msgIdHash >> 8);
        header[3] = (byte)(_msgIdHash & 0xFF);
        header[4] = srcIp[0]; header[5] = srcIp[1];
        header[6] = srcIp[2]; header[7] = srcIp[3];

        var packet = new byte[header.Length + mimeType.Length + sdpBytes.Length];
        Buffer.BlockCopy(header,   0, packet, 0,                    header.Length);
        Buffer.BlockCopy(mimeType, 0, packet, header.Length,        mimeType.Length);
        Buffer.BlockCopy(sdpBytes, 0, packet, header.Length + mimeType.Length, sdpBytes.Length);

        var ep = new IPEndPoint(IPAddress.Parse(SapMulticast), SapPort);
        try { _sock.Send(packet, packet.Length, ep); }
        catch { /* 네트워크 없으면 무시 */ }
    }

    private static byte[] GetSourceIpBytes()
    {
        foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    return ua.Address.GetAddressBytes();
            }
        }
        return new byte[] { 127, 0, 0, 1 };
    }

    public void Dispose() => Stop();
}
