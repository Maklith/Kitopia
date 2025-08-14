namespace Core.Services;

public interface IHotKeyEditor
{
    void EditByUuid(string uuid, object? owner);
}