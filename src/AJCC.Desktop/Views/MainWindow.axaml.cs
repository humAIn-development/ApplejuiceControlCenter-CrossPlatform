using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AJCC.Desktop.ViewModels;

namespace AJCC.Desktop.Views;

public sealed partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Closed += MainWindow_OnClosed;
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private async void ConnectButton_OnClick(object? sender, RoutedEventArgs e)
    {
        TextBox? passwordInput = this.FindControl<TextBox>("PasswordInput");
        string password = passwordInput?.Text ?? string.Empty;

        try
        {
            await _viewModel.ToggleConnectionAsync(password);
        }
        finally
        {
            if (passwordInput is not null)
                passwordInput.Text = string.Empty;
        }
    }

    private async void SearchButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.StartSearchAsync();

    private async void CancelSearchButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.CancelSelectedSearchAsync();

    private void MainWindow_OnClosed(object? sender, EventArgs e)
        => _viewModel.Dispose();
}
