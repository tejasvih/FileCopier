namespace FileCopier;

/// <summary>Menu shown when the app is started interactively without parameters.</summary>
internal static class StartMenu
{
    private static readonly HashSet<string> NoExclusions = [];

    internal static IReadOnlyList<(string Key, string Label, CommandLineOptions Options)> Choices(AppConfig config) =>
    [
        ("1", $"Copy photos to {Folder(config.AfcDestinationPath, nameof(config.AfcDestinationPath))} (AFC, recommended)", new(CommandLineMode.Copy, NoExclusions)),
        ("2", "Preview what would be copied, read-only (AFC)", new(CommandLineMode.ScanOnly, NoExclusions)),
        ("3", "Check the iPhone connection, read-only (AFC)", new(CommandLineMode.Diagnose, NoExclusions)),
        ("4", $"Copy photos to {Folder(config.DestinationPath, nameof(config.DestinationPath))} (WPD, previous method)", new(CommandLineMode.Copy, NoExclusions, UseWpd: true)),
        ("5", "Preview what would be copied, read-only (WPD)", new(CommandLineMode.ScanOnly, NoExclusions, UseWpd: true)),
        ("6", "Check the iPhone connection, read-only (WPD)", new(CommandLineMode.Diagnose, NoExclusions, UseWpd: true)),
        ("7", "Show command-line help", new(CommandLineMode.Help, NoExclusions)),
    ];

    private static string Folder(string path, string setting) => path == "" ? $"<{setting} not set>" : path;

    /// <summary>Returns the chosen options, or null when the user quits or input ends.</summary>
    public static CommandLineOptions? Show(AppConfig config, TextReader input, TextWriter output)
    {
        var choices = Choices(config);
        output.WriteLine("Choose an action:");
        foreach (var (key, label, _) in choices)
            output.WriteLine($"  {key}. {label}");
        output.WriteLine("  Q. Quit");

        while (true)
        {
            output.Write("Choice: ");
            var answer = input.ReadLine()?.Trim();
            if (answer == null || answer.Equals("Q", StringComparison.OrdinalIgnoreCase))
                return null;

            foreach (var (key, _, options) in choices)
            {
                if (answer == key) return options;
            }
            output.WriteLine($"Type a number from 1 to {choices.Count}, or Q to quit.");
        }
    }
}
