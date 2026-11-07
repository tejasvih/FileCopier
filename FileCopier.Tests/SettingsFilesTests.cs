using System.Text;
using FileCopier;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FileCopier.Tests;

public sealed class SettingsFilesTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "FileCopier.Settings.Tests", Guid.NewGuid().ToString("N"));
    public SettingsFilesTests() => Directory.CreateDirectory(root);

    private const string Template = """
        {
          // Folder that receives the backup.
          "AfcDestinationPath": "",

          "SourcePath": "\\Internal Storage",
          "DestinationPath": ""
        }
        """;

    [Fact]
    public void ChangesOnlyTheValueInAppSettingsAndKeepsComments()
    {
        Write("appsettings.json", Template);

        var file = SettingsFiles.Save(root, "AfcDestinationPath", @"E:\Photos\AFC\");

        Assert.Equal("appsettings.json", file);
        Assert.Equal(Template.Replace("\"AfcDestinationPath\": \"\"", "\"AfcDestinationPath\": \"E:\\\\Photos\\\\AFC\\\\\""), Read("appsettings.json"));
        Assert.Equal(@"E:\Photos\AFC\", Load()["AfcDestinationPath"]);
    }

    [Fact]
    public void SavesToTheLocalFileWhenItOverridesTheSetting()
    {
        Write("appsettings.json", Template);
        Write("appsettings.local.json", "{\n  \"afcdestinationpath\": \"E:\\\\Photos\\\\AFC\\\\\",\n  \"OtherSetting\": 64810\n}\n");

        var file = SettingsFiles.Save(root, "AfcDestinationPath", @"F:\Backup\");

        Assert.Equal("appsettings.local.json", file);
        Assert.Equal(Template, Read("appsettings.json"));
        Assert.Equal(@"F:\Backup\", Load()["AfcDestinationPath"]);
        Assert.Equal("64810", Load()["OtherSetting"]);
    }

    [Fact]
    public void LocalFileWithoutTheSettingLeavesItInAppSettings()
    {
        Write("appsettings.json", Template);
        Write("appsettings.local.json", "{ \"OtherSetting\": 5 }");

        Assert.Equal("appsettings.json", SettingsFiles.Save(root, "DestinationPath", @"E:\Photos\DCIM\"));
        Assert.Equal(@"E:\Photos\DCIM\", Load()["DestinationPath"]);
        Assert.Equal("{ \"OtherSetting\": 5 }", Read("appsettings.local.json"));
    }

    [Fact]
    public void AddsAMissingSettingAndKeepsTheOthers()
    {
        Write("appsettings.json", "{ \"SourcePath\": \"\\\\Internal Storage\" }");

        SettingsFiles.Save(root, "AfcDestinationPath", @"D:\写真\");

        Assert.Equal(@"D:\写真\", Load()["AfcDestinationPath"]);
        Assert.Equal(@"\Internal Storage", Load()["SourcePath"]);
        Assert.Contains(@"D:\\写真\\", Read("appsettings.json"));
    }

    [Fact]
    public void KeepsAByteOrderMark()
    {
        File.WriteAllText(Path.Combine(root, "appsettings.json"), Template, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        SettingsFiles.Save(root, "AfcDestinationPath", @"E:\Photos\AFC\");

        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), File.ReadAllBytes(Path.Combine(root, "appsettings.json"))[..3]);
        Assert.Equal(@"E:\Photos\AFC\", Load()["AfcDestinationPath"]);
    }

    [Fact]
    public void ACommentedOutSettingIsNotChanged()
    {
        Write("appsettings.json", "{\n  // \"AfcDestinationPath\": \"C:\\\\Old\\\\\",\n  \"AfcDestinationPath\": \"\"\n}");

        SettingsFiles.Save(root, "AfcDestinationPath", @"E:\New\");

        Assert.StartsWith("{\n  // \"AfcDestinationPath\": \"C:\\\\Old\\\\\",", Read("appsettings.json"));
        Assert.Equal(@"E:\New\", Load()["AfcDestinationPath"]);
    }

    private void Write(string name, string content) =>
        File.WriteAllText(Path.Combine(root, name), content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private string Read(string name) => File.ReadAllText(Path.Combine(root, name));

    // Loads the files the way the app does, so a saved file must still be valid settings.
    private IConfiguration Load() => new ConfigurationBuilder()
        .SetBasePath(root)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.local.json", optional: true)
        .Build();

    public void Dispose() => Directory.Delete(root, recursive: true);
}
