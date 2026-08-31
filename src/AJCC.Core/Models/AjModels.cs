using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using AJCC.Core.Helpers;

namespace AJCC.Core.Models;

public sealed class AjSettings
{
    public string Nick { get; set; } = "";
    public int Port { get; set; }
    public int XmlPort { get; set; }
    public long MaxUpload { get; set; }
    public long MaxDownload { get; set; }
    public int MaxConnections { get; set; }
    public bool AutoConnect { get; set; }
    public int MaxSourcesPerFile { get; set; }
    public int SpeedPerSlot { get; set; }
    public string IncomingDirectory { get; set; } = "";
    public string TemporaryDirectory { get; set; } = "";
    public int MaxNewConnectionsPerTurn { get; set; }
    public ObservableCollection<AjShareDirectory> SharedDirectories { get; } = new();
}

public sealed class AjShareDirectory
{
    public string Name { get; set; } = "";
    public string ShareMode { get; set; } = "";
    public string Icon => ShareMode == "subdirectory" ? "🍎" : "●";
    public string ShareModeText => ShareMode == "subdirectory" ? "Freigegeben inkl. Unterordner" : "Freigegeben";
}

public sealed class AjShareFile : INotifyPropertyChanged
{
    private int _priority;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get; set; }
    public string Filename { get; set; } = "";
    public long Size { get; set; }
    public string SizeText => DisplayFormatHelper.Bytes(Size);
    public string FileType => GetExtension(Filename);
    public string DisplayFilename => GetFileNameOnly(Filename);
    public string DirectoryPath => GetDirectoryOnly(Filename);
    public string Checksum { get; set; } = "";
    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority == value)
                return;

            _priority = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Priority)));
        }
    }
    public long LastAsked { get; set; }
    public long AskCount { get; set; }
    public long SearchCount { get; set; }

    private static string GetFileNameOnly(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        int slash = value.LastIndexOf('/');
        int backslash = value.LastIndexOf('\\');
        int index = Math.Max(slash, backslash);
        return index >= 0 && index + 1 < value.Length ? value[(index + 1)..] : value;
    }

    private static string GetExtension(string value)
    {
        string filename = GetFileNameOnly(value);
        int index = filename.LastIndexOf('.');
        return index >= 0 && index + 1 < filename.Length ? filename[index..].ToLowerInvariant() : "";
    }

    private static string GetDirectoryOnly(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        int slash = value.LastIndexOf('/');
        int backslash = value.LastIndexOf('\\');
        int index = Math.Max(slash, backslash);
        return index > 0 ? value[..index] : "";
    }
}

