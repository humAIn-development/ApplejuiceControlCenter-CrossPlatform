using AJCC.Core.Models;
using AJCC.Core.Parsers;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AjStateUpdaterTests
{
    [TestMethod]
    public void Apply_InvalidUploadName_UsesDownloadDisplayFilenameWithSameShareId()
    {
        AjState state = new();
        ModifiedParseResult result = new();
        result.Downloads.Add(new AjDownload
        {
            Id = 10,
            ShareId = 42,
            Filename = @"C:\Incoming\Movie 2.mkv"
        });
        result.Uploads.Add(new AjUpload
        {
            Id = 20,
            ShareId = 42,
            Filename = "ShareID 42"
        });

        AjStateUpdater.Apply(state, result);

        Assert.AreEqual("Movie 2.mkv", state.Uploads[0].Filename);
    }

    [TestMethod]
    public void Apply_UsableUploadName_IsNotReplacedByDownloadFallback()
    {
        AjState state = new();
        ModifiedParseResult result = new();
        result.Downloads.Add(new AjDownload
        {
            Id = 10,
            ShareId = 42,
            Filename = "Fallback.mkv"
        });
        result.Uploads.Add(new AjUpload
        {
            Id = 20,
            ShareId = 42,
            Filename = "Actual.mkv"
        });

        AjStateUpdater.Apply(state, result);

        Assert.AreEqual("Actual.mkv", state.Uploads[0].Filename);
    }

    [TestMethod]
    public void Apply_InvalidUploadName_DoesNotUseDownloadWithDifferentShareId()
    {
        AjState state = new();
        ModifiedParseResult result = new();
        result.Downloads.Add(new AjDownload
        {
            Id = 10,
            ShareId = 41,
            Filename = "Wrong.mkv"
        });
        result.Uploads.Add(new AjUpload
        {
            Id = 20,
            ShareId = 42,
            Filename = "12345.data"
        });

        AjStateUpdater.Apply(state, result);

        Assert.AreEqual("12345.data", state.Uploads[0].Filename);
    }

    [TestMethod]
    public void Apply_InvalidRefresh_PreservesPreviouslyUsableUploadNameBeforeDownloadFallback()
    {
        AjState state = new();
        state.Downloads.Add(new AjDownload
        {
            Id = 10,
            ShareId = 42,
            Filename = "Fallback.mkv"
        });
        state.Uploads.Add(new AjUpload
        {
            Id = 20,
            ShareId = 42,
            Filename = "Known.mkv"
        });

        ModifiedParseResult result = new();
        result.Uploads.Add(new AjUpload
        {
            Id = 20,
            ShareId = 42,
            Filename = "ShareID 42"
        });

        AjStateUpdater.Apply(state, result);

        Assert.AreEqual("Known.mkv", state.Uploads[0].Filename);
    }

    [TestMethod]
    public void Apply_InvalidUploadName_UsesShareFilenameCacheWhenDownloadMissing()
    {
        AjState state = new();
        state.ShareFilenameById[42] = @"D:\Shared\Shared Movie.mkv";
        ModifiedParseResult result = new();
        result.Uploads.Add(new AjUpload
        {
            Id = 20,
            ShareId = 42,
            Filename = "ShareID 42"
        });

        AjStateUpdater.Apply(state, result);

        Assert.AreEqual(@"D:\Shared\Shared Movie.mkv", state.Uploads[0].Filename);
    }

    [TestMethod]
    public void RebuildShareFilenameLookup_AndEnrichUploads_UseOnlyUsableMatchingShares()
    {
        AjState state = new();
        state.Shares.Add(new AjShareFile { Id = 42, Filename = @"D:\Shared\Resolved.mkv" });
        state.Shares.Add(new AjShareFile { Id = 43, Filename = "12345.data" });
        state.Shares.Add(new AjShareFile { Id = 0, Filename = "Ignored.mkv" });
        state.Uploads.Add(new AjUpload { Id = 20, ShareId = 42, Filename = "ShareID 42" });
        state.Uploads.Add(new AjUpload { Id = 21, ShareId = 42, Filename = "AlreadyGood.mkv" });
        state.Uploads.Add(new AjUpload { Id = 22, ShareId = 43, Filename = "54321.data" });

        AjStateUpdater.RebuildShareFilenameLookup(state);
        bool changed = AjStateUpdater.EnrichUploadsWithShareFilenames(state);

        Assert.IsTrue(changed);
        Assert.AreEqual(1, state.ShareFilenameById.Count);
        Assert.AreEqual(@"D:\Shared\Resolved.mkv", state.ShareFilenameById[42]);
        Assert.AreEqual(@"D:\Shared\Resolved.mkv", state.Uploads[0].Filename);
        Assert.AreEqual("AlreadyGood.mkv", state.Uploads[1].Filename);
        Assert.AreEqual("54321.data", state.Uploads[2].Filename);
    }

}
