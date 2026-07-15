using System.Reflection;
using Kitopia.Feature.DeviceCommunication;
using Kitopia.Feature.DeviceCommunication.Application;
using Kitopia.Feature.DeviceCommunication.Transport;
using Kitopia.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace KitopiaTest.DeviceCommunication;

[TestClass]
public sealed class DesktopCommunicationRuntimeRegistrationTests
{
    [TestMethod]
    public void ChatContracts_KeepAttachmentAndClipboardCapabilitiesSeparateFromPlatform()
    {
        var platformMethods = typeof(IChatPlatformService).GetMethods().Select(method => method.Name).ToArray();
        var attachmentMethods = typeof(IChatAttachmentStore).GetMethods().Select(method => method.Name).ToArray();
        var clipboardMethods = typeof(IChatClipboardService).GetMethods().Select(method => method.Name).ToArray();

        CollectionAssert.DoesNotContain(platformMethods, nameof(IChatAttachmentStore.PickFilesToSendAsync));
        CollectionAssert.DoesNotContain(platformMethods, nameof(IChatAttachmentStore.PickSaveTargetAsync));
        CollectionAssert.DoesNotContain(platformMethods, nameof(IChatAttachmentStore.GetFileIconPng));
        CollectionAssert.Contains(attachmentMethods, nameof(IChatAttachmentStore.PickFilesToSendAsync));
        CollectionAssert.Contains(attachmentMethods, nameof(IChatAttachmentStore.PickSaveTargetAsync));
        CollectionAssert.Contains(attachmentMethods, nameof(IChatAttachmentStore.GetFileIconPng));
        CollectionAssert.Contains(clipboardMethods, nameof(IChatClipboardService.SetTextAsync));
    }

    [TestMethod]
    public void ConfigureServices_ResolvesSharedRuntimeWithoutCoreMessageBridge()
    {
        var programType = typeof(Kitopia.Desktop.App).Assembly.GetType("Kitopia.Desktop.Program");
        var configureServices = programType?.GetMethod(
            "ConfigureServices",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.IsNotNull(configureServices);
        using var serviceProvider = (ServiceProvider)configureServices.Invoke(null, null)!;

        Assert.IsInstanceOfType<MessageAppService>(
            serviceProvider.GetRequiredService<IMessageAppService>());
        Assert.IsInstanceOfType<LocalDataListenerHost>(
            serviceProvider.GetRequiredService<ILocalDataListener>());
        Assert.IsInstanceOfType<DesktopIncomingMessageSink>(
            serviceProvider.GetRequiredService<IIncomingMessageSink>());
        Assert.IsInstanceOfType<DeviceCommunicationRuntime>(
            serviceProvider.GetRequiredService<IDeviceCommunicationRuntime>());
        Assert.IsInstanceOfType<DesktopChatAttachmentStore>(
            serviceProvider.GetRequiredService<IChatAttachmentStore>());
        Assert.IsInstanceOfType<DesktopChatNotificationSink>(
            serviceProvider.GetRequiredService<IChatNotificationSink>());
        Assert.IsInstanceOfType<DesktopChatClipboardService>(
            serviceProvider.GetRequiredService<IChatClipboardService>());
        Assert.IsInstanceOfType<DesktopChatPlatformService>(
            serviceProvider.GetRequiredService<IChatPlatformService>());
    }
}
