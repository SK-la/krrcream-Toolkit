using krrTools.Bindable;
using Microsoft.Extensions.Logging;

namespace krrTools.Beatmaps
{
    /// <summary>
    /// 谱面分析服务 - 负责谱面文件解析、数据分析和结果发布
    /// </summary>
    public class BeatmapAnalysisService
    {
        private readonly BeatmapCacheManager _cacheManager = new();

        // 公共属性注入事件总线
        [Inject] private IEventBus EventBus { get; set; } = null!;

        public BeatmapAnalysisService()
        {
            // 自动注入标记了 [Inject] 的属性
            this.InjectServices();

            // 订阅路径变化事件，收到后进行完整分析
            EventBus.Subscribe<BeatmapChangedEvent>(OnBeatmapPathChanged);
        }

        /// <summary>
        /// 处理谱面文件
        /// </summary>
        private async Task ProcessBeatmapAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) ||
                !File.Exists(filePath) ||
                !Path.GetExtension(filePath).Equals(".osu", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Run(async () =>
            {
                try
                {
                    // 响应式防重复处理检查
                    if (!_cacheManager.CanProcessFile(filePath)) return;

                    // 使用 using 语句确保资源自动释放
                    using var beatmapWrapper = BeatmapWrapper.Create(filePath);
                    if (beatmapWrapper?.Beatmap == null)
                    {
                        Logger.WriteLine(LogLevel.Error, "[BeatmapAnalysisService] Failed to decode beatmap: {0}",
                            filePath);
                        return;
                    }

                    var beatmap = beatmapWrapper.Beatmap;

                    // 获取基础信息和性能分析
                    var basicInfo = await OsuAnalyzer.AnalyzeBasicInfoAsync(beatmap);
                    var performance = await OsuAnalyzer.AnalyzeAdvancedAsync(beatmap);

                    Logger.WriteLine(LogLevel.Debug,
                        "[BeatmapAnalysisService] Beatmap analyzed: {0}, Keys: {1}, SR: {2:F2}",
                        basicInfo.Title, basicInfo.KeyCount, performance?.XXY_SR ?? 0);


                    // 发布专门的分析结果变化事件
                    EventBus.Publish(new AnalysisResultChangedEvent
                    {
                        AnalysisBasic = basicInfo,
                        AnalysisPerformance = performance,
                    });
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LogLevel.Error, "[BeatmapAnalysisService] ProcessBeatmapAsync failed: {0}",
                        ex.Message);
                }
            });
        }

        /// <summary>
        /// 处理谱面路径变化事件 - 进行完整分析
        /// </summary>
        private void OnBeatmapPathChanged(BeatmapChangedEvent e)
        {
            // 只处理路径变化事件
            if (e.ChangeType != BeatmapChangeType.FromMonitoring) return;
            // 异步处理新谱面
            _ = ProcessBeatmapAsync(e.FilePath);
        }
    }
}