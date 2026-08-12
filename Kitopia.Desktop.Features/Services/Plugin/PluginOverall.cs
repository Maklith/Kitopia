using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls.Notifications;
using Kitopia.Desktop.Features.Search.Semantic;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Utils;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;
using PluginCore.Onnx;
using PluginCore.SearchWindow.InputData;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Kitopia.Desktop.Features.Services.Plugin;

public class PluginOverall
{
    private const string BuiltInFeatureSource = "Kitopia";

    public static readonly Dictionary<string, List<ScreenCaptureExMethod>> ScreenCaptureExMethods = new();
    public static readonly Dictionary<string, List<OnnxModelInfoWrapper>> OnnxModelInfos = new();
    public static readonly Dictionary<string, Dictionary<string, Func<IInferenceSession>>> OnnxRuntimes = new();
    public static readonly ObservableDictionary<string, List<FeatureInfo>> Features = new();

    public static readonly ConcurrentDictionary<string, List<Func<InputDataAnalyzeTimeFlags, string?, IEnumerable<InputData>>>>
        SearchWindowInputDataIdentifies = new();

    public static readonly
        ConcurrentDictionary<string, List<(Func<InputDataAnalyzeTimeFlags>,
            Func<IEnumerable<InputData>, IEnumerable<SearchViewItem>>)>> SearchWindowInputDataAnalyzers = new();

    public static List<OnnxModelInfoWrapper> AllOnnxModelInfos =>
        OnnxModelInfos.Values.SelectMany(e => e).ToList();

    public static List<string> AllTargetDevices => OnnxRuntimes.Values.SelectMany(e => e.Keys).ToList();

    public static List<ScreenCaptureExMethod> AllScreenCaptureExMethods =>
        ScreenCaptureExMethods.Values.SelectMany(e => e).ToList();

    public static IReadOnlyList<FeatureInfo> AllFeatures
    {
        get
        {
            lock (Features)
            {
                return Features.Values
                    .SelectMany(features => features)
                    .OrderBy(feature => feature.Order)
                    .ThenBy(feature => feature.Name, StringComparer.Ordinal)
                    .ToList();
            }
        }
    }
    
    public static ObservableDictionary<string,ContextMenuItem> ContextMenuItems = new();

    public static Func<IInferenceSession>? GetOnnxRuntime(string targetDevice)
    {
        var firstOrDefault = OnnxRuntimes.Values.SelectMany(e => e).FirstOrDefault(e => e.Key == targetDevice);

        return firstOrDefault.Value ?? null;
    }

    static PluginOverall()
    {
        var builtInFeatures = new List<FeatureInfo>();
        foreach (var methodInfo in typeof(KitopiaFeatures).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (methodInfo.GetCustomAttribute<FeatureAttribute>() is { } featureAttribute)
            {
                builtInFeatures.Add(CreateFeature(
                    BuiltInFeatureSource,
                    methodInfo,
                    featureAttribute,
                    null));
            }
        }

        Features.Add(BuiltInFeatureSource, builtInFeatures);
        OnnxModelInfos.Add(BuiltInFeatureSource,
        [
            new OnnxModelInfoWrapper
            {
                Model = BgeModelPackage.CreateModelInfo(),
                PluginStr = BuiltInFeatureSource
            }
        ]);

        var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kitopia.Desktop.exe");
        
        ContextMenuItems.Add("kitopia", new ContextMenuItem
        {
            SubItems = [
                new ContextMenuItem
                {
                    Title = "添加到索引",
                    Icon = exePath,
                    Command = exePath,
                    Arguments = StartupArgumentManager.GenerateCmd(StartupAction.IndexAdd, "{0}"),
            
                },
                new ContextMenuItem
                {
                    Title = "文件占用解锁",
                    Icon = exePath,
                    Command = exePath,
                    Arguments = StartupArgumentManager.GenerateCmd(StartupAction.FileLocksmith, "{0}"), // Pass path to FileLocksmith
                },
                new ContextMenuItem
                {
                    Title = "局域网分享",
                    Icon = exePath,
                    Command = exePath,
                    Arguments = StartupArgumentManager.GenerateCmd(StartupAction.LanFileShare, "{all}"),
                }
            ]
        });
    }

    public static void InitializeContextMenu()
    {
        ServiceManager.Services?.GetService<IExplorerContextMenuConfiger>()
            ?.OverwriteMenuItems(ContextMenuItems.SelectMany(entry => entry.Value.SubItems).ToList());
    }

