using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OsuParsers.Beatmaps;
using OsuParsers.Beatmaps.Objects;

namespace krrTools.Beatmaps;

public class OsuAnalysisPerformance
{
    // 高级分析结果 - 高密集异步并发数据分析
    public double XXY_SR;
    public double KRR_LV;
    public double YLs_LV;
}

public class OsuAnalysisBasic
{
    // Basic metadata
    public string Diff = string.Empty;
    public string Title = string.Empty;
    public string Artist = string.Empty;
    public string Creator = string.Empty;
    public string BPMDisplay = string.Empty;

    // Difficulty settings
    public double OD;
    public double HP;

    // Beatmap identifiers
    public int BeatmapID;
    public int BeatmapSetID;

    // Basic counts - 添加基础属性
    public double NotesCount;
    public double KeyCount;
    public double LN_Percent;
    public double MaxKPS;
    public double AvgKPS;
}

public static class OsuAnalyzer
{
    /// <summary>
    /// 快速异步获取谱面基础信息，不进行复杂分析计算
    /// <para></para>
    /// 外部应提前预处理路径有效性和解码操作
    /// </summary>
    /// <returns>基础信息对象</returns>
    public static async Task<OsuAnalysisBasic> AnalyzeBasicInfoAsync(Beatmap beatmap)
    {
        var basicInfo = new OsuAnalysisBasic();

        try
        {
            // CPU密集型操作放到后台线程执行
            return await Task.Run(() =>
            {
                // 快速填充基础信息
                basicInfo.Diff = beatmap.MetadataSection.Version;
                basicInfo.Title = beatmap.MetadataSection.Title;
                basicInfo.Artist = beatmap.MetadataSection.Artist;
                basicInfo.Creator = beatmap.MetadataSection.Creator;
                basicInfo.BPMDisplay = beatmap.GetBPMDisplay();

                basicInfo.OD = beatmap.DifficultySection.OverallDifficulty;
                basicInfo.HP = beatmap.DifficultySection.HPDrainRate;
                basicInfo.BeatmapID = beatmap.MetadataSection.BeatmapID;
                basicInfo.BeatmapSetID = beatmap.MetadataSection.BeatmapSetID;

                // Logger.WriteLine(LogLevel.Debug, $"[OsuAnalyzer] BasicInfo: " +
                //                                  $"{beatmap.GeneralSection.ModeId}" +
                //                                  $"{(int)beatmap.DifficultySection.CircleSize}");

                // 3 为Mania模式，只有Mania模式才计算Mania特定的属性
                if (beatmap.GeneralSection.ModeId == 3)
                {
                    basicInfo.KeyCount = (int)beatmap.DifficultySection.CircleSize;

                    if (beatmap.HitObjects.Count > 0)
                    {
                        // 计算Mania HitObjects
                        basicInfo.NotesCount = beatmap.HitObjects.Count;
                        basicInfo.LN_Percent = beatmap.GetLNPercent();
                        var (maxKPS, avgKPS) = CalculateKPSMetrics(beatmap);
                        basicInfo.MaxKPS = maxKPS;
                        basicInfo.AvgKPS = avgKPS;
                    }
                }
                // 对于非Mania模式，保持属性为默认值

                return basicInfo;
            });
        }
        catch (Exception ex)
        {
            Logger.WriteLine(LogLevel.Error, "[OsuAnalyzer] Basic info failed: {0}", ex.Message);
            throw new InvalidOperationException("Failed to get basic info for beatmap", ex);
        }
    }

