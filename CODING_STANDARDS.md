# Kitopia C# 编码规范文档 / C# Coding Standards

## 目录 / Table of Contents

- [命名规范 / Naming Conventions](#命名规范--naming-conventions)
- [代码格式 / Code Formatting](#代码格式--code-formatting)
- [注释和文档 / Comments and Documentation](#注释和文档--comments-and-documentation)
- [异步编程 / Asynchronous Programming](#异步编程--asynchronous-programming)
- [异常处理 / Exception Handling](#异常处理--exception-handling)
- [最佳实践 / Best Practices](#最佳实践--best-practices)

---

## 命名规范 / Naming Conventions

### 1. 类和接口 / Classes and Interfaces

**✅ 正确 / Correct:**
```csharp
public class TaskEditorViewModel { }
public interface ISearchWindowService { }
public abstract class ScenarioNodeBase { }
public enum NodeStatus { }
```

**❌ 错误 / Incorrect:**
```csharp
public class taskEditorViewModel { }  // 首字母应大写
public interface SearchWindowService { }  // 接口应以 I 开头
public enum S节点状态 { }  // 避免中文命名
```

### 2. 方法和属性 / Methods and Properties

**✅ 正确 / Correct:**
```csharp
public string PropertyName { get; set; }
public void ExecuteCommand() { }
public async Task LoadDataAsync() { }  // 异步方法以 Async 结尾
```

**❌ 错误 / Incorrect:**
```csharp
public string propertyName { get; set; }  // 应使用 PascalCase
public void executeCommand() { }  // 应使用 PascalCase
public async Task LoadData() { }  // 异步方法应以 Async 结尾
```

### 3. 字段和变量 / Fields and Variables

**✅ 正确 / Correct:**
```csharp
// 私有字段
private string _fieldName;
private readonly ILogger _logger;

// 常量
public const string DEFAULT_VALUE = "default";
private const int MAX_RETRY_COUNT = 3;

// 局部变量和参数
public void Method(string parameterName)
{
    var localVariable = "value";
    int count = 0;
}
```

**❌ 错误 / Incorrect:**
```csharp
private string fieldName;  // 私有字段应有下划线前缀
private string _FieldName;  // 私有字段应使用 camelCase
public const string defaultValue = "default";  // 常量应使用 UPPER_CASE
```

### 4. 国际化命名 / Internationalization Naming

**原则：**公共 API 使用英文，内部注释可保留中文以保持业务含义。

**✅ 正确 / Correct:**
```csharp
/// <summary>
/// 节点状态枚举 / Node status enumeration
/// </summary>
public enum NodeStatus
{
    /// <summary>未验证 / Unverified</summary>
    Unverified,
    /// <summary>已验证 / Verified</summary>
    Verified,
    /// <summary>执行中 / Executing</summary>
    Executing,
    /// <summary>错误 / Error</summary>
    Error
}
```

**❌ 错误 / Incorrect:**
```csharp
public enum S节点状态  // 枚举名使用中文
{
    未验证,  // 枚举值使用中文
    已验证,
    执行中,
    错误
}
```

---

## 代码格式 / Code Formatting

### 1. 大括号风格 / Brace Style

使用 Allman 风格（大括号另起一行）

**✅ 正确 / Correct:**
```csharp
public class ExampleClass
{
    public void Method()
    {
        if (condition)
        {
            // 代码
        }
        else
        {
            // 代码
        }
    }
}
```

### 2. 缩进和空格 / Indentation and Spacing

- 使用 4 个空格缩进
- 操作符前后加空格
- 逗号后加空格

**✅ 正确 / Correct:**
```csharp
public void Method(string param1, int param2)
{
    var result = param1 + param2.ToString();
    if (result != null && result.Length > 0)
    {
        // 处理逻辑
    }
}
```

### 3. 文件组织 / File Organization

**建议的文件结构：**
```csharp
// 1. 文件头注释（如需要）
// 2. using 语句
using System;
using System.Collections.Generic;
using ThirdPartyLibrary;

// 3. namespace
namespace ProjectName.FeatureName
{
    // 4. 类定义
    /// <summary>
    /// 类说明
    /// </summary>
    public class ClassName
    {
        // 5. 常量
        private const string DEFAULT_VALUE = "default";
        
        // 6. 私有字段
        private readonly ILogger _logger;
        private string _cachePath;
        
        // 7. 构造函数
        public ClassName(ILogger logger)
        {
            _logger = logger;
        }
        
        // 8. 公共属性
        public string PropertyName { get; set; }
        
        // 9. 公共方法
        public void PublicMethod() { }
        
        // 10. 私有方法
        private void PrivateMethod() { }
    }
}
```

---

## 注释和文档 / Comments and Documentation

### 1. XML 文档注释 / XML Documentation Comments

所有公共 API 必须有 XML 文档注释：

**✅ 正确 / Correct:**
```csharp
/// <summary>
/// 任务编辑器视图模型，管理自定义场景的编辑操作
/// Task editor view model for managing custom scenario editing operations
/// </summary>
public partial class TaskEditorViewModel : ObservableRecipient
{
    /// <summary>
    /// 获取或设置场景是否已被修改
    /// Gets or sets whether the scenario has been modified
    /// </summary>
    [ObservableProperty]
    public bool IsModified { get; set; }

    /// <summary>
    /// 保存当前场景的修改
    /// Saves the current scenario modifications
    /// </summary>
    /// <param name="force">是否强制保存 / Whether to force save</param>
    /// <returns>保存是否成功 / Whether the save was successful</returns>
    public async Task<bool> SaveScenarioAsync(bool force = false)
    {
        // 实现
    }
}
```

### 2. 内联注释 / Inline Comments

**✅ 正确 / Correct:**
```csharp
public void ProcessData()
{
    // 验证输入数据的有效性
    if (!ValidateInput())
    {
        return;
    }

    // TODO: 优化数据处理算法的性能
    var processedData = TransformData();
    
    // 保存处理结果到缓存
    _cache.Store(processedData);
}
```

**❌ 错误 / Incorrect:**
```csharp
public void ProcessData()
{
    ValidateInput(); // 验证
    var data = TransformData(); // 处理数据
    _cache.Store(data); // 存储
}
```

---

## 异步编程 / Asynchronous Programming

### 1. 异步方法命名 / Async Method Naming

**✅ 正确 / Correct:**
```csharp
public async Task<string> LoadDataAsync()
{
    return await httpClient.GetStringAsync(url);
}

public async Task SaveDataAsync(string data)
{
    await repository.SaveAsync(data);
}
```

### 2. 避免阻塞调用 / Avoid Blocking Calls

**❌ 错误 / Incorrect:**
```csharp
public void LoadData()
{
    var result = LoadDataAsync().Result;  // 阻塞调用
    ProcessData().Wait();  // 阻塞调用
}
```

**✅ 正确 / Correct:**
```csharp
public async Task LoadDataAsync()
{
    var result = await LoadDataAsync();
    await ProcessDataAsync();
}
```

### 3. ConfigureAwait 使用 / ConfigureAwait Usage

在库代码中使用 `ConfigureAwait(false)`：

```csharp
public async Task<string> GetDataAsync()
{
    var response = await httpClient.GetAsync(url).ConfigureAwait(false);
    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
}
```

### 4. 避免 async void / Avoid async void

**❌ 错误 / Incorrect:**
```csharp
public async void ProcessData()  // 除了事件处理程序外，避免 async void
{
    await DoSomethingAsync();
}
```

**✅ 正确 / Correct:**
```csharp
public async Task ProcessDataAsync()
{
    await DoSomethingAsync();
}

// 事件处理程序例外
private async void Button_Click(object sender, EventArgs e)
{
    await ProcessDataAsync();
}
```

---

## 异常处理 / Exception Handling

### 1. 具体异常类型 / Specific Exception Types

**✅ 正确 / Correct:**
```csharp
try
{
    var data = await LoadDataAsync();
    return ProcessData(data);
}
catch (HttpRequestException ex)
{
    _logger.LogError(ex, "网络请求失败");
    throw new ServiceException("数据加载失败", ex);
}
catch (ArgumentException ex)
{
    _logger.LogError(ex, "参数无效");
    throw;
}
```

**❌ 错误 / Incorrect:**
```csharp
try
{
    var data = await LoadDataAsync();
    return ProcessData(data);
}
catch (Exception ex)  // 过于宽泛的异常捕获
{
    // 空的异常处理
}
```

### 2. 异常重新抛出 / Exception Rethrowing

**✅ 正确 / Correct:**
```csharp
try
{
    DoSomething();
}
catch (SpecificException ex)
{
    _logger.LogError(ex, "操作失败");
    throw;  // 保持原始堆栈跟踪
}
```

**❌ 错误 / Incorrect:**
```csharp
try
{
    DoSomething();
}
catch (SpecificException ex)
{
    _logger.LogError(ex, "操作失败");
    throw ex;  // 丢失原始堆栈跟踪
}
```

---

## 最佳实践 / Best Practices

### 1. SOLID 原则 / SOLID Principles

**单一职责原则 (SRP)**
```csharp
// ✅ 职责单一的类
public class FileLogger : ILogger
{
    public void Log(string message) { /* 只负责文件日志记录 */ }
}

public class EmailSender : IEmailSender
{
    public void SendEmail(string to, string message) { /* 只负责发送邮件 */ }
}
```

**依赖倒置原则 (DIP)**
```csharp
// ✅ 依赖抽象而非具体实现
public class UserService
{
    private readonly IUserRepository _repository;
    private readonly ILogger _logger;

    public UserService(IUserRepository repository, ILogger logger)
    {
        _repository = repository;
        _logger = logger;
    }
}
```

### 2. 资源管理 / Resource Management

**✅ 正确 / Correct:**
```csharp
// 使用 using 语句自动释放资源
public async Task<string> ReadFileAsync(string path)
{
    using var reader = new StreamReader(path);
    return await reader.ReadToEndAsync();
}

// 或使用 using 声明（C# 8.0+）
public async Task<string> ReadFileAsync(string path)
{
    using var reader = new StreamReader(path);
    return await reader.ReadToEndAsync();
}
```

### 3. 空值检查 / Null Checking

**✅ 正确 / Correct:**
```csharp
public void ProcessUser(User user)
{
    if (user is null)
        throw new ArgumentNullException(nameof(user));

    // 或使用模式匹配
    if (user?.Name is { Length: > 0 } name)
    {
        ProcessName(name);
    }
}
```

### 4. 集合处理 / Collection Handling

**✅ 正确 / Correct:**
```csharp
// 使用 LINQ 进行集合操作
var activeUsers = users
    .Where(u => u.IsActive)
    .OrderBy(u => u.Name)
    .ToList();

// 避免多次枚举
var usersList = users.ToList();
var count = usersList.Count;
var firstUser = usersList.FirstOrDefault();
```

### 5. 字符串处理 / String Handling

**✅ 正确 / Correct:**
```csharp
// 使用字符串插值
var message = $"用户 {user.Name} 在 {DateTime.Now:yyyy-MM-dd} 登录";

// 多行字符串使用 StringBuilder 或 string.Join
var sql = string.Join(Environment.NewLine,
    "SELECT * FROM Users",
    "WHERE IsActive = 1",
    "ORDER BY Name");
```

---

## 代码审查检查清单 / Code Review Checklist

### 提交代码前检查 / Pre-commit Checklist

- [ ] 所有公共 API 都有 XML 文档注释
- [ ] 命名符合约定（英文命名，PascalCase/camelCase）
- [ ] 没有中文标识符（除注释外）
- [ ] 异步方法以 `Async` 结尾
- [ ] 避免使用 `.Result` 和 `.Wait()`
- [ ] 正确处理异常
- [ ] 代码格式符合 .editorconfig 配置
- [ ] 单个方法不超过 50 行
- [ ] 单个文件不超过 1000 行
- [ ] 移除未使用的 using 语句
- [ ] 移除调试代码和注释掉的代码

### 代码审查要点 / Code Review Focus Points

1. **功能正确性**：代码是否实现了预期功能
2. **性能考虑**：是否存在性能问题
3. **安全性**：是否有安全漏洞
4. **可维护性**：代码是否易于理解和修改
5. **测试覆盖**：是否有足够的测试覆盖
6. **文档完整性**：重要功能是否有文档说明

---

## 工具配置 / Tool Configuration

### Visual Studio 设置
```xml
<!-- Directory.Build.props -->
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors />
    <WarningsNotAsErrors>CS1591</WarningsNotAsErrors>
  </PropertyGroup>
  
  <ItemGroup>
    <PackageReference Include="StyleCop.Analyzers" Version="1.1.118">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

### 推荐扩展 / Recommended Extensions
- **Visual Studio**: CodeMaid, SonarLint, ReSharper
- **VS Code**: C# extension, SonarLint, EditorConfig
- **JetBrains Rider**: 内置代码分析和格式化功能

---

最后更新时间：2025-08-16  
维护者：开发团队