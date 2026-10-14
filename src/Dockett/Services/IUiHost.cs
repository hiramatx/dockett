namespace Dockett.Services;

/// <summary>What view models need from the window: dialogs and a native owner handle.</summary>
public interface IUiHost
{
    nint OwnerHandle { get; }

    /// <summary>Asks for a line of text. Returns null when cancelled.</summary>
    Task<string?> PromptAsync(string title, string message, string initialText, bool selectStem = false);

    Task<bool> ConfirmAsync(string title, string message, string confirmText, bool destructive = false);

    Task AlertAsync(string title, string message);
}
