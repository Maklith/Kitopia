using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Threading;
using Kitopia.Desktop.Features.Services.Config;
using Kitopia.Desktop.Features.Services.Plugin;
using Kitopia.Desktop.Features.Utils;

namespace Kitopia.Desktop.Features.CustomScenario.Services;

public static class ScenarioMarketService
{
    private static readonly JsonSerializerOptions ApiOptions = new(JsonSerializerDefaults.Web);

    public static async Task<ScenarioMarketPage> GetScenariosAsync(int page, int pageSize, string keyword,
        bool own = false, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get,
            $"{(own ? "allself" : "all")}?page={page}&pageSize={pageSize}&query={Uri.EscapeDataString(keyword.Trim())}");
        return await SendAsync<ScenarioMarketPage>(request, cancellationToken);
    }

    public static async Task<ScenarioMarketItem> GetScenarioAsync(long id, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, id.ToString());
        return await SendAsync<ScenarioMarketItem>(request, cancellationToken);
    }

    public static async Task<ScenarioMarketItem> UploadAsync(CustomScenario scenario, bool isPublic,
        CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(scenario, ConfigManger.DefaultOptions);
        if (json.Length > 2 * 1024 * 1024) throw new InvalidOperationException("情景 JSON 不能超过 2 MiB。");
        using var request = CreateRequest(HttpMethod.Post, "upload");
        using var body = new MultipartFormDataContent();
        using var file = new ByteArrayContent(json);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        body.Add(file, "file", $"{scenario.Uuid}.json");
        body.Add(new StringContent(isPublic.ToString()), "isPublic");
        request.Content = body;
        return await SendAsync<ScenarioMarketItem>(request, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, $"{ConfigManger.ApiUrl.TrimEnd('/')}/api/v1/scenario/{path}");
        if (!string.IsNullOrWhiteSpace(ConfigManger.Config?.userToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ConfigManger.Config.userToken);
        return request;
    }

    internal static async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken,
        HttpClient? httpClient = null)
    {
        using var response = await (httpClient ?? PluginNetworkService.HttpClient).SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new HttpRequestException(response.StatusCode == HttpStatusCode.Unauthorized
                ? "登录凭据已失效，请重新登录。"
                : "当前登录未获得情景管理权限，请重新授权。", null, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!response.IsSuccessStatusCode || !root.TryGetProperty("flag", out var flag) || flag.ValueKind != JsonValueKind.True)
        {
            var message = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String
                ? data.GetString() : "请求失败，请刷新后重试。";
            throw new InvalidOperationException(message);
        }
        return root.GetProperty("data").Deserialize<T>(ApiOptions)
            ?? throw new JsonException("情景市场返回了空数据。");
    }

    internal static string PrepareImport(JsonObject root)
    {
        if (root["Name"] is not JsonValue name || !name.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) ||
            root["Nodes"] is null || root["Connections"] is null)
            throw new JsonException("情景 JSON 缺少名称、节点或连接。");
        var uuid = Guid.NewGuid().ToString();
        root["Uuid"] = uuid;
        if (root["AutoTriggers"] is JsonObject triggers)
            triggers["$values"] = new JsonArray();
        else root["AutoTriggers"] = new JsonArray();
        root["RunHotKey"] = null;
        root["StopHotKey"] = null;
        root["ExecutionManual"] = true;
        root["IsRunning"] = false;
        root["IsActive"] = false;
        root["LastRun"] = DateTime.MinValue;
        return uuid;
    }

    public static async Task<CustomScenario> ImportAsync(long id, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, $"{id}/download");
        using var response = await PluginNetworkService.HttpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            throw new InvalidOperationException("情景不存在或无权访问，请检查公开状态和登录授权。");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(json) > 2 * 1024 * 1024)
            throw new InvalidOperationException("情景 JSON 超过 2 MiB。");
        var root = JsonNode.Parse(json) as JsonObject ?? throw new JsonException("情景 JSON 无效。");
        var uuid = PrepareImport(root);
        Directory.CreateDirectory(KitopiaPaths.CustomScenariosDirectory);
        var path = KitopiaPaths.GetCustomScenarioFilePath(uuid);
        var created = false;
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                using var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = true });
                root.WriteTo(writer);
                await writer.FlushAsync(cancellationToken);
            }
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            CustomScenarioManger.Load(new FileInfo(path));
            return CustomScenarioManger.CustomScenarios.Single(scenario => scenario.Uuid == uuid);
        });
    }
}

public sealed class ScenarioMarketPage
{
    public List<ScenarioMarketItem> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int TotalPages { get; init; }
}

public sealed class ScenarioMarketItem
{
    public long Id { get; init; }
    public string SourceUuid { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string? AuthorNickname { get; init; }
    public string? AuthorUserName { get; init; }
    public int PublicationStatus { get; init; }
    public ScenarioMarketReview? Review { get; init; }
    public string Author => AuthorNickname ?? AuthorUserName ?? "未知作者";
    public string Visibility => PublicationStatus switch { 1 => "待公开审核", 2 => "公开", _ => "私有" };
    public string ReviewText => Review is null ? "" :
        $"{Review.Status switch { 0 => "待审核", 1 => "审核通过", 2 => "审核拒绝", _ => "已撤回" }} {Review.ReviewComment}";
}

public sealed record ScenarioMarketReview(int Status, string? ReviewComment);
