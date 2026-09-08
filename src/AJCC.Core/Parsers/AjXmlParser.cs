using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using AJCC.Core.Helpers;
using AJCC.Core.Models;

namespace AJCC.Core.Parsers;

public static class AjXmlParser
{
    public static AjSettings ParseSettings(string xml)
    {
        XElement root = XElement.Parse(xml);
        AjSettings settings = new()
        {
            Nick = root.ElementText("nick"),
            Port = root.ElementInt("port"),
            XmlPort = root.ElementInt("xmlport"),
            MaxUpload = root.ElementLong("maxupload"),
            MaxDownload = root.ElementLong("maxdownload"),
            SpeedPerSlot = root.ElementInt("speedperslot"),
            MaxConnections = root.ElementInt("maxconnections"),
            AutoConnect = root.ElementText("autoconnect").Equals("true", StringComparison.OrdinalIgnoreCase),
            MaxSourcesPerFile = root.ElementInt("maxsourcesperfile"),
            IncomingDirectory = root.ElementText("incomingdirectory"),
            TemporaryDirectory = root.ElementText("temporarydirectory"),
            MaxNewConnectionsPerTurn = root.ElementInt("maxnewconnectionsperturn")
        };

        XElement? share = root.Element("share");
        if (share is not null)
        {
            foreach (XElement dir in share.Elements("directory"))
            {
                settings.SharedDirectories.Add(new AjShareDirectory
                {
                    Name = dir.Attr("name"),
                    ShareMode = dir.Attr("sharemode")
                });
            }
        }

        return settings;
    }

    public static List<AjShareFile> ParseShares(string xml)
    {
        XElement root = XElement.Parse(xml);
        return root
            .DescendantsAndSelf()
            .Where(e => e.Name.LocalName.Equals("share", StringComparison.OrdinalIgnoreCase)
                && e.Attribute("filename") is not null)
            .Select(ParseShare)
            .ToList();
    }

    public static AjNetworkInfo ParseInformationXml(string xml)
    {
        XElement root = XElement.Parse(xml);
        XElement? networkInfo = root.Descendants("networkinfo").FirstOrDefault();
        return networkInfo is null ? new AjNetworkInfo() : ParseNetworkInfo(networkInfo);
    }

