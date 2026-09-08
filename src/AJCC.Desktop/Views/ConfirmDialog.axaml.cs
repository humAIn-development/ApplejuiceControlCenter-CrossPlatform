using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public ConfirmDialog(
        string title,
        string message,
        string confirmText = "Bestätigen",
        string cancelText = "Zurück",
        bool showSuppressFutureConfirmationOption = false)
        : this()
    {
        Title = title;

        TextBlock? titleText = this.FindControl<TextBlock>("TitleText");
        TextBlock? messageText = this.FindControl<TextBlock>("MessageText");
        Button? confirmButton = this.FindControl<Button>("ConfirmButton");
        Button? cancelButton = this.FindControl<Button>("CancelButton");
        CheckBox? suppressCheckBox = this.FindControl<CheckBox>("SuppressFutureConfirmationCheckBox");

        if (titleText is not null)
            titleText.Text = title;
        if (messageText is not null)
            messageText.Text = message;
        if (confirmButton is not null)
            confirmButton.Content = confirmText;
        if (cancelButton is not null)
            cancelButton.Content = cancelText;
        if (suppressCheckBox is not null)
            suppressCheckBox.IsVisible = showSuppressFutureConfirmationOption;
    }

    public bool SuppressFutureConfirmationRequested
    {
        get
        {
            CheckBox? suppressCheckBox = this.FindControl<CheckBox>("SuppressFutureConfirmationCheckBox");
            return suppressCheckBox?.IsVisible == true && suppressCheckBox.IsChecked == true;
        }
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void ConfirmButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(true);

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);
}
