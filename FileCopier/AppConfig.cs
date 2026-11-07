namespace FileCopier
{
    public class AppConfig
    {
        public string SourcePath { get; set; } = @"\Internal Storage";
        /// <summary>Backup folder for the WPD transport (--wpd), which mirrors SourcePath.</summary>
        public string DestinationPath { get; set; } = @"E:\Photos\DCIM\";
        /// <summary>Backup folder for the AFC transport (default), which mirrors the camera roll's 1xxAPPLE folders.</summary>
        public string AfcDestinationPath { get; set; } = @"E:\Photos\AFC\";
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
            if (string.Equals(Path.TrimEndingDirectorySeparator(DestinationPath),
                    Path.TrimEndingDirectorySeparator(AfcDestinationPath), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("DestinationPath and AfcDestinationPath must be different folders.");
        }

        public string GetDestinationPath(bool useWpd) => useWpd ? DestinationPath : AfcDestinationPath;

        private static string ValidateDestination(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
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
