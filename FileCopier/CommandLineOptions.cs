namespace FileCopier;

internal enum CommandLineMode
{
    Copy,
    Diagnose,
    ScanOnly,
    Help
}

internal sealed record CommandLineOptions(CommandLineMode Mode, IReadOnlySet<string> ExcludedFiles, bool UseWpd = false)
{
    public static CommandLineOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var mode = CommandLineMode.Copy;
        var explicitMode = false;
        var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var useWpd = false;

        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--exclude-file")
            {
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) ||
                    args[index].StartsWith("--", StringComparison.Ordinal) || args[index] == "-h")
                {
                    throw new ArgumentException("--exclude-file requires a relative phone file path.");
                }

                exclusions.Add(NormalizeExcludedPath(args[index]));
                continue;
            }

            if (args[index] == "--wpd")
            {
                useWpd = true;
                continue;
            }

            var requestedMode = args[index] switch
            {
                "--diagnose" => CommandLineMode.Diagnose,
                "--scan-only" => CommandLineMode.ScanOnly,
                "--help" or "-h" => CommandLineMode.Help,
                _ => throw new ArgumentException($"Unknown argument: '{args[index]}'.")
            };
            if (explicitMode)
                throw new ArgumentException("Specify only one mode: --diagnose, --scan-only, or --help.");

            mode = requestedMode;
            explicitMode = true;
        }

        if (mode != CommandLineMode.Copy && exclusions.Count > 0)
            throw new ArgumentException("--exclude-file applies only to copying; it cannot be combined with --diagnose, --scan-only, or --help.");

        return new CommandLineOptions(mode, exclusions, useWpd);
    }

    private static string NormalizeExcludedPath(string relativePath)
    {
        // Only path syntax is checked here. No directory or file is opened or created.
        var validationRoot = Path.Combine(Path.GetTempPath(), "iPhonePhotoCopier-exclusion-validation");
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
