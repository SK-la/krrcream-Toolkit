using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using krrTools.Beatmaps;
using Microsoft.Extensions.Logging;
using OsuParsers.Beatmaps;

namespace krrTools.Tools.KRRLVAnalysis
{
    public enum AnalysisStatus
    {
        Waiting,        // 等待处理
        BasicLoaded,    // 阶段1：基础信息已加载
        Analyzing,      // 阶段2：计算分析数据
        Completed,      // 完成
        Error           // 错误状态
    }
    
    public static class AnalysisStatusExtensions
    {
        public static string ToDisplayString(this AnalysisStatus status, string? errorMessage = null)
        {
            return status switch
            {
                AnalysisStatus.Waiting => "waiting",
                AnalysisStatus.BasicLoaded => "basic-ready",
                AnalysisStatus.Analyzing => "analyzing",
                AnalysisStatus.Completed => "√",
                AnalysisStatus.Error => string.IsNullOrEmpty(errorMessage) ? "error" : $"error: {errorMessage}",
                _ => "unknown"
            };
        }
        
        public static string? GetErrorMessage(this AnalysisStatus status, string? statusText)
        {
            if (status == AnalysisStatus.Error && !string.IsNullOrEmpty(statusText) && statusText.StartsWith("error: "))
            {
                return statusText.Substring("error: ".Length);
            }
            return null;
        }
    }

    public class KRRLVAnalysisItem : INotifyPropertyChanged, IDisposable
    {
        // 直接属性 - 基础信息
        private string? _title;
        private string? _artist;
        private string? _diff;
        private string? _creator;
        private string? _bpmDisplay;
        private double _od;
        private double _hp;
        private double _beatmapId;
        private double _beatmapSetId;
        private double _notesCount;
        private string? _errorMessage;

        // 直接属性 - 分析信息
        private double _keyCount;
        private double _lnPercent;
        private double _maxKps;
        private double _avgKps;
        private double _xxySr;
        private double _krrLv;
        private double _ylsLv;

        // 其他属性
        private string? _filePath;
        private AnalysisStatus _analysisStatus = AnalysisStatus.Waiting;
        private bool _disposed;

        // 基础信息属性
        public string? Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }

        public string? Artist
        {
            get => _artist;
            set => SetProperty(ref _artist, value);
        }

        public string? Diff
        {
            get => _diff;
            set => SetProperty(ref _diff, value);
        }

        public string? Creator
        {
            get => _creator;
            set => SetProperty(ref _creator, value);
        }

        public string? BPMDisplay
        {
            get => _bpmDisplay;
            set => SetProperty(ref _bpmDisplay, value);
        }

        public double OD
        {
            get => _od;
            set => SetProperty(ref _od, value);
        }

        public double HP
        {
            get => _hp;
            set => SetProperty(ref _hp, value);
        }

        public double BeatmapID
        {
            get => _beatmapId;
            set => SetProperty(ref _beatmapId, value);
        }

        public double BeatmapSetID
        {
            get => _beatmapSetId;
            set => SetProperty(ref _beatmapSetId, value);
        }

        public double NotesCount
        {
            get => _notesCount;
            set => SetProperty(ref _notesCount, value);
        }

        public string Status => _analysisStatus.ToDisplayString(_errorMessage);
        
        public string? ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        // 分析信息属性
        public double KeyCount
        {
            get => _keyCount;
            set => SetProperty(ref _keyCount, value);
        }

        public double LN_Percent
        {
            get => _lnPercent;
            set => SetProperty(ref _lnPercent, value);
        }

        public double MaxKPS
        {
            get => _maxKps;
            set => SetProperty(ref _maxKps, value);
        }

        public double AvgKPS
        {
            get => _avgKps;
            set => SetProperty(ref _avgKps, value);
        }

        public double XXY_SR
        {
            get => _xxySr;
            set => SetProperty(ref _xxySr, value);
        }

        public double KRR_LV
        {
            get => _krrLv;
            set => SetProperty(ref _krrLv, value);
        }

        public double YLs_LV
        {
            get => _ylsLv;
            set => SetProperty(ref _ylsLv, value);
        }

        // 其他属性
        public string? FilePath
        {
            get => _filePath;
            set => SetProperty(ref _filePath, value);
        }

        public AnalysisStatus Phase
        {
            get => _analysisStatus;
            set
            {
                if (SetProperty(ref _analysisStatus, value))
                {
                    OnPropertyChanged(nameof(Status)); // 当状态改变时，通知Status属性也改变了
                }
            }
        }

        // 异步加载基础信息的方法
        public async Task LoadBasicInfoAsync(Beatmap beatmap)
        {
            if (Phase >= AnalysisStatus.BasicLoaded) return;

            try
            {
                Phase = AnalysisStatus.BasicLoaded;
                
                // 获取基础信息
                var basicInfo = await OsuAnalyzer.AnalyzeBasicInfoAsync(beatmap);

                // 直接设置基础信息属性
                Title = basicInfo.Title;
                Artist = basicInfo.Artist;
                Diff = basicInfo.Diff;
                Creator = basicInfo.Creator;
                BPMDisplay = basicInfo.BPMDisplay;
                OD = basicInfo.OD;
                HP = basicInfo.HP;
                BeatmapID = basicInfo.BeatmapID;
                BeatmapSetID = basicInfo.BeatmapSetID;
                
                KeyCount = basicInfo.KeyCount;
                NotesCount = basicInfo.NotesCount;
                LN_Percent = basicInfo.LN_Percent;
                MaxKPS = basicInfo.MaxKPS;
                AvgKPS = basicInfo.AvgKPS;
                
                ErrorMessage = null; // 清除之前的错误信息
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
                Phase = AnalysisStatus.Error; // 设置为错误状态
            }
        }

        // 异步分析性能数据的方法
        public async Task LoadPerformanceAsync(Beatmap beatmap)
        {
            try
            {
                Phase = AnalysisStatus.Analyzing;
                
                var sw = Stopwatch.StartNew();
                var performance = await OsuAnalyzer.AnalyzeAdvancedAsync(beatmap);
                sw.Stop();
                
                // 记录详细的性能分析时间（可选择性开启）
                if (sw.ElapsedMilliseconds > 50) // 只记录耗时较长的
                {
                    Logger.WriteLine(LogLevel.Debug,
                        $"[Performance] 文件 {Path.GetFileName(FilePath)} 高级分析耗时: {sw.ElapsedMilliseconds}ms");
                }

                // 直接设置分析结果属性
                XXY_SR = performance?.XXY_SR ?? -1;
                KRR_LV = performance?.KRR_LV ?? -1;
                YLs_LV = performance?.YLs_LV ?? -1;

                ErrorMessage = null; // 清除之前的错误信息
                Phase = AnalysisStatus.Completed;
            }
            catch (Exception ex)
            {
                ErrorMessage = $"analysis-error: {ex.Message}";
                Phase = AnalysisStatus.Error; // 出错时标记为错误状态
            }
        }

        // 异步加载所有信息的方法（合并基础信息和性能分析）
        public async Task LoadAllInfoAsync(Beatmap beatmap)
        {
            // 第一阶段：加载基础信息，完成后UI会立即刷新
            await LoadBasicInfoAsync(beatmap);
            
            // 第二阶段：加载性能信息，完成后UI会再次刷新
            await LoadPerformanceAsync(beatmap);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                PropertyChanged = null;
            }

            _disposed = true;
        }
    }
}