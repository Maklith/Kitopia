using PluginCore;

namespace Kitopia.Desktop.Features.Services.Plugin;

public struct VersionDetail
{
    /*
     "id": 1,
            "pluginId": 7,
            "versionInt": 1,
            "version": "1.0.0",
            "detail": "第一个版本",
            "isAvailable": true
     */
    public int Id { get; set; }
    public int PluginId { get; set; }
    public int VersionInt { get; set; }
    public string Version { get; set; }
    public string Detail { get; set; }
    public bool IsAvailable { get; set; }
}

public class OnlinePluginInfo
{
    internal class ApiResponse
    {
        public bool flag { get; set; }
        public List<OnlinePluginInfo> data { get; set; }
    }

    public int Id { set; get; }


    public int AuthorId { set; get; }


    public string Name { set; get; }
    public string NameSign { set; get; }
    public bool IsPublic { set; get; }

    public string LastVersion { set; get; }
    public int LastVersionId { set; get; }

    public string DescriptionShort { set; get; }
    public string Description { set; get; }
    public List<string> SupportSystems { set; get; }

    public string ToPlgString()
    {
        return NameSign;
    }

    public override string ToString()
    {
        return ToPlgString();
    }

    public PluginBaseInfo ToPluginBaseInfo()
    {
        return new PluginBaseInfo
        {
            Id = Id,
            AuthorId = AuthorId,
            Name = Name,
            NameSign = NameSign
        };
    }
}
