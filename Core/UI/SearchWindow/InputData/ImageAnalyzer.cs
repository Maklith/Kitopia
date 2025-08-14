using Core.SDKs.Services;
using Core.ViewModel;
using Microsoft.Extensions.DependencyInjection;
using OpenCvSharp;
using PluginCore;
using PluginCore.SearchWindow.InputDataAnalyzer;

namespace Core.SearchWindow.InputData;

public class ImageAnalyzer : IInputDataAnalyzer
{

    public IInputDataAnalyzeTimeFlags AnalyzeTimeFlags => IInputDataAnalyzeTimeFlags.搜索前| IInputDataAnalyzeTimeFlags.仅有搜索内容打开时;

    public IEnumerable<SearchViewItem> AnalyzeInputData(IEnumerable<PluginCore.SearchWindow.InputData.InputData> inputDatas)
    {
        foreach (var inputData in inputDatas)
        {
            if (inputData.InputType == InputType.图像)
            {
                var image = inputData.Data as Mat;
                if (image != null)
                {
                    yield return new SearchViewItem()
                    {
                        ItemDisplayName = "保存剪贴板图像?",
                        FileType = FileType.自定义,
                        IconSymbol = 0xE357,
                        Icon = null,
                        IsVisible = true,
                        Action = ((item, s) => {
                            var ts = DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, 0);
                            var timeStamp = Convert.ToInt64(ts.TotalMilliseconds);
                            var f =Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads\\Kitopia" +
                                   timeStamp + ".png";
                            var imageTool = ServiceManager.Services.GetService<IImageTool>()!;
                            imageTool.SaveImageAndOpenTheFolder(image, f);
                            //image.Dispose();
                        })
                    };
                }
            }
        }
        yield break;
    }
}