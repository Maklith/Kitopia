using System.Threading.Tasks;

namespace Core.Services.DeviceCommunication.Presentation;

public interface IFilePickerService
{
    Task<string?> PickFileToSendAsync();
    Task<string?> PickImageToSendAsync();
    Task<string?> PickSaveFilePathAsync(string title, string suggestedFileName);
}
