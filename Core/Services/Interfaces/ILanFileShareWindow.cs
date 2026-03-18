using System.Collections.Generic;

namespace Core.Services.Interfaces;

public interface ILanFileShareWindow
{
    void Show(IReadOnlyCollection<string> filePaths);
}
