using AJCC.Core.Helpers;

namespace AJCC.Core.Models;

public sealed class AjLinkInfo
{
    public string RawLink { get; set; } = "";
    public string LinkType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Checksum { get; set; } = "";
    public long Size { get; set; }
    public string SizeText => DisplayFormatHelper.Bytes(Size);
    public string SourceRaw { get; set; } = "";
    public string SourceIp { get; set; } = "";
    public string SourcePort { get; set; } = "";
    public string SourceHost { get; set; } = "";
    public string SourceXmlPort { get; set; } = "";
    public bool HasSource => !string.IsNullOrWhiteSpace(SourceRaw);
    public bool HasTechnicalSource => !string.IsNullOrWhiteSpace(SourceIp)
        && !string.IsNullOrWhiteSpace(SourcePort)
        && !string.IsNullOrWhiteSpace(SourceHost)
        && !string.IsNullOrWhiteSpace(SourceXmlPort);
    public string SourceText => HasSource
        ? (HasTechnicalSource ? $"{SourceIp}:{SourcePort} / {SourceHost}:{SourceXmlPort}" : SourceRaw)
        : "keine Quelle im Link";
    public bool IsValid { get; set; }
    public string Error { get; set; } = "";
}
