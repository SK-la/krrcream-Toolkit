using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ClosedXML.Excel;
using CommunityToolkit.Mvvm.Input;
using krrTools.Beatmaps;
using krrTools.Bindable;
using krrTools.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using OsuParsers.Beatmaps;
using OsuParsers.Decoders;

namespace krrTools.Tools.KRRLVAnalysis
{
    /// <summary>
    /// LV分析视图模型
    /// 显示所有内容，不进行过滤，支持批量处理和进度更新
    /// 非mania谱和分析失败的谱面会显示对应状态，而不是过滤掉
    /// </summary>
    public partial class KRRLVAnalysisViewModel : ReactiveViewModelBase
    {
        // private readonly SemaphoreSlim _semaphore = new(100, 100);

        [Inject] private StateBarManager StateBarManager { get; set; } = null!;

        public Bindable<string> PathInput { get; set; } = new(string.Empty);

        public Bindable<ObservableCollection<KRRLVAnalysisItem>> OsuFiles { get; set; } =
            new(new ObservableCollection<KRRLVAnalysisItem>());

        private int _advancedAnalysisCompletedCount;
        
        // 批处理并发：50个文件为一组并行处理
        private const int BatchSize = 50;

        private Bindable<int> TotalCount { get; set; } = new();
        public Bindable<bool> IsProcessing { get; set; } = new();

        public KRRLVAnalysisViewModel()
        {
            // 自动注入标记了 [Inject] 的属性
            this.InjectServices();

            // 设置自动绑定通知
            SetupAutoBindableNotifications();

            // 优化线程池配置，平衡并发和单任务性能
            // 适度的线程配置，避免过度并发导致的资源竞争
            ThreadPool.SetMinThreads(Environment.ProcessorCount / 4, Environment.ProcessorCount / 4);
            ThreadPool.SetMaxThreads(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        }

        private Task CreateCombinedAnalysisTask(KRRLVAnalysisItem item)
        {
            return Task.Run(async () =>
            {
                try
                {
                    // 创建beatmap对象，整个分析过程复用
                    // using var beatmapWrapper = BeatmapWrapper.Create(item.FilePath!);
                    // if (beatmapWrapper?.Beatmap == null)
                    var beatmap = BeatmapDecoder.Decode(item.FilePath!);
                    if (beatmap == null)
                    {
                        item.ErrorMessage = $"无法解析文件: {item.FilePath}";
                        item.Phase = AnalysisStatus.Error;
                        return;
                    }

                    // var beatmap = beatmapWrapper.Beatmap;

                    // 第一阶段：处理基础信息，完成后立即更新UI
                    await item.LoadBasicInfoAsync(beatmap);

                    // 第二阶段：处理高级分析（基础信息已完成，UI已更新）
                    await ProcessAdvancedAnalysisAsync(item, beatmap);

                    // 高级分析完成后更新进度计数器
                    Interlocked.Increment(ref _advancedAnalysisCompletedCount);
                    
                    // 更新进度条
                    var completed = _advancedAnalysisCompletedCount;
                    var total = TotalCount.Value;
                    if (total > 0)
                    {
                        var progress = (double)completed / total * 100;
                        StateBarManager.ProgressValue.Value = progress;
                    }
                }
                catch (Exception ex)
                {
                    item.ErrorMessage = $"分析错误: {ex.Message}";
                    item.Phase = AnalysisStatus.Error;
                    // 即使出错也要更新计数器，避免进度条卡住
                    Interlocked.Increment(ref _advancedAnalysisCompletedCount);        
                }
            });
        }

        public async void ProcessDroppedFiles(string[] files)
        {
            try
            {
                IsProcessing.Value = true;

                var stopwatch = Stopwatch.StartNew();

                // 清空现有数据，避免追加问题
                OsuFiles.Value.Clear();
                _advancedAnalysisCompletedCount = 0;
                StateBarManager.ProgressValue.Value = 0;

                var preparationStopwatch = Stopwatch.StartNew();
                
                // 异步执行文件枚举并创建分析项目，避免阻塞UI
                var allAnalysisItems = await Task.Run(() =>
                {
                    return BeatmapFileHelper.EnumerateOsuFiles(files)
                        .Select(file => new KRRLVAnalysisItem
                        {
                            FilePath = file,
                            Phase = AnalysisStatus.Waiting
                        }).ToList();
                });

                TotalCount.Value = allAnalysisItems.Count;

                preparationStopwatch.Stop();
                Logger.WriteLine(LogLevel.Information,
                    $"[KRRLVAnalysisViewModel] 准备阶段完成: {allAnalysisItems.Count} 个文件，耗时 {preparationStopwatch.ElapsedMilliseconds}ms");

                // 立即添加所有项目到UI，避免延迟
                foreach (var item in allAnalysisItems)
                    OsuFiles.Value.Add(item);

                // 合并为单个task：每个文件先处理基础信息（立即更新UI），再处理高级分析
                var allAnalysisTasks = new List<Task>();

                // 批处理并发：每50个文件一组
                for (int i = 0; i < allAnalysisItems.Count; i += BatchSize)
                {
                    var batch = allAnalysisItems.Skip(i).Take(BatchSize);
                    var batchTasks = batch.Select(item => CreateCombinedAnalysisTask(item)).ToList();
                    allAnalysisTasks.AddRange(batchTasks);
                }

                // 追踪处理性能
                var analysisStopwatch = Stopwatch.StartNew();

                var analysisTask = Task.WhenAll(allAnalysisTasks).ContinueWith(_ =>
                {
                    analysisStopwatch.Stop();
                    Logger.WriteLine(LogLevel.Information, $"[KRRLVAnalysisViewModel] 所有分析完成，耗时 {analysisStopwatch.ElapsedMilliseconds}ms");
                });

                // 等待所有分析完成
                await analysisTask;
                
                // 分批处理已完成，继续后续清理工作

                await FinalizeUIUpdates();

#pragma warning disable CS4014
                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    Logger.WriteLine(LogLevel.Debug,
                        $"Processing completed: FinalValue={StateBarManager.ProgressValue.Value:F1}%");

                    stopwatch.Stop();
                    var totalFiles = TotalCount.Value;
                    var elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                    var speed = totalFiles / elapsedSeconds;
                    Logger.WriteLine(LogLevel.Information,
                        "[KRRLVAnalysisViewModel] {0}个文件分析完成，用时: {1:F2}s，速度: {2:F1}个/s",
                        totalFiles, elapsedSeconds, speed);

                    IsProcessing.Value = false;
                    StateBarManager.ProgressValue.Value = 100;
                }), DispatcherPriority.Background);
                
