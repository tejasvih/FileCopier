using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class StartMenuTests
{
    private static readonly string AfcFolder = Path.Combine(Path.GetTempPath(), "Photos", "AFC");
    private static readonly string WpdFolder = Path.Combine(Path.GetTempPath(), "Photos", "DCIM");

    private readonly List<(string Name, string Value)> saved = [];

    private static AppConfig Config()
    {
        var config = new AppConfig { DestinationPath = WpdFolder, AfcDestinationPath = AfcFolder };
        config.Validate();
        return config;
    }

    private CommandLineOptions? Choose(string input, StringWriter? output = null, AppConfig? config = null) =>
        StartMenu.Show(config ?? Config(), new StringReader(input), output ?? new StringWriter(), Save);

    private string Save(string name, string value)
    {
        saved.Add((name, value));
        return "appsettings.local.json";
    }

    [Theory]
    [InlineData("1\n", "Copy", false)]
    [InlineData("2\n", "ScanOnly", false)]
    [InlineData("3\n", "Verify", false)]
    [InlineData("4\n", "Diagnose", false)]
    [InlineData(" 6 \n", "Help", false)]
    [InlineData("w\n1\n", "Copy", true)]
    [InlineData("W\n2\n", "ScanOnly", true)]
    [InlineData("w\n3\n", "Verify", true)]
    [InlineData("w\n4\n", "Diagnose", true)]
    [InlineData("w\na\n1\n", "Copy", false)]
    public void EachChoiceSelectsItsModeAndTransport(string input, string mode, bool useWpd)
    {
        var options = Choose(input);

        Assert.NotNull(options);
        Assert.Equal(mode, options.Mode.ToString());
        Assert.Equal(useWpd, options.UseWpd);
        Assert.Empty(options.ExcludedFiles);
    }

    [Fact]
    public void EveryCommandLineModeIsOffered()
    {
        var offered = new[] { "1", "2", "3", "4", "6" }.Select(key => Choose(key + "\n")!.Mode);

        Assert.Equal(Enum.GetValues<CommandLineMode>().Order(), offered.Order());
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("Q\n")]
    [InlineData("")]
    [InlineData("w\nq\n")]
    [InlineData("5\n")]
    [InlineData("b\n")]
    public void QuitOrEndOfInputChoosesNothing(string input)
    {
        Assert.Null(Choose(input));
    }

    [Fact]
    public void InvalidAnswersAskAgain()
    {
        var output = new StringWriter();

        var options = Choose("9\nabc\na\n2\n", output);

        Assert.Equal(CommandLineMode.ScanOnly, options!.Mode);
        Assert.Equal(3, output.ToString().Split("Type a number from 1 to 6, B, W or Q.").Length - 1);
    }

    [Fact]
    public void SkipChoiceCollectsNormalizedPathsUntilAnEmptyLine()
    {
        var output = new StringWriter();

        var options = Choose("5\n126APPLE/IMG_6742.HEIC\n\"113APPLE\\IMG_3941.MOV\"\n..\\bad.HEIC\n\n", output);

        Assert.NotNull(options);
        Assert.Equal(CommandLineMode.Copy, options.Mode);
        Assert.False(options.UseWpd);
        Assert.Equal([@"113APPLE\IMG_3941.MOV", @"126APPLE\IMG_6742.HEIC"], options.ExcludedFiles.Order());
        Assert.Contains("Invalid --exclude-file path '..\\bad.HEIC'", output.ToString());
    }

    [Fact]
    public void SkipChoiceWithNoPathsReturnsToTheMenu()
    {
        var options = Choose("w\n5\n\n2\n");

        Assert.Equal(CommandLineMode.ScanOnly, options!.Mode);
        Assert.True(options.UseWpd);
    }

    [Fact]
    public void MenuShowsTheBackupFolderOfTheSelectedTransport()
    {
        var output = new StringWriter();

        Choose("w\nq\n", output);

        Assert.Contains($"Using AFC (recommended).{Environment.NewLine}Backup folder: {AfcFolder}", output.ToString());
        Assert.Contains($"Backup folder: {WpdFolder}", output.ToString());
        Assert.Contains("A. Switch to AFC", output.ToString());
    }

    [Fact]
    public void ChangedBackupFolderIsShownSavedAndUsed()
    {
        var config = Config();
        var output = new StringWriter();
        var newFolder = Path.Combine(Path.GetTempPath(), "Photos", "New AFC");

        var options = Choose($"b\n\"{newFolder}\"\n1\n", output, config);

        Assert.Equal(CommandLineMode.Copy, options!.Mode);
        Assert.Equal(newFolder, config.AfcDestinationPath);
        Assert.Equal(WpdFolder, config.DestinationPath);
        Assert.Equal([("AfcDestinationPath", newFolder)], saved);
        Assert.Contains($"Current backup folder: {AfcFolder}", output.ToString());
        Assert.Contains("Saved to appsettings.local.json.", output.ToString());
        Assert.Contains($"Backup folder: {newFolder}", output.ToString().Split("Saved to")[1]);
    }

    [Fact]
    public void EnterKeepsTheBackupFolder()
    {
        var config = Config();

        Choose("b\n\nq\n", config: config);

        Assert.Equal(AfcFolder, config.AfcDestinationPath);
        Assert.Empty(saved);
    }

    [Fact]
    public void InvalidBackupFolderAsksAgain()
    {
        var config = Config();
        var output = new StringWriter();
        var newFolder = Path.Combine(Path.GetTempPath(), "Photos", "WPD 2");

        Choose($"w\nb\nPhotos\n{AfcFolder}\n{newFolder}\nq\n", output, config);

        Assert.Equal(newFolder, config.DestinationPath);
        Assert.Equal([("DestinationPath", newFolder)], saved);
        Assert.Contains("must be an absolute", output.ToString());
        Assert.Contains("must be different folders", output.ToString());
    }

    [Fact]
    public void ActionThatNeedsAnUnsetFolderAsksForItFirst()
    {
        var config = new AppConfig();
        config.Validate();
        var output = new StringWriter();

        var options = Choose($"1\n{AfcFolder}\n", output, config);

        Assert.Equal(CommandLineMode.Copy, options!.Mode);
        Assert.Equal(AfcFolder, config.AfcDestinationPath);
        Assert.Contains("Backup folder: <not set>", output.ToString());
        Assert.Contains("Set the backup folder first.", output.ToString());
    }

    [Fact]
    public void UnsetFolderLeftUnsetReturnsToTheMenu()
    {
        var config = new AppConfig();
        config.Validate();

        var options = Choose("3\n\n4\n", config: config);

        Assert.Equal(CommandLineMode.Diagnose, options!.Mode);
        Assert.Empty(saved);
    }

    [Fact]
    public void FailedSaveKeepsTheFolderForThisRun()
    {
        var config = Config();
        var output = new StringWriter();
        var newFolder = Path.Combine(Path.GetTempPath(), "Photos", "Elsewhere");

        var options = StartMenu.Show(config, new StringReader($"b\n{newFolder}\n1\n"), output,
            (_, _) => throw new UnauthorizedAccessException("Access denied."));

        Assert.Equal(CommandLineMode.Copy, options!.Mode);
        Assert.Equal(newFolder, config.AfcDestinationPath);
        Assert.Contains("Could not save it (Access denied.)", output.ToString());
    }
}