public sealed class AjDownload : INotifyPropertyChanged
{
    private const int AppleSegmentTotal = 20;
    private long _id;
    private long _shareId;
    private string _hash = "";
    private long _size;
    private int _status;
    private string _filename = "";
    private string _targetDirectory = "";
    private int _powerDownload;
    private int _sourceCount;
    private int _activeSourceCount;
    private long _downloadSpeed;
    private bool _isRecentlyImported;
    private long _ready;
    private int _temporaryFileNumber;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get => _id; set => SetField(ref _id, value); }
    public long ShareId { get => _shareId; set => SetField(ref _shareId, value); }
    public string Hash { get => _hash; set => SetField(ref _hash, value ?? ""); }
    public long Size
    {
        get => _size;
        set
        {
            if (SetField(ref _size, value))
                NotifyProgressProperties();
        }
    }

    public int Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(DownloadStatusSortKey));
                OnPropertyChanged(nameof(StatusVisualRole));
                OnPropertyChanged(nameof(HasStatusVisualColor));
                OnPropertyChanged(nameof(IsCompletedStatusVisual));
                OnPropertyChanged(nameof(IsAbortedStatusVisual));
                OnPropertyChanged(nameof(IsPausedStatusVisual));
                OnPropertyChanged(nameof(PowerDownloadText));
                NotifyProgressProperties();
            }
        }
    }

    public string StatusText => Status == 0 && DownloadSpeed > 0 ? "aktiv" : AjStatusText.DownloadStatus(Status);
    public int DownloadStatusSortKey => Status switch
    {
        12 or 14 => 10,
        18 => 20,
        0 when DownloadSpeed > 0 => 30,
        0 => 40,
        15 or 17 => 50,
        16 => 60,
        1 or 13 => 70,
        _ => 90
    };

    public DownloadStatusVisualRole StatusVisualRole => DownloadStatusVisualSemantics.GetRole(Status);
    public bool HasStatusVisualColor => StatusVisualRole != DownloadStatusVisualRole.Neutral;
    public bool IsCompletedStatusVisual => StatusVisualRole == DownloadStatusVisualRole.Completed;
    public bool IsAbortedStatusVisual => StatusVisualRole == DownloadStatusVisualRole.Aborted;
    public bool IsPausedStatusVisual => StatusVisualRole == DownloadStatusVisualRole.Paused;

    public string Filename
    {
        get => _filename;
        set
        {
            if (SetField(ref _filename, value ?? ""))
            {
                OnPropertyChanged(nameof(DisplayFilename));
                OnPropertyChanged(nameof(FileType));
            }
        }
    }

    public string DisplayFilename => GetFileNameOnly(Filename);
    public string FileType => GetExtension(Filename);

    public string TargetDirectory
    {
        get => _targetDirectory;
        set
        {
            if (SetField(ref _targetDirectory, value ?? ""))
                OnPropertyChanged(nameof(TargetDirectoryText));
        }
    }

    public string TargetDirectoryText => string.IsNullOrWhiteSpace(TargetDirectory) ? "Incoming" : TargetDirectory;

    public int PowerDownload
    {
        get => _powerDownload;
        set
        {
            if (SetField(ref _powerDownload, value))
                OnPropertyChanged(nameof(PowerDownloadText));
        }
    }

    public string PowerDownloadText => Status is 12 or 14
        ? string.Empty
        : PowerDownload <= 0 ? "aus" : FormatPowerDownloadFactor(PowerDownload);

    public static double PowerDownloadRawToFactor(int rawValue)
        => rawValue <= 0 ? 0 : (rawValue + 10) / 10.0;

    public static int PowerDownloadFactorToRaw(int factor)
        => PowerDownloadFactorToRaw((double)factor);

    public static int PowerDownloadFactorToRaw(double factor)
    {
        if (factor <= 1.0)
            return 0;

        return PowerDownloadFactorHelper.ToRaw(factor);
    }

    private static string FormatPowerDownloadFactor(int rawValue)
    {
        double factor = PowerDownloadRawToFactor(rawValue);
        return Math.Abs(factor - Math.Round(factor)) < 0.001
            ? ((int)Math.Round(factor)).ToString()
            : factor.ToString("0.0");
    }

    public int SourceCount
    {
        get => _sourceCount;
        set
        {
            if (SetField(ref _sourceCount, value))
                OnPropertyChanged(nameof(SourceCountText));
        }
    }

    public int ActiveSourceCount
    {
        get => _activeSourceCount;
        set
        {
            if (SetField(ref _activeSourceCount, value))
                OnPropertyChanged(nameof(SourceCountText));
        }
    }

    public string SourceCountText => $"{ActiveSourceCount:N0} / {SourceCount:N0}";

    public long DownloadSpeed
    {
        get => _downloadSpeed;
        set
        {
            if (SetField(ref _downloadSpeed, Math.Max(0, value)))
            {
                OnPropertyChanged(nameof(DownloadSpeedText));
                OnPropertyChanged(nameof(RemainingTimeText));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(DownloadStatusSortKey));
            }
        }
    }

    public string DownloadSpeedText => DisplayFormatHelper.BytesPerSecond(DownloadSpeed);
    public string RemainingTimeText
    {
        get
        {
            if (DownloadSpeed <= 0 || Remaining <= 0)
                return "-";

            double seconds = Remaining / Math.Max(1.0, DownloadSpeed);
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds <= 0)
                return "-";

            TimeSpan time = TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds));
            string timeText = time.ToString(@"hh\:mm\:ss");
            return time.TotalDays >= 1 ? $"{(int)time.TotalDays}d {timeText}" : timeText;
        }
    }

    public bool IsRecentlyImported { get => _isRecentlyImported; set => SetField(ref _isRecentlyImported, value); }

    public long Ready
    {
        get => _ready;
        set
        {
            if (SetField(ref _ready, value))
                NotifyProgressProperties();
        }
    }

    public bool IsCompleteOrFinalizing => Status is 12 or 14;
    public long EffectiveReady => Size > 0 && IsCompleteOrFinalizing ? Size : Ready;
    public long Remaining => Size <= 0 ? 0 : Math.Max(0, Size - EffectiveReady);
    public int TemporaryFileNumber { get => _temporaryFileNumber; set => SetField(ref _temporaryFileNumber, value); }
    public double ProgressPercent => Size <= 0 ? 0 : EffectiveReady * 100.0 / Size;
    public string ProgressText => Size <= 0 ? "0,0 %" : ProgressPercent.ToString("N1") + " %";
    public string ReadyText => DisplayFormatHelper.Bytes(EffectiveReady);
    public string RemainingText => DisplayFormatHelper.Bytes(Remaining);
    public string SizeText => DisplayFormatHelper.Bytes(Size);
    public IReadOnlyList<AjProgressSegment> AppleProgressSegments => BuildAppleProgressSegments();

    private void NotifyProgressProperties()
    {
        OnPropertyChanged(nameof(IsCompleteOrFinalizing));
        OnPropertyChanged(nameof(EffectiveReady));
        OnPropertyChanged(nameof(Remaining));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(ReadyText));
        OnPropertyChanged(nameof(RemainingText));
        OnPropertyChanged(nameof(RemainingTimeText));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(AppleProgressSegments));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private List<AjProgressSegment> BuildAppleProgressSegments()
    {
        int filledCount = (int)Math.Round(ProgressPercent / 100.0 * AppleSegmentTotal, MidpointRounding.AwayFromZero);
        filledCount = Math.Clamp(filledCount, 0, AppleSegmentTotal);

        List<AjProgressSegment> segments = new(AppleSegmentTotal);
        for (int index = 0; index < AppleSegmentTotal; index++)
            segments.Add(new AjProgressSegment { IsFilled = index < filledCount });

        return segments;
    }

    private static string GetFileNameOnly(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        int slash = value.LastIndexOf('/');
        int backslash = value.LastIndexOf('\\');
        int index = Math.Max(slash, backslash);
        return index >= 0 && index + 1 < value.Length ? value[(index + 1)..] : value;
    }

    private static string GetExtension(string value)
    {
        string filename = GetFileNameOnly(value);
        int index = filename.LastIndexOf('.');
        return index >= 0 && index + 1 < filename.Length ? filename[index..].ToLowerInvariant() : "";
    }
}

