using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AJCC.Core.Protocol;
using AJCC.Desktop.Services;

namespace AJCC.Desktop.Views;

public sealed partial class CoreProfileManagerDialog : Window
{
    private readonly ObservableCollection<CoreProfileEntry> _profiles = new();
    private string _defaultProfileId = string.Empty;
    private string _activeProfileId = string.Empty;
    private string _activeEndpoint = string.Empty;

    public CoreProfileManagerDialog()
    {
        InitializeComponent();
    }

    public CoreProfileManagerDialog(
        IEnumerable<CoreProfileEntry> profiles,
        string defaultProfileId,
        string activeProfileId,
        string activeEndpoint)
        : this()
    {
        foreach (CoreProfileEntry profile in profiles)
            _profiles.Add(Clone(profile));

        if (_profiles.Count == 0)
        {
            CoreProfileEntry standard = new()
            {
                Name = "Standard-Core",
                Endpoint = "http://127.0.0.1:9851/"
            };
            _profiles.Add(standard);
            defaultProfileId = standard.Id;
        }

        _defaultProfileId = _profiles.Any(profile =>
                string.Equals(profile.Id, defaultProfileId, StringComparison.OrdinalIgnoreCase))
            ? defaultProfileId
            : _profiles[0].Id;
        _activeProfileId = activeProfileId ?? string.Empty;
        _activeEndpoint = activeEndpoint ?? string.Empty;

        UpdateActiveProfileHint();
        RefreshProfileRows(
            _profiles.Any(profile =>
                string.Equals(profile.Id, _activeProfileId, StringComparison.OrdinalIgnoreCase))
                ? _activeProfileId
                : _profiles[0].Id);
    }

    public IReadOnlyList<CoreProfileEntry> ResultProfiles { get; private set; } = Array.Empty<CoreProfileEntry>();
    public string ResultDefaultProfileId { get; private set; } = string.Empty;

    private void InitializeComponent()
        => AvaloniaXamlLoader.Load(this);

    private void ProfilesList_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
        => LoadSelectedProfileIntoEditor();

    private void LoadSelectedProfileIntoEditor()
    {
        ProfileRow? row = GetSelectedRow();
        TextBox? nameInput = this.FindControl<TextBox>("ProfileNameInput");
        TextBox? hostInput = this.FindControl<TextBox>("ProfileHostInput");
        TextBox? portInput = this.FindControl<TextBox>("ProfilePortInput");
        TextBlock? validation = this.FindControl<TextBlock>("ValidationText");

        if (validation is not null)
            validation.Text = string.Empty;

        if (row is null)
        {
            if (nameInput is not null)
                nameInput.Text = string.Empty;
            if (hostInput is not null)
                hostInput.Text = string.Empty;
            if (portInput is not null)
                portInput.Text = string.Empty;
            UpdateSelectedProfileStatus(null);
            return;
        }

        CoreEndpoint endpoint = CoreEndpoint.Parse(row.Profile.Endpoint);
        if (nameInput is not null)
            nameInput.Text = row.Profile.Name;
        if (hostInput is not null)
            hostInput.Text = endpoint.Host;
        if (portInput is not null)
            portInput.Text = endpoint.BaseUri.Port.ToString();

        UpdateSelectedProfileStatus(row.Profile);
    }

    private void NewProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        int candidatePort = 9851;
        while (_profiles.Any(profile =>
                   string.Equals(
                       CoreProfileStore.TryNormalizeEndpoint(profile.Endpoint),
                       $"http://127.0.0.1:{candidatePort}/",
                       StringComparison.OrdinalIgnoreCase)))
        {
            candidatePort++;
        }

