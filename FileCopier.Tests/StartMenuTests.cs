using FileCopier;
using Xunit;

namespace FileCopier.Tests;

public sealed class StartMenuTests
{
    private static AppConfig Config()
    {
        var config = new AppConfig
        {
            DestinationPath = Path.Combine(Path.GetTempPath(), "Photos", "DCIM"),
            AfcDestinationPath = Path.Combine(Path.GetTempPath(), "Photos", "AFC"),
        };
        config.Validate();
        return config;
    }

    [Theory]
    [InlineData("1", "Copy", false)]
    [InlineData("2", "ScanOnly", false)]
    [InlineData("3", "Diagnose", false)]
    [InlineData("4", "Copy", true)]
    [InlineData("5", "ScanOnly", true)]
    [InlineData("6", "Diagnose", true)]
    [InlineData(" 7 ", "Help", false)]
    public void EachChoiceSelectsItsModeAndTransport(string answer, string mode, bool useWpd)
    {
        var options = StartMenu.Show(Config(), new StringReader(answer + Environment.NewLine), new StringWriter());

        Assert.NotNull(options);
        Assert.Equal(mode, options.Mode.ToString());
        Assert.Equal(useWpd, options.UseWpd);
        Assert.Empty(options.ExcludedFiles);
    }

    [Theory]
    [InlineData("q\n")]
    [InlineData("Q\n")]
    [InlineData("")]
    public void QuitOrEndOfInputChoosesNothing(string input)
    {
        Assert.Null(StartMenu.Show(Config(), new StringReader(input), new StringWriter()));
    }

    [Fact]
    public void InvalidAnswersAskAgain()
    {
        var output = new StringWriter();

        var options = StartMenu.Show(Config(), new StringReader("9\nabc\n2\n"), output);

        Assert.Equal(CommandLineMode.ScanOnly, options!.Mode);
        Assert.Equal(2, output.ToString().Split("Type a number from 1 to 7, or Q to quit.").Length - 1);
    }

    [Fact]
    public void CopyChoicesShowTheirDestinations()
    {
        var config = Config();
        var output = new StringWriter();

        StartMenu.Show(config, new StringReader("q\n"), output);

        Assert.Contains($"1. Copy photos to {config.AfcDestinationPath} (AFC", output.ToString());
        Assert.Contains($"4. Copy photos to {config.DestinationPath} (WPD", output.ToString());
    }
}
