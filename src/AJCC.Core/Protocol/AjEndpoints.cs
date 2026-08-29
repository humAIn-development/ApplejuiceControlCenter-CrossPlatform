namespace AJCC.Core.Protocol;

public static class AjEndpoints
{
    public const string Directory = "/xml/directory.xml";
    public const string DownloadPartList = "/xml/downloadpartlist.xml";
    public const string GetObject = "/xml/getobject.xml";
    public const string GetSession = "/xml/getsession.xml";
    public const string Information = "/xml/information.xml";
    public const string Modified = "/xml/modified.xml";
    public const string Settings = "/xml/settings.xml";
    public const string Share = "/xml/share.xml";
    public const string UserPartList = "/xml/userpartlist.xml";

    public const string CancelDownload = "/function/canceldownload";
    public const string CancelSearch = "/function/cancelsearch";
    public const string CleanDownloadList = "/function/cleandownloadlist";
    public const string ExitCore = "/function/exitcore";
    public const string PauseDownload = "/function/pausedownload";
    public const string ProcessLink = "/function/processlink";
    public const string RemoveServer = "/function/removeserver";
    public const string RenameDownload = "/function/renamedownload";
    public const string ResumeDownload = "/function/resumedownload";
    public const string Search = "/function/search";
    public const string ServerLogin = "/function/serverlogin";
    public const string SetPassword = "/function/setpassword";
    public const string SetPowerDownload = "/function/setpowerdownload";
    public const string SetPriority = "/function/setpriority";
    public const string SetSettings = "/function/setsettings";
    public const string SetTargetDir = "/function/settargetdir";
}
