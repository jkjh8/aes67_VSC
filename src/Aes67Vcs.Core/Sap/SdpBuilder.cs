using System.Net;
using System.Net.NetworkInformation;
using Aes67Vcs.Core.Models;

namespace Aes67Vcs.Core.Sap;

/// <summary>
/// AES67 SDP (RFC 4566) 생성기.
/// RAVENNA / AES67 표준 필드를 포함.
/// </summary>
public static class SdpBuilder
{
    public static string Build(Aes67Config cfg)
    {
        string localIp   = GetLocalIp(cfg.LocalInterface);
        string sessionId = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        int channels     = (int)cfg.Channels;
        int payloadType  = 96; // dynamic RTP payload for L24

        // AES67 ptime: 1ms = 48 샘플 (48kHz)
        // 보통 1ms 패킷타임 사용
        int samplesPerPacket = cfg.SampleRate / 1000; // 48
        int ptimeMs          = 1;

        return
            $"v=0\r\n" +
            $"o=- {sessionId} 0 IN IP4 {localIp}\r\n" +
            $"s={cfg.StreamName}\r\n" +
            $"c=IN IP4 {cfg.MulticastAddress}/32\r\n" +
            $"t=0 0\r\n" +
            $"a=clock-domain:PTPv2 {cfg.PtpDomain}\r\n" +
            $"m=audio {cfg.RtpPort} RTP/AVP {payloadType}\r\n" +
            $"a=rtpmap:{payloadType} L24/{cfg.SampleRate}/{channels}\r\n" +
            $"a=framecount:{samplesPerPacket}\r\n" +
            $"a=ptime:{ptimeMs}\r\n" +
            $"a=mediaclk:direct=0\r\n" +
            $"a=ts-refclk:ptp=IEEE1588-2008:{GetPtpClockId()}\r\n" +
            $"a=recvonly\r\n";
    }

    private static string GetLocalIp(string preferred)
    {
        if (!string.IsNullOrEmpty(preferred)) return preferred;
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback) continue;
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
        return "127.0.0.1";
    }

    private static string GetPtpClockId()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var mac = nic.GetPhysicalAddress().GetAddressBytes();
            if (mac.Length == 6 && mac.Any(b => b != 0))
            {
                // EUI-64 형식: XX-XX-XX-FF-FE-XX-XX-XX
                return $"{mac[0]:X2}-{mac[1]:X2}-{mac[2]:X2}-FF-FE-{mac[3]:X2}-{mac[4]:X2}-{mac[5]:X2}";
            }
        }
        return "00-00-00-FF-FE-00-00-00";
    }
}
