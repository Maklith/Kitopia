using System.Threading;
using System.Threading.Tasks;
using PluginCore.CustomScenario.Attribute.Scenario;
using SharpHook;
using SharpHook.Data;
using SharpHook.Native;

namespace KitopiaEx.CustomScenarioMethods;

public class KeyboardSimulation
{
    [ScenarioMethod("lang.kitopiaex.press_keyboard_key", "key=lang.kitopiaex.key", Id = "按下键盘按键")]
    public void PressKey([SelfInput] KeyCode key, CancellationToken ct)
    {
        var eventSimulator = new EventSimulator();
        eventSimulator.SimulateKeyPress(key);
    }

    [ScenarioMethod("lang.kitopiaex.release_keyboard_key", "key=lang.kitopiaex.key", Id = "释放键盘按键")]
    public void ReleaseKey([SelfInput] KeyCode key, CancellationToken ct)
    {
        var eventSimulator = new EventSimulator();
        eventSimulator.SimulateKeyRelease(key);
    }

    [ScenarioMethod("lang.kitopiaex.press_keyboard_key_and_release_after_delay", "key=lang.kitopiaex.key", Id = "按下键盘按键并延迟释放")]
    public void PressAndReleaseKey([SelfInput] KeyCode key, CancellationToken ct)
    {
        PressKey(key, ct);
        Task.Delay(200).GetAwaiter().GetResult();
        ReleaseKey(key, ct);
    }
}