    public static string ParseCoreVersion(string xml)
    {
        XElement root = XElement.Parse(xml);
        return root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName.Equals("version", StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim() ?? string.Empty;
    }

    public static string ParseSessionId(string xml)
    {
        XElement root = XElement.Parse(xml);
        return root.Descendants("session").FirstOrDefault()?.Attr("id") ?? "";
    }

    public static ModifiedParseResult ParseModified(string xml)
    {
        XElement root = XElement.Parse(xml);
        ModifiedParseResult result = new()
        {
            CoreTimestamp = ParseModifiedTimestamp(root)
        };

        XElement? ids = root.Element("ids");
        if (ids is not null)
        {
            foreach (XElement child in ids.Elements())
            {
                long id = child.LongAttr("id");
                if (id <= 0)
                    continue;

                switch (child.Name.LocalName.ToLowerInvariant())
                {
                    case "serverid":
                        result.ActiveServerIds.Add(id);
                        break;
                    case "uploadid":
                        result.ActiveUploadIds.Add(id);
                        break;
                    case "downloadid":
                        result.ActiveDownloadIds.Add(id);
                        break;
                }
            }
        }

        XElement? removed = root.Element("removed");
        if (removed is not null)
        {
            foreach (XElement child in removed.Elements())
            {
                long id = child.LongAttr("id");
                if (id > 0)
                    result.RemovedIds.Add(id);
            }
        }

        foreach (XElement element in root.Descendants())
        {
            switch (element.Name.LocalName.ToLowerInvariant())
            {
                case "download":
                    result.Downloads.Add(ParseDownload(element));
                    break;
                case "upload":
                    result.Uploads.Add(ParseUpload(element));
                    break;
                case "user":
                    result.Users.Add(ParseUser(element));
                    break;
                case "server":
                    result.Servers.Add(ParseServer(element));
                    break;
                case "networkinfo":
                    result.NetworkInfo = ParseNetworkInfo(element);
                    break;
                case "information":
                    result.Information = ParseInformation(element);
                    break;
                case "search":
                    result.Searches.Add(ParseSearch(element));
                    break;
                case "searchentry":
                    result.SearchEntries.Add(ParseSearchEntry(element));
                    break;
            }
        }

        return result;
    }

    public static AjDownload ParseDownload(XElement e) => new()
    {
        Id = e.LongAttr("id"),
        ShareId = e.LongAttr("shareid"),
        Hash = e.Attr("hash"),
        Size = e.LongAttr("size"),
        Status = e.IntAttr("status"),
        Filename = e.Attr("filename"),
        TargetDirectory = e.Attr("targetdirectory"),
        PowerDownload = e.IntAttr("powerdownload"),
        Ready = e.LongAttr("ready"),
        TemporaryFileNumber = e.IntAttr("temporaryfilenumber")
    };

    public static AjUpload ParseUpload(XElement e) => new()
    {
        Id = e.LongAttr("id"),
        ShareId = e.LongAttr("shareid"),
        Version = e.Attr("version"),
        OperatingSystem = e.IntAttr("operatingsystem"),
        Status = e.IntAttr("status"),
        DirectState = e.IntAttr("directstate"),
        Priority = e.IntAttr("priority"),
        Nick = e.Attr("nick"),
        UploadFrom = e.LongAttr("uploadfrom"),
        UploadTo = e.LongAttr("uploadto"),
        ActualUploadPosition = e.LongAttr("actualuploadposition"),
        Speed = e.LongAttr("speed"),
        LastConnection = e.LongAttr("lastconnection"),
        Loaded = double.TryParse(e.Attr("loaded"), NumberStyles.Float, CultureInfo.InvariantCulture, out double loaded) ? loaded : 0,
        Filename = e.Attr("filename")
    };

    public static AjUserSource ParseUser(XElement e) => new()
    {
        Id = e.LongAttr("id"),
        Status = e.IntAttr("status"),
        DirectState = e.IntAttr("directstate"),
        DownloadFrom = e.LongAttr("downloadfrom"),
        DownloadTo = e.LongAttr("downloadto"),
        ActualDownloadPosition = e.LongAttr("actualdownloadposition"),
        Speed = e.LongAttr("speed"),
        Version = e.Attr("version"),
        OperatingSystem = e.IntAttr("operatingsystem"),
        QueuePosition = e.IntAttr("queueposition"),
        Nickname = e.Attr("nickname"),
        PowerDownload = e.IntAttr("powerdownload"),
        Filename = e.Attr("filename"),
        DownloadId = e.LongAttr("downloadid"),
        Source = e.IntAttr("source")
    };

    public static AjServer ParseServer(XElement e) => new()
    {
        Id = e.LongAttr("id"),
        Name = e.Attr("name"),
        Host = e.Attr("host"),
        Port = e.IntAttr("port"),
        LastSeen = e.LongAttr("lastseen"),
        ConnectionTry = e.IntAttr("connectiontry")
    };

    public static AjNetworkInfo ParseNetworkInfo(XElement e) => new()
    {
        Users = FlexibleLongAttr(e, "users", "usercount", "networkusers"),
        Files = FlexibleLongAttr(e, "files", "filecount", "networkfiles"),
        FileSizeMb = FlexibleLongAttr(e, "filesize", "fileSize", "filesizemb", "fileSizeMb", "networkfilesize", "networkFileSize", "totalfilesize", "totalFileSize", "size"),
        Firewalled = e.BoolAttr("firewalled"),
        Ip = e.Attr("ip"),
        TryConnectToServer = e.LongAttr("tryconnecttoserver"),
        ConnectedWithServerId = e.LongAttr("connectedwithserverid"),
        ConnectedSince = FlexibleLongAttr(e, "connectedsince", "connectedSince", "connected_since"),
        WelcomeMessage = e.Element("welcomemessage")?.Value ?? ""
    };

    private static long FlexibleLongAttr(XElement element, params string[] names)
    {
        foreach (string name in names)
        {
            string raw = element.Attr(name);
            if (TryParseFlexibleLong(raw, out long value))
                return value;
        }

        return 0;
    }

    private static bool TryParseFlexibleLong(string raw, out long value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        raw = raw.Trim();
        if (long.TryParse(raw, out value))
            return true;

        string digitsOnly = new(raw.Where(char.IsDigit).ToArray());
        return !string.IsNullOrWhiteSpace(digitsOnly) && long.TryParse(digitsOnly, out value);
    }

    public static AjInformation ParseInformation(XElement e) => new()
    {
        Id = e.LongAttr("id"),
        SessionUpload = e.LongAttr("sessionupload"),
        SessionDownload = e.LongAttr("sessiondownload"),
        Credits = e.LongAttr("credits"),
        UploadSpeed = e.LongAttr("uploadspeed"),
        DownloadSpeed = e.LongAttr("downloadspeed"),
        OpenConnections = e.IntAttr("openconnections"),
        MaxUploadPositions = e.IntAttr("maxuploadpositions")
    };

    public static AjShareFile ParseShare(XElement e) => new()
    {
        Id = e.LongAttr("id"),
        Filename = e.Attr("filename"),
        Size = e.LongAttr("size"),
        Checksum = e.Attr("checksum"),
        Priority = e.IntAttr("priority"),
        LastAsked = e.LongAttr("lastasked"),
        AskCount = e.LongAttr("askcount"),
        SearchCount = e.LongAttr("searchcount")
    };

    public static List<AjDirectoryEntry> ParseDirectory(string xml)
        => ParseDirectoryList(xml).Directories;

    public static AjDirectoryListResult ParseDirectoryList(string xml)
    {
        XElement root = XElement.Parse(xml);
        AjDirectoryListResult result = new();

        XElement? filesystem = root.Element("filesystem");
        if (filesystem is not null)
            result.Separator = filesystem.Attr("seperator");

        foreach (XElement e in root.Elements("dir"))
        {
            result.Directories.Add(new AjDirectoryEntry
            {
                Name = e.Attr("name"),
                IsFileSystem = e.BoolAttr("isfilesystem"),
                Type = e.IntAttr("type"),
                Path = e.Attr("path")
            });
        }

        return result;
    }

    public static List<AjPart> ParseParts(string xml)
    {
        XElement root = XElement.Parse(xml);
        return root.DescendantsAndSelf()
            .Where(e => e.Name.LocalName.Equals("part", StringComparison.OrdinalIgnoreCase))
            .Select(e => new AjPart
            {
                FromPosition = e.LongAttr("fromposition"),
                Type = e.IntAttr("type")
            })
            .ToList();
    }

    public static long ParseFileSizeFromPartList(string xml)
    {
        XElement root = XElement.Parse(xml);
        return root.DescendantsAndSelf()
            .FirstOrDefault(e => e.Name.LocalName.Equals("fileinformation", StringComparison.OrdinalIgnoreCase))
            ?.LongAttr("filesize") ?? 0;
    }

    public static AjSearch ParseSearch(XElement e)
    {
        AjSearch search = new()
        {
            Id = e.LongAttr("id"),
            SearchText = e.Attr("searchtext"),
            OpenSearches = e.LongAttr("opensearches"),
            FoundFiles = e.LongAttr("foundfiles"),
            SumSearches = e.LongAttr("sumsearches"),
            Running = e.BoolAttr("running")
        };

        foreach (XElement entryElement in e.Descendants().Where(x => x.Name.LocalName.Equals("searchentry", StringComparison.OrdinalIgnoreCase)))
            search.Entries.Add(ParseSearchEntry(entryElement, search.Id));

        return search;
    }

    public static AjSearchEntry ParseSearchEntry(XElement entryElement, long fallbackSearchId = 0)
    {
        List<XElement> filenames = entryElement.Elements()
            .Where(x => x.Name.LocalName.Equals("filename", StringComparison.OrdinalIgnoreCase))
            .ToList();

        XElement? displayFilename = filenames
            .OrderByDescending(x => x.IntAttr("user"))
            .ThenBy(x => x.Attr("name") ?? x.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        int summedFilenameUsers = filenames.Sum(x => Math.Max(0, x.IntAttr("user")));
        if (summedFilenameUsers <= 0 && displayFilename is not null)
            summedFilenameUsers = Math.Max(0, displayFilename.IntAttr("user"));

        long searchId = entryElement.LongAttr("searchid");
        if (searchId <= 0)
            searchId = fallbackSearchId;

        return new AjSearchEntry
        {
            Id = entryElement.LongAttr("id"),
            SearchId = searchId,
            Checksum = entryElement.Attr("checksum"),
            Size = NormalizeLegacyFileSize(entryElement.LongAttr("size")),
            Filename = displayFilename?.Attr("name") ?? displayFilename?.Value ?? "",
            SourceText = ExtractSearchEntrySourceText(entryElement, displayFilename, filenames),
            FilenameUsers = summedFilenameUsers
        };
    }

    private static string ExtractSearchEntrySourceText(XElement entryElement, XElement? displayFilename, IReadOnlyList<XElement> filenames)
    {
        string sourceText = FirstNonEmptyAttr(entryElement, "source", "sources", "sourceaddress", "address", "host", "nick", "nickname", "usernick", "username");
        if (!string.IsNullOrWhiteSpace(sourceText))
            return sourceText;

        if (displayFilename is not null)
        {
            sourceText = FirstNonEmptyAttr(displayFilename, "source", "sources", "sourceaddress", "address", "host", "nick", "nickname", "usernick", "username");
            if (!string.IsNullOrWhiteSpace(sourceText))
                return sourceText;
        }

        foreach (XElement filename in filenames)
        {
            sourceText = FirstNonEmptyAttr(filename, "source", "sources", "sourceaddress", "address", "host", "nick", "nickname", "usernick", "username");
            if (!string.IsNullOrWhiteSpace(sourceText))
                return sourceText;
        }

        foreach (XElement child in entryElement.Elements())
        {
            string localName = child.Name.LocalName;
            if (!localName.Contains("source", StringComparison.OrdinalIgnoreCase)
                && !localName.Contains("peer", StringComparison.OrdinalIgnoreCase)
                && !localName.Contains("nick", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sourceText = FirstNonEmptyAttr(child, "source", "sources", "sourceaddress", "address", "host", "nick", "nickname", "usernick", "username", "name");
            if (!string.IsNullOrWhiteSpace(sourceText))
                return sourceText;

            string value = child.Value?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static string FirstNonEmptyAttr(XElement element, params string[] names)
    {
        foreach (string name in names)
        {
            string value = element.Attr(name);
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return string.Empty;
    }

    private static long NormalizeLegacyFileSize(long size)
    {
        if (size < 0 && size >= int.MinValue)
            return unchecked((uint)(int)size);

        return size < 0 ? 0 : size;
    }

    private static long ParseModifiedTimestamp(XElement root)
    {
        string raw = root
            .DescendantsAndSelf()
            .FirstOrDefault(e => e.Name.LocalName.Equals("time", StringComparison.OrdinalIgnoreCase))
            ?.Value
            .Trim() ?? string.Empty;

        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) && value > 0
            ? value
            : 0;
    }
}

public sealed class ModifiedParseResult
{
    public long CoreTimestamp { get; set; }
    public List<long> ActiveServerIds { get; } = new();
    public List<long> ActiveUploadIds { get; } = new();
    public List<long> ActiveDownloadIds { get; } = new();
    public List<long> RemovedIds { get; } = new();
    public List<AjDownload> Downloads { get; } = new();
    public List<AjUpload> Uploads { get; } = new();
    public List<AjUserSource> Users { get; } = new();
    public List<AjServer> Servers { get; } = new();
    public List<AjSearch> Searches { get; } = new();
    public List<AjSearchEntry> SearchEntries { get; } = new();
    public AjNetworkInfo? NetworkInfo { get; set; }
    public AjInformation? Information { get; set; }
}
