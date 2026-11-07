namespace FileCopier;

/// <summary>
/// Menu shown when the app is started interactively without parameters. It offers every
/// command-line option: each mode, --exclude-file, --backup-folder (saved to the settings file),
/// and --wpd as a switch between transports.
/// </summary>
internal static class StartMenu
{
    private static readonly HashSet<string> NoExclusions = [];

    private static readonly (string Key, string Label, CommandLineMode Mode)[] Actions =
    [
        ("1", "Copy new and changed photos", CommandLineMode.Copy),
        ("2", "Preview what would be copied (read-only)", CommandLineMode.ScanOnly),
        ("3", "Verify the backup against the iPhone (read-only; reads every file, about as long as a full copy)", CommandLineMode.Verify),
        ("4", "Check the iPhone connection (read-only)", CommandLineMode.Diagnose),
        ("5", "Copy, skipping files you name", CommandLineMode.Copy),
        ("6", "Show command-line help", CommandLineMode.Help),
    ];

    private const string SkipFilesKey = "5";

    /// <summary>
    /// Returns the chosen options, or null when the user quits or input ends. A changed backup folder
    /// is set in <paramref name="config"/> and passed to <paramref name="saveSetting"/>, which returns
    /// the name of the settings file it wrote.
    /// </summary>
    public static CommandLineOptions? Show(AppConfig config, TextReader input, TextWriter output,
        Func<string, string, string> saveSetting)
    {
        var useWpd = false;
        while (true)
        {
            WriteMenu(config, useWpd, output);
            while (true)
            {
                output.Write(ConsoleUtils.Colorize("Choice: ", ConsoleColor.Yellow));
                var answer = input.ReadLine()?.Trim();
                if (answer == null || answer.Equals("Q", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (answer.Equals(useWpd ? "A" : "W", StringComparison.OrdinalIgnoreCase))
                {
                    useWpd = !useWpd;
                    break;
                }

                if (answer.Equals("B", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ChangeBackupFolder(config, useWpd, input, output, saveSetting)) return null;
                    break;
                }

                var action = Actions.FirstOrDefault(action => action.Key == answer);
                if (action.Key == null)
                {
                    output.WriteLine(ConsoleUtils.Colorize(
                        $"Type a number from 1 to {Actions.Length}, B, {(useWpd ? "A" : "W")} or Q.", ConsoleColor.Yellow));
                    continue;
                }

                if (action.Mode is CommandLineMode.Copy or CommandLineMode.ScanOnly or CommandLineMode.Verify &&
                    BackupFolder(config, useWpd) == "")
                {
                    output.WriteLine(ConsoleUtils.Colorize("Set the backup folder first.", ConsoleColor.Yellow));
                    if (!ChangeBackupFolder(config, useWpd, input, output, saveSetting)) return null;
                    if (BackupFolder(config, useWpd) == "") break;
                }

                if (action.Key != SkipFilesKey)
                    return new CommandLineOptions(action.Mode, NoExclusions, useWpd);

                var exclusions = ReadExclusions(useWpd ? config.SourcePath : "/DCIM", input, output);
                if (exclusions == null) return null;
                if (exclusions.Count > 0)
                    return new CommandLineOptions(CommandLineMode.Copy, exclusions, useWpd);
                break;
            }
        }
    }

    private static void WriteMenu(AppConfig config, bool useWpd, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine(ConsoleUtils.Colorize(
            useWpd ? "Using WPD (Windows Portable Devices, previous method)." : "Using AFC (recommended).", ConsoleColor.Cyan));
        output.WriteLine(ConsoleUtils.Colorize($"Backup folder: {Folder(config, useWpd)}", ConsoleColor.Cyan));
        output.WriteLine("Choose an action:");
        foreach (var (key, label, _) in Actions)
            WriteChoice(output, key, label);
        WriteChoice(output, "B", "Change backup folder");
        if (useWpd) WriteChoice(output, "A", "Switch to AFC (recommended)");
        else WriteChoice(output, "W", "Switch to WPD (previous method)");
        WriteChoice(output, "Q", "Quit");
    }

    private static void WriteChoice(TextWriter output, string key, string label) =>
        output.WriteLine($"  {ConsoleUtils.Colorize(key + ".", ConsoleColor.Yellow)} {label}");

    private static string BackupFolder(AppConfig config, bool useWpd) =>
        useWpd ? config.DestinationPath : config.AfcDestinationPath;

    private static string Folder(AppConfig config, bool useWpd) =>
        BackupFolder(config, useWpd) is { Length: > 0 } path ? path : "<not set>";

    /// <summary>
    /// Shows the backup folder and lets the user replace it; Enter keeps it. A new folder is saved
    /// to the settings file. Returns false when input ends.
    /// </summary>
    private static bool ChangeBackupFolder(AppConfig config, bool useWpd, TextReader input, TextWriter output,
        Func<string, string, string> saveSetting)
    {
        output.WriteLine($"Current backup folder: {Folder(config, useWpd)}");
        while (true)
        {
            output.Write("New backup folder (Enter keeps the current one): ");
            var line = input.ReadLine();
            if (line == null) return false;
            line = line.Trim().Trim('"').Trim();
            if (line.Length == 0) return true;

            try
            {
                config.SetDestinationPath(useWpd, line);
            }
            catch (ArgumentException ex)
            {
                output.WriteLine(ConsoleUtils.Colorize(ex.Message, ConsoleColor.Yellow));
                continue;
            }

            var folder = BackupFolder(config, useWpd);
            output.WriteLine(Directory.Exists(folder)
                ? $"Backup folder: {folder}"
                : $"Backup folder: {folder} (it will be created when copying starts)");
            try
            {
                output.WriteLine(ConsoleUtils.Colorize(
                    $"Saved to {saveSetting(AppConfig.DestinationSetting(useWpd), folder)}.", ConsoleColor.Green));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                output.WriteLine(ConsoleUtils.Colorize(
                    $"Could not save it ({ex.Message}). The new folder is used until Photo Copier closes.", ConsoleColor.Yellow));
            }
            return true;
        }
    }

    /// <summary>Reads phone paths to skip until an empty line; returns null when input ends.</summary>
    private static HashSet<string>? ReadExclusions(string sourcePath, TextReader input, TextWriter output)
    {
        output.WriteLine($"Type each phone file to skip, relative to {sourcePath} (for example 126APPLE\\IMG_6742.HEIC).");
        output.WriteLine("Press Enter on an empty line when done. Skipped files keep the backup marked incomplete.");
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            output.Write("Skip: ");
            var line = input.ReadLine();
            if (line == null) return null;
            line = line.Trim().Trim('"');
            if (line.Length == 0)
            {
                if (exclusions.Count == 0) output.WriteLine("No files named.");
                return exclusions;
            }

            try
            {
                exclusions.Add(CommandLineOptions.NormalizeExcludedPath(line));
            }
            catch (ArgumentException ex)
            {
                output.WriteLine(ConsoleUtils.Colorize(ex.Message, ConsoleColor.Yellow));
            }
        }
    }
}
