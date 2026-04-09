using System.Collections.Generic;
using System.Threading.Tasks;

namespace Core.Services.DeviceCommunication.FileTransfer;

public interface IClipboardAssetExtractor
{
    Task<IReadOnlyList<string>> TryGetClipboardFilePathsAsync();
    string? TryExtractClipboardImageToTempFilePath();
}
