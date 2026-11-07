using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public void NormalizesPhoneSeparatorsAndAbsoluteDestination()
    {
        var config = new AppConfig
        {
            SourcePath = "//Internal Storage//DCIM/",
            DestinationPath = Path.Combine(Path.GetTempPath(), "Photos", "..", "iPhone")
        };

        config.Validate();

        Assert.Equal(@"\Internal Storage\DCIM", config.SourcePath);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "iPhone"), config.DestinationPath);
    }

    [Fact]
    public void DeviceRootIsValidSource()
    {
        var config = new AppConfig { SourcePath = @"\", DestinationPath = Path.GetTempPath() };

        config.Validate();

        Assert.Equal(@"\", config.SourcePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(@"\Internal Storage\..\DCIM")]
    [InlineData(@"\Internal Storage\.\DCIM")]
    public void RejectsInvalidSourceBeforeTransfer(string source)
    {
        var config = new AppConfig { SourcePath = source, DestinationPath = Path.GetTempPath() };

        Assert.Throws<ArgumentException>(config.Validate);
    }

    [Theory]
    [InlineData("Photos")]
    [InlineData("C:Photos")]
    [InlineData(@"\Photos")]
    public void RejectsDestinationThatDependsOnWorkingDirectory(string destination)
    {
        var config = new AppConfig { DestinationPath = destination };

        Assert.Throws<ArgumentException>(config.Validate);
    }

    [Theory]
    [InlineData("Photos")]
    [InlineData(@"\Photos")]
    public void RejectsAfcDestinationThatDependsOnWorkingDirectory(string destination)
    {
        var config = new AppConfig { AfcDestinationPath = destination };

        Assert.Throws<ArgumentException>(config.Validate);
    }

    [Fact]
    public void NoBackupFolderIsConfiguredByDefault()
    {
        var config = new AppConfig();

        config.Validate();

        Assert.Throws<ArgumentException>(() => config.GetDestinationPath(useWpd: false));
        Assert.Throws<ArgumentException>(() => config.GetDestinationPath(useWpd: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void UnsetDestinationIsReportedOnlyWhenItsTransportIsUsed(string? unset)
    {
        var afcFolder = Path.Combine(Path.GetTempPath(), "Photos", "AFC");
        var config = new AppConfig { AfcDestinationPath = afcFolder, DestinationPath = unset! };

        config.Validate();

        Assert.Equal(afcFolder, config.GetDestinationPath(useWpd: false));
        var error = Assert.Throws<ArgumentException>(() => config.GetDestinationPath(useWpd: true));
        Assert.Contains("set DestinationPath in appsettings.json", error.Message);
        Assert.Contains("Choose B in the menu", error.Message);
    }

    [Fact]
    public void SetDestinationPathChangesOnlyTheSelectedTransport()
    {
        var config = new AppConfig { DestinationPath = Path.Combine(Path.GetTempPath(), "Photos", "DCIM") };
        config.Validate();

        config.SetDestinationPath(useWpd: false, Path.Combine(Path.GetTempPath(), "Photos", "x", "..", "AFC"));

        Assert.Equal(Path.Combine(Path.GetTempPath(), "Photos", "AFC"), config.AfcDestinationPath);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Photos", "DCIM"), config.DestinationPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Photos")]
    [InlineData(@"\Photos")]
    public void SetDestinationPathRejectsAnInvalidFolderAndKeepsTheOldOne(string folder)
    {
        var afc = Path.Combine(Path.GetTempPath(), "Photos", "AFC");
        var config = new AppConfig { AfcDestinationPath = afc };
        config.Validate();

        Assert.Throws<ArgumentException>(() => config.SetDestinationPath(useWpd: false, folder));
        Assert.Equal(afc, config.AfcDestinationPath);
    }

    [Fact]
    public void SetDestinationPathRejectsTheOtherTransportsFolder()
    {
        var dcim = Path.Combine(Path.GetTempPath(), "Photos", "DCIM");
        var config = new AppConfig { DestinationPath = dcim };
        config.Validate();

        Assert.Throws<ArgumentException>(() => config.SetDestinationPath(useWpd: false, dcim));
        Assert.Equal("", config.AfcDestinationPath);
    }

    [Fact]
    public void EachTransportHasItsOwnDestination()
    {
        var config = new AppConfig
        {
            DestinationPath = Path.Combine(Path.GetTempPath(), "Photos", "DCIM"),
            AfcDestinationPath = Path.Combine(Path.GetTempPath(), "Photos", "x", "..", "AFC"),
        };

        config.Validate();

        Assert.Equal(Path.Combine(Path.GetTempPath(), "Photos", "DCIM"), config.GetDestinationPath(useWpd: true));
        Assert.Equal(Path.Combine(Path.GetTempPath(), "Photos", "AFC"), config.GetDestinationPath(useWpd: false));
    }

    [Fact]
    public void TransportsCannotShareADestination()
    {
        var folder = Path.Combine(Path.GetTempPath(), "Photos");
        var config = new AppConfig { DestinationPath = folder, AfcDestinationPath = folder + Path.DirectorySeparatorChar };

        Assert.Throws<ArgumentException>(config.Validate);
    }
}
