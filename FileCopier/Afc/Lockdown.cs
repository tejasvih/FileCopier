using System.Buffers.Binary;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace FileCopier.Afc;

/// <summary>
/// Paired lockdownd session used to start device services. Uses the pairing record the
/// Apple Mobile Device Service created when the user tapped Trust; it never pairs by itself.
/// </summary>
internal sealed class Lockdown : IDisposable
{
    private const ushort LockdownPort = 62078;
    private const int MaximumMessageLength = 16 * 1024 * 1024;
    private const string Label = "PhotoCopier";

    private readonly UsbmuxDevice device;
    private readonly Dictionary<string, object> pairRecord;
    private Stream stream;
    private X509Certificate2? clientCertificate;

    private Lockdown(UsbmuxDevice device, Dictionary<string, object> pairRecord, Stream stream)
    {
        this.device = device;
        this.pairRecord = pairRecord;
        this.stream = stream;
    }

    public string DeviceName { get; private set; } = "iPhone";
    public string ProductVersion { get; private set; } = "";

    public static Lockdown Start(UsbmuxDevice device)
    {
        var pairRecord = Usbmux.ReadPairRecord(device.Udid);
        var lockdown = new Lockdown(device, pairRecord, Usbmux.Connect(device.DeviceId, LockdownPort));
        try
        {
            lockdown.StartSession();
            return lockdown;
        }
        catch
        {
            lockdown.Dispose();
            throw;
        }
    }

    private void StartSession()
    {
        var type = Request(new() { ["Request"] = "QueryType" });
        if (!type.TryGetValue("Type", out var name) || name is not "com.apple.mobile.lockdown")
            throw new IOException("The iPhone did not answer as lockdownd.");

        var session = Request(new()
        {
            ["Request"] = "StartSession",
            ["HostID"] = pairRecord.GetString("HostID"),
            ["SystemBUID"] = pairRecord.GetString("SystemBUID"),
        });
        if (session.GetBoolean("EnableSessionSSL"))
            stream = AuthenticateTls(stream);

        DeviceName = GetValue("DeviceName") as string ?? DeviceName;
        ProductVersion = GetValue("ProductVersion") as string ?? "";
    }

    /// <summary>Starts a service and returns an open connection to it. The caller owns the stream.</summary>
    public Stream StartService(string service)
    {
        var reply = Request(new() { ["Request"] = "StartService", ["Service"] = service });
        var port = reply.GetInteger("Port");
        if (port is <= 0 or > ushort.MaxValue)
            throw new InvalidDataException($"lockdownd returned invalid port {port} for {service}.");

        var connection = Usbmux.Connect(device.DeviceId, (ushort)port);
        try
        {
            return reply.GetBoolean("EnableServiceSSL") ? AuthenticateTls(connection) : connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private object? GetValue(string key)
    {
        var reply = Request(new() { ["Request"] = "GetValue", ["Key"] = key });
        return reply.TryGetValue("Value", out var value) ? value : null;
    }

    private Dictionary<string, object> Request(Dictionary<string, object> message)
    {
        message["Label"] = Label;
        var body = PropertyList.Serialize(message);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        stream.Write(length);
        stream.Write(body);
        stream.Flush();

        stream.ReadExactly(length);
        var replyLength = BinaryPrimitives.ReadUInt32BigEndian(length);
        if (replyLength is 0 or > MaximumMessageLength)
            throw new InvalidDataException("lockdownd returned a malformed reply.");
        var reply = new byte[replyLength];
        stream.ReadExactly(reply);
        var result = PropertyList.Parse(reply);

        if (result.TryGetValue("Error", out var error))
        {
            var request = message["Request"];
            throw new IOException(error switch
            {
                "InvalidHostID" or "InvalidPairRecord" =>
                    $"The iPhone no longer trusts this computer ({error}). Unlock it, reconnect, and tap Trust.",
                "PasswordProtected" => "The iPhone is locked. Unlock it and retry.",
                _ => $"lockdownd {request} failed: {error}.",
            });
        }
        return result;
    }

    private SslStream AuthenticateTls(Stream inner)
    {
        if (clientCertificate == null)
        {
            using var pemCertificate = X509Certificate2.CreateFromPem(
                Encoding.ASCII.GetString(pairRecord.GetData("HostCertificate")),
                Encoding.ASCII.GetString(pairRecord.GetData("HostPrivateKey")));
            // SChannel cannot use the ephemeral key from PEM import; round-trip through PKCS#12.
            // The imported key is deleted when this session disposes the certificate.
            clientCertificate = X509CertificateLoader.LoadPkcs12(pemCertificate.Export(X509ContentType.Pkcs12), null);
        }
        var certificate = clientCertificate;
        using var deviceCertificate = X509Certificate2.CreateFromPem(
            Encoding.ASCII.GetString(pairRecord.GetData("DeviceCertificate")));
        var expectedDevice = deviceCertificate.GetPublicKey();

        var tls = new SslStream(inner, leaveInnerStreamOpen: false);
        try
        {
            tls.AuthenticateAsClient(new SslClientAuthenticationOptions
            {
                TargetHost = "",
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificates = [certificate],
                LocalCertificateSelectionCallback = (_, _, _, _, _) => certificate,
                // The device certificate is self-issued during pairing; pin its key to the pairing record.
                RemoteCertificateValidationCallback = (_, remote, _, _) =>
                    remote != null && remote.GetPublicKey().AsSpan().SequenceEqual(expectedDevice),
            });
            tls.ReadTimeout = (int)Usbmux.IoTimeout.TotalMilliseconds;
            tls.WriteTimeout = (int)Usbmux.IoTimeout.TotalMilliseconds;
            return tls;
        }
        catch (AuthenticationException ex)
        {
            tls.Dispose();
            throw new IOException("TLS session with the iPhone failed. Unlock it, reconnect, and tap Trust if asked.", ex);
        }
        catch
        {
            tls.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        try { stream.Dispose(); }
        finally { clientCertificate?.Dispose(); }
    }
}
