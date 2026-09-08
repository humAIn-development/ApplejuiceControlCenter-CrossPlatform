using AJCC.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace AJCC.Desktop.Views;

public sealed class StatisticsTileConfigurationDialog : Window
{
    private readonly Dictionary<string, CheckBox> _checkBoxes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TextBlock _statusText;

    public StatisticsTileConfigurationDialog(IEnumerable<string> selectedKeys)
    {
        ArgumentNullException.ThrowIfNull(selectedKeys);

        Title = "Statistik-Kacheln konfigurieren";
        Width = 560;
        Height = 690;
        MinWidth = 460;
        MinHeight = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        HashSet<string> selected = new(
            StatisticsTileCatalog.NormalizeSelection(selectedKeys),
            StringComparer.OrdinalIgnoreCase);

        TextBlock header = new()
        {
            Text = "Cockpit-Kacheln auswählen (max. 8)",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };

        TextBlock hint = new()
        {
            Text = "Die Reihenfolge ist fest vorgegeben. Mindestens eine und höchstens acht Kacheln können gleichzeitig sichtbar sein.",
            TextWrapping = TextWrapping.Wrap
        };
        hint.Classes.Add("muted");

        StackPanel checkPanel = new() { Spacing = 4, Margin = new Thickness(0, 10, 0, 8) };
        foreach (StatisticsTileDefinition definition in StatisticsTileCatalog.Definitions)
        {
            CheckBox checkBox = new()
            {
                Content = definition.Title,
                IsChecked = selected.Contains(definition.Key),
                Margin = new Thickness(0, 2)
            };
            ToolTip.SetTip(checkBox, definition.Description);
            checkBox.IsCheckedChanged += CheckBox_OnChanged;
            _checkBoxes[definition.Key] = checkBox;
            checkPanel.Children.Add(checkBox);
        }

        ScrollViewer selectionScroll = new()
        {
            Content = checkPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        _statusText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 8)
        };
        _statusText.Classes.Add("muted");

        Button cancelButton = new()
        {
            Content = "Abbrechen",
            MinWidth = 100
        };
        cancelButton.Click += CancelButton_OnClick;

        Button applyButton = new()
        {
            Content = "Übernehmen",
            MinWidth = 110
        };
        applyButton.Classes.Add("primary");
        applyButton.Click += ApplyButton_OnClick;

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(applyButton);

        Grid root = new()
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto")
        };
        root.Children.Add(header);
        Grid.SetRow(hint, 1);
        root.Children.Add(hint);
        Grid.SetRow(selectionScroll, 2);
        root.Children.Add(selectionScroll);
        Grid.SetRow(_statusText, 3);
        root.Children.Add(_statusText);
        Grid.SetRow(buttons, 4);
        root.Children.Add(buttons);

        Content = root;
        UpdateStatus();
    }

    public string[] SelectedKeys { get; private set; } =
        StatisticsTileCatalog.DefaultSelectedKeys.ToArray();

    private void CheckBox_OnChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox changed || changed.IsChecked != true)
        {
            UpdateStatus();
            return;
        }

        if (CountChecked() <= StatisticsTileCatalog.MaximumVisibleTiles)
        {
            UpdateStatus();
            return;
        }

        changed.IsChecked = false;
        _statusText.Text = "Maximal 8 Kacheln können gleichzeitig sichtbar sein.";
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void ApplyButton_OnClick(object? sender, RoutedEventArgs e)
    {
        string[] selected = StatisticsTileCatalog.Definitions
            .Where(definition =>
                _checkBoxes.TryGetValue(definition.Key, out CheckBox? checkBox)
                && checkBox.IsChecked == true)
            .Select(static definition => definition.Key)
            .ToArray();

        if (selected.Length < StatisticsTileCatalog.MinimumVisibleTiles)
        {
            _statusText.Text = "Mindestens eine Kachel muss sichtbar bleiben.";
            return;
        }

        SelectedKeys = selected;
        Close(true);
    }

    private int CountChecked()
        => _checkBoxes.Values.Count(static checkBox => checkBox.IsChecked == true);

    private void UpdateStatus()
    {
        int count = CountChecked();
        _statusText.Text = $"{count} von maximal {StatisticsTileCatalog.MaximumVisibleTiles} Kacheln ausgewählt.";
    }
}
