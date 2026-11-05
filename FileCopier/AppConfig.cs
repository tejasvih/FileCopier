namespace FileCopier
{
    public class AppConfig
    {
        public string SourcePath { get; set; } = @"\Internal Storage";
        public string DestinationPath { get; set; } = @"E:\Photos\DCIM\";
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

            if (string.IsNullOrWhiteSpace(DestinationPath) || !Path.IsPathFullyQualified(DestinationPath))
                throw new ArgumentException("DestinationPath must be an absolute local or UNC folder path.");

            DestinationPath = Path.GetFullPath(DestinationPath);
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
