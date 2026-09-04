using System.Net.Mail;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AJCC.Desktop.Views;

public sealed partial class FeedbackDialog : Window
{
    private const string FeedbackEndpoint = "https://applejuice-control-center.de.cool/feedback_send.php";
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(45) };

    private readonly string _technicalContext;
    private readonly byte[] _diagnosticsZip;
    private bool _sending;

    public FeedbackDialog()
    {
        InitializeComponent();
        _technicalContext = string.Empty;
        _diagnosticsZip = Array.Empty<byte>();
    }

    public FeedbackDialog(
        string ajccVersion,
        string coreVersion,
        string systemVersion,
        string technicalContext,
        byte[] diagnosticsZip)
        : this()
    {
        _technicalContext = technicalContext ?? string.Empty;
        _diagnosticsZip = diagnosticsZip ?? Array.Empty<byte>();

        this.FindControl<TextBox>("AjccVersionBox")!.Text = ajccVersion;
        this.FindControl<TextBox>("CoreVersionBox")!.Text = coreVersion;
        this.FindControl<TextBox>("SystemVersionBox")!.Text = systemVersion;
        this.FindControl<TextBox>("TechnicalDataBox")!.Text = _technicalContext;
    }

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private async void SendButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (_sending)
            return;

        string title = ReadText("TitleBox");
        string description = ReadText("DescriptionBox");
        string email = ReadText("EmailBox");

        if (title.Length == 0)
        {
            SetStatus("Bitte einen Kurztitel eingeben.");
            this.FindControl<TextBox>("TitleBox")?.Focus();
            return;
        }

        if (description.Length == 0)
        {
            SetStatus("Bitte eine Beschreibung eingeben.");
            this.FindControl<TextBox>("DescriptionBox")?.Focus();
            return;
        }

        if (email.Length > 0 && !IsValidEmail(email))
        {
            SetStatus("Die E-Mail-Adresse ist nicht gültig.");
            this.FindControl<TextBox>("EmailBox")?.Focus();
            return;
        }

        _sending = true;
        SetBusy(true);
        SetStatus("Feedback wird gesendet ...");

        try
        {
            string[] categories = ["BUG", "QUESTION", "JAVA-COMPARE", "FEATURE", "OTHER"];
            int selectedIndex = this.FindControl<ComboBox>("CategoryBox")?.SelectedIndex ?? 4;
            string category = selectedIndex >= 0 && selectedIndex < categories.Length
                ? categories[selectedIndex]
                : "OTHER";

            bool includeTechnicalData =
                this.FindControl<CheckBox>("IncludeTechnicalDataBox")?.IsChecked == true;
            Dictionary<string, string> fields = new(StringComparer.Ordinal)
            {
                ["category"] = category,
                ["ajcc_version"] = ReadText("AjccVersionBox"),
                ["core_version"] = ReadText("CoreVersionBox"),
                ["windows_version"] = ReadText("SystemVersionBox"),
                ["reporter_name"] = ReadText("NameBox"),
                ["reply_email"] = email,
                ["title"] = title,
                ["description"] = description,
                ["steps"] = ReadText("StepsBox"),
                ["technical_context"] = includeTechnicalData
                    ? _technicalContext
                    : string.Empty
            };

            using MultipartFormDataContent content = new();
            foreach ((string key, string value) in fields)
                content.Add(new StringContent(value ?? string.Empty, Encoding.UTF8), key);

            if (includeTechnicalData && _diagnosticsZip.Length > 0)
            {
                ByteArrayContent diagnostics = new(_diagnosticsZip);
                content.Add(diagnostics, "diagnostics", "AJCC-X-diagnostics.zip");
            }

            using HttpResponseMessage response = await HttpClient.PostAsync(FeedbackEndpoint, content);
            string responseText = await response.Content.ReadAsStringAsync();
            (bool? ok, string message) = ParseResponse(responseText);

            if (!response.IsSuccessStatusCode || ok == false)
            {
                SetStatus(message.Length > 0
                    ? message
                    : $"Feedback konnte nicht gesendet werden (HTTP {(int)response.StatusCode}).");
                return;
            }

            Close(true);
        }
        catch (Exception ex)
        {
            SetStatus("Feedback konnte nicht gesendet werden: " + ex.Message);
        }
        finally
        {
            _sending = false;
            if (IsVisible)
                SetBusy(false);
        }
    }

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private string ReadText(string name)
        => this.FindControl<TextBox>(name)?.Text?.Trim() ?? string.Empty;

    private void SetStatus(string message)
    {
        TextBlock? status = this.FindControl<TextBlock>("StatusText");
        if (status is not null)
            status.Text = message;
    }

    private void SetBusy(bool busy)
    {
        if (this.FindControl<Button>("SendButton") is { } sendButton)
            sendButton.IsEnabled = !busy;
        if (this.FindControl<Button>("CloseButton") is { } closeButton)
            closeButton.IsEnabled = !busy;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            MailAddress address = new(email);
            return string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static (bool? Ok, string Message) ParseResponse(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return (null, string.Empty);

        try
        {
            using JsonDocument document = JsonDocument.Parse(responseText);
            JsonElement root = document.RootElement;
            bool? ok = root.TryGetProperty("ok", out JsonElement okElement)
                ? okElement.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => null
                }
                : null;
            string message = root.TryGetProperty("message", out JsonElement messageElement)
                             && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;
            return (ok, message);
        }
        catch
        {
            return (null, string.Empty);
        }
    }
}
