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
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Photos")]
    [InlineData("C:Photos")]
    [InlineData(@"\Photos")]
    public void RejectsDestinationThatDependsOnWorkingDirectory(string destination)
    {
        var config = new AppConfig { DestinationPath = destination };

        Assert.Throws<ArgumentException>(config.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Photos")]
    [InlineData(@"\Photos")]
    public void RejectsAfcDestinationThatDependsOnWorkingDirectory(string destination)
    {
        var config = new AppConfig { AfcDestinationPath = destination };

        Assert.Throws<ArgumentException>(config.Validate);
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

    [Fact]
    public void ExpectedFileCountIsDisabledByDefault()
    {
        var config = new AppConfig();

        Assert.Equal(0, config.MinimumExpectedFiles);
        config.ValidateSourceFileCount(0);
        config.ValidateSourceFileCount(4930);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RejectsNegativeMinimumExpectedFiles(int minimum)
    {
        var config = new AppConfig { MinimumExpectedFiles = minimum };

        var error = Assert.Throws<ArgumentOutOfRangeException>(config.Validate);

        Assert.Equal(nameof(AppConfig.MinimumExpectedFiles), error.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4930)]
    [InlineData(64809)]
    public void RejectsSourceCountBelowConfiguredExpectation(int foundFiles)
    {
        var config = new AppConfig { MinimumExpectedFiles = 64810 };

        var error = Assert.Throws<IOException>(() => config.ValidateSourceFileCount(foundFiles));

        Assert.Contains(foundFiles.ToString(), error.Message);
        Assert.Contains("64810", error.Message);
        Assert.Contains("No files were copied", error.Message);
        Assert.Contains("USB visibility", error.Message);
        Assert.Contains("intentionally changed", error.Message);
    }

    [Theory]
    [InlineData(64810)]
    [InlineData(64811)]
    public void AcceptsSourceCountAtOrAboveConfiguredExpectation(int foundFiles)
    {
        var config = new AppConfig { MinimumExpectedFiles = 64810 };

        config.ValidateSourceFileCount(foundFiles);
    }
}
