using System.Text;
using FileCopier.Afc;
using Xunit;

namespace FileCopier.Tests;

public sealed class PropertyListTests
{
    [Fact]
    public void RoundTripsMessageValues()
    {
        var message = new Dictionary<string, object>
        {
            ["MessageType"] = "Connect",
            ["DeviceID"] = 1L,
            ["PortNumber"] = 32498L,
            ["EnableSessionSSL"] = true,
            ["Disabled"] = false,
            ["PairRecordData"] = new byte[] { 0, 1, 254, 255 },
            ["List"] = new List<object> { "a", 2L },
            ["Nested"] = new Dictionary<string, object> { ["Key"] = "value" },
        };

        var parsed = PropertyList.Parse(PropertyList.Serialize(message));

        Assert.Equal("Connect", parsed.GetString("MessageType"));
        Assert.Equal(1L, parsed.GetInteger("DeviceID"));
        Assert.Equal(32498L, parsed.GetInteger("PortNumber"));
        Assert.True(parsed.GetBoolean("EnableSessionSSL"));
        Assert.False(parsed.GetBoolean("Disabled"));
        Assert.False(parsed.GetBoolean("Missing"));
        Assert.Equal(new byte[] { 0, 1, 254, 255 }, parsed.GetData("PairRecordData"));
        Assert.Equal(new List<object> { "a", 2L }, parsed["List"]);
        Assert.Equal("value", ((Dictionary<string, object>)parsed["Nested"]).GetString("Key"));
    }

    [Fact]
    public void ParsesAppleDocumentWithDoctypeAndWrappedData()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>HostCertificate</key>
                <data>
                AAEC
                AwQ=
                </data>
                <key>Big</key>
                <integer>18446744073709551615</integer>
            </dict>
            </plist>
            """;

        var parsed = PropertyList.Parse(Encoding.UTF8.GetBytes(xml));

        Assert.Equal(new byte[] { 0, 1, 2, 3, 4 }, parsed.GetData("HostCertificate"));
        Assert.Equal(ulong.MaxValue, parsed["Big"]);
    }

    [Fact]
    public void DoesNotExpandDocumentEntities()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE plist [<!ENTITY secret SYSTEM "file:///c:/windows/win.ini">]>
            <plist version="1.0"><dict><key>Value</key><string>&secret;</string></dict></plist>
            """;

        // The DTD is ignored, so the entity is undeclared and the document is rejected.
        Assert.Throws<InvalidDataException>(() => PropertyList.Parse(Encoding.UTF8.GetBytes(xml)));
    }

    [Theory]
    [InlineData("<plist version=\"1.0\"><array/></plist>")]
    [InlineData("<plist version=\"1.0\"><dict><key>A</key></dict></plist>")]
    [InlineData("<html/>")]
    [InlineData("not xml")]
    public void RejectsMalformedDocuments(string xml)
    {
        Assert.Throws<InvalidDataException>(() => PropertyList.Parse(Encoding.UTF8.GetBytes(xml)));
    }
}
