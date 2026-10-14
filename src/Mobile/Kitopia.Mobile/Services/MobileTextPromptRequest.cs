namespace Kitopia.Mobile.Services;

public sealed class MobileTextPromptRequest
{
    private readonly TaskCompletionSource<string?> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public MobileTextPromptRequest(string title, string prompt, string? initialValue)
    {
        Title = title;
        Prompt = prompt;
        InitialValue = initialValue;
    }

    public string Title { get; }
    public string Prompt { get; }
    public string? InitialValue { get; }
    public Task<string?> Completion => _completion.Task;

    public bool TryComplete(string? value) => _completion.TrySetResult(value);

    public bool TryCancel() => _completion.TrySetResult(null);
}
