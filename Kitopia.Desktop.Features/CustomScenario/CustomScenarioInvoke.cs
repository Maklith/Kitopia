namespace Kitopia.Desktop.Features.CustomScenario;

public enum TriggerType
{
    手动,
    自动,
    定时,
    定周期
}

public enum AutoTriggerType
{
    软件启动时 = 0,
    软件关闭时 = 1,
    系统关闭时 = 2,
    Custom = 1000
}

public class CustomScenarioInvoke
{
    private CustomScenario _customScenario;

    public bool IsUsed { get; set; } = false;

    public TriggerType TriggerType { get; set; }

    public AutoTriggerType AutoTriggerType { get; set; }

    public string AutoTriggerTypeFrom { get; set; }

    public object? Value { get; set; }
}