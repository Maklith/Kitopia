using PluginCore;

namespace Core.Services.Plugin;

public partial class OnlinePluginInfo
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
        return $"{Id}_{AuthorId}_{NameSign}";
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
