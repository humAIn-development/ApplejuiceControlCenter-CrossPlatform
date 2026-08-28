using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class CoreProfilePasswordDialog : Window
{
    public CoreProfilePasswordDialog()
    {
        InitializeComponent();
    }

    public CoreProfilePasswordDialog(string profileName, string endpoint)
        : this()
    {
        TextBlock? profileText = this.FindControl<TextBlock>("ProfileText");
        TextBox? passwordInput = this.FindControl<TextBox>("PasswordInput");

        if (profileText is not null)
        {
            profileText.Text =
                $"{profileName} · {endpoint}\nDas Passwort wird nur für die laufende AJCC-X-Sitzung verwendet.";
        }

        if (passwordInput is not null)
            Opened += (_, _) => passwordInput.Focus();
    }

    public string Password
        => this.FindControl<TextBox>("PasswordInput")?.Text ?? string.Empty;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void ContinueButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(true);

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);
}
