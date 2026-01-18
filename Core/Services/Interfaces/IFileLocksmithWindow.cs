using System.Collections.Generic;
using Core.Services.Interfaces;

namespace Core.Services.Interfaces;

public interface IFileLocksmithWindow
{
    void Show(List<LockingProcessInfo> processes);
}
