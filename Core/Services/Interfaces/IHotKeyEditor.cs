namespace Core.Services.Interfaces;

public interface IHotKeyEditor
{
    void EditByUuid(string uuid, object? owner);
}