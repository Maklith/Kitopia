using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Kitopia.Desktop.Abstractions.TextSelection;
using Kitopia.Desktop.Features.Services;
using Vanara.PInvoke;

namespace Kitopia.Desktop.Platform.Windows;

public sealed class WindowsTextSelectionService : ITextSelectionService
{
    private const int ClipboardTimeoutMilliseconds = 1000;
    private const short KeyDown = unchecked((short)0x8000);

    public Task<TextSelectionSnapshot?> TryGetSelectionAsync(
        bool allowClipboardFallback,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selection = TryReadAutomationSelection(out var isPassword);
        return selection is not null || !allowClipboardFallback || isPassword
            ? Task.FromResult(selection)
            : TryReadClipboardSelectionAsync(cancellationToken);
    }

    private static TextSelectionSnapshot? TryReadAutomationSelection(out bool isPassword)
    {
        isPassword = false;
        try
        {
            var foregroundHandle = User32.GetForegroundWindow();
            var foreground = (nint)foregroundHandle;
            if (foreground == 0) return null;

            User32.GetWindowThreadProcessId(foregroundHandle, out var processId);
            using var foregroundProcess = Process.GetProcessById((int)processId);
            var focused = AutomationElement.FocusedElement;
            if (focused is null || focused.Current.ProcessId != foregroundProcess.Id)
                return null;

            for (var element = focused; element is not null;
                 element = TreeWalker.RawViewWalker.GetParent(element))
            {
                if (element.Current.IsPassword)
                {
                    isPassword = true;
                    return null;
                }

                var selection = TryReadTextPatternSelection(
                    element, foreground, foregroundProcess.Id, foregroundProcess.ProcessName);
                if (selection is not null) return selection;
            }

            // Word often exposes the selection on its document element instead of the focused child.
            var document = AutomationElement.FromHandle(foreground).FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document));
            return document is null
                ? null
                : TryReadTextPatternSelection(
                    document, foreground, foregroundProcess.Id, foregroundProcess.ProcessName);
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or ArgumentException)
        {
            LogManager.Logger.Debug(exception, "无法读取 Windows UI Automation 选区");
        }

        return null;
    }

    private static TextSelectionSnapshot? TryReadTextPatternSelection(
        AutomationElement element, nint foreground, int processId, string processName)
    {
        try
        {
            if (element.Current.ProcessId != processId ||
                !element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject) ||
                patternObject is not TextPattern textPattern)
                return null;

            var ranges = textPattern.GetSelection();
            var text = string.Join(Environment.NewLine,
                ranges.Select(range => range.GetText(-1)));
            if (string.IsNullOrWhiteSpace(text)) return null;

            return new TextSelectionSnapshot(
                text.Trim(),
                foreground,
                processName,
                GetBounds(ranges));
        }
        catch (ElementNotAvailableException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }

        return null;
    }

    private static TextSelectionBounds? GetBounds(IEnumerable<TextPatternRange> ranges)
    {
        foreach (var range in ranges)
        {
            var rectangles = range.GetBoundingRectangles();
            foreach (var rectangle in rectangles)
            {
                var x = (int)Math.Round(rectangle.X);
                var y = (int)Math.Round(rectangle.Y);
                var width = (int)Math.Round(rectangle.Width);
                var height = (int)Math.Round(rectangle.Height);
                if (width > 0 && height > 0)
                    return new TextSelectionBounds(x, y, width, height);
            }
        }

        return null;
    }

    private static async Task<TextSelectionSnapshot?> TryReadClipboardSelectionAsync(
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<TextSelectionSnapshot?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => ReadClipboardSelection(completion))
        {
            IsBackground = true,
            Name = "Kitopia text selection clipboard"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ReadClipboardSelection(TaskCompletionSource<TextSelectionSnapshot?> completion)
    {
        IDataObject? original = null;
        TextSelectionSnapshot? result = null;
        var originalSequence = GetClipboardSequenceNumber();
        var copySequence = originalSequence;
        try
        {
            var sourceWindow = (nint)User32.GetForegroundWindow();
            if (sourceWindow == 0)
                return;

            original = Clipboard.GetDataObject();
            if (!WaitForModifierRelease(sourceWindow))
                return;
            if (GetClipboardSequenceNumber() != originalSequence)
                return;

            User32.keybd_event(User32.VK.VK_CONTROL, 0, 0, IntPtr.Zero);
            try
            {
                User32.keybd_event(User32.VK.VK_C, 0, 0, IntPtr.Zero);
                User32.keybd_event(User32.VK.VK_C, 0, User32.KEYEVENTF.KEYEVENTF_KEYUP, IntPtr.Zero);
            }
            finally
            {
                User32.keybd_event(User32.VK.VK_CONTROL, 0, User32.KEYEVENTF.KEYEVENTF_KEYUP, IntPtr.Zero);
            }

            var deadline = Stopwatch.GetTimestamp() +
                           (long)(Stopwatch.Frequency * (ClipboardTimeoutMilliseconds / 1000d));
            while ((copySequence = GetClipboardSequenceNumber()) == originalSequence &&
                   Stopwatch.GetTimestamp() < deadline)
                Thread.Sleep(20);

            if (copySequence == originalSequence ||
                !Clipboard.ContainsText(TextDataFormat.UnicodeText))
                return;

            var text = Clipboard.GetText(TextDataFormat.UnicodeText);
            if (string.IsNullOrWhiteSpace(text))
                return;

            User32.GetWindowThreadProcessId(sourceWindow, out var processId);
            string? processName = null;
            try
            {
                using var process = Process.GetProcessById((int)processId);
                processName = process.ProcessName;
            }
            catch (ArgumentException)
            {
            }

            result = new TextSelectionSnapshot(
                text.Trim(),
                sourceWindow,
                processName,
                null);
        }
        catch (ExternalException exception)
        {
            LogManager.Logger.Debug(exception, "无法通过剪贴板读取选区");
        }
        finally
        {
            if (copySequence != originalSequence && GetClipboardSequenceNumber() == copySequence)
            {
                try
                {
                    if (original is null) Clipboard.Clear();
                    else Clipboard.SetDataObject(original, true);
                }
                catch (ExternalException exception)
                {
                    LogManager.Logger.Debug(exception, "恢复剪贴板内容失败");
                }
            }

            completion.TrySetResult(result);
        }
    }

    private static bool WaitForModifierRelease(nint sourceWindow)
    {
        for (var attempt = 0; attempt < 75; attempt++)
        {
            if ((User32.GetAsyncKeyState(User32.VK.VK_CONTROL) & KeyDown) == 0 &&
                (User32.GetAsyncKeyState(User32.VK.VK_MENU) & KeyDown) == 0 &&
                (User32.GetAsyncKeyState(User32.VK.VK_SHIFT) & KeyDown) == 0)
                return (nint)User32.GetForegroundWindow() == sourceWindow;
            Thread.Sleep(10);
        }

        return false;
    }

    private static uint GetClipboardSequenceNumber() => User32.GetClipboardSequenceNumber();
}
