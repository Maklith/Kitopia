namespace Kitopia.Desktop.Features.Services.Account;

public sealed class UserInfo
{
    public string UserName { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string? UserMobile { get; set; }
    public string? UserEmail { get; set; }
    public int State { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime? CreateTime { get; set; }
    public string? AvatarLocalPath { get; set; }
    public byte[]? AvatarBytes { get; set; }

    public string DisplayName => !string.IsNullOrWhiteSpace(Nickname) ? Nickname : UserName;

    public string PrimaryRole
    {
        get
        {
            if (Roles.Contains("superadmin", StringComparer.OrdinalIgnoreCase)) return "管理员";
            if (Roles.Contains("admin", StringComparer.OrdinalIgnoreCase)) return "管理员";
            if (Roles.Contains("developer", StringComparer.OrdinalIgnoreCase)) return "开发者";
            return "用户";
        }
    }
}
