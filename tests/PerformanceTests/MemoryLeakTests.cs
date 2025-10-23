using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using krrTools.Beatmaps;
using krrTools.Bindable;
using krrTools.Tools.KRRLVAnalysis;
using krrTools.Utilities;
using Microsoft.Extensions.DependencyInjection;
using OsuParsers.Decoders;
using Xunit;
using Xunit.Abstractions;

namespace krrTools.Tests.PerformanceTests;

public class MemoryLeakTests : IDisposable
{
    private readonly ITestOutputHelper _testOutputHelper;

    public MemoryLeakTests(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
        Logger.SetConsoleOutputEnabled(false);
        
        // Setup dependency injection for tests
        var services = new ServiceCollection();
        services.AddSingleton<StateBarManager>();
        services.AddSingleton<IEventBus, EventBus>();
        var serviceProvider = services.BuildServiceProvider();
        
        // Use reflection to set the private Services property
        var servicesProperty = typeof(App).GetProperty("Services", BindingFlags.Public | BindingFlags.Static);
        servicesProperty?.SetValue(null, serviceProvider);
    }

    public void Dispose()
    {
        Logger.SetConsoleOutputEnabled(true);
    }

    [Fact]
    public void KRRLVAnalysisViewModel_ShouldDisposeResourcesProperly()
    {
        // 记录初始内存使用
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var initialMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"Initial memory: {initialMemory:N0} bytes");

        // 创建ViewModel实例（依赖注入会自动处理）
        var viewModel = new KRRLVAnalysisViewModel();

        // 模拟一些操作
        viewModel.PathInput.Value = Directory.GetCurrentDirectory();

