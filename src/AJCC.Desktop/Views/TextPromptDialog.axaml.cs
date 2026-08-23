using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class TextPromptDialog : Window
{
    private bool _allowEmpty;

    public TextPromptDialog()
    {
        InitializeComponent();
    }

    public TextPromptDialog(
        string title,
        string prompt,
        string initialValue,
        string detail = "",
        string acceptText = "Übernehmen",
        bool allowEmpty = false)
        : this()
    {
        Title = title;
        _allowEmpty = allowEmpty;

        TextBlock? promptText = this.FindControl<TextBlock>("PromptText");
        TextBlock? detailText = this.FindControl<TextBlock>("DetailText");
        TextBox? input = this.FindControl<TextBox>("InputTextBox");
        Button? acceptButton = this.FindControl<Button>("AcceptButton");

        if (promptText is not null)
            promptText.Text = prompt;
        if (detailText is not null)
            detailText.Text = detail;
        if (input is not null)
        {
            input.Text = initialValue ?? string.Empty;
            input.SelectionStart = 0;
            input.SelectionEnd = input.Text?.Length ?? 0;
            Opened += (_, _) =>
            {
                input.Focus();
                input.SelectAll();
            };
        }
        if (acceptButton is not null)
            acceptButton.Content = acceptText;
    }

    public string TextValue { get; private set; } = string.Empty;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void AcceptButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? input = this.FindControl<TextBox>("InputTextBox");
        string value = input?.Text?.Trim() ?? string.Empty;
        if (!_allowEmpty && value.Length == 0)
        {
            input?.Focus();
            return;
        }

        TextValue = value;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);
}
