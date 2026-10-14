using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Protocol;
using Kitopia.Desktop.Features.Services.Interfaces;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Feature.Localization;
using PluginCore;
using PluginCore.Onnx;

namespace Kitopia.Desktop.Services;

public sealed class OnnxRuntimeProbe(string executablePath, string entryAssemblyPath, int mqttPort, TimeSpan? timeout = null)
    : IOnnxRuntimeProbe
{
    public const string CommandLineArgument = "--onnx-runtime-probe";
    private readonly ConditionalWeakTable<Plugin, ConcurrentDictionary<string,
        (bool? Available, string? Error, bool TimedOut)>> _results = new();

    public async Task<bool?> CheckAsync(string device, CancellationToken cancellationToken = default, bool forceRefresh = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = PluginOverall.OnnxRuntimes.FirstOrDefault(runtime => runtime.Value.ContainsKey(device)).Key;
        if (source is null || !PluginManager.GetEnablePlugins().TryGetValue(source, out var plugin))
            throw new InvalidOperationException(Lang.Get("lang.kitopia.onnx.runtime_not_installed"));

        // Reloading creates a new plugin key; cached strings must not keep the old plugin alive.
        var results = _results.GetOrCreateValue(plugin);
        if (forceRefresh) results.TryRemove(device, out _);
        else if (results.TryGetValue(device, out var cached))
        {
            if (cached.Error is { } error)
            {
                if (cached.TimedOut) throw new TimeoutException(error);
                throw new InvalidOperationException(error);
            }
            return cached.Available;
        }

        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Queue<string>(plugin.PluginInfo.PluginBaseInfo.Dependencies.Keys);
        while (pending.TryDequeue(out var name))
        {
            if (name == "Kitopia" || dependencies.ContainsKey(name)) continue;
            if (!PluginManager.GetEnablePlugins().TryGetValue(name, out var dependency))
                throw new InvalidOperationException($"The runtime dependency {name} is not enabled.");
            dependencies.Add(name, dependency.PluginInfo.FullPath);
            foreach (var nested in dependency.PluginInfo.PluginBaseInfo.Dependencies.Keys) pending.Enqueue(nested);
        }
        try
        {
            var available = await CheckAsync(plugin.PluginInfo.FullPath, device, cancellationToken, dependencies).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (forceRefresh) results[device] = (available, null, false);
            else results.TryAdd(device, (available, null, false));
            return available;
        }
        catch (Exception exception) when (exception is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            var failure = ((bool?)false, exception.GetBaseException().Message, exception is TimeoutException);
            if (forceRefresh) results[device] = failure;
            else results.TryAdd(device, failure);
            throw;
        }
    }

    public async Task<bool?> CheckAsync(string pluginPath, string device, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? dependencies = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid().ToString("N");
        var resultTopic = $"kitopia/onnx-probe/{requestId}";
        var reply = new TaskCompletionSource<(bool? Available, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
            startInfo.ArgumentList.Add(entryAssemblyPath);
        startInfo.ArgumentList.Add(CommandLineArgument);
        startInfo.ArgumentList.Add(pluginPath);
        startInfo.ArgumentList.Add(device);
        startInfo.ArgumentList.Add(mqttPort.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add(resultTopic);
        startInfo.ArgumentList.Add(JsonSerializer.Serialize(dependencies));

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(60));
        using var client = new MqttClientFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += args =>
        {
            if (args.ApplicationMessage.Topic != resultTopic) return Task.CompletedTask;
            try
            {
                using var result = JsonDocument.Parse(args.ApplicationMessage.Payload);
                var available = result.RootElement.GetProperty("IsAvailable");
                reply.TrySetResult((available.ValueKind == JsonValueKind.Null ? null : available.GetBoolean(),
                    result.RootElement.GetProperty("Error").GetString()));
            }
            catch (Exception exception) { reply.TrySetException(exception); }
            return Task.CompletedTask;
        };
        using var process = new Process { StartInfo = startInfo };
        var started = false;
        Task output = Task.CompletedTask;
        Task errors = Task.CompletedTask;
        try
        {
            await client.ConnectAsync(new MqttClientOptionsBuilder().WithClientId($"onnx-probe-parent-{requestId}")
                .WithTcpServer("127.0.0.1", mqttPort).Build(), deadline.Token).ConfigureAwait(false);
            await client.SubscribeAsync(new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(resultTopic, MqttQualityOfServiceLevel.AtLeastOnce).Build(), deadline.Token).ConfigureAwait(false);
            started = process.Start();
            if (!started) throw new InvalidOperationException(Lang.Get("lang.kitopia.onnx.probe_no_result"));
            // Drain native logging without retaining it or mixing it with the JSON result.
            output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
            errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null, deadline.Token);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await Task.WhenAll(output, errors).WaitAsync(deadline.Token).ConfigureAwait(false);
            if (process.ExitCode != 0)
                throw new InvalidOperationException(Lang.Format("lang.kitopia.onnx.probe_process_failed",
                    $"0x{unchecked((uint)process.ExitCode):X8}"));
            var result = await reply.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
            if (result.Error is { } error) throw new InvalidOperationException(error);
            return result.Available;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(Lang.Get("lang.kitopia.onnx.probe_timeout"));
        }
        finally
        {
            if (started)
            {
                if (!process.HasExited)
                {
                    try { process.Kill(entireProcessTree: true); }
                    catch (InvalidOperationException) { }
                }
                await process.WaitForExitAsync().ConfigureAwait(false);
                await deadline.CancelAsync().ConfigureAwait(false);
                try { await Task.WhenAll(output, errors).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }
        }
    }

    public static async Task RunAsync(string pluginPath, string device, int mqttPort, string resultTopic, string dependenciesJson)
    {
        bool? available = null;
        string? error = null;
        try
        {
            var contexts = new List<AssemblyLoadContextH>();
            var context = new AssemblyLoadContextH(pluginPath, "onnx-runtime-probe", []);
            contexts.Add(context);
            var dependencies = JsonSerializer.Deserialize<Dictionary<string, string>>(dependenciesJson);
            if (dependencies is not null)
                foreach (var (name, path) in dependencies)
                    contexts.Add(new AssemblyLoadContextH(path, name, []));
            foreach (var loader in contexts)
                loader.Resolving += (_, name) =>
                {
                    foreach (var dependency in contexts.Where(candidate => candidate != loader))
                    {
                        if (dependency.Assembly.GetName().Name == name.Name) return dependency.Assembly;
                    }
                    return null;
                };

            var types = context.Assembly.GetExportedTypes();
            var entryType = types.Single(type => type.IsClass && !type.IsAbstract && typeof(IPlugin).IsAssignableFrom(type));
            var factory = entryType.GetMethod(nameof(IPlugin.GetServiceProvider), BindingFlags.Public | BindingFlags.Static)
                          ?? throw new InvalidOperationException("The runtime plugin has no service provider factory.");
            var provider = factory.Invoke(null, null) as IServiceProvider
                           ?? throw new InvalidOperationException("The runtime plugin did not create a service provider.");
            try
            {
                var found = false;
                foreach (var type in types.Where(type => type.IsClass && !type.IsAbstract && typeof(IInferenceSession).IsAssignableFrom(type)))
                {
                    using var session = provider.GetService(type) as IInferenceSession
                                        ?? throw new InvalidOperationException($"The runtime session {type.FullName} is not registered.");
                    if (session.Device != device) continue;
                    available = session.CheckAvailability();
                    found = true;
                    break;
                }
                if (!found) throw new InvalidOperationException($"The runtime plugin does not provide device {device}.");
            }
            finally
            {
                if (provider is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                else (provider as IDisposable)?.Dispose();
            }
        }
        catch (Exception exception)
        {
            error = exception.GetBaseException().Message;
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var client = new MqttClientFactory().CreateMqttClient();
        await client.ConnectAsync(new MqttClientOptionsBuilder().WithClientId($"onnx-probe-worker-{Guid.NewGuid():N}")
            .WithTcpServer("127.0.0.1", mqttPort).Build(), deadline.Token).ConfigureAwait(false);
        await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic(resultTopic)
            .WithPayload(JsonSerializer.SerializeToUtf8Bytes(new { IsAvailable = available, Error = error }))
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce).Build(), deadline.Token).ConfigureAwait(false);
    }
}
