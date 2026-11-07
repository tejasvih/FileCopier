namespace FileCopier;

internal enum CommandLineMode
{
    Copy,
    Verify,
    Diagnose,
    ScanOnly,
    Help
}

internal sealed record CommandLineOptions(CommandLineMode Mode, IReadOnlySet<string> ExcludedFiles, bool UseWpd = false,
    string? BackupFolder = null)
{
    public static CommandLineOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var mode = CommandLineMode.Copy;
        var explicitMode = false;
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var useWpd = false;
        string? backupFolder = null;

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--exclude-file")
            {
                if (!HasValue(args, ++index))
                    throw new ArgumentException("--exclude-file requires a relative phone file path.");

                exclusions.Add(NormalizeExcludedPath(args[index]));
                continue;
            }

            if (args[index] == "--backup-folder")
            {
                if (!HasValue(args, ++index))
                    throw new ArgumentException("--backup-folder requires a full folder path.");
                if (backupFolder != null)
                    throw new ArgumentException("Specify --backup-folder only once.");

                backupFolder = args[index];
                continue;
            }

            if (args[index] == "--wpd")
            {
                useWpd = true;
                continue;
            }

            var requestedMode = args[index] switch
            {
                "--copy" => CommandLineMode.Copy,
                "--verify" => CommandLineMode.Verify,
                "--diagnose" => CommandLineMode.Diagnose,
                "--scan-only" => CommandLineMode.ScanOnly,
                "--help" or "-h" or "/?" => CommandLineMode.Help,
                _ => throw new ArgumentException($"Unknown argument: '{args[index]}'.")
            };
            if (explicitMode)
                throw new ArgumentException("Specify only one mode: --copy, --verify, --diagnose, --scan-only, or --help.");

            mode = requestedMode;
            explicitMode = true;
        }

        if (mode != CommandLineMode.Copy && exclusions.Count > 0)
            throw new ArgumentException("--exclude-file applies only to copying; it cannot be combined with --verify, --diagnose, --scan-only, or --help.");
        if (mode is CommandLineMode.Diagnose or CommandLineMode.Help && backupFolder != null)
            throw new ArgumentException("--backup-folder applies only to --copy, --verify and --scan-only.");

        return new CommandLineOptions(mode, exclusions, useWpd, backupFolder);
    }

    private static bool HasValue(string[] args, int index) =>
        index < args.Length && !string.IsNullOrWhiteSpace(args[index]) &&
        !args[index].StartsWith("--", StringComparison.Ordinal) && args[index] is not ("-h" or "/?");

    /// <summary>Validates a phone path relative to the source and returns it in backup form.</summary>
    internal static string NormalizeExcludedPath(string relativePath)
    {
        // Only path syntax is checked here. No directory or file is opened or created.
        var validationRoot = Path.Combine(Path.GetTempPath(), "PhotoCopier-exclusion-validation");
        try
        {
            var fullPath = BackupFile.GetDestinationPath(validationRoot, relativePath);
            return Path.GetRelativePath(validationRoot, fullPath).Replace('/', '\\');
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"Invalid --exclude-file path '{relativePath}': {ex.Message}", ex);
        }
    }
}
