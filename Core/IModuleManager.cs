using System.Collections.Generic;

namespace krrTools.Core;

/// <summary>
/// 模块管理器接口 - 只负责模块注册和管理
/// </summary>
public interface IModuleManager
{
    /// <summary>
    /// 获取所有模块
    /// </summary>
    IEnumerable<IToolModule> GetAllModules();

    /// <summary>
    /// 注册模块
    /// </summary>
    void RegisterModule(IToolModule module);

    /// <summary>
    /// 注销模块
    /// </summary>
    void UnregisterModule(IToolModule module);

    /// <summary>
    /// 根据名称获取工具
    /// </summary>
    /// <param name="toolName">工具名称</param>
    /// <returns>工具实例，失败返回null</returns>
    ITool? GetToolName(string toolName);
}