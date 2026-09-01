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
}
