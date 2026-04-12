using Core.Services.DeviceCommunication.Messages;

namespace Core.Services.DeviceCommunication.Application;

public sealed record IncomingMessageEvent(AppMessage Message);
