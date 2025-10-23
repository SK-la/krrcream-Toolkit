#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace krrTools.Tests;

/// <summary>
/// 用于在STA线程中运行WPF UI测试的帮助类
/// </summary>
public static class STATestHelper
{
    private static Application? _application;
    private static readonly object _applicationLock = new();

    /// <summary>
    /// 在STA线程中运行测试方法
    /// </summary>
    public static void RunInSTA(Action testAction)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 确保只有一个Application实例
                lock (_applicationLock)
                {
                    if (_application == null)
                    {
                        _application = new Application();
                    }
                }
                testAction();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null) throw exception;
    }

    /// <summary>
    /// 在STA线程中运行异步测试方法
    /// </summary>
    public static async Task RunInSTAAsync(Func<Task> testAction)
    {
        Exception? exception = null;
        var thread = new Thread(async () =>
        {
            try
            {
                if (Application.Current == null) 
                    new Application();
                await testAction();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null) throw exception;
    }

    /// <summary>
    /// 在STA线程中运行有返回值的测试方法
    /// </summary>
    public static T RunInSTA<T>(Func<T> testFunc)
    {
        T? result = default;
        Exception? exception = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = testFunc();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null) throw exception;

        return result!;
    }
}