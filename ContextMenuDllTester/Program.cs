using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace ContextMenuDllTester;

class Program
{
    [STAThread]
    static async Task Main(string[] args)
    {
        Console.WriteLine("Registering Kitopia Context Menu (Sparse Package)...");

        // 1. 确定路径
        var exePath = AppDomain.CurrentDomain.BaseDirectory;
        // 注意：通常文件名是 Package.appxmanifest，请确认你的文件名
        var manifestPath = Path.Combine(exePath, "Package.appxmanifest"); 

        if (!File.Exists(manifestPath))
        {
            Console.WriteLine($"Error: Manifest not found at {manifestPath}");
            // 尝试查找 AppxManifest.xml 作为备选
            manifestPath = Path.Combine(exePath, "AppxManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Console.WriteLine("Critical: No manifest file found.");
                Console.ReadKey();
                return;
            }
        }

        try
        {
            var packageManager = new PackageManager();
            
            // 2. 配置注册选项
            // 由于移除了 AllowExternalContent，我们使用标准的松散文件注册 (Developer Mode)
            // 这种方式不需要 ExternalLocationUri，直接注册 Manifest 即可
            var options = new RegisterPackageOptions
            {
                // 标准开发模式注册不需要指定 ExternalLocationUri (除非是稀疏包)
                // ExternalLocationUri = new Uri(exePath), 
                
                // 允许未签名 (必须开启开发者模式)
                AllowUnsigned = true, 
                
                // 标记为开发者模式注册
                DeveloperMode = true 
            };

            Console.WriteLine($"Registering package from: {manifestPath}");
            Console.WriteLine($"External Location: {exePath}");

            // 3. 执行注册
            var result = await packageManager.RegisterPackageByUriAsync(
                new Uri(manifestPath), 
                options
            );
            
            // 4. 检查结果
            if (result.ExtendedErrorCode == null)
            {
                 Console.WriteLine("SUCCESS! Package registered successfully.");
                 Console.WriteLine("Right-click a file/folder to test your Context Menu.");
            }
            else
            {
                 Console.WriteLine("--------------------------------------------------");
                 Console.WriteLine("REGISTRATION FAILED");
                 Console.WriteLine($"Error Code: {result.ExtendedErrorCode}");
                 Console.WriteLine($"Error Text: {result.ErrorText}");
                 Console.WriteLine("--------------------------------------------------");
                 
                 if (result.ExtendedErrorCode.HResult == unchecked((int)0x80073CF9))
                 {
                     Console.WriteLine("Tip: This error usually means the 'ExternalLocationUri' didn't match or wasn't accepted.");
                 }
            }
        }
        catch (UnauthorizedAccessException)
        {
            Console.WriteLine("Error: Access denied. Please run as Administrator.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}