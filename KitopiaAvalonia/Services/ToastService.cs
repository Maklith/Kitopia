#region

using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Core.Services;
using PluginCore;
using Serilog;
using WinRT;

#endregion

namespace KitopiaAvalonia.Services;

public class ToastService : IToastService
{
    private static ILogger Log = LogManager.Logger.ForContext<ToastService>();
    private ToastNotifier _toastNotifier;

    public void Init()
    {
        ComWrappersSupport.InitializeComWrappers();
        var toastNotificationManagerForUser = ToastNotificationManager.GetDefault();
        _toastNotifier = toastNotificationManagerForUser.CreateToastNotifier("Kitopia");
    }


    public void Show(string header, string text)
    {
        Log.Debug($"{nameof(ToastService)}的接口{nameof(Show)}被调用,header：{header},text：{text}");
        var xmlDocument = new XmlDocument();
        // lang=xml
        xmlDocument = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
        var xml = xmlDocument.GetXml();
        var stringElements = xmlDocument.GetElementsByTagName("text");
        stringElements[0].AppendChild(xmlDocument.CreateTextNode(header));
        stringElements[1].AppendChild(xmlDocument.CreateTextNode(text));
        var toastNotification = new ToastNotification(xmlDocument);

        _toastNotifier.Show(toastNotification);
    }

    public void Unregister()
    {
    }
}