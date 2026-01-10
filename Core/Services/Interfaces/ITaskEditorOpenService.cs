namespace Core.Services.Interfaces;

public interface ITaskEditorOpenService
{
    void Open();
    void Open(CustomScenario.CustomScenario name);
}