                // 异步执行内存清理，避免阻塞UI线程
                _ = Task.Run(() => PerformMemoryCleanup());
            }
            catch (Exception ex)
            {
                Logger.WriteLine(LogLevel.Error, $"[ERROR] 处理文件时发生异常: {ex.Message}");
                IsProcessing.Value = false;
            }
        }

        /// <summary>
        /// 强制执行最终UI更新，确保所有待处理项目都被添加并更新进度
        /// </summary>
        private async Task FinalizeUIUpdates()
        {
            await Task.Run(() =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // 设置进度为100%
                        StateBarManager.ProgressValue.Value = 100;
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteLine(LogLevel.Error,
                            $"FinalizeUIUpdates: Error in final UI update - {ex.Message}");
                    }
                });
            });
        }

        private async Task ProcessAdvancedAnalysisAsync(KRRLVAnalysisItem item, Beatmap beatmap)
        {
            // 批处理中的高级分析：简化但高效的处理逻辑
            try
            {
                await item.LoadPerformanceAsync(beatmap);
            }
            catch (Exception ex)
            {
                item.ErrorMessage = $"高级分析错误: {ex.Message}";
                item.Phase = AnalysisStatus.Error;
            }
        }

        private void PerformMemoryCleanup()
        {
            try
            {
                // 重置计数器
                _advancedAnalysisCompletedCount = 0;

                // 建议垃圾回收，但不强制等待
                // 避免在UI线程上调用GC.WaitForPendingFinalizers()导致阻塞
                GC.Collect();
                GC.Collect(2, GCCollectionMode.Optimized, false);

                Logger.WriteLine(LogLevel.Information,
                    "[KRRLVAnalysisViewModel] Memory cleanup completed, processed count reset");
            }
            catch (Exception ex)
            {
                Logger.WriteLine(LogLevel.Warning, "[KRRLVAnalysisViewModel] Memory cleanup failed: {0}", ex.Message);
            }
        }

        #region 文件交互，导出相关命令

        [RelayCommand]
        private void Browse()
        {
            var selected = FilesHelper.ShowFolderBrowserDialog("选择文件夹");
            if (!string.IsNullOrEmpty(selected))
            {
                PathInput.Value = selected;
                ProcessDroppedFiles([selected]);
            }
        }

        [RelayCommand]
        private void OpenPath()
        {
            if (!string.IsNullOrEmpty(PathInput.Value))
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = PathInput.Value,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 无法打开路径: {ex.Message}");
                }
        }

        [RelayCommand]
        private async Task Save()
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "导出数据",
                Filter = "CSV 文件 (*.csv)|*.csv|Excel 文件 (*.xlsx)|*.xlsx",
                DefaultExt = "csv",
                AddExtension = true
            };

            if (saveDialog.ShowDialog() == true)
            {
                var filePath = saveDialog.FileName;
                var extension = Path.GetExtension(filePath).ToLower();

                try
                {
                    // 异步执行导出操作，避免阻塞UI
                    await Task.Run(() =>
                    {
                        if (extension == ".csv")
                            ExportToCsv(filePath);
                        else if (extension == ".xlsx")
                            ExportToExcel(filePath);
                    });

                    // 在UI线程中打开导出的文件
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var processStartInfo = new ProcessStartInfo(filePath)
                        {
                            UseShellExecute = true
                        };
                        Process.Start(processStartInfo);
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] 导出文件失败: {ex.Message}");
                }
            }
        }

        private void ExportToCsv(string filePath)
        {
            var csv = new StringBuilder();

            // 使用共享的导出属性配置
            var exportProperties = KRRLVAnalysisColumnConfig.ExportProperties;

            // 添加CSV头部
            csv.AppendLine(string.Join(",", exportProperties.Select(p => p.Header)));

            // 添加数据行
            foreach (var file in OsuFiles.Value)
            {
                var values = exportProperties.Select(prop =>
                {
                    // 直接从KRRLVAnalysisItem获取属性值
                    var property = typeof(KRRLVAnalysisItem).GetProperty(prop.Property);
                    object? value = property?.GetValue(file);

                    // 格式化数值类型
                    if (value is double d)
                        return $"\"{d:F2}\"";
                    else if (value is int i)
                        return i.ToString();
                    else
                        return $"\"{value ?? ""}\"";
                });

                csv.AppendLine(string.Join(",", values));
            }

            File.WriteAllText(filePath, csv.ToString(), Encoding.UTF8);
        }

        private void ExportToExcel(string filePath)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("KRR LV Analysis");

            // 使用共享的导出属性配置
            var exportProperties = KRRLVAnalysisColumnConfig.ExportProperties;

            // 添加头部
            for (var i = 0; i < exportProperties.Length; i++)
            {
                worksheet.Cell(1, i + 1).Value = exportProperties[i].Header;
            }

            // 添加数据行
            var row = 2;
            foreach (var file in OsuFiles.Value)
            {
                for (var col = 0; col < exportProperties.Length; col++)
                {
                    var propName = exportProperties[col].Property;

                    // 直接从KRRLVAnalysisItem获取属性值
                    var property = typeof(KRRLVAnalysisItem).GetProperty(propName);
                    object? value = property?.GetValue(file);

                    worksheet.Cell(row, col + 1).Value = Convert.ToString(value ?? "");
                }

                row++;
            }

            // 自动调整列宽
            worksheet.Columns().AdjustToContents();

            workbook.SaveAs(filePath);
        }

        #endregion
    }
}