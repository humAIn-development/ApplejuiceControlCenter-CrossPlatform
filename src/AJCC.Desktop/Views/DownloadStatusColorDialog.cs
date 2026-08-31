using AJCC.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace AJCC.Desktop.Views;

public sealed class DownloadStatusColorDialog : Window
{
    private readonly List<RowState> _rows = new();
    private readonly StackPanel _rowsPanel = new()
    {
        Spacing = 8
    };

    public DownloadStatusColorConfiguration Configuration { get; private set; }

    public DownloadStatusColorDialog(DownloadStatusColorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Configuration = configuration;

        Title = "Download-Statusfarben";
        Width = 820;
        Height = 700;
        MinWidth = 760;
        MinHeight = 620;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        DockPanel root = new()
        {
            Margin = new Thickness(18),
            LastChildFill = true
        };

        StackPanel header = new()
        {
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 12)
        };
        header.Children.Add(new TextBlock
        {
            Text = "Download-Statusfarben",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "Die Liste enthält die Download-Statuswerte, die auch in der Downloadliste erscheinen können. "
                + "Jede Regel kann einzeln aktiviert werden; Hintergrund- und Textfarbe werden gemeinsam bearbeitet.",
            TextWrapping = TextWrapping.Wrap
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        Button defaultsButton = new()
        {
            Content = "Standard",
            MinWidth = 100
        };
        defaultsButton.Click += (_, _) => LoadRules(
            new DownloadStatusColorConfiguration
            {
                Rules = DownloadStatusColorConfiguration.CreateDefaultRules()
            });

        Button cancelButton = new()
        {
            Content = "Abbrechen",
            MinWidth = 110
        };
        cancelButton.Click += (_, _) => Close(false);

        Button applyButton = new()
        {
            Content = "Übernehmen",
            MinWidth = 120
        };
        applyButton.Click += (_, _) =>
        {
            Configuration = BuildConfigurationFromRows();
            Close(true);
        };

        buttons.Children.Add(defaultsButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(applyButton);
        root.Children.Add(buttons);

        Border listBorder = new()
        {
            BorderBrush = CreateBrush("#39404C", "#39404C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10)
        };

        ScrollViewer scroll = new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = _rowsPanel
        };
        listBorder.Child = scroll;
        root.Children.Add(listBorder);

        LoadRules(configuration);
        Content = root;
    }

    private void LoadRules(DownloadStatusColorConfiguration configuration)
    {
        _rows.Clear();
        _rowsPanel.Children.Clear();

        foreach (DownloadStatusColorRule defaultRule in DownloadStatusColorConfiguration.CreateDefaultRules())
        {
            DownloadStatusColorRule? stored =
                configuration.Rules.FirstOrDefault(rule => rule.Status == defaultRule.Status);

            DownloadStatusColorRule source = stored ?? defaultRule;
            RowState state = new(
                defaultRule.Status,
                defaultRule.Label,
                source.Enabled,
                NormalizeColor(source.Background, defaultRule.Background),
                NormalizeColor(source.Foreground, defaultRule.Foreground));

            _rows.Add(state);
            _rowsPanel.Children.Add(CreateRuleRow(state));
        }
    }

    private Border CreateRuleRow(RowState state)
    {
        CheckBox enabled = new()
        {
            IsChecked = state.Enabled,
            Width = 28,
            VerticalAlignment = VerticalAlignment.Center
        };
        state.EnabledCheckBox = enabled;

        TextBlock label = new()
        {
            Text = state.Status >= 0
                ? $"{state.Label}  ({state.Status})"
                : state.Label,
            Width = 210,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        TextBlock previewText = new()
        {
            Text = "Vorschau: " + state.Label,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        Border preview = new()
        {
            Width = 330,
            Padding = new Thickness(10, 5),
            CornerRadius = new CornerRadius(6),
            BorderBrush = CreateBrush("#5B6575", "#5B6575"),
            BorderThickness = new Thickness(1),
            Child = previewText
        };
        state.Preview = preview;
        state.PreviewText = previewText;

        Button colorsButton = new()
        {
            Content = "Farben...",
            MinWidth = 110
        };
        colorsButton.Click += async (_, _) =>
        {
            StatusColorPairDialog dialog = new(
                state.Label,
                state.Background,
                state.Foreground);

            if (!await dialog.ShowDialog<bool>(this))
                return;

            state.Background = dialog.BackgroundColor;
            state.Foreground = dialog.ForegroundColor;
            UpdatePreview(state);
        };

        enabled.Click += (_, _) => UpdatePreview(state);

        StackPanel row = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(enabled);
        row.Children.Add(label);
        row.Children.Add(preview);
        row.Children.Add(colorsButton);

        Border host = new()
        {
            BorderBrush = CreateBrush("#39404C", "#39404C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = row
        };

        UpdatePreview(state);
        return host;
    }

    private static void UpdatePreview(RowState state)
    {
        bool enabled = state.EnabledCheckBox?.IsChecked == true;
        state.Enabled = enabled;

        if (state.Preview is null || state.PreviewText is null)
            return;

        state.Preview.Background = enabled
            ? CreateBrush(state.Background, "#00000000")
            : CreateBrush("#1B1F26", "#1B1F26");

        state.PreviewText.Foreground = enabled
            ? CreateBrush(state.Foreground, "#FFFFFF")
            : CreateBrush("#8892A0", "#8892A0");
    }

    private DownloadStatusColorConfiguration BuildConfigurationFromRows()
    {
        List<DownloadStatusColorRule> rules = _rows
            .Select(state => new DownloadStatusColorRule(
                state.Status,
                state.Label,
                state.EnabledCheckBox?.IsChecked == true,
                NormalizeColor(state.Background, "#00000000"),
                NormalizeColor(state.Foreground, "#FFFFFF")))
            .ToList();

        DownloadStatusColorRule completed = rules.First(rule => rule.Status == 14);
        DownloadStatusColorRule aborted = rules.First(rule => rule.Status == 17);
        DownloadStatusColorRule paused = rules.First(rule => rule.Status == 18);
        DownloadStatusColorRule other = rules.First(rule => rule.Status == -1);

        return new DownloadStatusColorConfiguration
        {
            CompletedEnabled = completed.Enabled,
            CompletedBackground = completed.Background,
            CompletedForeground = completed.Foreground,
            AbortedEnabled = aborted.Enabled,
            AbortedBackground = aborted.Background,
            AbortedForeground = aborted.Foreground,
            PausedEnabled = paused.Enabled,
            PausedBackground = paused.Background,
            PausedForeground = paused.Foreground,
            OtherEnabled = other.Enabled,
            OtherBackground = other.Background,
            OtherForeground = other.Foreground,
            Rules = rules
        };
    }

    internal static SolidColorBrush CreateBrush(string? value, string fallback)
        => new(Color.Parse(NormalizeColor(value, fallback)));

    internal static string NormalizeColor(string? value, string fallback)
    {
        string candidate = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        try
        {
            _ = Color.Parse(candidate);
            return candidate;
        }
        catch
        {
            return fallback;
        }
    }

    private sealed class RowState
    {
        public RowState(
            int status,
            string label,
            bool enabled,
            string background,
            string foreground)
        {
            Status = status;
            Label = label;
            Enabled = enabled;
            Background = background;
            Foreground = foreground;
        }

        public int Status { get; }
        public string Label { get; }
        public bool Enabled { get; set; }
        public string Background { get; set; }
        public string Foreground { get; set; }
        public CheckBox? EnabledCheckBox { get; set; }
        public Border? Preview { get; set; }
        public TextBlock? PreviewText { get; set; }
    }
}

public sealed class StatusColorPairDialog : Window
{
    private readonly Border _preview;
    private readonly TextBlock _previewText;
    private readonly Button _backgroundButton;
    private readonly Button _foregroundButton;
    private readonly string _statusLabel;

    public string BackgroundColor { get; private set; }
    public string ForegroundColor { get; private set; }

    public StatusColorPairDialog(
        string statusLabel,
        string backgroundColor,
        string foregroundColor)
    {
        _statusLabel = string.IsNullOrWhiteSpace(statusLabel)
            ? "Downloadstatus"
            : statusLabel.Trim();

        BackgroundColor = DownloadStatusColorDialog.NormalizeColor(
            backgroundColor,
            "#00000000");
        ForegroundColor = DownloadStatusColorDialog.NormalizeColor(
            foregroundColor,
            "#FFFFFF");

        Title = "Statusfarben bearbeiten";
        Width = 560;
        Height = 360;
        MinWidth = 520;
        MinHeight = 330;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        StackPanel root = new()
        {
            Margin = new Thickness(18),
            Spacing = 12
        };

        root.Children.Add(new TextBlock
        {
            Text = $"Status: {_statusLabel}\nHintergrund- und Textfarbe werden gemeinsam gespeichert.",
            TextWrapping = TextWrapping.Wrap
        });

        _previewText = new TextBlock
        {
            Text = "Vorschau: " + _statusLabel,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        _preview = new Border
        {
            Height = 58,
            CornerRadius = new CornerRadius(8),
            BorderBrush = DownloadStatusColorDialog.CreateBrush("#5B6575", "#5B6575"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            Child = _previewText
        };
        root.Children.Add(_preview);

        _backgroundButton = new Button
        {
            MinWidth = 225
        };
        _backgroundButton.Click += async (_, _) =>
        {
            ColorPaletteDialog dialog = new(BackgroundColor);
            if (!await dialog.ShowDialog<bool>(this))
                return;

            BackgroundColor = dialog.SelectedColor;
            UpdatePreview();
        };

        _foregroundButton = new Button
        {
            MinWidth = 225
        };
        _foregroundButton.Click += async (_, _) =>
        {
            ColorPaletteDialog dialog = new(ForegroundColor);
            if (!await dialog.ShowDialog<bool>(this))
                return;

            ForegroundColor = dialog.SelectedColor;
            UpdatePreview();
        };

        StackPanel colorButtons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10
        };
        colorButtons.Children.Add(_backgroundButton);
        colorButtons.Children.Add(_foregroundButton);
        root.Children.Add(colorButtons);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button cancelButton = new()
        {
            Content = "Abbrechen",
            MinWidth = 110
        };
        cancelButton.Click += (_, _) => Close(false);

        Button applyButton = new()
        {
            Content = "Übernehmen",
            MinWidth = 110
        };
        applyButton.Click += (_, _) => Close(true);

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(applyButton);
        root.Children.Add(buttons);

        UpdatePreview();
        Content = root;
    }

    private void UpdatePreview()
    {
        _preview.Background = DownloadStatusColorDialog.CreateBrush(
            BackgroundColor,
            "#00000000");
        _previewText.Foreground = DownloadStatusColorDialog.CreateBrush(
            ForegroundColor,
            "#FFFFFF");
        _backgroundButton.Content = "Hintergrund: " + BackgroundColor;
        _foregroundButton.Content = "Text: " + ForegroundColor;
    }
}

public sealed class ColorPaletteDialog : Window
{
    private static readonly string[] Palette =
    {
        "#00000000",
        "#FFFFFF",
        "#000000",
        "#39FF14",
        "#FF2020",
        "#FFD700",
        "#00BFFF",
        "#FF66CC",
        "#FF8C00",
        "#7CFFCB",
        "#7F7FFF",
        "#808080",
        "#20242B",
        "#071407",
        "#1B1F26",
        "#B8C0CC"
    };

    private readonly Border _preview;
    private readonly TextBox _hexBox;
    private readonly TextBlock _validationText;

    public string SelectedColor { get; private set; }

    public ColorPaletteDialog(string initialColor)
    {
        SelectedColor = DownloadStatusColorDialog.NormalizeColor(
            initialColor,
            "#00000000");

        Title = "Farbe wählen";
        Width = 460;
        Height = 450;
        MinWidth = 430;
        MinHeight = 420;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        StackPanel root = new()
        {
            Margin = new Thickness(18),
            Spacing = 10
        };

        root.Children.Add(new TextBlock
        {
            Text = "AJCC-Palette oder freie Hex-/ARGB-Farbe wählen.",
            TextWrapping = TextWrapping.Wrap
        });

        for (int index = 0; index < Palette.Length; index += 4)
        {
            StackPanel row = new()
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };

            for (int offset = 0; offset < 4 && index + offset < Palette.Length; offset++)
            {
                string color = Palette[index + offset];
                Button swatch = new()
                {
                    Content = color,
                    Width = 96,
                    Height = 36,
                    Background = DownloadStatusColorDialog.CreateBrush(
                        color,
                        "#00000000")
                };
                swatch.Click += (_, _) => SetColor(color);
                row.Children.Add(swatch);
            }

            root.Children.Add(row);
        }

        _preview = new Border
        {
            Height = 48,
            CornerRadius = new CornerRadius(8),
            BorderBrush = DownloadStatusColorDialog.CreateBrush("#5B6575", "#5B6575"),
            BorderThickness = new Thickness(1)
        };
        root.Children.Add(_preview);

        root.Children.Add(new TextBlock
        {
            Text = "Hex/ARGB (#RRGGBB oder #AARRGGBB)"
        });

        _hexBox = new TextBox
        {
            Text = SelectedColor
        };
        _validationText = new TextBlock();
        _hexBox.TextChanged += (_, _) =>
        {
            if (TryParseColor(_hexBox.Text, out string normalized))
            {
                SelectedColor = normalized;
                _preview.Background = DownloadStatusColorDialog.CreateBrush(
                    SelectedColor,
                    "#00000000");
                _validationText.Text = string.Empty;
            }
            else
            {
                _validationText.Text = "Ungültige Farbe.";
            }
        };
        root.Children.Add(_hexBox);
        root.Children.Add(_validationText);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button cancelButton = new()
        {
            Content = "Abbrechen",
            MinWidth = 110
        };
        cancelButton.Click += (_, _) => Close(false);

        Button applyButton = new()
        {
            Content = "Übernehmen",
            MinWidth = 110
        };
        applyButton.Click += (_, _) =>
        {
            if (!TryParseColor(_hexBox.Text, out string normalized))
            {
                _validationText.Text = "Bitte eine gültige Farbe eingeben.";
                return;
            }

            SelectedColor = normalized;
            Close(true);
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(applyButton);
        root.Children.Add(buttons);

        _preview.Background = DownloadStatusColorDialog.CreateBrush(
            SelectedColor,
            "#00000000");
        Content = root;
    }

    private void SetColor(string color)
    {
        SelectedColor = color;
        _hexBox.Text = color;
        _preview.Background = DownloadStatusColorDialog.CreateBrush(
            color,
            "#00000000");
    }

    private static bool TryParseColor(string? value, out string normalized)
    {
        normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0)
            return false;

        try
        {
            _ = Color.Parse(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
