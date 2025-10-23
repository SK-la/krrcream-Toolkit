using System;
using OsuParsers.Beatmaps;
using OsuParsers.Decoders;

namespace krrTools.Beatmaps
{
    /// <summary>
    /// Beatmap 对象的包装器，实现 IDisposable 接口以确保资源正确释放
    /// </summary>
    public class BeatmapWrapper : IDisposable
    {
        private bool _disposed;

        public Beatmap? Beatmap { get; private set; }

        public BeatmapWrapper(Beatmap beatmap)
        {
            Beatmap = beatmap ?? throw new ArgumentNullException(nameof(beatmap));
        }

        /// <summary>
        /// 创建 BeatmapWrapper 的静态工厂方法
        /// </summary>
        public static BeatmapWrapper? Create(string filePath)
        {
            try
            {
                Beatmap? beatmap = BeatmapDecoder.Decode(filePath);
                return beatmap != null ? new BeatmapWrapper(beatmap) : null;
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing && Beatmap != null)
            {
                // 清理 Beatmap 对象的集合引用
                Beatmap.HitObjects?.Clear();
                Beatmap.TimingPoints?.Clear();
                Beatmap.BPMEvents?.Clear();

                // 清理各个 Section 对象（如果需要的话）
                // 注意：这些 Section 对象通常包含基本数据类型，不需要特殊清理

                Beatmap = null;
            }

            _disposed = true;
        }

        /// <summary>
        /// 检查对象是否已释放
        /// </summary>
        public void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BeatmapWrapper));
        }
    }
}