    /// <summary>
    /// 基于基础信息进行高级分析计算（SR、LV、KPS等）
    /// <para></para>
    /// 需要先调用GetBasicInfoAsync获取基础信息和谱面对象
    /// </summary>
    /// <returns>完整的分析信息，失败时返回null</returns>
    public static async Task<OsuAnalysisPerformance?> AnalyzeAdvancedAsync(Beatmap beatmap)
    {
        try
        {
            var totalStopwatch = Stopwatch.StartNew();
            
            // 模式检查和基础验证移到外部，这里只做分析计算
            var keys = (int)beatmap.DifficultySection.CircleSize;
            var calculator = SRCalculator.Instance;
            
            // SR计算是主要耗时，单独计时
            var srStopwatch = Stopwatch.StartNew();
            var (xxySR, _) = await calculator.CalculateSRAsync(beatmap).ConfigureAwait(false);
            srStopwatch.Stop();
            
            // LV计算通常很快
            var krrLv = CalculateKrrLevel(keys, xxySR);
            var ylsLv = CalculateYlsLevel(xxySR);
            
            totalStopwatch.Stop();
            
            // 详细性能日志
            // if (totalStopwatch.ElapsedMilliseconds > 30) // 只记录耗时较长的
            // {
            //     // 输出SR计算内部各section的耗时
            //     if (times.Count > 0)
            //     {
            //         var sectionTimes = string.Join(", ", times.Select(kvp => $"{kvp.Key}: {kvp.Value}ms"));
            //         Logger.WriteLine(LogLevel.Debug, $"[SRCalculator] Section耗时详情: {sectionTimes}");
            //     }
            // }

            // 返回性能属性
            return new OsuAnalysisPerformance
            {
                XXY_SR = xxySR,
                KRR_LV = krrLv,
                YLs_LV = ylsLv
            };
        }
        catch (Exception ex)
        {
            Logger.WriteLine(LogLevel.Error, "[OsuAnalyzer] Advanced analysis failed: {0}", ex.Message);
            // throw new InvalidOperationException("Failed to get Analyze Advanced for beatmap", ex);
            return null;
        }
    }

    private static (double maxKPS, double avgKPS) CalculateKPSMetrics(Beatmap beatmap)
    {
        var hitObjects = beatmap.HitObjects;
        if (hitObjects.Count == 0)
            return (0.0, 0.0);

        // 计算KPS
        var notes = hitObjects.Where(obj => obj is HitCircle || obj is Slider || obj is Spinner)
            .OrderBy(obj => obj.StartTime)
            .ToList();

        if (notes.Count == 0)
            return (0.0, 0.0);

        // 使用滑动窗口计算最大KPS
        const int windowMs = 1000; // 1秒窗口
        double maxKPS = 0;
        double totalKPS = 0;
        var windowCount = 0;

        for (var i = 0; i < notes.Count; i++)
        {
            var count = 1;
            for (var j = i + 1; j < notes.Count; j++)
                if (notes[j].StartTime - notes[i].StartTime <= windowMs)
                    count++;
                else
                    break;

            double kps = count;
            maxKPS = Math.Max(maxKPS, kps);
            totalKPS += kps;
            windowCount++;
        }

        var avgKPS = windowCount > 0 ? totalKPS / windowCount : 0;

        return (maxKPS, avgKPS);
    }

    private static double CalculateKrrLevel(int keys, double xxySr)
    {
        double krrLv = -1;
        if (keys <= 10)
        {
            var (a, b, c) = keys == 10
                ? (-0.0773, 3.8651, -3.4979)
                : (-0.0644, 3.6139, -3.0677);

            var LV = a * xxySr * xxySr + b * xxySr + c;
            krrLv = LV > 0 ? LV : -1;
        }

        return krrLv;
    }

    // YLS LV主要用于8K
    private static double CalculateYlsLevel(double xxyStarRating)
    {
        const double LOWER_BOUND = 2.76257856739498;
        const double UPPER_BOUND = 10.5541834716376;

        if (xxyStarRating is >= LOWER_BOUND and <= UPPER_BOUND) return FittingFormula(xxyStarRating);

        if (xxyStarRating is < LOWER_BOUND and > 0) return 3.6198 * xxyStarRating;

        if (xxyStarRating is > UPPER_BOUND and < 12.3456789) return 2.791 * xxyStarRating + 0.5436;

        return double.NaN;
    }

    private static double FittingFormula(double x)
    {
        // TODO: 凉雨算法，等待实现正确的拟合公式
        return x * 1.5;
    }
}