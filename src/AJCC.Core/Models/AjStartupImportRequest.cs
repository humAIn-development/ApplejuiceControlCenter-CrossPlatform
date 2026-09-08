namespace AJCC.Core.Models;

public sealed class AjStartupImportRequest
{
    public List<string> Links { get; } = new();
    public List<string> LinkListFiles { get; } = new();
    public bool HasItems => Links.Count > 0 || LinkListFiles.Count > 0;
}
