using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AJCC.Desktop.Services;

namespace AJCC.Desktop.Views;

public sealed partial class CoreProfileEditDialog : Window
{
    public CoreProfileEditDialog()
    {
        InitializeComponent();
    }

    public CoreProfileEditDialog(string initialName, string initialEndpoint)
        : this()
    {
        TextBox? nameInput = this.FindControl<TextBox>("ProfileNameInput");
        TextBox? endpointInput = this.FindControl<TextBox>("EndpointInput");
        if (nameInput is not null)
            nameInput.Text = initialName ?? string.Empty;
        if (endpointInput is not null)
            endpointInput.Text = initialEndpoint ?? string.Empty;
    }

    public string ProfileName { get; private set; } = string.Empty;
    public string Endpoint { get; private set; } = string.Empty;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? nameInput = this.FindControl<TextBox>("ProfileNameInput");
        TextBox? endpointInput = this.FindControl<TextBox>("EndpointInput");
        TextBlock? status = this.FindControl<TextBlock>("StatusText");

        string name = (nameInput?.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            if (status is not null)
                status.Text = "Bitte einen Profilnamen eingeben.";
            return;
        }

        string endpoint;
        try
        {
            endpoint = CoreProfileStore.NormalizeEndpoint(endpointInput?.Text ?? string.Empty);
        }
        catch (Exception ex)
        {
            if (status is not null)
                status.Text = "Core-Endpunkt ist ungültig: " + ex.Message;
            return;
        }

        ProfileName = name;
        Endpoint = endpoint;
        Close(true);
    }
}
