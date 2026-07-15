using System;
using System.Threading.Tasks;
using Kitopia.Feature.DeviceCommunication.Application;

namespace Kitopia.Mobile.Services;

public sealed class MobileChatPlatformService : IChatPlatformService
{
    private readonly MobileTopLevelContext _topLevel;

    public MobileChatPlatformService(MobileTopLevelContext topLevel)
    {
        _topLevel = topLevel;
    }

    public bool CanOpenFile => false;

    public void OpenFile(string path) { }

    public Task<string?> PromptTextAsync(string title, string prompt, string? initialValue) =>
        _topLevel.PromptTextAsync(title, prompt, initialValue);

    public ChatDisplayContext GetDisplayContext(string? selectedConversationId)
    {
        _ = selectedConversationId;
        var isActive = _topLevel.IsActivityActive && _topLevel.CurrentTopLevel is not null;
        return new ChatDisplayContext(isActive, isActive);
    }

}
