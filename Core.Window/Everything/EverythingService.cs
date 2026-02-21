using Core.Services.Interfaces;

namespace Core.Window.Everything;

public class EverythingService : IEverythingService
{
    

    public bool IsRun()
    {
        return EverythingTools.IsRun();
    }
}