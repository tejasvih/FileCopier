using FileCopier.Afc;
using MediaDevices;
using Microsoft.Extensions.Configuration;

namespace FileCopier;

internal class Program
{
    private static int Main(string[] args)
    {
        ConsoleUtils.UseUnicodeOutput();
        CommandLineOptions options;
        try
        {
            options = CommandLineOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"{ex.Message} Use --help for usage.");
            return 1;
        }

        // A person starting the app without parameters picks an action; scripts keep copying.
        var showMenu = args.Length == 0 && !Console.IsInputRedirected;
        if (!showMenu && options.Mode == CommandLineMode.Help)
        {
            PrintHelp();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        // The first Ctrl+C stops cleanly after the current file; a second one ends the program at
        // once. That is safe: a backup file is replaced only after a complete download, so at worst
        // a .partial file is left behind.
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            if (cancellation.IsCancellationRequested) return;
            e.Cancel = true;
            cancellation.Cancel();
            ConsoleUtils.WriteWarning("Stopping after the current file. Press Ctrl+C again to stop immediately.");
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            // Explorer, shortcuts, and other working directories must use the same settings.
            using var configuration = new ConfigurationManager();
            configuration.SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                // Personal settings that stay out of source control override the shipped defaults.
                .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false);
            var config = configuration.Get<AppConfig>() ?? new AppConfig();
            config.Validate();

            ConsoleUtils.WriteHeading("Photo Copier for iPhone");
            if (showMenu)
            {
                var chosen = StartMenu.Show(config, Console.In, Console.Out,
                    (name, value) => SettingsFiles.Save(AppContext.BaseDirectory, name, value));
                if (chosen == null) return 0;
                if (chosen.Mode == CommandLineMode.Help)
                {
                    PrintHelp();
                    return 0;
                }
                options = chosen;
            }

            // A folder named on the command line applies to this run only.
            if (options.BackupFolder != null)
            {
                try
                {
                    config.SetDestinationPath(options.UseWpd, options.BackupFolder);
                }
                catch (ArgumentException ex)
                {
                    ConsoleUtils.WriteError($"Invalid --backup-folder '{options.BackupFolder}': {ex.Message}");
                    return 1;
                }
            }

            // Report a missing backup folder before touching the phone. Diagnosis copies nothing.
            var destinationPath = options.Mode == CommandLineMode.Diagnose ? "" : config.GetDestinationPath(options.UseWpd);
            if (destinationPath != "")
                ConsoleUtils.WriteHeading($"Backup folder: {destinationPath} ({(options.UseWpd ? "WPD" : "AFC")})");

            // The COM worker belongs to the process. Close the phone first, then the manager.
            using var deviceManager = options.UseWpd ? MediaDeviceManager.Instance : null;
            using IPhoneSource phone = options.UseWpd ? new WpdSource(config.SourcePath) : new AfcSource();
            phone.Connect();
            if (options.Mode == CommandLineMode.Diagnose)
            {
                phone.Diagnose();
                ConsoleUtils.WriteSuccess("Diagnosis completed. No photos downloaded.");
                return 0;
            }

            ConsoleUtils.WriteHeading($"Phone: {phone.PhoneName}");
            ConsoleUtils.WriteHeading($"Source Path: {phone.SourcePath} ({(options.UseWpd ? "WPD" : "AFC")})");
            var scanOnly = options.Mode == CommandLineMode.ScanOnly;
            var verify = options.Mode == CommandLineMode.Verify;
            if (scanOnly) ConsoleUtils.WriteHeading("Scan only: no photos will be downloaded.");
            if (verify) ConsoleUtils.WriteHeading("Verify: every file is read from the iPhone and compared with the backup. Nothing is written.");
            var backup = new PhoneBackup(phone, destinationPath);
            try
            {
                if (verify) backup.VerifyBackup(cancellation.Token);
                else backup.CopyPhotos(cancellation.Token, scanOnly, options.ExcludedFiles);
            }
            finally
            {
                backup.PrintSummary();
            }

            if (backup.Errors.Count > 0)
            {
                ConsoleUtils.WriteError(options.Mode switch
                {
                    CommandLineMode.ScanOnly => "Source scan incomplete. Resolve the errors above and rerun. No photos downloaded.",
                    CommandLineMode.Verify => "Verification failed. The files listed above are missing, different, or could not be read.",
                    _ => "Backup incomplete. Review the errors and any excluded files above before rerunning.",
                });
                return 1;
            }
            ConsoleUtils.WriteSuccess(options.Mode switch
            {
                CommandLineMode.ScanOnly => "Source scan completed. No photos downloaded.",
                CommandLineMode.Verify => "Verification completed. Every backup file matches the iPhone.",
                _ => "Backup completed.",
            });
            return 0;
        }
        catch (OperationCanceledException)
        {
            ConsoleUtils.WriteWarning(options.Mode == CommandLineMode.Verify
                ? "Stopped. Verification is incomplete."
                : "Stopped. Backup is incomplete; rerun to continue.");
            return 2;
        }
        catch (Exception ex)
        {
            ConsoleUtils.WriteError($"Backup failed: {ex.Message} (0x{ex.HResult:X8})");
            if (ex.HResult == unchecked((int)0x80042008))
            {
                ConsoleUtils.WriteWarning("iPhone returned an invalid storage ID. Reconnect the unlocked phone, allow access, then retry --diagnose.");
            }
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            ConsoleUtils.Reset();
            // A window opened by double-clicking closes on exit; keep the summary readable.
            if (showMenu)
            {
                Console.Write("\nPress Enter to close.");
                Console.ReadLine();
            }
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("PhotoCopier                 (no parameters: choose an action from a menu)");
        Console.WriteLine("PhotoCopier [--wpd] [--backup-folder <folder>] [--copy] [--exclude-file <relative-path>]...");
        Console.WriteLine("PhotoCopier [--wpd] [--backup-folder <folder>] --verify | --scan-only");
        Console.WriteLine("PhotoCopier [--wpd] --diagnose");
        Console.WriteLine("PhotoCopier --help | -h | /?");
        Console.WriteLine("The backup folder is set in the menu (B) or in appsettings.json beside the executable.");
        Console.WriteLine("--backup-folder uses another folder for this run only, without saving it.");
        Console.WriteLine("By default photos are read through AFC (Apple Mobile Device Service) from the camera roll /DCIM");
        Console.WriteLine("  and copied to AfcDestinationPath.");
        Console.WriteLine("--wpd reads SourcePath through Windows Portable Devices instead (the Explorer view)");
        Console.WriteLine("  and copies to DestinationPath.");
        Console.WriteLine("--copy copies new and changed files without showing the menu (the default when input is redirected).");
        Console.WriteLine("--verify reads every file from the iPhone again and compares it byte for byte with the backup.");
        Console.WriteLine("  Nothing is written. Takes about as long as a full copy.");
        Console.WriteLine("--scan-only scans every source folder and file and previews the copy, without downloading photos.");
        Console.WriteLine("--diagnose checks photo-folder access without downloading photos.");
        Console.WriteLine("--exclude-file skips one phone file relative to the source (/DCIM, or SourcePath with --wpd); repeat for more files.");
        Console.WriteLine("Exclusions apply only while copying. Excluded files keep the backup incomplete.");
    }
}