public sealed class AjProgressSegment
{
    public bool IsFilled { get; set; }
}

public sealed class AjUpload
{
    public long Id { get; set; }
    public long ShareId { get; set; }
    public string Version { get; set; } = "";
    public int OperatingSystem { get; set; }
    public int Status { get; set; }
    public string StatusText => AjStatusText.UploadStatus(Status);
    public int DirectState { get; set; }
    public int Priority { get; set; }
    public string Nick { get; set; } = "";
    public long UploadFrom { get; set; }
    public long UploadTo { get; set; }
    public long ActualUploadPosition { get; set; }
    public long Speed { get; set; }
    public IReadOnlyList<long> SpeedHistory { get; set; } = Array.Empty<long>();
    public long SpeedActivitySortKey => Speed;
    public string SpeedText => DisplayFormatHelper.BytesPerSecond(Speed);
    public long LastConnection { get; set; }
    public double Loaded { get; set; }
    public string Filename { get; set; } = "";
    public string DisplayFilename
    {
        get
        {
            string filename = GetFileNameOnly(Filename);
            return IsTechnicalUploadFilename(filename) ? "Name wird geladen" : filename;
        }
    }

    public long UploadSize => Math.Max(0, UploadTo - UploadFrom);
    public long UploadedBytes => UploadSize <= 0 ? 0 : Math.Clamp(ActualUploadPosition - UploadFrom, 0, UploadSize);
    public string UploadedText => UploadSize > 0 ? DisplayFormatHelper.Bytes(UploadedBytes) : "-";
    public double LoadedPercent
    {
        get
        {
            double raw = Loaded;
            if (double.IsNaN(raw) || double.IsInfinity(raw) || raw <= 0.0)
                return 0.0;

            double percent = raw <= 1.0 ? raw * 100.0 : raw;
            return Math.Clamp(percent, 0.0, 100.0);
        }
    }
    public double ProgressPercent => UploadSize <= 0 ? LoadedPercent : Math.Clamp((double)UploadedBytes / UploadSize * 100.0, 0.0, 100.0);
    public string ProgressPercentText => UploadSize > 0 || LoadedPercent > 0 ? $"{ProgressPercent:0.0} %" : "-";
    public string WatermarkText => LoadedPercent > 0 ? $"{LoadedPercent:0.0} %" : ProgressPercentText;
    public string ClientText => string.IsNullOrWhiteSpace(Version) ? "-" : Version;
    public bool IsActiveTransfer
    {
        get
        {
            if (Status != 1)
                return false;

            if (Speed > 0)
                return true;

            return UploadTo > UploadFrom && ActualUploadPosition < UploadTo;
        }
    }
    public string LastConnectionText
    {
        get
        {
            if (LastConnection <= 0)
                return "-";

            try
            {
                if (LastConnection > 10_000_000_000L)
                    return DateTimeOffset.FromUnixTimeMilliseconds(LastConnection).LocalDateTime.ToString("dd.MM.yyyy HH:mm");

                if (LastConnection > 1_000_000_000L)
                    return DateTimeOffset.FromUnixTimeSeconds(LastConnection).LocalDateTime.ToString("dd.MM.yyyy HH:mm");

                TimeSpan age = TimeSpan.FromSeconds(LastConnection);
                if (age.TotalDays >= 1)
                    return $"vor {(int)age.TotalDays} d";
                if (age.TotalHours >= 1)
                    return $"vor {(int)age.TotalHours} h";
                if (age.TotalMinutes >= 1)
                    return $"vor {(int)age.TotalMinutes} min";

                return "gerade eben";
            }
            catch
            {
                return "-";
            }
        }
    }

