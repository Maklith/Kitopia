using PluginCore;

namespace Kitopia.Desktop.Features.Services.Plugin;

public sealed class PluginApiResponse<T>
{
    public bool Flag { get; set; }
    public T? Data { get; set; }
}

public sealed class PluginPage
{
    public List<OnlinePluginInfo> Items { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
}

public sealed class PluginTag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class VersionDetail
{
    public int Id { get; set; }
    public long PluginId { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public List<string> AvailablePlatforms { get; set; } = [];
    public int Status { get; set; }
    public DateTime CreateTime { get; set; }
    public DateTime Updatetime { get; set; }
    public bool IsCurrent { get; set; }
    public bool CanDownload { get; set; }

    public string FormattedVersion => !string.IsNullOrWhiteSpace(Version) ? $"v{Version}" : "—";

    public string FormattedCreateTime =>
        CreateTime != default ? $"提交于 {CreateTime:yyyy年M月d日 HH:mm}" : string.Empty;

    public string StatusText =>
        Status switch
        {
            1 => "待审核",
            3 => "已驳回",
            4 => "已撤回",
            _ => "已发布"
        };

    public IReadOnlyList<string> DisplayPlatforms =>
        AvailablePlatforms is { Count: > 0 }
            ? AvailablePlatforms.Select(PluginInfoUiHelper.FormatPlatformName).Distinct().ToList()
            : ["Windows"];
}

public sealed class UserBaseInfo
{
    public string? UserName { get; set; }
}

public sealed class OnlinePluginInfo
{
    public long Id { get; set; }
    public int AuthorId { get; set; }
    public string? AuthorUserName { get; set; }
    public string? AuthorNickname { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NameSign { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public int PublicationStatus { get; set; }
    public string? LastVersion { get; set; }
    public string? DescriptionShort { get; set; }
    public string? Description { get; set; }
    public List<string> SupportSystems { get; set; } = [];
    public List<string> AvailablePlatforms { get; set; } = [];
    public List<PluginTag> Tags { get; set; } = [];
    public long DownloadCounts { get; set; }
    public DateTime CreateTime { get; set; }
    public DateTime Updatetime { get; set; }
    public int Rank { get; set; }

    public string ToPlgString() => NameSign;

    public override string ToString() => ToPlgString();

    public PluginBaseInfo ToPluginBaseInfo() => new()
    {
        Name = Name,
        NameSign = NameSign,
        Version = LastVersion ?? string.Empty,
        Description = Description ?? DescriptionShort ?? string.Empty,
        Dependencies = []
    };
}
