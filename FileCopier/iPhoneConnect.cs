using MediaDevices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace FileCopier
{
    internal class iPhoneConnect
    {
        public const string DCIMFolder = @"\\Internal Storage\DCIM";
        private AppConfig _config;

        public iPhoneConnect(AppConfig config) 
        { 
            _config = config;
            CopyToBasePath = config.DestinationPath;
        }

        public string CopyToBasePath { get; set; }
        public int TotalFiles { get; set; } = 0;
        public ulong TotalFileSize { get; set; } = 0;
        public MediaDevice? Device { get; set; } = null;
        public bool IsPhoneAvailable { get => Device != null; }

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

        public void CopySequentially2()
        {
            if (Device == null) return;

            ConsoleUtils.WriteLine("\nPhase 1: Analyzing Source and Destination...");
            stopWatch.Restart();

            // 1. Analyze Source
            Console.WriteLine("Reading iPhone metadata...");
            var sourceFiles = new List<(MediaFileInfo Info, string RelativePath)>();
            
            // Try to find the DCIM folder or Root dynamically using Drives (to match legacy behavior)
            MediaDirectoryInfo sourceDir = null;
            try 
            {
                 // Priority: Match legacy CopySequentially logic
                 // Legacy: var drive = drives.FirstOrDefault(d => d.Name == "\\Internal Storage");
                 
                 var drives = Device.GetDrives();
                 var rootDrive = drives.FirstOrDefault(d => d.Name.Equals("\\Internal Storage", StringComparison.OrdinalIgnoreCase));
                 
                 if (rootDrive != null)
                 {
                     sourceDir = rootDrive.RootDirectory;
                     // Check if DCIM exists inside and if the user wants that instead
                     // But legacy just took RootDirectory. Let's start with Root.
                     
                     // If configured path is explicitly NOT Internal Storage, we might want to respect it,
                     // but currently appsettings is \Internal Storage.
                     ConsoleUtils.WriteSuccess($"Accessed drive: {rootDrive.Name}");
                 }
                 else
                 {
                     // Fallback to config path
                     sourceDir = Device.GetDirectoryInfo(_config.SourcePath);
                 }
            }
            catch(Exception ex)
            {
                ConsoleUtils.WriteError($"Error finding source directory: {ex.Message}");
            }

            if (sourceDir == null)
            {
                ConsoleUtils.WriteError($"Could not find DCIM folder. Please check connection and unlock phone.");
                Console.WriteLine("Available Drives/Folders:");
                try 
                {
                    var drives = Device.GetDrives();
                    foreach (var d in drives)
                    {
                        Console.WriteLine($" - Drive: {d.Name}");
                        if (d.RootDirectory != null)
                        {
                            foreach(var dir in d.RootDirectory.EnumerateDirectories())
                            {
                                 Console.WriteLine($"   - {dir.Name}");
                            }
                        }
                    }
                }
                catch {}
                return;
            }

            var sourceFolders = sourceDir.EnumerateDirectories().ToList();
            int totalFolderCount = sourceFolders.Count;
            Console.WriteLine($"Found {totalFolderCount} folders.");

            int totalSourceFilesFound = 0;
            int currentFolderIndex = 0;
            
            // Parallel Scanning
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = _config.ThreadCount };
            var cancellationTokenSource = new CancellationTokenSource();
            parallelOptions.CancellationToken = cancellationTokenSource.Token;
            
            // Use ConcurrentBag for thread safety during parallel execution
            var tempSourceFiles = new ConcurrentBag<(MediaFileInfo Info, string RelativePath)>();

            Console.WriteLine($"Scanning with {_config.ThreadCount} threads (Press 'Q' to abort)...");

            // Start a background task to monitor for 'Q' key
            var isScanning = true;
            Task.Run(() => 
            {
                while (isScanning && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        if (key.Key == ConsoleKey.Q)
                        {
                            ConsoleUtils.WriteWarning("\n\n'Q' pressed. Aborting analysis...");
                            cancellationTokenSource.Cancel();
                            break;
                        }
                    }
                    Thread.Sleep(200); // Check 5 times a second
                }
            });

            try
            {
                Parallel.ForEach(sourceFolders, parallelOptions, (folder) =>
                {
                    if (cancellationTokenSource.Token.IsCancellationRequested) return;

                    // Thread-safe update of folder index for progress (approximate is fine)
                    int myFolderIndex = Interlocked.Increment(ref currentFolderIndex);

                    try
                    {


                        // NOTE: Device concurrency depends on the library/driver support.
                        var files = folder.EnumerateFiles();
                        foreach (var file in files)
                        {
                            if (cancellationTokenSource.Token.IsCancellationRequested) return;

                            var relativePath = Path.Combine(folder.Name, file.Name);
                            tempSourceFiles.Add((file, relativePath));
                            int currentCount = Interlocked.Increment(ref totalSourceFilesFound);
                            
                            if (currentCount % 50 == 0) 
                            {
                                Console.Write($"\rScanning folder {myFolderIndex}/{totalFolderCount} ({folder.Name}) » Found {currentCount} files...     ");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!(ex is OperationCanceledException))
                           ConsoleUtils.WriteWarning($"\nError reading folder {folder.Name}: {ex.Message}");
                    }
                });
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\nAnalysis canceled.");
                return;
            }
            finally
            {
                isScanning = false;
            }
            
            // Add collected files to main list
            sourceFiles.AddRange(tempSourceFiles);

            Console.WriteLine($"\rFound {totalSourceFilesFound} files on iPhone.          ");

            // 2. Analyze Destination
            Console.WriteLine("Reading Destination metadata...");
            var destFilesMap = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            
            if (Directory.Exists(_config.DestinationPath))
            {
                var destDirInfo = new DirectoryInfo(_config.DestinationPath);
                // Get all files recursively
                var allDestFiles = destDirInfo.GetFiles("*.*", SearchOption.AllDirectories);
                
                foreach (var dFile in allDestFiles)
                {
                    // Calculate relative path from CopyToBasePath
                    // dFile.FullName: E:\Photos\DCIM\100APPLE\IMG_1234.JPG
                    // Base: E:\Photos\DCIM\
                    // Relative: 100APPLE\IMG_1234.JPG
                    
                    var relative = Path.GetRelativePath(_config.DestinationPath, dFile.FullName);
                    if (!destFilesMap.ContainsKey(relative))
                    {
                        destFilesMap[relative] = dFile;
                    }
                }
            }
            Console.WriteLine($"Found {destFilesMap.Count} files in Destination.");

            // 3. Phase 2: Diff
            ConsoleUtils.WriteLine("\nPhase 2: Calculating Differences...");
            var filesToCopy = new List<(MediaFileInfo Info, string RelativePath)>();
            
            foreach (var source in sourceFiles)
            {
                if (destFilesMap.TryGetValue(source.RelativePath, out var destFile))
                {
                    // File exists, check if we need to copy (Size or Date)
                    // Note: MTP LastWriteTime might differ slightly? Using size as primary check or exact match?
                    
                    bool sizeMatch = (ulong)destFile.Length == source.Info.Length;
                    // Comparison tolerance for time?
                    // Let's use the ToCopy logic inline or simplified
                    
                    bool timeMatch = destFile.LastWriteTime == source.Info.LastWriteTime;

                    if (!sizeMatch || !timeMatch)
                    {
                         // If size or time differs, we copy (overwrite)
                         // Actually, strictly speaking if size matches we might skip to save time if timestamps are flaky
                         // But reusing existing ToCopy logic principle:
                         // if (fileInfo.LastWriteTime != mediaFileInfo.LastWriteTime) returns true
                         // if ((ulong)fileInfo.Length != mediaFileInfo.Length) returns true
                         
                         filesToCopy.Add(source);
                    }
                }
                else
                {
                    // New file
                    filesToCopy.Add(source);
                }
            }

            ConsoleUtils.WriteLine($"Phase 3: Copying {filesToCopy.Count} files...");
            
            // 4. Phase 3: Copy
            int currentCount = 0;
            int totalToCopy = filesToCopy.Count;
            int skipped = 0;
            int errorsCount = 0;
            ulong processedBytes = 0;
            ulong totalBytesToCopy = 0;
            filesToCopy.ForEach(f => totalBytesToCopy += f.Info.Length);

            foreach (var item in filesToCopy)
            {
                 // Graceful Exit Check
                 if (Console.KeyAvailable)
                 {
                     var key = Console.ReadKey(true);
                     if (key.Key == ConsoleKey.Q)
                     {
                         ConsoleUtils.WriteWarning("\n\n'Q' pressed. Stopping gracefully...");
                         break;
                     }
                 }

                 currentCount++;
                 var sourceFile = item.Info;
                 var relativePath = item.RelativePath;
                 var destFilePath = Path.Combine(_config.DestinationPath, relativePath);
                 var destFolder = Path.GetDirectoryName(destFilePath);

                 try
                 {
                     if (destFolder != null && !Directory.Exists(destFolder))
                     {
                         Directory.CreateDirectory(destFolder);
                     }

                     Device.DownloadFile(sourceFile.FullName, destFilePath);
                     
                     // Set timestamps
                     File.SetCreationTime(destFilePath, (DateTime)sourceFile.CreationTime);
                     File.SetLastWriteTime(destFilePath, (DateTime)sourceFile.LastWriteTime);

                     processedBytes += sourceFile.Length;
                     
                     // Progress logging
                     ConsoleUtils.ClearLine();
                     ConsoleUtils.Write($"\r{elapsedTimeString} {ConsoleUtils.CYAN}{processedBytes.ToSize()} / {totalBytesToCopy.ToSize()} » ({currentCount}/{totalToCopy}) » {relativePath}");
                 }
                 catch (Exception ex)
                 {
                     errorsCount++;
                     Errors.Add((sourceFile.FullName, ex.Message));
                     ConsoleUtils.WriteError($"\nError copying {relativePath}: {ex.Message}");
                 }
            }

            stopWatch.Stop();
            ConsoleUtils.WriteLine("\n\nFinished.");
            ConsoleUtils.WriteLine($"Copied: {currentCount - errorsCount - skipped}");
            ConsoleUtils.WriteLine($"Errors: {errorsCount}");
        }
    }
}
