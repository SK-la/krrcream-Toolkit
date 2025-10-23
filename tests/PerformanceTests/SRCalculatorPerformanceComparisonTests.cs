using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using krrTools.Beatmaps;
using OsuParsers.Beatmaps;
using OsuParsers.Decoders;
using Xunit;
using Xunit.Abstractions;

namespace krrTools.Tests.PerformanceTests;

/// <summary>
/// SR计算器性能对比测试
/// </summary>
public class SRCalculatorPerformanceComparisonTests : IDisposable
{
    private readonly ITestOutputHelper _testOutputHelper;

    // 测试文件数量常量
    private const int TestFileCount = 20;

    public SRCalculatorPerformanceComparisonTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        Logger.SetConsoleOutputEnabled(false);
    }

    public void Dispose()
    {
        Logger.SetConsoleOutputEnabled(true);
    }

    /// <summary>
    /// SR计算器性能测试结果数据结构
    /// </summary>
    public class SRPerformanceResult
    {
        public string CalculatorName { get; set; } = "";
        public TimeSpan TotalTime { get; set; }
        public double AverageTime { get; set; }
        public double Throughput { get; set; } // 计算/秒
        public bool ResultsConsistent { get; set; }
        public int CalculationCount { get; set; }
        public double SpeedupRatio { get; set; } // 相对于基准的倍数
        public string PerformanceRating { get; set; } = ""; // 性能评级
        public long PeakMemoryMB { get; set; } // 峰值内存增量(MB)
        public double AverageMemoryMB { get; set; } // 平均内存增量(MB)
        public double AverageSR { get; set; } // 平均SR值
    }

    /// <summary>
    /// 以表格形式输出SR计算器性能测试结果
    /// </summary>
    private void OutputSRPerformanceTable(string testName, List<SRPerformanceResult> results)
    {
        _testOutputHelper.WriteLine($"\n=== {testName} SR计算器性能对比结果 ===");
        _testOutputHelper.WriteLine($"测试计算数量: {results.First().CalculationCount}");

        // 表格头部
        _testOutputHelper.WriteLine(
            "┌─────────────┬─────────────┬─────────────┬─────────────┬─────────────┬─────────────┬─────────────┬─────────────┬─────────────┬─────────────┐");
        _testOutputHelper.WriteLine("│  计算器版本  │  总用时(ms)  │  平均用时(ms)│ 吞吐量(个/s) │   结果一致性  │   性能倍数   │   性能评级   │ 峰值内存(MB) │ 平均内存(MB) │   平均SR    │");
        _testOutputHelper.WriteLine(
            "├─────────────┼─────────────┼─────────────┼─────────────┼─────────────┼─────────────┼─────────────┼─────────────┼─────────────┼─────────────┤");

        // 表格内容
        foreach (var result in results.OrderBy(r => r.TotalTime))
        {
            var consistency = result.ResultsConsistent ? "✓" : "✗";
            var speedup = result.SpeedupRatio >= 1 ? $"{result.SpeedupRatio:F2}x" : $"{1 / result.SpeedupRatio:F2}x慢";
            var rating = GetPerformanceRating(result.SpeedupRatio);

            _testOutputHelper.WriteLine("│ {0,-11} │ {1,11:F2} │ {2,11:F2} │ {3,11:F2} │ {4,11} │ {5,11} │ {6,11} │ {7,11:F1} │ {8,11:F1} │ {9,11:F2} │",
                result.CalculatorName,
                result.TotalTime.TotalMilliseconds,
                result.AverageTime,
                result.Throughput,
                consistency,
                speedup,
                rating,
                result.PeakMemoryMB,
                result.AverageMemoryMB,
                result.AverageSR);
        }

        // 表格底部
        _testOutputHelper.WriteLine(
            "└─────────────┴─────────────┴─────────────┴─────────────┴─────────────┴─────────────┴─────────────┴─────────────┴─────────────┴─────────────┘");

        // 总结信息
        var bestResult = results.OrderBy(r => r.TotalTime).First();
        var worstResult = results.OrderByDescending(r => r.TotalTime).First();
        var improvement = worstResult.TotalTime.TotalMilliseconds / bestResult.TotalTime.TotalMilliseconds;

        _testOutputHelper.WriteLine($"\n📊 总结:");
        _testOutputHelper.WriteLine(
            $"• 最快计算器: {bestResult.CalculatorName} ({bestResult.TotalTime.TotalMilliseconds:F2}ms)");
        _testOutputHelper.WriteLine(
            $"• 最慢计算器: {worstResult.CalculatorName} ({worstResult.TotalTime.TotalMilliseconds:F2}ms)");
        _testOutputHelper.WriteLine($"• 性能提升: {improvement:F2}x (从最慢到最快)");
        _testOutputHelper.WriteLine($"• 结果一致性: {(results.All(r => r.ResultsConsistent) ? "全部通过 ✓" : "存在不一致 ✗")}");

        // 额外统计
        var avgThroughput = results.Average(r => r.Throughput);
        var estimatedTimeFor1000 = 1000.0 / avgThroughput;

        _testOutputHelper.WriteLine($"\n📈 扩展预测:");
        _testOutputHelper.WriteLine($"• 平均吞吐量: {avgThroughput:F1} 个/秒");
        _testOutputHelper.WriteLine($"• 计算1000个谱面预估时间: {estimatedTimeFor1000:F1} 秒");
    }

    /// <summary>
    /// 根据性能倍数获取性能评级
    /// </summary>
    private string GetPerformanceRating(double speedupRatio)
    {
        if (speedupRatio >= 2.0) return "优秀";
        if (speedupRatio >= 1.5) return "良好";
        if (speedupRatio >= 1.2) return "一般";
        if (speedupRatio >= 0.8) return "及格";
        return "待改进";
    }

    [Fact]
    public async Task CompareSRCalculatorPerformance()
    {
        // 从TestOsuFile文件夹读取实际的osu文件
        var testOsuFileDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "TestOsuFile");
        var osuFiles = Directory.GetFiles(testOsuFileDir, "*.osu", SearchOption.TopDirectoryOnly);

        if (osuFiles.Length == 0)
        {
            _testOutputHelper.WriteLine("No .osu files found in TestOsuFile directory. Skipping SR performance test.");
            return;
        }

        // 读取第一个真实文件到内存中作为测试样本
        var sampleFilePath = osuFiles.First();
        var sampleBeatmap = BeatmapDecoder.Decode(sampleFilePath);
        _testOutputHelper.WriteLine($"Loaded sample beatmap from: {Path.GetFileName(sampleFilePath)}");

        if (sampleBeatmap == null)
        {
            _testOutputHelper.WriteLine("Failed to decode sample beatmap. Skipping test.");
            return;
        }

        // 预热阶段
        _testOutputHelper.WriteLine("Warmup phase...");
        for (int i = 0; i < 3; i++)
        {
            var csharpSr = SRCalculator.Instance.CalculateSR(sampleBeatmap, out _);
            var rustSr = CalculateSRRust(sampleBeatmap);
        }
        _testOutputHelper.WriteLine("Warmup completed.");

        // 测试结果存储
        var results = new List<SRPerformanceResult>();

        // 测试C#版本
        var csharpResults = await TestSRCalculator("C#", sampleBeatmap, TestFileCount);
        results.Add(csharpResults);

        // 测试Rust版本
        var rustResults = await TestSRCalculator("Rust", sampleBeatmap, TestFileCount);
        results.Add(rustResults);

        // 计算性能倍数（相对于最慢的）
        var slowestTime = results.Max(r => r.TotalTime.TotalMilliseconds);
        foreach (var result in results)
        {
            result.SpeedupRatio = slowestTime / result.TotalTime.TotalMilliseconds;
        }

        // 输出性能对比表格
        OutputSRPerformanceTable("SR计算器性能对比", results);

        // 断言SR值精度要求：C#和Rust版本的SR值差异应小于0.0001
        var csharpResult = results.First(r => r.CalculatorName == "C#");
        var rustResult = results.First(r => r.CalculatorName == "Rust");
        var srDifference = Math.Abs(csharpResult.AverageSR - rustResult.AverageSR);
        
        _testOutputHelper.WriteLine($"\n🔍 SR值精度检查:");
        _testOutputHelper.WriteLine($"C# SR: {csharpResult.AverageSR:F6}");
        _testOutputHelper.WriteLine($"Rust SR: {rustResult.AverageSR:F6}");
        _testOutputHelper.WriteLine($"差异: {srDifference:F6}");
        _testOutputHelper.WriteLine($"精度要求: < 0.0001");
        
        // 如果差异过大，输出详细的调试信息
        if (srDifference >= 0.0001)
        {
            _testOutputHelper.WriteLine("\n⚠️  SR值差异过大，进行详细分析...");
            
            // 计算单次SR值进行比较
            var singleCsharpSr = SRCalculator.Instance.CalculateSR(sampleBeatmap, out _);
            var singleRustSr = CalculateSRRust(sampleBeatmap);
            var singleDifference = Math.Abs(singleCsharpSr - singleRustSr);
            
            _testOutputHelper.WriteLine($"单次计算 - C#: {singleCsharpSr:F6}, Rust: {singleRustSr:F6}, 差异: {singleDifference:F6}");
            
            // 检查输入数据
            var beatmapData = new
            {
                difficulty_section = new
                {
                    overall_difficulty = sampleBeatmap.DifficultySection.OverallDifficulty,
                    circle_size = sampleBeatmap.DifficultySection.CircleSize
                },
                hit_objects = sampleBeatmap.HitObjects.Select(ho => new
                {
                    position = new { x = ho.Position.X },
                    start_time = ho.StartTime,
                    end_time = ho.EndTime
                }).ToArray()
            };
            
            _testOutputHelper.WriteLine($"谱面信息 - OD: {sampleBeatmap.DifficultySection.OverallDifficulty}, CS: {sampleBeatmap.DifficultySection.CircleSize}");
            _testOutputHelper.WriteLine($"Note数量: {sampleBeatmap.HitObjects.Count}");
        }
        
        Assert.True(srDifference < 0.0001, $"SR值差异 {srDifference:F6} 超过精度要求 0.0001");
    }

    private async Task<SRPerformanceResult> TestSRCalculator(string calculatorName, Beatmap beatmap, int testCount)
    {
        var srValues = new List<double>();
        var memoryUsages = new List<long>();
        var initialMemory = GC.GetTotalMemory(true);

        var stopwatch = Stopwatch.StartNew();

        // 并行执行多次计算
        var tasks = Enumerable.Range(0, testCount).Select(async _ =>
        {
            var memoryBefore = GC.GetTotalMemory(false);

            double sr;
            if (calculatorName == "C#")
            {
                var times = new Dictionary<string, long>();
                sr = SRCalculator.Instance.CalculateSR(beatmap, out times);
            }
            else // Rust
            {
                sr = CalculateSRRust(beatmap);
            }

            var memoryAfter = GC.GetTotalMemory(false);
            var memoryDelta = memoryAfter - memoryBefore;

            return (sr, memoryDelta);
        });

        var taskResults = await Task.WhenAll(tasks);
        stopwatch.Stop();

        foreach ((double sr, long memoryDelta) in taskResults)
        {
            srValues.Add(sr);
            memoryUsages.Add(memoryDelta);
        }

        var finalMemory = GC.GetTotalMemory(false);
        var totalMemoryDelta = finalMemory - initialMemory;

        // 计算统计信息
        var averageSR = srValues.Average();
        var minSR = srValues.Min();
        var maxSR = srValues.Max();
        var srVariance = srValues.Sum(sr => Math.Pow(sr - averageSR, 2)) / srValues.Count;

        // 检查结果一致性（允许小误差）
        var resultsConsistent = srVariance < 0.01; // SR方差小于0.01认为一致

        var result = new SRPerformanceResult
        {
            CalculatorName = calculatorName,
            TotalTime = stopwatch.Elapsed,
            AverageTime = stopwatch.Elapsed.TotalMilliseconds / testCount,
            Throughput = testCount / (stopwatch.Elapsed.TotalMilliseconds / 1000.0),
            ResultsConsistent = resultsConsistent,
            CalculationCount = testCount,
            PeakMemoryMB = memoryUsages.Max() / 1024 / 1024,
            AverageMemoryMB = memoryUsages.Average() / 1024 / 1024,
            AverageSR = averageSR
        };

        _testOutputHelper.WriteLine($"{calculatorName} SR Statistics:");
        _testOutputHelper.WriteLine($"  SR Range: {minSR:F2} - {maxSR:F2} (Avg: {averageSR:F2})");
        _testOutputHelper.WriteLine($"  SR Variance: {srVariance:F6}");
        _testOutputHelper.WriteLine($"  Memory Delta: {totalMemoryDelta / 1024 / 1024} MB");

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CHitObject
    {
        public double position_x;
        public int start_time;
        public int end_time;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CBeatmapData
    {
        public double overall_difficulty;
        public double circle_size;
        public ulong hit_objects_count; // Use ulong for usize
        public IntPtr hit_objects_ptr;
    }

    [DllImport("rust_sr_calculator.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern double calculate_sr_from_struct(IntPtr data);

    private static double CalculateSRRust(Beatmap beatmap)
    {
        // Prepare hit objects array
        var hitObjects = beatmap.HitObjects.Select(ho => new CHitObject
        {
            position_x = ho.Position.X,
            start_time = ho.StartTime,
            end_time = ho.EndTime
        }).ToArray();

        // Pin the array in memory
        GCHandle hitObjectsHandle = GCHandle.Alloc(hitObjects, GCHandleType.Pinned);
        GCHandle dataHandle = default;
        
        try
        {
            // Create beatmap data structure
            var beatmapData = new CBeatmapData
            {
                overall_difficulty = beatmap.DifficultySection.OverallDifficulty,
                circle_size = beatmap.DifficultySection.CircleSize,
                hit_objects_count = (ulong)hitObjects.Length,
                hit_objects_ptr = hitObjectsHandle.AddrOfPinnedObject()
            };

            dataHandle = GCHandle.Alloc(beatmapData, GCHandleType.Pinned);
            
            // Call Rust function
            double sr = calculate_sr_from_struct(dataHandle.AddrOfPinnedObject());
            
            if (sr < 0)
            {
                throw new Exception("Rust SR calculation failed");
            }
            
            return sr;
        }
        finally
        {
            if (dataHandle.IsAllocated)
                dataHandle.Free();
            hitObjectsHandle.Free();
        }
    }
}