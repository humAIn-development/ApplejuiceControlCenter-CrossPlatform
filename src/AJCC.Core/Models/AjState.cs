using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace AJCC.Core.Models;

public sealed class AjState
{
    public ObservableCollection<AjDownload> Downloads { get; } = new();
    public ObservableCollection<AjUpload> Uploads { get; } = new();
    public ObservableCollection<AjUserSource> Users { get; } = new();
    public ObservableCollection<AjServer> Servers { get; } = new();
    public ObservableCollection<AjShareFile> Shares { get; } = new();
    public Dictionary<long, string> ShareFilenameById { get; } = new();
    public ObservableCollection<AjSearch> Searches { get; } = new();

    public AjNetworkInfo NetworkInfo { get; set; } = new();
    public AjInformation Information { get; set; } = new();
    public AjSettings Settings { get; set; } = new();
    public long LastTimestamp { get; set; }
    public string SessionId { get; set; } = "";
}
