﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using krrTools.Beatmaps;
using krrTools.Bindable;
using Microsoft.Extensions.Logging;
using OsuParsers.Decoders;
using Application = System.Windows.Application;

namespace krrTools.Tools.FilesManager
{
    public partial class FilesManagerViewModel : ReactiveViewModelBase
    {
        public Bindable<ObservableCollection<FilesManagerInfo>> OsuFiles { get; set; } = new(new ObservableCollection<FilesManagerInfo>());
        public Bindable<ICollectionView> FilteredOsuFiles { get; set; }
        public Bindable<bool> IsProcessing { get; set; } = new();
        public Bindable<int> ProgressValue { get; set; } = new();
        public Bindable<int> ProgressMaximum { get; set; } = new(100);
        public Bindable<string> ProgressText { get; set; } = new(string.Empty);
        public Bindable<string> SelectedFolderPath { get; set; } = new("");

        public FilesManagerViewModel()
        {
            // 初始化数据源
            FilteredOsuFiles = new Bindable<ICollectionView>(CollectionViewSource.GetDefaultView(OsuFiles.Value));

            // 设置自动绑定通知
            SetupAutoBindableNotifications();
        }


        [RelayCommand]
        private async Task SetSongsFolderAsync()
        {
            var selectedPath = FilesHelper.ShowFolderBrowserDialog("Please select the osu! Songs folder");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                SelectedFolderPath.Value = selectedPath;
                await ProcessFilesAsync(BeatmapFileHelper.EnumerateOsuFiles([selectedPath]).ToArray());
            }
        }

        public async void ProcessDroppedFiles(string[] files)
        {
            try
            {
                var allOsuFiles = BeatmapFileHelper.EnumerateOsuFiles(files).ToArray();
                await ProcessFilesAsync(allOsuFiles);
            }
            catch (Exception ex)
            {
                Logger.WriteLine(LogLevel.Error, "[FilesManagerViewModel] ProcessDroppedFiles error: {0}", ex.Message);
                ProgressText.Value = $"处理出错: {ex.Message}";
            }
        }

        public async Task ProcessFilesAsync(string[] files)
        {
            IsProcessing.Value = true;
            ProgressValue.Value = 0;
            ProgressText.Value = "Loading...";

            try
            {
                var validFiles = files.Where(f =>
                    File.Exists(f) && Path.GetExtension(f).Equals(".osu", StringComparison.OrdinalIgnoreCase)).ToArray();
                ProgressMaximum.Value = validFiles.Length;
                ProgressText.Value = $"Found {validFiles.Length} files, processing...";

                // Set selected folder path to the directory of the first file
                if (validFiles.Length > 0) SelectedFolderPath.Value = Path.GetDirectoryName(validFiles[0]) ?? "";

                // Clear existing data
                OsuFiles.Value.Clear();

                // 分批处理文件，避免UI冻结
                const int batchSize = 50;
                var batch = new ObservableCollection<FilesManagerInfo>();

                for (var i = 0; i < validFiles.Length; i++)
                {
                    try
                    {
                        // 在后台线程解析文件
                        var fileInfo = await Task.Run(() => ParseOsuFile(validFiles[i]));

                        if (fileInfo != null) batch.Add(fileInfo);
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine(LogLevel.Error, "[FilesManagerViewModel] 解析文件 {0} 时出错: {1}", validFiles[i],
                            ex.Message);
                    }

                    // 更新进度
                    ProgressValue.Value = i + 1;
                    ProgressText.Value = $"正在处理 {i + 1}/{validFiles.Length}...";

                    // 每处理一批就更新UI
                    if (batch.Count >= batchSize || i == validFiles.Length - 1)
                    {
                        // 在UI线程上更新数据
                        Application.Current.Dispatcher.Invoke((Action)(() =>
                        {
                            foreach (var item in batch) OsuFiles.Value.Add(item);
                        }));

                        batch.Clear();

                        // UI更新
                        await Task.Delay(1);
                    }
                }

                ProgressText.Value = $"完成处理 {validFiles.Length} 个文件";
            }
            catch (Exception ex)
            {
                Logger.WriteLine(LogLevel.Error, "[FilesManagerViewModel] 读取文件时出错: {0}", ex.Message);
                ProgressText.Value = $"处理出错: {ex.Message}";
            }
            finally
            {
                IsProcessing.Value = false;
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    FilteredOsuFiles.Value = CollectionViewSource.GetDefaultView(OsuFiles.Value);
                    Logger.WriteLine(LogLevel.Information,
                        "[FilesManagerViewModel] FilteredOsuFiles refreshed in ProcessFilesAsync, count: {0}",
                        FilteredOsuFiles.Value.Cast<object>().Count());
                });
            }
        }

        private FilesManagerInfo? ParseOsuFile(string filePath)
        {
            try
            {
                var beatmap = BeatmapDecoder.Decode(filePath);

                var fileInfo = new FilesManagerInfo
                {
                    FilePath = { Value = filePath },
                    Title = { Value = beatmap.MetadataSection.Title ?? string.Empty },
                    Artist = { Value = beatmap.MetadataSection.Artist ?? string.Empty },
                    Creator = { Value = beatmap.MetadataSection.Creator ?? string.Empty },
                    OD = { Value = beatmap.DifficultySection.OverallDifficulty },
                    HP = { Value = beatmap.DifficultySection.HPDrainRate },
                    Keys = (int)beatmap.DifficultySection.CircleSize,
                    Diff = { Value = beatmap.MetadataSection.Version ?? string.Empty },
                    BeatmapID = beatmap.MetadataSection.BeatmapID,
                    BeatmapSetID = beatmap.MetadataSection.BeatmapSetID
                };

                return fileInfo;
            }
            catch (Exception ex)
            {
                Logger.WriteLine(LogLevel.Error, "[FilesManagerViewModel] 解析文件 {0} 时出错: {1}", filePath, ex.Message);
                return null;
            }
        }

        // 定义OsuFileInfo类来存储解析后的数据


    }
}