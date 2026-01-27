using MediaDevices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FileCopier
{
    internal class iPhoneConnect
    {
        public const string DCIMFolder = @"\\Internal Storage\DCIM";
        public string CopyToBasePath { get; set; } = @"E:\Photos\DCIM\";
        //public string CopyToBasePath { get; set; } = @"\\nas\home\Photos\iPhone 12 Pro\";

        public int TotalFiles { get; set; } = 0;
        public ulong TotalFileSize { get; set; } = 0;

        public MediaDevice? Device { get; set; } = null;

        public bool IsPhoneAvailable { get => Device != null; }

        public iPhoneConnect() { }

        public string[] Folders { get; set; } = new string[0];
        public int FolderCount { get => Folders.Length; }

        public List<string> Files { get; set; } = new List<string>();
        public int FilesCount { get => Files.Count; }

        public List<MediaFileInfo> FilesInfo { get; set; } = new List<MediaFileInfo>();

        public List<(string FileName, string Message)> Errors { get; set; } = new List<(string FileName, string Message)>();

        Stopwatch stopWatch = new Stopwatch();

        string elapsedTimeString {
            get {
                stopWatch.Stop();
                var val = $"{ConsoleUtils.GREY}{stopWatch.Elapsed.Hours:00}:{stopWatch.Elapsed.Minutes:00}:{stopWatch.Elapsed.Seconds:00}{ConsoleUtils.NORMAL}";
                stopWatch.Start();
                return val;
            }
        }
        public bool Connect()
        {
            var devices = MediaDevice.GetDevices();
            Device = devices?.FirstOrDefault(d => d.FriendlyName == "Apple iPhone");
            if (Device == null) return false;

            Device.Connect();
            var drives = Device.GetDrives();
            var drive = drives.FirstOrDefault(d => d.Name == "\\Internal Storage");

            var root = drive?.RootDirectory;

            var folders = root?.EnumerateDirectories();

            //Folders = Device.GetDirectories(DCIMFolder);
            return true;
        }
        public void Disconnect()
        {
            Device?.Disconnect();
        }

        public bool CollectFiles()
        {
            
            ConsoleUtils.WriteLine("\nCollecting file names...");
            if (!IsPhoneAvailable) return false;
            Files.Clear();
            var processedFolderCount = 0;
            var collectedFiilesCount = 0;
            foreach (var folder in Folders)
            {
                var files = Device.GetFiles(folder);
                Files.AddRange(files);
                collectedFiilesCount += files.Length;
                processedFolderCount++;


                
                ConsoleUtils.WriteProgress($"{elapsedTimeString} {ConsoleUtils.CYAN}{collectedFiilesCount.ToString("#,##0")} {ConsoleUtils.YELLOW}files, folder", processedFolderCount, FolderCount);
                

            }
            TotalFileSize = 0;
            TotalFiles = Files.Count;
            return true;
        }
        public bool CollectFilesInfo()
        {
            if (!CollectFiles()) return false;
            ConsoleUtils.WriteLine("\nCollecting fileinfo...");
            FilesInfo.Clear();
            var processedFilesCount = 0;
            ulong collectedFiileSize = 0;
            foreach (var file in Files)
            {
                var fileInfo = Device.GetFileInfo(file);
                FilesInfo.Add(fileInfo);

                collectedFiileSize += fileInfo.Length;
                processedFilesCount++;

                ConsoleUtils.WriteProgress($"{elapsedTimeString} {ConsoleUtils.CYAN}{collectedFiileSize.ToSize()} {ConsoleUtils.YELLOW}file", processedFilesCount, TotalFiles);

            }
            TotalFileSize = (ulong)FilesInfo.Sum(x => (long)x.Length);
            TotalFiles = FilesInfo.Count;

            return true;
        }
        static bool ToCopy(MediaFileInfo mediaFileInfo, string destFile)
        {

            //Get file info of destFile
            var fileInfo = new FileInfo(destFile);
            if (fileInfo == null) return true;
            //If file does not exist, copy
            if (!fileInfo.Exists)
                return true;
            //If file exists, check date
            if (fileInfo.LastWriteTime != mediaFileInfo.LastWriteTime)
                return true;
            //If date is same, check size
            if ((ulong)fileInfo.Length != mediaFileInfo.Length)
                return true;
            //If size is same, do not copy
            return false;
        }
        public void CopyFiles()
        {
            if (!IsPhoneAvailable) return;
            stopWatch = new Stopwatch();
            ConsoleUtils.WriteLine("\nCopying files...");
            CollectFilesInfo();

            var processedFileCount = 0;
            ulong processedFileSize = 0;

            foreach (var fileInfo in FilesInfo)
            {

                try
                {
                    string folderName = Path.GetFileName(fileInfo.Directory.FullName);
                    var destFolder = Path.Combine(CopyToBasePath, folderName);
                    var destFile = Path.Combine(destFolder, fileInfo.Name);

                    Directory.CreateDirectory(destFolder);

                    if (ToCopy(fileInfo, destFile))
                    {
                        Device.DownloadFile(fileInfo.FullName, destFile);
                        //Set file date time based on fileInfo
                        File.SetCreationTime(destFile, (DateTime)fileInfo.CreationTime);
                        File.SetLastWriteTime(destFile, (DateTime)fileInfo.LastWriteTime);
                    }

                    processedFileCount++;
                    processedFileSize += fileInfo.Length;
                    //·»■▀▄□▪○●▬
                    //█▌▐░▒▓
                    ConsoleUtils.ClearLine();

                    ConsoleUtils.Write($"\r{elapsedTimeString} {ConsoleUtils.CYAN}{processedFileSize.GetPercentageCompleted(TotalFileSize)}{ConsoleUtils.YELLOW}, {processedFileSize.ToSize()} / {TotalFileSize.ToSize()} » File ({processedFileCount}/{TotalFiles}) » {destFile} ({fileInfo.Length.ToSize()}) ");
                }
                catch (Exception e)
                {

                    Errors.Add((fileInfo.FullName, e.Message));
                    ConsoleUtils.WriteError($"\n{fileInfo.FullName} {e.Message}");
                }

            }
            stopWatch.Stop();
            Console.ResetColor();
        }
        public void CopySequentially()
        {
            ConsoleUtils.WriteLine("\nCopying files...");
            stopWatch = new Stopwatch();
            //var totalFiles = 0;
            //ulong totalProcessedSize = 0;

            //var drives = device.GetDrives();
            //var folders = Device.GetDirectories(DCIMFolder);

            var drives = Device?.GetDrives();
            var drive = drives?.FirstOrDefault(d => d.Name == "\\Internal Storage");
            var root = drive?.RootDirectory;

            var directories = root?.EnumerateDirectories();
            var folders = directories?.Select(x => x.FullName).ToArray();  

            var folderCount = folders?.Length;
            var curFolderCount = 1;
            var errorFileCount = 0;
            var skippedFileCount = 0;
            //var errorFiles = new StringBuilder();
            try
            {

                if (folders != null) 
                foreach (var folder in folders)
                {
                    //var destFolder = $"{CopyToBasePath}{folder.Replace(@"\Internal Storage\DCIM\", "")}";

                    string folderName = Path.GetFileName(folder);
                    var destFolder = Path.Combine(CopyToBasePath, folderName);

                    Directory.CreateDirectory(destFolder);
                    var files = Device.GetFiles(folder);
                    var fileCount = files.Length;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    ConsoleUtils.ClearLine();
                    Console.Write($"\r{folder} ({fileCount})");
                    var curFileCount = 1;
                    foreach (var file in files)
                    {

                        try
                        {
                            var fileInfo = Device.GetFileInfo(file);
                            ulong fileSize = fileInfo.Length;


                            var destFilePath = Path.Combine(destFolder, fileInfo.Name);
                            if (ToCopy(fileInfo, destFilePath))
                            {
                                Device.DownloadFile(file, destFilePath);
                                //Set file date time based on fileInfo
                                File.SetCreationTime(destFilePath, (DateTime)fileInfo.CreationTime);
                                File.SetLastWriteTime(destFilePath, (DateTime)fileInfo.LastWriteTime);
                            }
                            else
                            {
                                skippedFileCount++;
                            }

                            TotalFiles++;
                            TotalFileSize += fileSize;
                            //·»■▀▄□▪○●▬
                            //█▌▐░▒▓
                            ConsoleUtils.ClearLine();
                            var statsStr = skippedFileCount > 0 ? $" {ConsoleUtils.YELLOW}» {ConsoleUtils.BLUE}{skippedFileCount} Skipped" : "";
                            statsStr += errorFileCount > 0 ? $" » {ConsoleUtils.RED}{errorFileCount} Errors" : "";
                            var fileRelativePath = Path.Combine(folderName, fileInfo.Name);
                            ConsoleUtils.Write($"\r{elapsedTimeString} {ConsoleUtils.CYAN}{TotalFileSize.ToSize()}{statsStr} {ConsoleUtils.YELLOW}» File ({curFileCount}/{fileCount}) in folder ({curFolderCount}/{folderCount}) » {ConsoleUtils.GREEN}{fileRelativePath} {ConsoleUtils.CYAN}({fileSize.ToSize()}) ");
                        }
                        catch (Exception ex)
                        {
                            errorFileCount++;
                            Console.ForegroundColor = ConsoleColor.Red;
                            //errorFiles.AppendLine(file);
                            ConsoleUtils.WriteError($"\n{file} {ex.Message}");
                            Errors.Add((file, ex.Message));
                        }
                        curFileCount++;
                    }
                    curFolderCount++;
                }
            }
            catch (Exception e)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                ConsoleUtils.WriteError("\n" + e.Message);
            }
            /*if (errorFiles.Length > 0)
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
            }*/
            Console.ResetColor();
            stopWatch.Stop();

        }
    }
}
