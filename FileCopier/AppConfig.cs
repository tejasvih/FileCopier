namespace FileCopier
{
    public class AppConfig
    {
        public string SourcePath { get; set; } = @"\Internal Storage";
        /// <summary>Backup folder for the WPD transport (--wpd), which mirrors SourcePath. Empty until configured.</summary>
        public string DestinationPath { get; set; } = "";
        /// <summary>Backup folder for the AFC transport (default), which mirrors the camera roll's 1xxAPPLE folders. Empty until configured.</summary>
        public string AfcDestinationPath { get; set; } = "";

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(SourcePath))
                throw new ArgumentException("SourcePath must name an iPhone folder.");

            var sourceSegments = SourcePath.Replace('/', '\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries);
            if (sourceSegments.Any(segment => segment is "." or ".."))
                throw new ArgumentException("SourcePath cannot contain '.' or '..' segments.");

            SourcePath = @"\" + string.Join(@"\", sourceSegments);

            DestinationPath = ValidateDestination(DestinationPath, nameof(DestinationPath));
            AfcDestinationPath = ValidateDestination(AfcDestinationPath, nameof(AfcDestinationPath));
            // The layouts differ; sharing a folder would mix WPD month folders with AFC 1xxAPPLE folders.
            if (DestinationPath != "" && string.Equals(Path.TrimEndingDirectorySeparator(DestinationPath),
                    Path.TrimEndingDirectorySeparator(AfcDestinationPath), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("DestinationPath and AfcDestinationPath must be different folders.");
        }

        /// <summary>Returns the selected transport's backup folder; an unset folder is reported here, when it is needed.</summary>
        public string GetDestinationPath(bool useWpd)
        {
            var path = useWpd ? DestinationPath : AfcDestinationPath;
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException($"No backup folder is set. Choose B in the menu, use --backup-folder, or set {DestinationSetting(useWpd)} in appsettings.json beside the executable.");
            return path;
        }

        /// <summary>Name of the setting that holds the selected transport's backup folder.</summary>
        public static string DestinationSetting(bool useWpd) => useWpd ? nameof(DestinationPath) : nameof(AfcDestinationPath);

        /// <summary>Changes the selected transport's backup folder after the same checks as the settings file.</summary>
        public void SetDestinationPath(bool useWpd, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException(@"Type a full folder path, for example D:\iPhone Backup\.");

            var changed = (AppConfig)MemberwiseClone();
            if (useWpd) changed.DestinationPath = path;
            else changed.AfcDestinationPath = path;
            changed.Validate();
            DestinationPath = changed.DestinationPath;
            AfcDestinationPath = changed.AfcDestinationPath;
        }

        private static string ValidateDestination(string? path, string name)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException($"{name} must be an absolute local or UNC folder path.");
            return Path.GetFullPath(path);
        }
    }
}
