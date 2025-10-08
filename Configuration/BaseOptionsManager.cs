using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace krrTools.Configuration;

public static class BaseOptionsManager
{
    // 统一的配置文件名
    private const string ConfigFileName = "config.json";

    // 统一的配置文件路径 (exe 所在文件夹)
    private static string ConfigFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

    // 缓存的配置实例
    private static AppConfig? _cachedConfig;
    private static readonly Lock _configLock = new();

    /// <summary>
    /// 设置变化事件
    /// </summary>
    public static event Action<ConverterEnum>? SettingsChanged;

    /// <summary>
    /// 全局设置变化事件
    /// </summary>
    public static event Action? GlobalSettingsChanged;

    /// <summary>
    /// 获取源ID - 从枚举获取
    /// </summary>
    public static int GetSourceId(ConverterEnum converter)
    {
        return converter switch
        {
            ConverterEnum.N2NC => 1,
            ConverterEnum.DP => 3,
            ConverterEnum.KRRLN => 4,
            _ => 0
        };
    }

    /// <summary>
    /// 加载统一的应用程序配置
    /// </summary>
    private static AppConfig LoadConfig()
    {
        lock (_configLock)
        {
            if (_cachedConfig != null) return _cachedConfig;

            var path = ConfigFilePath;
            if (!File.Exists(path))
            {
                _cachedConfig = new AppConfig();
                return _cachedConfig;
            }

            try
            {
                var json = File.ReadAllText(path);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                _cachedConfig = JsonSerializer.Deserialize<AppConfig>(json, opts) ?? new AppConfig();

                return _cachedConfig;
            }
            catch (Exception ex)
            {
                Logger.WriteLine(LogLevel.Debug,
                    $"[BaseOptionsManager]Failed to load config file '{path}': {ex.Message}. Creating default config and overwriting file.");
                _cachedConfig = new AppConfig();
                SaveConfig(); // 覆盖损坏的文件
                return _cachedConfig;
            }
        }
    }

