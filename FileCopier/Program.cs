using MediaDevices;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace FileCopier
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

            IConfiguration config = builder.Build();
            var appConfig = config.Get<AppConfig>();

            Console.WriteLine("iPhone Photo Copier, Calibre Technologies");
            
            var iPhone = new iPhoneConnect(appConfig);
            if (iPhone.Connect())
            {
                try
                {
                    Console.WriteLine($"Phone: {iPhone.Device.FriendlyName}");
                    Console.WriteLine($"Source Path: {appConfig.SourcePath}");
                    Console.WriteLine($"Destination Path: {appConfig.DestinationPath}");
                    iPhone.CopySequentially2();
                }
                finally
                {
                    iPhone.Disconnect();
                }
                if (iPhone.Errors.Count > 0)
                {
                    
                    ConsoleUtils.WriteError($"\nFiles copied with error. Copied {iPhone.TotalFiles} files in {iPhone.FolderCount} folders");
                    ConsoleUtils.WriteWarning($"Could not copy {iPhone.Errors.Count} files:");
                    iPhone.Errors.ForEach(x => ConsoleUtils.WriteWarning(x.FileName));
                    
                }
                else
                {

                    ConsoleUtils.WriteSuccess($"\nFiles copied successfully: {iPhone.TotalFiles} files in {iPhone.FolderCount} folders");
                }
            }
            else 
            {
                 ConsoleUtils.WriteError("Could not connect to iPhone.");
            }

            Console.WriteLine("Done");
        }


    }

}