using System;
using System.Threading.Tasks;
using Core.Services.DeviceCommunication.Protocol;
using PluginCore;

namespace Core.Services.DeviceCommunication.Clipboard;

public interface IClipboardSyncService : IDisposable
{
    bool IsEnabled { get; }
    DeviceModel? TargetDevice { get; }

    event EventHandler<DeviceClipboardSyncStateChangedEventArgs>? StateChanged;
    event EventHandler<DeviceClipboardSyncAuthorizedEventArgs>? Authorized;

    Task SendTextAsync(DeviceModel target, string text);
    Task<bool> RequestAsync(DeviceModel target, TimeSpan timeout, string duplicateError);
    Task<bool> EnableAsync(DeviceModel target, TimeSpan timeout, string duplicateError);
    void Disable(string status = "实时同步剪贴板已关闭", bool keepTargetWhenDisabled = false);

    void ApplyIncomingClipboardText(DeviceModel sender, string text);
    Task HandleIncomingRequestAsync(PacketMetadata packet, DeviceModel sender, Func<DeviceModel, Task<bool>> promptIncomingAsync);
    Task HandleIncomingResponseAsync(PacketMetadata packet, DeviceModel sender);
}
