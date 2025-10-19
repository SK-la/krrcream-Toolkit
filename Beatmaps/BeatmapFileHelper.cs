using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using krrTools.Localization;
using Microsoft.Extensions.Logging;
using OsuParsers.Beatmaps;

namespace krrTools.Beatmaps
{
    /// <summary>
    /// Beatmap 文件相关工具类
    /// </summary>
    public static class BeatmapFileHelper
    {
        /// <summary>
        /// 判断文件路径是否为有效的 .osu 文件
        /// </summary>
        public static bool IsValidOsuFile(string? filePath)
        {
            return !string.IsNullOrEmpty(filePath) && File.Exists(filePath) &&
                   Path.GetExtension(filePath).Equals(".osu", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 统计路径集合中的 .osu 文件数量（包括 .osz 压缩包）
        /// </summary>
        public static int GetOsuFilesCount(IEnumerable<string> paths)
        {
            var count = 0;
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path);
                    if (string.Equals(ext, ".osu", StringComparison.OrdinalIgnoreCase))
                        count++;
                    else if (string.Equals(ext, ".osz", StringComparison.OrdinalIgnoreCase))
                        try
                        {
                            using var archive = System.IO.Compression.ZipFile.OpenRead(path);
                            count += archive.Entries.Count(e =>
                                e.Name.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));
                        }
                        catch
                        {
                            Logger.WriteLine(LogLevel.Error, "[BeatmapFileHelper] Error opening .osz file: {0}", path);
                        }
                }
                else if (Directory.Exists(path))
                {
                    count += Directory.EnumerateFiles(path, "*.osu", SearchOption.AllDirectories).Count();
                }
            }
            return count;
        }

        /// <summary>
        /// 遍历路径集合中的所有 .osu 文件
        /// </summary>
        public static IEnumerable<string> EnumerateOsuFiles(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path) && Path.GetExtension(path).Equals(".osu", StringComparison.OrdinalIgnoreCase))
                    yield return path;
                else if (Directory.Exists(path))
                    foreach (var file in Directory.EnumerateFiles(path, "*.osu", SearchOption.AllDirectories))
                        yield return file;
            }
        }

        /// <summary>
        /// 将 Beatmap 写入到指定文件路径（安全版本，处理路径长度、目录创建、文件冲突）
        /// 如果提供 filename，则 path 视为目录，filename 为文件名
        /// </summary>
        /// <param name="beatmap">要写入的 Beatmap 对象</param>
        /// <param name="path">输出目录或完整路径</param>
        /// <param name="filename">文件名（可选，如果提供则 path 为目录）</param>
        /// <returns>是否写入成功</returns>
        public static bool SaveBeatmapToFile(Beatmap beatmap, string path, string? filename = null)
        {
            string outputPath = filename != null ? Path.Combine(path, filename) : path;
            try
            {
                // 处理路径过长问题：截断文件名而不是跳过
                if (outputPath.Length > 255)
                {
                    string directory = Path.GetDirectoryName(outputPath) ?? "";
                    string fileName = Path.GetFileName(outputPath);
                    string filenameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string extension = Path.GetExtension(fileName);
                    int maxFilenameLength = 255 - directory.Length - 1 - extension.Length; // 1 for backslash
                    if (maxFilenameLength > 3 && filenameWithoutExt.Length > maxFilenameLength)
                    {
                        filenameWithoutExt = filenameWithoutExt.Substring(0, maxFilenameLength - 3) + "...";
                        fileName = filenameWithoutExt + extension;
                        outputPath = Path.Combine(directory, fileName);
                        Console.WriteLine($"路径过长，已截断文件名: {outputPath}");
                    }
                    else
                    {
                        Console.WriteLine($"输出路径过长，无法处理: {outputPath}");
                        return false;
                    }
                }
                // 确保输出目录存在
                string? directoryPath = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }
                // 处理文件冲突：如果存在旧转换文件，删除它
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                    Console.WriteLine($"删除旧转换文件: {outputPath}");
                }
                // 使用 beatmap.Save 方法写入文件
                beatmap.Save(outputPath);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"写入 Beatmap 文件失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 生成标准化的输出文件路径
        /// </summary>
        /// <param name="inputPath">输入文件路径</param>
        /// <param name="suffix">文件名后缀</param>
        /// <returns>输出文件路径</returns>
        public static string GenerateOutputPath(string inputPath, string suffix)
        {
            string directory = Path.GetDirectoryName(inputPath) ?? ".";
            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputPath);
            string extension = Path.GetExtension(inputPath);
            return Path.Combine(directory, $"{fileNameWithoutExtension}_{suffix}{extension}");
        }
        
                public static string? AddNewBeatmapToSongFolder(string newBeatmapFile, bool openOsz = false)
        {
            // 获取.osu文件所在的目录作为歌曲文件夹
            string? songFolder = Path.GetDirectoryName(newBeatmapFile);
            if (string.IsNullOrEmpty(songFolder))
            {
                Logger.WriteLine(LogLevel.Error, Strings.InvalidBeatmapFilePath.Localize() + ": " + newBeatmapFile);
                return null;
            }

            Logger.WriteLine(LogLevel.Debug,$"OsuAnalyzer{songFolder}");

            // 创建.osz文件
            string outputOsz = Path.GetFileName(songFolder) + ".osz";
            string? parentDir = Path.GetDirectoryName(songFolder);
            if (string.IsNullOrEmpty(parentDir))
            {
                Logger.WriteLine(LogLevel.Error, Strings.UnableToDetermineParentDirectory.Localize() + ": " + songFolder);
                return null;
            }

            string fullOutputPath = Path.Combine(parentDir, outputOsz);

            if (File.Exists(fullOutputPath))
                File.Delete(fullOutputPath);

            try
            {
                // Ensure source directory exists before creating archive
                if (!Directory.Exists(songFolder))
                {
                    Logger.WriteLine(LogLevel.Error, Strings.SourceSongFolderDoesNotExist.Localize() + ": " + songFolder);
                    return null;
                }

                ZipFile.CreateFromDirectory(songFolder, fullOutputPath);
            }
            catch (Exception e)
            {
                Logger.WriteLine(LogLevel.Error, $"Failed to create {fullOutputPath} {Environment.NewLine}{Environment.NewLine}{e.Message}");
                return null;
            }

            // 2. 加入新的谱面文件到.osz
            try
            {
                using ZipArchive archive = ZipFile.Open(fullOutputPath, ZipArchiveMode.Update);
                archive.CreateEntryFromFile(newBeatmapFile, Path.GetFileName(newBeatmapFile));
            }
            catch (Exception e)
            {
                Logger.WriteLine(LogLevel.Error, Strings.FailedToAddBeatmapToArchive.Localize() + ": " + Environment.NewLine + Environment.NewLine + e.Message);
                return null;
            }

            // 3. 删除原本谱面
            try
            {
                if (File.Exists(newBeatmapFile))
                {
                    File.Delete(newBeatmapFile);
                }
            }
            catch (Exception e)
            {
                Logger.WriteLine(LogLevel.Warning, Strings.FailedToDeleteTemporaryBeatmapFile.Localize() + ": " + newBeatmapFile + " " + Environment.NewLine + Environment.NewLine + e.Message);
            }

            // 4. 打开 .osz（仅当调用方请求时）
            if (openOsz)
            {
                Process proc = new Process();
                proc.StartInfo.FileName = fullOutputPath;
                proc.StartInfo.UseShellExecute = true;
                try
                {
                    proc.Start();
                }
                catch
                {
                    Logger.WriteLine(LogLevel.Error, "There was an error opening the generated .osz file. This is probably because .osz files have not been configured to open with osu!.exe on this system." + Environment.NewLine + Environment.NewLine +
                                    "To fix this, download any map from the website, right click the .osz file, click properties, beside Opens with... click Change..., and select osu!. " +
                                    "You'll know the problem is fixed when you can double click .osz files to open them with osu!");
                }
            }

            // return the created osz path as success indicator
            return fullOutputPath;
        }
    }
}