    private static string GetFileNameOnly(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        int slash = value.LastIndexOf('/');
        int backslash = value.LastIndexOf('\\');
        int index = Math.Max(slash, backslash);
        return index >= 0 && index + 1 < value.Length ? value[(index + 1)..] : value;
    }

    private static bool IsTechnicalUploadFilename(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        string name = value.Trim();
        if (name.StartsWith("ShareID ", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.EndsWith(".data", StringComparison.OrdinalIgnoreCase)
            && name[..^5].All(char.IsDigit);
    }
}

public sealed class AjUserSource
{
    public long Id { get; set; }
    public int Status { get; set; }
    public string StatusText => AjStatusText.UserStatus(Status);
    public int DirectState { get; set; }
    public long DownloadFrom { get; set; }
    public long DownloadTo { get; set; }
    public long ActualDownloadPosition { get; set; }
    public long Speed { get; set; }
    public string SpeedText => DisplayFormatHelper.BytesPerSecond(Speed);
    public long Size => Math.Max(0, DownloadTo - DownloadFrom);
    public string SizeText => Size > 0 ? DisplayFormatHelper.Bytes(Size) : "-";
    public long AlreadyLoaded => Size <= 0 ? 0 : Math.Clamp(ActualDownloadPosition - DownloadFrom, 0, Size);
    public string AlreadyLoadedText => Size > 0 ? DisplayFormatHelper.Bytes(AlreadyLoaded) : "-";
    public long Remaining => Size <= 0 ? 0 : Math.Max(0, DownloadTo - ActualDownloadPosition);
    public string RemainingText => Size > 0 ? DisplayFormatHelper.Bytes(Remaining) : "-";
    public double ProgressPercent => Size <= 0 ? 0 : Math.Clamp((double)AlreadyLoaded / Size * 100.0, 0.0, 100.0);
    public string ProgressPercentText => Size > 0 ? $"{ProgressPercent:0.0} %" : "-";
    public string Version { get; set; } = "";
    public int OperatingSystem { get; set; }
    public int QueuePosition { get; set; }
    public bool IsQueuePositionMeaningful => QueuePosition > 0 && QueuePosition < 999;
    public bool IsTransferring => Status == 7 || Speed > 0;
    public int QueueSortKey => IsTransferring ? 0 : IsQueuePositionMeaningful ? QueuePosition : int.MaxValue;
    public string QueuePositionText => IsTransferring ? "jetzt" : IsQueuePositionMeaningful ? QueuePosition.ToString() : "-";
    public string Nickname { get; set; } = "";
    public string NicknameText => string.IsNullOrWhiteSpace(Nickname) || Nickname.Trim() == "?" ? "unbekannt (?)" : Nickname;
    public int PowerDownload { get; set; }
    public string Filename { get; set; } = "";
    public long DownloadId { get; set; }
    public int Source { get; set; }
    public string DirectStateText => DirectState > 0 ? "direkt" : "indirekt";
    public string SourceText => Source switch
    {
        1 => "AJFSP",
        2 => "Client",
        3 => "Upload",
        4 => "Start",
        5 => "Textsuche",
        6 => "Serversuche",
        _ => Source <= 0 ? "unbekannt" : Source.ToString()
    };
}

public sealed class AjServer : INotifyPropertyChanged
{
    private long _id;
    private string _name = "";
    private string _host = "";
    private int _port;
    private long _lastSeen;
    private int _connectionTry;
    private bool? _reachabilityProbeSucceeded;
    private long _reachabilityProbeUtc;
    private bool _reachabilityProbeRunning;
    private string _serverStatusKind = "unknown";
    private string _serverStatusText = "Unbekannt";
    private string _connectionDurationText = "-";

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get => _id; set => SetField(ref _id, value); }
    public string Name { get => _name; set => SetField(ref _name, value ?? ""); }
    public string Host
    {
        get => _host;
        set
        {
            if (SetField(ref _host, value ?? ""))
                OnPropertyChanged(nameof(HostDisplay));
        }
    }
    public string HostDisplay => SecurityHelper.MaskIpAddressesForDisplay(Host);
    public int Port { get => _port; set => SetField(ref _port, value); }
    public long LastSeen { get => _lastSeen; set => SetField(ref _lastSeen, value); }
    public int ConnectionTry { get => _connectionTry; set => SetField(ref _connectionTry, value); }
    public bool? ReachabilityProbeSucceeded { get => _reachabilityProbeSucceeded; set => SetField(ref _reachabilityProbeSucceeded, value); }
    public long ReachabilityProbeUtc { get => _reachabilityProbeUtc; set => SetField(ref _reachabilityProbeUtc, value); }
    public bool ReachabilityProbeRunning { get => _reachabilityProbeRunning; set => SetField(ref _reachabilityProbeRunning, value); }
    public string ServerStatusKind { get => _serverStatusKind; set => SetField(ref _serverStatusKind, value ?? "unknown"); }
    public string ServerStatusText { get => _serverStatusText; set => SetField(ref _serverStatusText, value ?? ""); }
    public string ConnectionDurationText { get => _connectionDurationText; set => SetField(ref _connectionDurationText, value ?? "-"); }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AjNetworkInfo
{
    public long Users { get; set; }
    public long Files { get; set; }
    public long FileSizeMb { get; set; }
    public bool Firewalled { get; set; }
    public string Ip { get; set; } = "";
    public long TryConnectToServer { get; set; }
    public long ConnectedWithServerId { get; set; }
    public long ConnectedSince { get; set; }
    public string WelcomeMessage { get; set; } = "";
}

public sealed class AjInformation
{
    public long Id { get; set; }
    public long SessionUpload { get; set; }
    public string SessionUploadText => DisplayFormatHelper.Bytes(SessionUpload);
    public long SessionDownload { get; set; }
    public string SessionDownloadText => DisplayFormatHelper.Bytes(SessionDownload);
    public long Credits { get; set; }
    public string CreditsText => DisplayFormatHelper.Bytes(Credits);
    public long UploadSpeed { get; set; }
    public string UploadSpeedText => DisplayFormatHelper.BytesPerSecond(UploadSpeed);
    public long DownloadSpeed { get; set; }
    public string DownloadSpeedText => DisplayFormatHelper.BytesPerSecond(DownloadSpeed);
    public int OpenConnections { get; set; }
    public int MaxUploadPositions { get; set; }
}

public sealed class AjSearch : INotifyPropertyChanged
{
    private long _id;
    private string _searchText = "";
    private long _openSearches;
    private long _foundFiles;
    private long _sumSearches;
    private bool _running;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get => _id; set => SetField(ref _id, value); }
    public string SearchText { get => _searchText; set => SetField(ref _searchText, value ?? ""); }
    public long OpenSearches { get => _openSearches; set => SetField(ref _openSearches, value); }
    public long FoundFiles { get => _foundFiles; set => SetField(ref _foundFiles, value); }
    public long SumSearches { get => _sumSearches; set => SetField(ref _sumSearches, value); }
    public bool Running
    {
        get => _running;
        set
        {
            if (SetField(ref _running, value))
                OnPropertyChanged(nameof(RunningText));
        }
    }

    public ObservableCollection<AjSearchEntry> Entries { get; } = new();
    public int ResultCount => Entries.Count;
    public string RunningText => Running ? "Ja" : "Nein";
    public void NotifyEntriesChanged() => OnPropertyChanged(nameof(ResultCount));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AjSearchEntry : INotifyPropertyChanged
{
    private bool _isExistingDownload;

    public event PropertyChangedEventHandler? PropertyChanged;

    public long Id { get; set; }
    public long SearchId { get; set; }
    public string Checksum { get; set; } = "";
    public long Size { get; set; }
    public string SizeText => Size <= 0 ? "unbekannt" : DisplayFormatHelper.Bytes(Size);
    public string Filename { get; set; } = "";
    public string SourceText { get; set; } = "";
    public bool HasSourceText => !string.IsNullOrWhiteSpace(SourceText);
    public int FilenameUsers { get; set; }

    public bool IsExistingDownload
    {
        get => _isExistingDownload;
        set
        {
            if (_isExistingDownload == value)
                return;

            _isExistingDownload = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanImportAsDownload));
            OnPropertyChanged(nameof(DownloadActionText));
            OnPropertyChanged(nameof(ExistingDownloadToolTip));
        }
    }

