namespace Core.CustomScenario;

/// <summary>
/// 场景方法类型枚举，定义了自定义场景中支持的各种操作类型
/// Enumeration of scenario method types that defines various operation types supported in custom scenarios
/// </summary>
public enum ScenarioMethodType
{
    /// <summary>插件方法 / Plugin method</summary>
    PluginMethod,
    /// <summary>条件判断 / Conditional operation</summary>
    Condition,
    /// <summary>一对二分支 / One-to-two branch</summary>
    OneToTwo,
    /// <summary>一对多分支 / One-to-many branch</summary>
    OneToMany,
    /// <summary>相等比较 / Equality comparison</summary>
    Equal,
    /// <summary>变量设置 / Variable assignment</summary>
    VariableSet,
    /// <summary>变量获取 / Variable retrieval</summary>
    VariableGet,
    /// <summary>临时变量设置 / Temporary variable assignment</summary>
    TempVariableSet,
    /// <summary>临时变量获取 / Temporary variable retrieval</summary>
    TempVariableGet,
    /// <summary>打开运行本地项目 / Open and run local project</summary>
    OpenRunLocalProject,
    /// <summary>默认操作 / Default operation</summary>
    Default
}