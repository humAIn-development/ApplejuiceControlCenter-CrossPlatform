using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class CoreProfileSaveDialog : Window
{
    public CoreProfileSaveDialog()
    {
        InitializeComponent();
    }

    public CoreProfileSaveDialog(string initialName)
        : this()
    {
        TextBox? input = this.FindControl<TextBox>("ProfileNameInput");
        if (input is not null)
        {
            input.Text = initialName ?? string.Empty;
            input.SelectAll();
        }
    }

    public string ProfileName { get; private set; } = string.Empty;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? input = this.FindControl<TextBox>("ProfileNameInput");
        TextBlock? status = this.FindControl<TextBlock>("StatusText");
        string name = (input?.Text ?? string.Empty).Trim();

        if (name.Length == 0)
        {
            if (status is not null)
                status.Text = "Bitte einen Profilnamen eingeben.";
            return;
        }

        ProfileName = name;
        Close(true);
    }
}
