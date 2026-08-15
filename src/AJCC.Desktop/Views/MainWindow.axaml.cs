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
        string password = PasswordInput.Text ?? string.Empty;
        await _viewModel.ToggleConnectionAsync(password);
        PasswordInput.Text = string.Empty;
    }

    private async void SearchButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.StartSearchAsync();

    private async void CancelSearchButton_OnClick(object? sender, RoutedEventArgs e)
        => await _viewModel.CancelSelectedSearchAsync();

    private void MainWindow_OnClosed(object? sender, EventArgs e)
        => _viewModel.Dispose();
}
