namespace Kitopia.Desktop.Features.CustomScenario;

/// <summary>
/// 自定义情景值元组 / Custom scenario value tuple for type-value pairs
/// </summary>
public sealed class CustomScenarioValueTuple
{
    /// <summary>获取或设置类型 / Gets or sets the type</summary>
    public required Type Type { get; set; }

    /// <summary>获取或设置值 / Gets or sets the value</summary>
    public required object Value { get; set; }
}
