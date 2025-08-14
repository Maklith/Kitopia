namespace Core.SDKs.Services;

public interface IThemeChange
{
    public void changeTo(string name);
    public void changeAnother();
    public void followSys(bool follow);
    public bool isDark();
}