#region

using System.Threading.Tasks;
using System.Windows.Threading;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using log4net;
using PluginCore;

#endregion

namespace KitopiaAvalonia.Services;

public class ToastService : IToastService
{
    private static readonly ILog log = LogManager.GetLogger(nameof(ToastService));
    private ToastNotifier _toastNotifier;

    public void Init()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        var toastNotificationManagerForUser = ToastNotificationManager.GetDefault();
        _toastNotifier = toastNotificationManagerForUser.CreateToastNotifier("Kitopia");
    }


    public void Show(string header, string text)
    {
        log.Debug($"{nameof(ToastService)}的接口{nameof(Show)}被调用,header：{header},text：{text}");
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