        CoreProfileEntry profile = new()
        {
            Name = $"Core {_profiles.Count + 1}",
            Endpoint = $"http://127.0.0.1:{candidatePort}/"
        };
        _profiles.Add(profile);
        RefreshProfileRows(profile.Id);
    }

    private void DeleteProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ProfileRow? row = GetSelectedRow();
        if (row is null)
            return;

        TextBlock? validation = this.FindControl<TextBlock>("ValidationText");
        if (_profiles.Count <= 1)
        {
            if (validation is not null)
                validation.Text = "Mindestens ein Core-Profil muss erhalten bleiben.";
            return;
        }

        if (string.Equals(row.Profile.Id, _activeProfileId, StringComparison.OrdinalIgnoreCase))
        {
            if (validation is not null)
                validation.Text = "Das aktuell verbundene Profil kann erst nach einem Wechsel gelöscht werden.";
            return;
        }

        int index = _profiles.IndexOf(row.Profile);
        bool wasDefault = string.Equals(
            row.Profile.Id,
            _defaultProfileId,
            StringComparison.OrdinalIgnoreCase);
        _profiles.Remove(row.Profile);

        if (wasDefault)
            _defaultProfileId = _profiles[0].Id;

        int fallbackIndex = Math.Min(Math.Max(index, 0), _profiles.Count - 1);
        RefreshProfileRows(_profiles[fallbackIndex].Id);
    }

    private void SetDefaultProfileButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!ApplyEditorToSelectedProfile())
            return;

        ProfileRow? row = GetSelectedRow();
        if (row is null)
            return;

        _defaultProfileId = row.Profile.Id;
        RefreshProfileRows(row.Profile.Id);
    }

    private void ApplyProfileChangesButton_OnClick(object? sender, RoutedEventArgs e)
    {
        ProfileRow? row = GetSelectedRow();
        if (row is null)
            return;

        string selectedId = row.Profile.Id;
        if (ApplyEditorToSelectedProfile())
            RefreshProfileRows(selectedId);
    }

    private bool ApplyEditorToSelectedProfile()
    {
        ProfileRow? row = GetSelectedRow();
        TextBlock? validation = this.FindControl<TextBlock>("ValidationText");
        TextBox? nameInput = this.FindControl<TextBox>("ProfileNameInput");
        TextBox? hostInput = this.FindControl<TextBox>("ProfileHostInput");
        TextBox? portInput = this.FindControl<TextBox>("ProfilePortInput");

        if (validation is not null)
            validation.Text = string.Empty;

        if (row is null)
        {
            if (validation is not null)
                validation.Text = "Kein Profil ausgewählt.";
            return false;
        }

        string name = (nameInput?.Text ?? string.Empty).Trim();
        string host = (hostInput?.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            if (validation is not null)
                validation.Text = "Profilname fehlt.";
            nameInput?.Focus();
            return false;
        }

        if (host.Length == 0)
        {
            if (validation is not null)
                validation.Text = "Core-IP / Host fehlt.";
            hostInput?.Focus();
            return false;
        }

        if (!int.TryParse((portInput?.Text ?? string.Empty).Trim(), out int port)
            || port <= 0
            || port > 65535)
        {
            if (validation is not null)
                validation.Text = "XML-Port ist ungültig.";
            portInput?.Focus();
            return false;
        }

        string hostForUri = host.Contains(":", StringComparison.Ordinal)
            && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]"
            : host;
        string endpoint;
        try
        {
            endpoint = CoreProfileStore.NormalizeEndpoint($"http://{hostForUri}:{port}/");
        }
        catch (Exception ex)
        {
            if (validation is not null)
                validation.Text = "Core-Endpunkt ist ungültig: " + ex.Message;
            return false;
        }

        bool duplicateEndpoint = _profiles.Any(other =>
            !ReferenceEquals(other, row.Profile)
            && string.Equals(
                CoreProfileStore.TryNormalizeEndpoint(other.Endpoint),
                endpoint,
                StringComparison.OrdinalIgnoreCase));
        if (duplicateEndpoint)
        {
            if (validation is not null)
                validation.Text = "Für diesen Host und XML-Port existiert bereits ein Profil.";
            return false;
        }

        row.Profile.Name = name;
        row.Profile.Endpoint = endpoint;
        UpdateSelectedProfileStatus(row.Profile);
        return true;
    }

    private void SaveAndCloseButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (GetSelectedRow() is not null && !ApplyEditorToSelectedProfile())
            return;

        ResultProfiles = _profiles.Select(Clone).ToList();
        ResultDefaultProfileId = _defaultProfileId;
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e)
        => Close(false);

    private void RefreshProfileRows(string? selectedProfileId)
    {
        ListBox? list = this.FindControl<ListBox>("ProfilesList");
        if (list is null)
            return;

        List<ProfileRow> rows = _profiles
            .Select(profile => new ProfileRow(
                profile,
                string.Equals(profile.Id, _activeProfileId, StringComparison.OrdinalIgnoreCase),
                string.Equals(profile.Id, _defaultProfileId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        list.ItemsSource = rows;
        list.SelectedItem = rows.FirstOrDefault(row =>
                string.Equals(row.Profile.Id, selectedProfileId, StringComparison.OrdinalIgnoreCase))
            ?? rows.FirstOrDefault();

        UpdateActiveProfileHint();
    }

    private ProfileRow? GetSelectedRow()
        => this.FindControl<ListBox>("ProfilesList")?.SelectedItem as ProfileRow;

    private void UpdateActiveProfileHint()
    {
        TextBlock? hint = this.FindControl<TextBlock>("ActiveProfileHintText");
        if (hint is null)
            return;

        CoreProfileEntry? active = _profiles.FirstOrDefault(profile =>
            string.Equals(profile.Id, _activeProfileId, StringComparison.OrdinalIgnoreCase));
        hint.Text = active is not null
            ? $"Aktiv verbunden: {active.Name} · {active.Endpoint}"
            : _activeEndpoint.Length > 0
                ? $"Aktiv verbunden: nicht eindeutig einem gespeicherten Profil zugeordnet · {_activeEndpoint}"
                : "Aktiv verbunden: keiner (offline).";
    }

    private void UpdateSelectedProfileStatus(CoreProfileEntry? profile)
    {
        TextBlock? status = this.FindControl<TextBlock>("SelectedProfileStatusText");
        if (status is null)
            return;

        if (profile is null)
        {
            status.Text = string.Empty;
            return;
        }

        bool isActive = string.Equals(
            profile.Id,
            _activeProfileId,
            StringComparison.OrdinalIgnoreCase);
        bool isDefault = string.Equals(
            profile.Id,
            _defaultProfileId,
            StringComparison.OrdinalIgnoreCase);

        status.Text = isActive && isDefault
            ? "Dieses Profil ist aktuell verbunden und Standard beim Start."
            : isActive
                ? "Dieses Profil ist aktuell verbunden."
                : isDefault
                    ? "Dieses Profil ist Standard beim nächsten Start."
                    : "Gespeichertes Core-Profil.";
    }

    private static CoreProfileEntry Clone(CoreProfileEntry profile)
        => new()
        {
            Id = profile.Id,
            Name = profile.Name,
            Endpoint = profile.Endpoint
        };

    private sealed class ProfileRow
    {
        public ProfileRow(CoreProfileEntry profile, bool isActive, bool isDefault)
        {
            Profile = profile;
            CoreEndpoint endpoint = CoreEndpoint.Parse(profile.Endpoint);
            Name = profile.Name;
            Host = endpoint.Host;
            XmlPort = endpoint.BaseUri.Port.ToString();
            StatusMark = isActive && isDefault
                ? "● ★"
                : isActive
                    ? "●"
                    : isDefault
                        ? "★"
                        : string.Empty;
        }

        public CoreProfileEntry Profile { get; }
        public string StatusMark { get; }
        public string Name { get; }
        public string Host { get; }
        public string XmlPort { get; }
    }
}