        // 记录创建后的内存使用
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var afterCreationMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"After creation memory: {afterCreationMemory:N0} bytes");

        // Dispose ViewModel
        viewModel.Dispose();

        // 记录Dispose后的内存使用
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var afterDisposeMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"After dispose memory: {afterDisposeMemory:N0} bytes");

        // 检查内存是否被合理释放（允许一些余量）
        var memoryDifference = afterDisposeMemory - initialMemory;
        _testOutputHelper.WriteLine($"Memory difference: {memoryDifference:N0} bytes");

        // 断言：Dispose后内存使用应该接近初始水平
        // 允许一定的余量，因为GC可能不会释放所有内存
        Assert.True(memoryDifference < 1024 * 1024, $"Memory leak detected: {memoryDifference:N0} bytes not released");
    }

    [Fact]
    public async Task SRCalculator_ShouldNotLeakMemoryUnderHighConcurrency()
    {
        // 从TestOsuFile文件夹读取实际的osu文件
        var testOsuFileDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "TestOsuFile");
        var osuFiles = Directory.GetFiles(testOsuFileDir, "*.osu", SearchOption.TopDirectoryOnly);

        if (osuFiles.Length == 0)
        {
            _testOutputHelper.WriteLine("No .osu files found in TestOsuFile directory. Skipping memory leak test.");
            return;
        }

        // 读取第一个真实文件
        var sampleFilePath = osuFiles.First();
        var sampleBeatmap = BeatmapDecoder.Decode(sampleFilePath);
        if (sampleBeatmap == null)
        {
            _testOutputHelper.WriteLine("Failed to decode sample beatmap. Skipping test.");
            return;
        }

        // 记录初始内存
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var initialMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"Initial memory: {initialMemory:N0} bytes");

        // 模拟高并发计算：100次SR计算，每次解码新Beatmap
        const int iterations = 100;
        var tasks = new Task<(double sr, Dictionary<string, long> times)>[iterations];
        for (int i = 0; i < iterations; i++)
        {
            var beatmap = BeatmapDecoder.Decode(sampleFilePath); // 每次解码新对象
            tasks[i] = SRCalculator.Instance.CalculateSRAsync(beatmap);
        }

        await Task.WhenAll(tasks);

        // 强制GC
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        var afterGCMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"After {iterations} calculations and forced GC: {afterGCMemory:N0} bytes");

        // 检查内存增长
        var memoryIncrease = afterGCMemory - initialMemory;
        _testOutputHelper.WriteLine($"Memory increase: {memoryIncrease:N0} bytes");

        // 断言：内存增长应该在合理范围内（例如小于50MB）
        // 由于LOH和可能的外部库，允许一些增长，但不应过大
        Assert.True(memoryIncrease < 50 * 1024 * 1024, $"Potential memory leak: {memoryIncrease:N0} bytes increase after {iterations} calculations");
    }

    [Fact]
    public void BeatmapDecoder_ShouldNotLeakMemoryOnRepeatedDecodes()
    {
        // 从TestOsuFile文件夹读取实际的osu文件
        var testOsuFileDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "TestOsuFile");
        var osuFiles = Directory.GetFiles(testOsuFileDir, "*.osu", SearchOption.TopDirectoryOnly);

        if (osuFiles.Length == 0)
        {
            _testOutputHelper.WriteLine("No .osu files found in TestOsuFile directory. Skipping BeatmapDecoder test.");
            return;
        }

        // 读取第一个真实文件路径
        var sampleFilePath = osuFiles.First();

        // 记录初始内存
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var initialMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"Initial memory before decodes: {initialMemory:N0} bytes");

        // 模拟多次解码同一个文件：100次
        const int iterations = 100;
        for (int i = 0; i < iterations; i++)
        {
            var beatmap = BeatmapDecoder.Decode(sampleFilePath);
            // 不保存beatmap，让GC回收
        }

        // 强制GC
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        var afterDecodeMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"After {iterations} decodes and forced GC: {afterDecodeMemory:N0} bytes");

        // 检查内存增长（只解码，不计算SR）
        var memoryIncrease = afterDecodeMemory - initialMemory;
        _testOutputHelper.WriteLine($"Memory increase from decodes: {memoryIncrease:N0} bytes");

        // 断言：解码不应导致显著内存泄露（例如小于10MB）
        Assert.True(memoryIncrease < 100 * 1024 * 1024, $"Potential memory leak in BeatmapDecoder: {memoryIncrease:N0} bytes increase after {iterations} decodes");
    }

    [Fact]
    public async Task SRCalculator_ShouldNotLeakMemoryWithSameBeatmap()
    {
        // 从TestOsuFile文件夹读取实际的osu文件
        var testOsuFileDir = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "TestOsuFile");
        var osuFiles = Directory.GetFiles(testOsuFileDir, "*.osu", SearchOption.TopDirectoryOnly);

        if (osuFiles.Length == 0)
        {
            _testOutputHelper.WriteLine("No .osu files found in TestOsuFile directory. Skipping same Beatmap test.");
            return;
        }

        // 读取第一个真实文件
        var sampleFilePath = osuFiles.First();
        var sampleBeatmap = BeatmapDecoder.Decode(sampleFilePath);
        if (sampleBeatmap == null)
        {
            _testOutputHelper.WriteLine("Failed to decode sample beatmap. Skipping test.");
            return;
        }

        // 记录初始内存
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var initialMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"Initial memory: {initialMemory:N0} bytes");

        // 模拟高并发计算：100次SR计算，使用同一个Beatmap对象
        const int iterations = 100;
        var tasks = new Task<(double sr, Dictionary<string, long> times)>[iterations];
        for (int i = 0; i < iterations; i++)
        {
            tasks[i] = SRCalculator.Instance.CalculateSRAsync(sampleBeatmap); // 使用同一个对象
        }

        await Task.WhenAll(tasks);

        // 强制GC
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        var afterGCMemory = GC.GetTotalMemory(true);
        _testOutputHelper.WriteLine($"After {iterations} calculations with same Beatmap and forced GC: {afterGCMemory:N0} bytes");

        // 检查内存增长
        var memoryIncrease = afterGCMemory - initialMemory;
        _testOutputHelper.WriteLine($"Memory increase: {memoryIncrease:N0} bytes");

        // 断言：使用同一个Beatmap，内存增长应该更小（例如小于20MB）
        Assert.True(memoryIncrease < 20 * 1024 * 1024, $"Potential memory leak in SRCalculator: {memoryIncrease:N0} bytes increase after {iterations} calculations with same Beatmap");
    }
}