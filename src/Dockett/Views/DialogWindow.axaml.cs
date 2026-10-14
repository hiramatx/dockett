using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Dockett.Views;

/// <summary>
/// Small modal used for confirmations, alerts, and single-line prompts.
/// Closes with the entered text (or "" for a plain confirmation), or null when cancelled.
/// </summary>
public partial class DialogWindow : Window
{
    readonly bool _hasInput;

    public DialogWindow() : this("", "", "OK")
    {
    }

    public DialogWindow(string title, string message, string confirmText, string? initialText = null,
        bool selectStem = false, bool destructive = false, bool showCancel = true)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        CancelButton.IsVisible = showCancel;
        if (destructive)
            ConfirmButton.Classes.Add("danger");

        _hasInput = initialText is not null;
        Input.IsVisible = _hasInput;
        Input.Text = initialText ?? "";

        KeyDown += OnKeyDown;
        Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (_hasInput)
            {
                Input.Focus(NavigationMethod.Tab);
                // Select after focus has settled; focusing can reset the selection.
                Dispatcher.UIThread.Post(() =>
                {
                    var stemLength = selectStem ? Path.GetFileNameWithoutExtension(Input.Text ?? "").Length : 0;
                    Input.SelectionStart = 0;
                    Input.SelectionEnd = stemLength > 0 ? stemLength : Input.Text?.Length ?? 0;
                }, DispatcherPriority.Background);
            }
            else
            {
                ConfirmButton.Focus(NavigationMethod.Tab);
            }
        }, DispatcherPriority.Background);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Confirm();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
        }
    }

    void OnConfirm(object? sender, RoutedEventArgs e) => Confirm();

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    void Confirm() => Close(_hasInput ? Input.Text ?? "" : "");
}
