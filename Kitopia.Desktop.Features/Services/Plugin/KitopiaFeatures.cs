using Kitopia.Feature.Localization;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Kitopia.Desktop.Abstractions.FileSystem;
using Kitopia.Desktop.Features.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using PluginCore;

namespace Kitopia.Desktop.Features.Services.Plugin;

public static class KitopiaFeatures
{
    [Feature("search", "lang.kitopia.quick_search", "lang.kitopia.find_and_launch_apps_files_and_actions", "lang.kitopia.search_and_windows", 0xf4b8, 10)]
    private static Task OpenSearchWindowAsync()
    {
        var searchWindowService = ServiceManager.Services?.GetService<ISearchWindowService>();
        if (searchWindowService is null)
        {
            ShowUnavailable(Lang.Get("lang.kitopia.search_feature"));
            return Task.CompletedTask;
        }

        searchWindowService.ShowOrHiddenSearchWindow();
        return Task.CompletedTask;
    }

    [Feature("index", "lang.kitopia.file_and_app_index", "lang.kitopia.manage_the_local_index_and_everything_search", "lang.kitopia.search_and_windows", 0xf3ae, 20)]
    private static Task OpenIndexAsync()
    {
        return OpenSearchWindowAsync();
    }

    [Feature("window-switcher", "lang.kitopia.window_switcher", "lang.kitopia.find_open_windows_by_title_and_switch_to_them", "lang.kitopia.search_and_windows", 0xf60a, 30)]
    private static Task OpenWindowSwitcherAsync()
    {
        return OpenSearchWindowAsync();
    }

    [Feature("window-topmost", "lang.kitopia.always_on_top", "lang.kitopia.toggle_always_on_top_for_a_selected_window", "lang.kitopia.search_and_windows", 0xf602, 40)]
    private static Task ExecuteWindowTopmostAsync()
    {
        var windowTool = ServiceManager.Services?.GetService<IWindowTool>();
        if (windowTool is null)
        {
            ShowUnavailable(Lang.Get("lang.kitopia.always_on_top"));
            return Task.CompletedTask;
        }

        windowTool.SelectAndSetWindowTopMost();
        return Task.CompletedTask;
    }

    [Feature("mouse-quick", "lang.kitopia.file_preview", "lang.kitopia.preview_images_documents_and_media_selected_in_explorer", "lang.kitopia.search_and_windows", 0xf4b8, 50)]
    private static Task OpenMouseQuickMenuAsync()
    {
        var mouseQuickWindowService = ServiceManager.Services?.GetService<IMouseQuickWindowService>();
        if (mouseQuickWindowService is null)
        {
            ShowUnavailable(Lang.Get("lang.kitopia.file_preview"));
            return Task.CompletedTask;
        }

        mouseQuickWindowService.Open();
        return Task.CompletedTask;
    }

    [Feature("screen-capture", "lang.kitopia.screenshot", "lang.kitopia.capture_and_annotate_screens_including_scrolling_areas", "lang.kitopia.screenshots_and_images", 0xf4b8, 100)]
    private static Task CaptureScreenAsync()
    {
        var screenCaptureWindow = ServiceManager.Services?.GetService<IScreenCaptureWindow>();
        if (screenCaptureWindow is null)
        {
            ShowUnavailable(Lang.Get("lang.kitopia.screen_capture_feature"));
            return Task.CompletedTask;
        }

        screenCaptureWindow.CaptureScreen();
        return Task.CompletedTask;
    }

    [Feature("ocr", "lang.kitopia.ocr", "lang.kitopia.recognize_and_copy_text_from_a_screen_area_using_local_paddleocr", "lang.kitopia.screenshots_and_images", 0xea72, 110,
        Activation = FeatureActivationMode.ScreenCapture)]
    private static async Task RecognizeScreenTextAsync(ScreenCaptureResult captureResult, CancellationToken cancellationToken)
    {
        if (captureResult.Source is null)
        {
            ShowToast(Lang.Get("lang.kitopia.ocr"), Lang.Get("lang.kitopia.the_capture_contains_no_readable_image_data"), NotificationType.Warning);
            return;
        }

        var ocr = ServiceManager.Services?.GetService<PluginCore.IOcrService>();
        if (ocr is null || !ocr.IsAvailable)
        {
            ShowToast(Lang.Get("lang.kitopia.ocr"), Lang.Get("lang.kitopia.the_local_ocr_model_is_unavailable"), NotificationType.Warning);
            return;
        }

        var regions = await ocr.RecognizeAsync(captureResult.Source, cancellationToken);
        var text = string.Join(Environment.NewLine, regions.Select(region => region.Text));
        if (string.IsNullOrWhiteSpace(text))
        {
            ShowToast(Lang.Get("lang.kitopia.ocr"), Lang.Get("lang.kitopia.no_text_was_recognized"), NotificationType.Information);
            return;
        }

        ServiceManager.Services?.GetService<IClipboardService>()?.SetText(text);
        ShowToast(Lang.Get("lang.kitopia.ocr"), text, NotificationType.Information);
    }