    /// <summary>
    /// 保存统一的应用程序配置
    /// </summary>
    private static void SaveConfig()
    {
        lock (_configLock)
        {
            if (_cachedConfig == null) return;

            var path = ConfigFilePath;
            try
            {
                var opts = new JsonSerializerOptions
                    { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
                var json = JsonSerializer.Serialize(_cachedConfig, opts);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Logger.WriteLine(LogLevel.Error,
                    $"[BaseOptionsManager]Failed to save config file '{path}': {ex.Message}");
                throw new IOException($"Unable to save configuration to '{path}': {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// 获取指定工具的选项
    /// </summary>
    public static T? LoadOptions<T>(ConverterEnum converter)
    {
        var config = LoadConfig();
        var value = config.Converters.GetValueOrDefault(converter);
        if (value is JsonElement jsonElement) return jsonElement.Deserialize<T>();
        return (T?)value;
    }

    /// <summary>
    /// 保存指定工具的选项
    /// </summary>
    public static void SaveOptions<T>(ConverterEnum converter, T options)
    {
        var config = LoadConfig();
        config.Converters[converter] = options;
        SaveConfig();
        SettingsChanged?.Invoke(converter);
    }

    /// <summary>
    /// 获取指定模块的选项
    /// </summary>
    public static T? LoadModuleOptions<T>(ModuleEnum module)
    {
        var config = LoadConfig();
        var value = config.Modules.GetValueOrDefault(module);
        if (value is JsonElement jsonElement) return jsonElement.Deserialize<T>();
        return (T?)value;
    }

    /// <summary>
    /// 保存指定模块的选项
    /// </summary>
    public static void SaveModuleOptions<T>(ModuleEnum module, T options)
    {
        var config = LoadConfig();
        config.Modules[module] = options;
        SaveConfig();
    }

    /// <summary>
    /// 保存预设
    /// </summary>
    public static void SavePreset<T>(string toolName, string presetName, T options)
    {
        var config = LoadConfig();
        if (!config.Presets.ContainsKey(toolName)) config.Presets[toolName] = new Dictionary<string, object?>();
        config.Presets[toolName][presetName] = options;
        SaveConfig();
    }

    /// <summary>
    /// 加载预设
    /// </summary>
    public static IEnumerable<(string Name, T? Options)> LoadPresets<T>(string toolName)
    {
        var config = LoadConfig();
        if (config.Presets.TryGetValue(toolName, out var toolPresets))
            foreach (var kvp in toolPresets)
            {
                T? opt = default;
                try
                {
                    if (kvp.Value is JsonElement jsonElement)
                        opt = jsonElement.Deserialize<T>();
                    else
                        opt = (T?)kvp.Value;
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(LogLevel.Error,
                        $"[BaseOptionsManager] Failed to deserialize preset '{kvp.Key}': {ex.Message}");
                }

                yield return (kvp.Key, opt);
            }
    }

    /// <summary>
    /// 保存管道预设
    /// </summary>
    public static void SavePipelinePreset(string presetName, PipelineOptions pipelineOptions)
    {
        var config = LoadConfig();
        config.PipelinePresets[presetName] = pipelineOptions;
        SaveConfig();
    }

    /// <summary>
    /// 加载管道预设
    /// </summary>
    public static IEnumerable<(string Name, PipelineOptions? Options)> LoadPipelinePresets()
    {
        var config = LoadConfig();
        foreach (var kvp in config.PipelinePresets) yield return (kvp.Key, kvp.Value);
    }

    /// <summary>
    /// 删除预设
    /// </summary>
    public static void DeletePreset(string toolName, string presetName)
    {
        var config = LoadConfig();
        if (config.Presets.TryGetValue(toolName, out var toolPresets))
        {
            toolPresets.Remove(presetName);
            if (toolPresets.Count == 0) config.Presets.Remove(toolName);
            SaveConfig();
        }
    }

    /// <summary>
    /// 获取全局设置
    /// </summary>
    public static GlobalSettings GetGlobalSettings()
    {
        return GetAppSetting(c => c.GlobalSettings);
    }

    /// <summary>
    /// 保存全局设置
    /// </summary>
    private static void SetGlobalSettings(GlobalSettings settings)
    {
        SetAppSetting(c => c.GlobalSettings = settings);
        GlobalSettingsChanged?.Invoke();
    }

    /// <summary>
    /// 更新全局设置（部分更新）
    /// </summary>
    public static void UpdateGlobalSettings(Action<GlobalSettings> updater)
    {
        var settings = GetGlobalSettings();
        updater(settings);
        SetGlobalSettings(settings);
    }

    /// <summary>
    /// 获取应用设置
    /// </summary>
    private static T GetAppSetting<T>(Func<AppConfig, T> getter)
    {
        return getter(LoadConfig());
    }

    /// <summary>
    /// 设置应用设置
    /// </summary>
    private static void SetAppSetting(Action<AppConfig> setter)
    {
        var config = LoadConfig();
        setter(config);
        SaveConfig();
    }

    /// <summary>
    /// 获取实时预览设置
    /// </summary>
    public static bool GetRealTimePreview()
    {
        return GetGlobalSettings().RealTimePreview;
    }

    /// <summary>
    /// 保存实时预览设置
    /// </summary>
    public static void SetRealTimePreview(bool value)
    {
        UpdateGlobalSettings(s => s.RealTimePreview = value);
    }

    /// <summary>
    /// 获取应用程序主题设置
    /// </summary>
    public static string? GetApplicationTheme()
    {
        return GetGlobalSettings().ApplicationTheme;
    }

    /// <summary>
    /// 保存应用程序主题设置
    /// </summary>
    public static void SetApplicationTheme(string? theme)
    {
        UpdateGlobalSettings(s => s.ApplicationTheme = theme);
    }

    /// <summary>
    /// 获取窗口背景类型设置
    /// </summary>
    public static string? GetWindowBackdropType()
    {
        return GetGlobalSettings().WindowBackdropType;
    }

    /// <summary>
    /// 保存窗口背景类型设置
    /// </summary>
    public static void SetWindowBackdropType(string? backdropType)
    {
        UpdateGlobalSettings(s => s.WindowBackdropType = backdropType);
    }

    /// <summary>
    /// 获取是否更新主题色设置
    /// </summary>
    public static bool GetUpdateAccent()
    {
        return GetGlobalSettings().UpdateAccent;
    }

    /// <summary>
    /// 保存是否更新主题色设置
    /// </summary>
    public static void SetUpdateAccent(bool updateAccent)
    {
        UpdateGlobalSettings(s => s.UpdateAccent = updateAccent);
    }

    /// <summary>
    /// 获取是否强制中文设置
    /// </summary>
    public static bool GetForceChinese()
    {
        return GetGlobalSettings().ForceChinese;
    }

    /// <summary>
    /// 保存是否强制中文设置
    /// </summary>
    public static void SetForceChinese(bool forceChinese)
    {
        UpdateGlobalSettings(s => s.ForceChinese = forceChinese);
    }

    // DP specific constants
    public const string DPDefaultTag = "krrcream's converter DP";

    // LN specific constants
    public const string KRRLNDefaultTag = "krrcream's converter LN";
}