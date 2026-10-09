using CommunityToolkit.Mvvm.ComponentModel;

namespace Kitopia.Desktop.Features.ViewModel.Pages;

public sealed partial class OnnxRuntimeStatus(string device) : ObservableObject
{
    public string Device { get; } = device;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect), nameof(StatusKey), nameof(IsVerified), nameof(IsUnavailable))]
    private bool _isChecking = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelect), nameof(StatusKey), nameof(IsVerified), nameof(IsUnavailable))]
    private bool? _isAvailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public bool CanSelect => !IsChecking && IsAvailable != false;
    public bool IsVerified => !IsChecking && IsAvailable == true;
    public bool IsUnavailable => !IsChecking && IsAvailable == false;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string StatusKey => IsChecking ? "lang.kitopia.onnx.checking"
        : IsAvailable switch
        {
            true => "lang.kitopia.onnx.available",
            false => "lang.kitopia.onnx.unavailable",
            null => "lang.kitopia.onnx.unknown"
        };

    public string RequirementsKey => Device switch
    {
        "CPU" => "lang.kitopia.onnx.cpu_requirements",
        "GPU(CUDA)" => "lang.kitopia.onnx.cuda_requirements",
        "CPU(OpenVino)" => "lang.kitopia.onnx.openvino_cpu_requirements",
        "GPU(OpenVino)" => "lang.kitopia.onnx.openvino_gpu_requirements",
        "NPU(OpenVino)" => "lang.kitopia.onnx.openvino_npu_requirements",
        _ => "lang.kitopia.onnx.plugin_requirements"
    };

    public string DocumentationUrl => Device switch
    {
        "GPU(CUDA)" => "https://onnxruntime.ai/docs/execution-providers/CUDA-ExecutionProvider.html#requirements",
        "CPU(OpenVino)" or "GPU(OpenVino)" or "NPU(OpenVino)" =>
            "https://docs.openvino.ai/2025/get-started/install-openvino/configurations.html",
        _ => "https://onnxruntime.ai/docs/install/"
    };

    public string? DriverUrl => Device switch
    {
        "GPU(CUDA)" => "https://www.nvidia.com/en-us/drivers/",
        "GPU(OpenVino)" or "NPU(OpenVino)" => "https://www.intel.com/content/www/us/en/download-center/home.html",
        _ => null
    };

    public bool HasDriverUrl => DriverUrl is not null;
}
