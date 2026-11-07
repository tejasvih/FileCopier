using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace FileCopier.Afc;

internal sealed record UsbmuxDevice(long DeviceId, string Udid, string ConnectionType);

/// <summary>
/// Client for Apple's usbmuxd (Apple Mobile Device Service, installed with Apple Devices or iTunes),
/// which tunnels TCP connections to services on a USB-attached iPhone.
/// </summary>
internal static class Usbmux
{
    private const int UsbmuxPort = 27015;
    private const uint ProtocolVersion = 1;
    private const uint PlistMessage = 8;
    private const int MaximumMessageLength = 16 * 1024 * 1024;
    public static readonly TimeSpan IoTimeout = TimeSpan.FromSeconds(60);

    public static IReadOnlyList<UsbmuxDevice> ListDevices()
    {
        using var stream = Open();
        var reply = Request(stream, new Dictionary<string, object> { ["MessageType"] = "ListDevices" });
        if (!reply.TryGetValue("DeviceList", out var value) || value is not List<object> list)
            throw new InvalidDataException("usbmuxd returned no device list.");

        return list.OfType<Dictionary<string, object>>()
            .Select(entry => entry.GetValueOrDefault("Properties"))
            .OfType<Dictionary<string, object>>()
            .Select(properties => new UsbmuxDevice(
                properties.GetInteger("DeviceID"),
                properties.GetString("SerialNumber"),
                properties.TryGetValue("ConnectionType", out var type) ? type as string ?? "" : ""))
            .ToList();
    }

    public static Dictionary<string, object> ReadPairRecord(string udid)
    {
        using var stream = Open();
        var reply = Request(stream, new Dictionary<string, object>
        {
            ["MessageType"] = "ReadPairRecord",
            ["PairRecordID"] = udid,
        });
        if (!reply.TryGetValue("PairRecordData", out var data) || data is not byte[] record)
        {
            throw new IOException("This computer has no pairing record for the iPhone. Unlock the phone, tap Trust, " +
                "and open the Apple Devices app once before retrying.");
        }
        return PropertyList.Parse(record);
    }

    /// <summary>Opens a raw tunnel to a TCP port on the device. The caller owns the returned stream.</summary>
    public static Stream Connect(long deviceId, ushort port)
    {
        var stream = Open();
        try
        {
            var reply = Request(stream, new Dictionary<string, object>
            {
                ["MessageType"] = "Connect",
                ["DeviceID"] = deviceId,
                // usbmuxd expects the port in network byte order.
                ["PortNumber"] = (long)BinaryPrimitives.ReverseEndianness(port),
            });
            EnsureSuccess(reply, $"connect to iPhone port {port}");
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static NetworkStream Open()
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            socket.Connect(IPAddress.Loopback, UsbmuxPort);
        }
        catch (SocketException ex)
        {
            socket.Dispose();
            throw new IOException("Apple Mobile Device Service (usbmuxd) is not running. Install or open the Apple Devices app, " +
                "then reconnect the iPhone.", ex);
        }
        return new NetworkStream(socket, ownsSocket: true)
        {
            ReadTimeout = (int)IoTimeout.TotalMilliseconds,
            WriteTimeout = (int)IoTimeout.TotalMilliseconds,
        };
    }

    private static Dictionary<string, object> Request(Stream stream, Dictionary<string, object> message)
    {
        message["ClientVersionString"] = "iPhonePhotoCopier";
        message["ProgName"] = "iPhonePhotoCopier";
        message["kLibUSBMuxVersion"] = 3L;
        var body = PropertyList.Serialize(message);

        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(16 + body.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], ProtocolVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..], PlistMessage);
        BinaryPrimitives.WriteUInt32LittleEndian(header[12..], 1);
        stream.Write(header);
        stream.Write(body);

        stream.ReadExactly(header);
        var length = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (length < 16 || length > MaximumMessageLength || BinaryPrimitives.ReadUInt32LittleEndian(header[8..]) != PlistMessage)
            throw new InvalidDataException("usbmuxd returned a malformed reply.");

        var reply = new byte[length - 16];
        stream.ReadExactly(reply);
        return PropertyList.Parse(reply);
    }

    private static void EnsureSuccess(Dictionary<string, object> reply, string action)
    {
        if (reply.TryGetValue("MessageType", out var type) && type is "Result" &&
            reply.TryGetValue("Number", out var number) && number is long code)
        {
            if (code == 0) return;
            var reason = code switch
            {
                2 => "device not found; reconnect the iPhone",
                3 => "connection refused by the iPhone",
                _ => $"usbmuxd error {code}",
            };
            throw new IOException($"Could not {action}: {reason}.");
        }
        throw new InvalidDataException($"usbmuxd returned an unexpected reply while trying to {action}.");
    }
}
