using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

internal static class Program
{
    internal static string Bin => Path.Combine(Repo, "tests", "KitopiaTest", "bin", "Debug", "net10.0-windows10.0.19041.0");
    internal static string Repo { get; } = FindRepositoryRoot();
    internal static string Root => Path.Combine(Repo, "docs", "tools", "render");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kitopia.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the Kitopia repository root.");
    }

    [STAThread]
    private static async Task Main(string[] args)
    {
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(Bin, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        Environment.SetEnvironmentVariable("PATH", Path.Combine(Bin, "runtimes", "win-x64", "native") + ";" + Bin + ";" + Environment.GetEnvironmentVariable("PATH"));
        var openCv=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(Bin, "OpenCvSharp.dll"));
        System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(openCv,(name,assembly,search)=>
            name=="OpenCvSharpExtern" ? System.Runtime.InteropServices.NativeLibrary.Load(Path.Combine(Bin, "runtimes", "win-x64", "native", "OpenCvSharpExtern.dll"),assembly,System.Runtime.InteropServices.DllImportSearchPath.UseDllDirectoryForDependencies) : IntPtr.Zero);
        if (args.Contains("pdf") || args.Contains("pdf-en")) {
            var renderThread = new Thread(() => Renderer.RenderNativePdf(args.Contains("pdf-en")));
            renderThread.SetApartmentState(ApartmentState.STA);
            renderThread.Start();
            renderThread.Join();
            return;
        }
        await Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Task Run(string[] args) => Renderer.Run(args);
}
