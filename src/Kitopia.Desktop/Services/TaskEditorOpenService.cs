#region

using Avalonia.Threading;
using Kitopia.Desktop.Features.CustomScenario;
using Kitopia.Desktop.Features.CustomScenario.Services;
using PluginCore;
using CustomScenarioModel = Kitopia.Desktop.Features.CustomScenario.CustomScenario;
using TaskEditor = Kitopia.Desktop.Windows.TaskEditors.TaskEditor;

#endregion

namespace Kitopia.Desktop.Services;

public class TaskEditorOpenService : ITaskEditorOpenService
{
    public void Open()
    {
        Dispatcher.UIThread.Post(() =>
        {
            ((TaskEditor)ServiceManager.Services!.GetService(typeof(TaskEditor))!)!.Show();
        });
    }

    public void Open(CustomScenarioModel name)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var taskEditor = ((TaskEditor)ServiceManager.Services!.GetService(typeof(TaskEditor))!)!;
            taskEditor.LoadTask(name);
            taskEditor.Show();
        });
    }
}