    public bool CanImportAsDownload => !IsExistingDownload;
    public string DownloadActionText => IsExistingDownload
        ? "ist bereits in der Downloadliste"
        : "Als Download übernehmen";
    public string ExistingDownloadToolTip => IsExistingDownload
        ? "Diese Datei ist bereits in der Downloadliste."
        : string.Empty;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (!string.IsNullOrWhiteSpace(propertyName))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class AjDirectoryEntry
{
    public string Name { get; set; } = "";
    public bool IsFileSystem { get; set; }
    public int Type { get; set; }
    public string Path { get; set; } = "";
}

public sealed class AjPart
{
    public long FromPosition { get; set; }
    public int Type { get; set; }
}

public sealed class AjDirectoryListResult
{
    public string Separator { get; set; } = "\\";
    public List<AjDirectoryEntry> Directories { get; } = new();
}

public static class AjStatusText
{
    public static string DownloadStatus(int status) => status switch
    {
        0 => "Suchen/Laden",
        1 => "Plattenfehler",
        12 => "Fertigstellen",
        13 => "Fehler beim Fertigstellen",
        14 => "Fertig",
        15 => "Abbrechen",
        16 => ".data wird erstellt",
        17 => "Abgebrochen",
        18 => "Pausiert",
        _ => "Unbekannt"
    };

    public static string UploadStatus(int status) => status switch
    {
        1 => "Übertragung",
        2 => "Warteschlange",
        _ => "Unbekannt"
    };

    public static string UserStatus(int status) => status switch
    {
        1 => "Ungefragt",
        2 => "Verbinden",
        3 => "Version zu alt",
        4 => "Datei nicht offen",
        5 => "Warteschlange",
        6 => "Keine Parts",
        7 => "Übertragung",
        8 => "Plattenplatz fehlt",
        9 => "Fertiggestellt",
        11 => "Keine Verbindung",
        12 => "Indirekt verbinden",
        13 => "Pausiert",
        14 => "Queue voll",
        15 => "Eigenes Limit",
        16 => "Indirect abgewiesen",
        _ => "Unbekannt"
    };
}
