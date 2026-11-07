namespace FileCopier
{
    public class AppConfig
    {
        public string SourcePath { get; set; } = @"\Internal Storage";
        /// <summary>Backup folder for the WPD transport (--wpd), which mirrors SourcePath. Empty until configured.</summary>
        public string DestinationPath { get; set; } = "";
        /// <summary>Backup folder for the AFC transport (default), which mirrors the camera roll's 1xxAPPLE folders. Empty until configured.</summary>
        public string AfcDestinationPath { get; set; } = "";
        public int MinimumExpectedFiles { get; set; }

        public void Validate()
        {
            if (MinimumExpectedFiles < 0)
                throw new ArgumentOutOfRangeException(nameof(MinimumExpectedFiles), "MinimumExpectedFiles must be zero or greater.");

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
            var (path, name) = useWpd ? (DestinationPath, nameof(DestinationPath)) : (AfcDestinationPath, nameof(AfcDestinationPath));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException($"Set {name} in appsettings.json beside the executable to the folder that should receive the backup.");
            return path;
        }

        private static string ValidateDestination(string? path, string name)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException($"{name} must be an absolute local or UNC folder path.");
            return Path.GetFullPath(path);
        }

        public void ValidateSourceFileCount(int foundFiles)
        {
            if (foundFiles < MinimumExpectedFiles)
            {
                throw new IOException($"Source scan found {foundFiles} files, below configured MinimumExpectedFiles of {MinimumExpectedFiles}. " +
                    "No files were copied. Check iPhone USB visibility, or update MinimumExpectedFiles if the photo library or SourcePath intentionally changed.");
            }
        }
    }
}
