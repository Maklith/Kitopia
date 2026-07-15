namespace Kitopia.Desktop.Features.Services.Interfaces;

public interface IHotKeyEditor
{
    void EditByUuid(string uuid, object? owner);
}