    public static FeatureInfo CreateFeature(
        string source,
        MethodInfo methodInfo,
        FeatureAttribute featureAttribute,
        IServiceProvider? serviceProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(methodInfo);
        ArgumentNullException.ThrowIfNull(featureAttribute);
        ValidateFeatureMethod(methodInfo, featureAttribute.Activation);

        return new FeatureInfo
        {
            Id = featureAttribute.Id,
            Name = featureAttribute.Name,
            Description = featureAttribute.Description,
            Category = featureAttribute.Category,
            Source = source,
            IconSymbol = featureAttribute.IconSymbol,
            Order = featureAttribute.Order,
            ExecuteAsync = featureAttribute.Activation switch
            {
                FeatureActivationMode.Direct => cancellationToken =>
                    InvokeFeatureMethodAsync(methodInfo, serviceProvider, cancellationToken),
                FeatureActivationMode.ScreenCapture => cancellationToken =>
                    StartScreenCaptureFeatureAsync(methodInfo, serviceProvider, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(featureAttribute),
                    featureAttribute.Activation,
                    "不支持的功能启动方式。")
            }
        };
    }

    private static void ValidateFeatureMethod(MethodInfo methodInfo, FeatureActivationMode activation)
    {
        var parameters = methodInfo.GetParameters();
        var hasSupportedParameters = activation switch
        {
            FeatureActivationMode.Direct =>
                parameters.Length == 0
                || parameters is [{ ParameterType: var parameterType }]
                && parameterType == typeof(CancellationToken),
            FeatureActivationMode.ScreenCapture =>
                parameters is [{ ParameterType: var parameterType }]
                && parameterType == typeof(ScreenCaptureResult)
                || parameters is
                [
                    { ParameterType: var captureType },
                    { ParameterType: var cancellationType }
                ]
                && captureType == typeof(ScreenCaptureResult)
                && cancellationType == typeof(CancellationToken),
            _ => false
        };

        if (!hasSupportedParameters)
        {
            throw new InvalidOperationException(
                $"功能入口 {methodInfo.DeclaringType?.FullName}.{methodInfo.Name} 的参数签名不受支持。");
        }

        var returnType = methodInfo.ReturnType;
        if (returnType != typeof(void)
            && !typeof(Task).IsAssignableFrom(returnType)
            && returnType != typeof(ValueTask))
        {
            throw new InvalidOperationException(
                $"功能入口 {methodInfo.DeclaringType?.FullName}.{methodInfo.Name} 必须返回 void、Task 或 ValueTask。");
        }
    }

    private static async Task InvokeFeatureMethodAsync(
        MethodInfo methodInfo,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken,
        ScreenCaptureResult? captureResult = null)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = methodInfo.IsStatic
            ? null
            : serviceProvider?.GetService(methodInfo.DeclaringType!)
              ?? throw new InvalidOperationException($"无法创建功能入口 {methodInfo.DeclaringType?.FullName}.{methodInfo.Name}。");

        var parameters = methodInfo.GetParameters();
        object?[] arguments = (captureResult, parameters.Length) switch
        {
            (null, 0) => [],
            (null, 1) when parameters[0].ParameterType == typeof(CancellationToken) => [cancellationToken],
            (not null, 1) when parameters[0].ParameterType == typeof(ScreenCaptureResult) => [captureResult],
            (not null, 2) when parameters[0].ParameterType == typeof(ScreenCaptureResult)
                               && parameters[1].ParameterType == typeof(CancellationToken) =>
                [captureResult, cancellationToken],
            _ => throw new InvalidOperationException(
                $"功能入口 {methodInfo.DeclaringType?.FullName}.{methodInfo.Name} 的参数签名不受支持。")
        };

        var result = methodInfo.Invoke(target, arguments);
        if (result is Task task)
        {
            await task;
        }
        else if (result is ValueTask valueTask)
        {
            await valueTask;
        }
    }

    private static Task StartScreenCaptureFeatureAsync(
        MethodInfo methodInfo,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var screenCaptureWindow = ServiceManager.Services?.GetService<IScreenCaptureWindow>();
        if (screenCaptureWindow is null)
        {
            ShowUnavailable("截图功能");
            return Task.CompletedTask;
        }

        screenCaptureWindow.RequestUserSelectScreenBytes(ExecuteCaptureFeature, static () => { });
        return Task.CompletedTask;

        async void ExecuteCaptureFeature(ScreenCaptureResult captureResult)
        {
            try
            {
                await InvokeFeatureMethodAsync(methodInfo, serviceProvider, cancellationToken, captureResult);
            }
            catch (Exception exception)
            {
                ShowToast(
                    "功能执行失败",
                    exception.InnerException?.Message ?? exception.Message,
                    NotificationType.Error);
            }
        }
    }

    private static void ShowUnavailable(string featureName)
    {
        ShowToast(featureName, "当前平台暂不支持此功能。", NotificationType.Warning);
    }

    private static void ShowToast(string title, string message, NotificationType notificationType)
    {
        _ = ServiceManager.Services?.GetService<IToastService>()?.Show(title, message, notificationType);
    }

    public void UpdateContextMenuItems(ObservableDictionary<string, ContextMenuItem> items)
    {
        
    }
}
