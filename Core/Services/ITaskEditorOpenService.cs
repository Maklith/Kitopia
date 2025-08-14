namespace Core.Services;

public interface ITaskEditorOpenService
{
    void Open();
    void Open(CustomScenario.CustomScenario name);
}