    [Feature("file-locksmith", "lang.kitopia.file_lock_manager", "lang.kitopia.find_and_end_processes_locking_selected_files", "lang.kitopia.files_and_devices", 0xe61c, 200)]
    private static async Task CheckFileLocksAsync(CancellationToken cancellationToken)
    {
        var services = ServiceManager.Services;
        var filePicker = services?.GetService<IFeatureFilePicker>();
        var fileLockService = services?.GetService<IFileLockService>();
        var fileLocksmithWindow = services?.GetService<IFileLocksmithWindow>();
        if (filePicker is null || fileLockService is null || fileLocksmithWindow is null)
        {
            ShowUnavailable(Lang.Get("lang.kitopia.file_lock_manager"));
            return;
        }

        var filePaths = await filePicker.PickFilesAsync(Lang.Get("lang.kitopia.messages.choose_files_to_check_for_locks"), true, cancellationToken);
        if (filePaths.Count == 0)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => fileLocksmithWindow.ShowForScope(null, filePaths));
    }

    [Feature("lan-file-share", "lang.kitopia.lan_sharing", "lang.kitopia.send_files_to_devices_on_the_local_network", "lang.kitopia.files_and_devices", 0xe974, 210)]
    private static async Task ShareFilesAsync(CancellationToken cancellationToken)
    {
        var services = ServiceManager.Services;
        var filePicker = services?.GetService<IFeatureFilePicker>();
        var lanFileShareWindow = services?.GetService<ILanFileShareWindow>();
        if (filePicker is null || lanFileShareWindow is null)
        {
            ShowUnavailable(Lang.Get("lang.kitopia.lan_sharing"));
            return;
        }

        var filePaths = await filePicker.PickFilesAsync(Lang.Get("lang.kitopia.messages.choose_files_to_share"), true, cancellationToken);
        if (filePaths.Count == 0)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => lanFileShareWindow.Show(filePaths));
    }

    [Feature("device-chat", "lang.kitopia.device_chat_and_file_sharing", "lang.kitopia.send_text_images_files_and_clipboard_content_to_devices", "lang.kitopia.files_and_devices", 0xe975, 220)]
    private static Task OpenDeviceChatAsync()
    {
        return NavigateAsync("device/chat");
    }

    [Feature("scenario", "lang.kitopia.custom_scenarios", "lang.kitopia.combine_triggers_and_actions_into_automated_scenarios", "lang.kitopia.automation_and_management", 0xe065, 300)]
    private static Task OpenScenarioManagerAsync()
    {
        return NavigateAsync("scenario");
    }

    [Feature("hotkey", "lang.kitopia.hotkey_manager", "lang.kitopia.manage_kitopia_s_global_hotkeys", "lang.kitopia.automation_and_management", 0xf4b9, 310)]
    private static Task OpenHotKeyManagerAsync()
    {
        return NavigateAsync("hotkey");
    }

    [Feature("market", "lang.kitopia.plugin_marketplace", "lang.kitopia.browse_and_install_plugins", "lang.kitopia.automation_and_management", 0xf151, 320)]
    private static Task OpenPluginMarketAsync()
    {
        return NavigateAsync("market");
    }

    [Feature("plugin", "lang.kitopia.plugin_manager", "lang.kitopia.manage_installed_plugins_and_their_settings", "lang.kitopia.automation_and_management", 0xf60a, 330)]
    private static Task OpenPluginManagerAsync()
    {
        return NavigateAsync("plugin");
    }

    [Feature("onnx", "lang.kitopia.onnx_models", "lang.kitopia.manage_onnx_models_for_ocr_and_other_features", "lang.kitopia.automation_and_management", 0xf83b, 340)]
    private static Task OpenOnnxModelManagerAsync()
    {
        return NavigateAsync("onnx/model-manager");
    }

    [Feature("index-status", "lang.kitopia.index_status", "lang.kitopia.inspect_and_rebuild_pinyin_text_and_image_sqlite_vec_indexes", "lang.kitopia.automation_and_management", 0xf105, 345)]
    private static Task OpenIndexStatusAsync()
    {
        return NavigateAsync("index/status");
    }

    [Feature("settings", "lang.kitopia.settings_and_updates", "lang.kitopia.configure_themes_screenshots_search_and_integrations_and_check_for_updates", "lang.kitopia.automation_and_management", 0xf6aa, 350)]
    private static Task OpenSettingsAsync()
    {
        return NavigateAsync("settings");
    }

    private static Task NavigateAsync(string route)
    {
        var navigationService = ServiceManager.Services?.GetService<INavigationService>();
        if (navigationService is null)
        {
            ShowUnavailable(Lang.Get("lang.kitopia.navigation"));
            return Task.CompletedTask;
        }

        navigationService.Navigate(route);
        return Task.CompletedTask;
    }

    private static void ShowUnavailable(string featureName)
    {
        ShowToast(featureName, Lang.Get("lang.kitopia.this_feature_is_unavailable_on_this_platform"), NotificationType.Warning);
    }

    private static void ShowToast(string title, string message, NotificationType notificationType)
    {
        _ = ServiceManager.Services?.GetService<IToastService>()?.Show(title, message, notificationType);
    }
}
