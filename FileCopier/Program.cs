using FileCopier.Afc;
using MediaDevices;
using Microsoft.Extensions.Configuration;

namespace FileCopier;

internal class Program
{
    private static int Main(string[] args)
    {
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

        if (options.Mode == CommandLineMode.Help)
        {
            Console.WriteLine("iPhonePhotoCopier [--wpd] [--exclude-file <relative-path>]...");
            Console.WriteLine("iPhonePhotoCopier [--wpd] --diagnose | --scan-only | --help");
            Console.WriteLine("Configure appsettings.json beside the executable.");
            Console.WriteLine("By default photos are read through AFC (Apple Mobile Device Service) from the camera roll /DCIM");
            Console.WriteLine("  and copied to AfcDestinationPath.");
            Console.WriteLine("--wpd reads SourcePath through Windows Portable Devices instead (the Explorer view)");
            Console.WriteLine("  and copies to DestinationPath.");
            Console.WriteLine("--diagnose checks photo-folder access without downloading photos.");
            Console.WriteLine("--scan-only scans every source folder and file without downloading photos.");
            Console.WriteLine("--exclude-file skips one phone file relative to the source (/DCIM, or SourcePath with --wpd); repeat for more files.");
            Console.WriteLine("Exclusions apply only while copying. Excluded files keep the backup incomplete.");
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            // Explorer, shortcuts, and other working directories must use the same settings.
            using var configuration = new ConfigurationManager();
            configuration.SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            var config = configuration.Get<AppConfig>() ?? new AppConfig();
            config.Validate();

            Console.WriteLine("iPhone Photo Copier, Calibre Technologies");
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

            Console.WriteLine($"Phone: {phone.PhoneName}");
            Console.WriteLine($"Source Path: {phone.SourcePath} ({(options.UseWpd ? "WPD" : "AFC")})");
            var destinationPath = config.GetDestinationPath(options.UseWpd);
            Console.WriteLine($"Destination Path: {destinationPath}");
            var scanOnly = options.Mode == CommandLineMode.ScanOnly;
            if (scanOnly) Console.WriteLine("Scan only: no photos will be downloaded.");
            var backup = new PhoneBackup(config, phone, destinationPath);
            try
            {
                backup.CopyPhotos(cancellation.Token, scanOnly, options.ExcludedFiles);
            }
            finally
            {
                backup.PrintSummary();
            }

            if (backup.Errors.Count > 0)
            {
                ConsoleUtils.WriteError(scanOnly
                    ? "Source scan incomplete. Resolve the errors above and rerun. No photos downloaded."
                    : "Backup incomplete. Review the errors and any excluded files above before rerunning.");
                return 1;
            }
            ConsoleUtils.WriteSuccess(scanOnly ? "Source scan completed. No photos downloaded." : "Backup completed.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            ConsoleUtils.WriteWarning("Stopped. Backup is incomplete; rerun to continue.");
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
        }
    }
}
