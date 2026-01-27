namespace FileCopier
{
    public class AppConfig
    {
        public string SourcePath { get; set; } = @"\\Internal Storage\DCIM";
        public string DestinationPath { get; set; } = @"E:\Photos\DCIM\";
        public int ThreadCount { get; set; } = 10;
    }
}
