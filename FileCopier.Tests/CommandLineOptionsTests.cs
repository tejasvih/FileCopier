using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class CommandLineOptionsTests
{
    [Fact]
    public void NoArgumentsCopiesWithoutExclusions()
    {
        var options = CommandLineOptions.Parse([]);

        Assert.Equal(CommandLineMode.Copy, options.Mode);
        Assert.Empty(options.ExcludedFiles);
    }

    [Theory]
    [InlineData("--diagnose", "Diagnose")]
    [InlineData("--scan-only", "ScanOnly")]
    [InlineData("--help", "Help")]
    [InlineData("-h", "Help")]
    public void ExistingModeFlagsKeepTheirDistinctBehavior(string flag, string expectedMode)
    {
        var options = CommandLineOptions.Parse([flag]);

        Assert.Equal(expectedMode, options.Mode.ToString());
        Assert.Empty(options.ExcludedFiles);
    }

    [Fact]
    public void RepeatableExclusionsNormalizeSeparatorsAndCompareWithoutCase()
    {
        var options = CommandLineOptions.Parse([
            "--exclude-file", @"202409_a\IMG_E7443.MOV",
            "--exclude-file", "202409_A/img_e7443.mov",
            "--exclude-file", "202405_a/IMG_1234.HEIC"
        ]);

        Assert.Equal(CommandLineMode.Copy, options.Mode);
        Assert.Equal(2, options.ExcludedFiles.Count);
        Assert.True(options.ExcludedFiles.Contains(@"202409_A\img_e7443.mov"));
        Assert.True(options.ExcludedFiles.Contains(@"202405_a\IMG_1234.HEIC"));
    }

    [Theory]
    [InlineData("--diagnose")]
    [InlineData("--scan-only")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void ExclusionsCannotChangeNonCopyModesRegardlessOfArgumentOrder(string mode)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse([mode, "--exclude-file", @"202409_a\IMG_E7443.MOV"]));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--exclude-file", @"202409_a\IMG_E7443.MOV", mode]));
    }

    [Theory]
    [InlineData("--diagnose", "--scan-only")]
    [InlineData("--diagnose", "--diagnose")]
    [InlineData("--scan-only", "--scan-only")]
    [InlineData("--help", "-h")]
    public void DuplicateOrConflictingModesAreRejected(string first, string second)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse([first, second]));
    }

    [Theory]
    [InlineData("--unknown")]
    [InlineData("photo.HEIC")]
    [InlineData("--exclude-file=photo.HEIC")]
    public void UnknownArgumentsAreRejected(string argument)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse([argument]));
    }

    [Fact]
    public void ExclusionRequiresPathValue()
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--exclude-file"]));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--exclude-file", "--scan-only"]));
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--exclude-file", "--exclude-file", "photo.HEIC"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(@"C:\Photos\IMG_1234.HEIC")]
    [InlineData(@"C:IMG_1234.HEIC")]
    [InlineData(@"\Internal Storage\202409_a\IMG_E7443.MOV")]
    [InlineData(@"..\IMG_1234.HEIC")]
    [InlineData(@"202409_a\..\IMG_E7443.MOV")]
    [InlineData(@"202409_a\bad?.MOV")]
    [InlineData(@"202409_a\IMG_E7443.MOV:stream")]
    [InlineData(@"202409_a\NUL.MOV")]
    public void InvalidPhonePathsAreRejected(string path)
    {
        Assert.Throws<ArgumentException>(() => CommandLineOptions.Parse(["--exclude-file", path]));
    }
}
