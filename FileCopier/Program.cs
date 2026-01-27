using MediaDevices;
using System.IO;
using System.Text;

namespace FileCopier
{
    internal class Program
    {
        static void Main(string[] args)
        {

            Console.WriteLine("iPhone Photo Copier, Calibre Technologies");
            
            var iPhone = new iPhoneConnect();
            iPhone.Connect();
            try
            {
                Console.WriteLine($"Phone: {iPhone.Device.FriendlyName}");
                Console.WriteLine($"Source Path: {iPhoneConnect.DCIMFolder}");
                Console.WriteLine($"Destination Path: {iPhone.CopyToBasePath}");
                iPhone.CopySequentially();
                
                //iPhone.CopyFiles();
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

            Console.WriteLine("Done");
        }
        /*
        public static void FindiPhoneDrive()
        {
            var DCIMFolder = @"\Internal Storage\DCIM";
            var CopyToBasePath = @"D:\Photos\DCIM\";
            var totalFiles = 0;
            ulong totalProcessedSize = 0;
            var devices = MediaDevice.GetDevices();
            using (var device = devices.First(d => d.FriendlyName == "Apple iPhone"))
            {
                device.Connect();
                //var drives = device.GetDrives();
                var folders = device.GetDirectories(DCIMFolder);
                var folderCount = folders.Length;
                var curFolderCount = 1;
                var errorFileCount = 0;
                var errorFiles = new StringBuilder();
                try
                {


                    foreach (var folder in folders)
                    {
                        var destFolder = $"{CopyToBasePath}{folder.Replace(@"\Internal Storage\DCIM\", "")}";
                        Directory.CreateDirectory(destFolder);
                        var files = device.GetFiles(folder);
                        var fileCount = files.Length;
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        ClearLine();
                        Console.Write($"\r{folder} ({fileCount})");
                        var curFileCount = 1;
                        foreach (var file in files)
                        {
                            var filename = Path.GetFileName(file);
                            try
                            {
                                var fileInfo = device.GetFileInfo(file);
                                ulong fileSize = fileInfo.Length;
                                
                                //Console.WriteLine($"{file} {fileSize.ToSize(MyExtension.SizeUnits.MB)} File ({curFileCount}/{fileCount}) in folder ({curFolderCount}/{folderCount})");
                                var destFilePath = $"{destFolder}\\{filename}";
                                if (ToCopy(fileInfo, destFilePath))
                                {
                                    device.DownloadFile(file, destFilePath);
                                    //Set file date time based on fileInfo
                                    File.SetCreationTime(destFilePath, (DateTime)fileInfo.CreationTime);
                                    File.SetLastWriteTime(destFilePath, (DateTime)fileInfo.LastWriteTime);
                                }
                                
                                totalFiles++;
                                totalProcessedSize += fileSize;
                                //·»■▀▄□▪○●▬
                                //█▌▐░▒▓
                                ClearLine();
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.Write($"\r{totalProcessedSize.ToSize(MyExtension.SizeUnits.GB)} » File ({curFileCount}/{fileCount}) in folder ({curFolderCount}/{folderCount}) » {file} ({fileSize.ToSize(MyExtension.SizeUnits.MB)}) ");
                            }
                            catch (Exception ex)
                            {
                                errorFileCount++;
                                Console.ForegroundColor = ConsoleColor.Red;
                                errorFiles.AppendLine(file);
                                Console.WriteLine($"\n{file}");
                                Console.WriteLine(ex.Message);
                            }
                            curFileCount++;
                        }
                        curFolderCount++;
                    }
                }
                catch (Exception e)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n"+e.Message);
                }
                if (errorFiles.Length > 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\nFiles copied with error. Copied {totalFiles} files in {folderCount} folders");
                    
                    Console.WriteLine($"Could not copy {errorFileCount} files:");
                    Console.WriteLine(errorFiles.ToString());
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Blue;
                    Console.WriteLine($"\nFiles copied successfully: {totalFiles} files in {folderCount} folders");
                }
                Console.ResetColor();

                device.Disconnect();
            }
        }
        */

    }

}