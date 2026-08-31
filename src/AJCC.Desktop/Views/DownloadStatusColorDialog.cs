using AJCC.Desktop.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AJCC.Desktop.Views;

public sealed class DownloadStatusColorDialog : Window
{
    private static readonly string[] Palette =
    {
        "#15171C",
        "#20232B",
        "#282C35",
        "#111318",
        "#39404C",
        "#4DA3FF",
        "#57D38C",
        "#FF6B6B",
        "#F3F6FA",
        "#AEB7C2",
        "#39FF14",
        "#071407",
        "#FF2020",
        "#FFFFFF",
        "#FF77C8",
        "#1A0010"
    };

    private readonly ComboBox _completedBackground;
    private readonly ComboBox _completedForeground;
    private readonly ComboBox _abortedBackground;
    private readonly ComboBox _abortedForeground;
    private readonly ComboBox _pausedBackground;
    private readonly ComboBox _pausedForeground;

    public DownloadStatusColorConfiguration Configuration { get; private set; }

    public DownloadStatusColorDialog(DownloadStatusColorConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Configuration = configuration;

        Title = "Download-Statusfarben";
        Width = 620;
        Height = 570;
        MinWidth = 560;
        MinHeight = 520;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _completedBackground = CreateColorSelector(configuration.CompletedBackground, "#39FF14");
        _completedForeground = CreateColorSelector(configuration.CompletedForeground, "#071407");
        _abortedBackground = CreateColorSelector(configuration.AbortedBackground, "#FF2020");
        _abortedForeground = CreateColorSelector(configuration.AbortedForeground, "#FFFFFF");
        _pausedBackground = CreateColorSelector(configuration.PausedBackground, "#FF77C8");
        _pausedForeground = CreateColorSelector(configuration.PausedForeground, "#1A0010");

        StackPanel root = new()
        {
            Margin = new Thickness(18),
            Spacing = 12
        };

        root.Children.Add(new TextBlock
        {
            Text = "Download-Statusfarben",
            FontSize = 18,
            FontWeight = FontWeight.SemiBold
        });
        root.Children.Add(new TextBlock
        {
            Text = "Hintergrund- und Textfarbe für die drei bereits unterstützten Statusrollen auswählen. "
                + "Die Auswahl nutzt die AJCC-Palette plus die bewährten Statusfarben.",
            TextWrapping = TextWrapping.Wrap
        });

        root.Children.Add(CreateRoleEditor(
            "Fertig",
            _completedBackground,
            _completedForeground,
            "#39FF14",
            "#071407"));
        root.Children.Add(CreateRoleEditor(
            "Abbrechen / Abgebrochen",
            _abortedBackground,
            _abortedForeground,
            "#FF2020",
            "#FFFFFF"));
        root.Children.Add(CreateRoleEditor(
            "Pausiert",
            _pausedBackground,
            _pausedForeground,
            "#FF77C8",
            "#1A0010"));

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0)
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
            Configuration = new DownloadStatusColorConfiguration
            {
                CompletedBackground = SelectedColor(_completedBackground, "#39FF14"),
                CompletedForeground = SelectedColor(_completedForeground, "#071407"),
                AbortedBackground = SelectedColor(_abortedBackground, "#FF2020"),
                AbortedForeground = SelectedColor(_abortedForeground, "#FFFFFF"),
                PausedBackground = SelectedColor(_pausedBackground, "#FF77C8"),
                PausedForeground = SelectedColor(_pausedForeground, "#1A0010")
            };
            Close(true);
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(applyButton);
        root.Children.Add(buttons);

        Content = root;
    }

    private static Border CreateRoleEditor(
        string title,
        ComboBox backgroundSelector,
        ComboBox foregroundSelector,
        string fallbackBackground,
        string fallbackForeground)
    {
        TextBlock previewText = new()
        {
            Text = "Vorschau: " + title,
            FontWeight = FontWeight.SemiBold
        };
        Border preview = new()
        {
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(4),
            Child = previewText
        };

        void RefreshPreview()
        {
            preview.Background = CreateBrush(
                SelectedColor(backgroundSelector, fallbackBackground),
                fallbackBackground);
            previewText.Foreground = CreateBrush(
                SelectedColor(foregroundSelector, fallbackForeground),
                fallbackForeground);
        }

        backgroundSelector.SelectionChanged += (_, _) => RefreshPreview();
        foregroundSelector.SelectionChanged += (_, _) => RefreshPreview();
        RefreshPreview();

        StackPanel selectors = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        selectors.Children.Add(new TextBlock
        {
            Text = "Hintergrund",
            VerticalAlignment = VerticalAlignment.Center
        });
        selectors.Children.Add(backgroundSelector);
        selectors.Children.Add(new TextBlock
        {
            Text = "Text",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        });
        selectors.Children.Add(foregroundSelector);

        StackPanel content = new()
        {
            Spacing = 7
        };
        content.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeight.SemiBold
        });
        content.Children.Add(selectors);
        content.Children.Add(preview);

        return new Border
        {
            BorderBrush = CreateBrush("#39404C", "#39404C"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = content
        };
    }

    private static ComboBox CreateColorSelector(string? value, string fallback)
    {
        string normalized = NormalizeColor(value, fallback);
        List<string> options = new(Palette);
        bool exists = false;
        foreach (string option in options)
        {
            if (string.Equals(option, normalized, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
            options.Insert(0, normalized);

        return new ComboBox
        {
            Width = 118,
            ItemsSource = options,
            SelectedItem = normalized
        };
    }

    private static string SelectedColor(ComboBox selector, string fallback)
        => NormalizeColor(selector.SelectedItem as string, fallback);

    private static SolidColorBrush CreateBrush(string? value, string fallback)
        => new(Color.Parse(NormalizeColor(value, fallback)));

    private static string NormalizeColor(string? value, string fallback)
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
}
