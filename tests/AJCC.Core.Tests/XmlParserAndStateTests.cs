using System.Xml.Linq;
using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class XmlParserAndStateTests
{
    [TestMethod]
    public void ParseSettings_PreservesCoreValuesAndShares()
    {
        const string xml = """
            <settings>
              <nick>tester</nick>
              <port>7000</port>
              <xmlport>9851</xmlport>
              <maxupload>1234</maxupload>
              <maxdownload>5678</maxdownload>
              <speedperslot>12</speedperslot>
              <maxconnections>90</maxconnections>
              <autoconnect>true</autoconnect>
              <maxsourcesperfile>42</maxsourcesperfile>
              <incomingdirectory>/data/incoming</incomingdirectory>
              <temporarydirectory>/data/temp</temporarydirectory>
              <maxnewconnectionsperturn>7</maxnewconnectionsperturn>
              <share><directory name="/data/share" sharemode="subdirectory" /></share>
            </settings>
            """;

        AjSettings settings = AjXmlParser.ParseSettings(xml);

        Assert.AreEqual("tester", settings.Nick);
        Assert.AreEqual(7000, settings.Port);
        Assert.AreEqual(9851, settings.XmlPort);
        Assert.AreEqual(1234L, settings.MaxUpload);
        Assert.AreEqual(5678L, settings.MaxDownload);
        Assert.IsTrue(settings.AutoConnect);
        Assert.AreEqual(1, settings.SharedDirectories.Count);
        Assert.AreEqual("/data/share", settings.SharedDirectories[0].Name);
        Assert.AreEqual("subdirectory", settings.SharedDirectories[0].ShareMode);
    }

    [TestMethod]
    public void ParseInformationXml_AcceptsFlexibleNetworkAttributeNames()
    {
        const string xml = "<root><networkinfo usercount='123' filecount='456' fileSizeMb='789 MB' firewalled='yes' connectedSince='77' /></root>";

        AjNetworkInfo info = AjXmlParser.ParseInformationXml(xml);

        Assert.AreEqual(123L, info.Users);
        Assert.AreEqual(456L, info.Files);
        Assert.AreEqual(789L, info.FileSizeMb);
        Assert.AreEqual(77L, info.ConnectedSince);
        Assert.IsTrue(info.Firewalled);
    }

    [TestMethod]
    public void ParseModified_ReadsTimestampIdsObjectsAndInformation()
    {
        const string xml = """
            <modified>
              <time>123456</time>
              <ids>
                <downloadid id="10" />
                <uploadid id="20" />
                <serverid id="30" />
              </ids>
              <removed><serverid id="99" /></removed>
              <download id="10" shareid="5" hash="abc" size="1000" status="0" filename="file.bin" targetdirectory="target" powerdownload="13" ready="250" temporaryfilenumber="4" />
              <upload id="20" shareid="5" status="1" nick="peer" uploadfrom="0" uploadto="1000" actualuploadposition="400" speed="20" filename="file.bin" />
              <server id="30" name="server" host="1.2.3.4" port="12345" />
              <information id="1" sessionupload="11" sessiondownload="22" credits="33" uploadspeed="44" downloadspeed="55" openconnections="6" maxuploadpositions="7" />
            </modified>
            """;

        ModifiedParseResult result = AjXmlParser.ParseModified(xml);

        Assert.AreEqual(123456L, result.CoreTimestamp);
        CollectionAssert.Contains(result.ActiveDownloadIds, 10L);
        CollectionAssert.Contains(result.ActiveUploadIds, 20L);
        CollectionAssert.Contains(result.ActiveServerIds, 30L);
        CollectionAssert.Contains(result.RemovedIds, 99L);
        Assert.AreEqual(1, result.Downloads.Count);
        Assert.AreEqual("file.bin", result.Downloads[0].Filename);
        Assert.AreEqual(1, result.Uploads.Count);
        Assert.AreEqual(1, result.Servers.Count);
        Assert.IsNotNull(result.Information);
        Assert.AreEqual(33L, result.Information.Credits);
    }

    [TestMethod]
    public void ParseSearchEntry_NormalizesLegacySigned32BitFileSize()
    {
        XElement element = XElement.Parse("<searchentry id='1' size='-60000000' checksum='abc'><filename name='large.iso' user='2' /></searchentry>");

        AjSearchEntry entry = AjXmlParser.ParseSearchEntry(element, fallbackSearchId: 9);

        Assert.AreEqual(9L, entry.SearchId);
        Assert.AreEqual(4_234_967_296L, entry.Size);
        Assert.AreEqual("large.iso", entry.Filename);
        Assert.AreEqual(2, entry.FilenameUsers);
    }

    [TestMethod]
    public void StateUpdater_PreservesRecentlyImportedFlagOnDownloadUpdate()
    {
        AjState state = new();
        AjDownload existing = new() { Id = 1, Filename = "old.bin", Ready = 10, IsRecentlyImported = true };
        state.Downloads.Add(existing);

        ModifiedParseResult update = new();
        update.Downloads.Add(new AjDownload { Id = 1, Filename = "new.bin", Ready = 20 });

        AjStateUpdater.Apply(state, update);

        Assert.AreSame(existing, state.Downloads[0]);
        Assert.AreEqual("new.bin", state.Downloads[0].Filename);
        Assert.AreEqual(20L, state.Downloads[0].Ready);
        Assert.IsTrue(state.Downloads[0].IsRecentlyImported);
    }

    [TestMethod]
    public void StateUpdater_PreservesUsableUploadFilenameWhenCoreReturnsTechnicalName()
    {
        AjState state = new();
        state.Uploads.Add(new AjUpload { Id = 2, Filename = "real-name.bin" });

        ModifiedParseResult update = new();
        update.Uploads.Add(new AjUpload { Id = 2, Filename = "12345.data", Speed = 99 });

        AjStateUpdater.Apply(state, update);

        Assert.AreEqual(1, state.Uploads.Count);
        Assert.AreEqual("real-name.bin", state.Uploads[0].Filename);
        Assert.AreEqual(99L, state.Uploads[0].Speed);
    }

    [TestMethod]
    public void StateUpdater_AttachesSearchEntryToMatchingSearch()
    {
        AjState state = new();
        state.Searches.Add(new AjSearch { Id = 7, SearchText = "test" });

        ModifiedParseResult update = new();
        update.SearchEntries.Add(new AjSearchEntry { Id = 8, SearchId = 7, Filename = "result.bin", Size = 123 });

        AjStateUpdater.Apply(state, update);

        Assert.AreEqual(1, state.Searches[0].Entries.Count);
        Assert.AreEqual("result.bin", state.Searches[0].Entries[0].Filename);
    }
}
