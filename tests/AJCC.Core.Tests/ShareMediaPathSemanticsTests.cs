using AJCC.Core.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ShareMediaPathSemanticsTests
{
    [TestMethod]
    public void IsPlausibleMediaFileName_MatchesProductiveMediaExtensions()
    {
        Assert.IsTrue(ShareMediaPathSemantics.IsPlausibleMediaFileName(@"C:\Incoming\Film.MKV"));
        Assert.IsTrue(ShareMediaPathSemantics.IsPlausibleMediaFileName("/incoming/song.flac"));
        Assert.IsTrue(ShareMediaPathSemantics.IsPlausibleMediaFileName("clip.m2ts"));
        Assert.IsFalse(ShareMediaPathSemantics.IsPlausibleMediaFileName("notes.txt"));
        Assert.IsFalse(ShareMediaPathSemantics.IsPlausibleMediaFileName(" "));
    }

    [TestMethod]
    public void TryGetRelativePathBelowIncoming_MapsWindowsCorePath()
    {
        bool ok = ShareMediaPathSemantics.TryGetRelativePathBelowIncoming(
            @"D:\Applefiles\Incoming",
            @"D:\Applefiles\Incoming\Filme\movie.mkv",
            out string relative);

        Assert.IsTrue(ok);
        Assert.AreEqual("Filme/movie.mkv", relative);
    }

    [TestMethod]
    public void TryGetRelativePathBelowIncoming_MapsUnixCorePath()
    {
        bool ok = ShareMediaPathSemantics.TryGetRelativePathBelowIncoming(
            "/srv/applejuice/Incoming/",
            "/srv/applejuice/Incoming/Music/song.mp3",
            out string relative);

        Assert.IsTrue(ok);
        Assert.AreEqual("Music/song.mp3", relative);
    }

    [TestMethod]
    public void TryGetRelativePathBelowIncoming_RejectsOutsideAndTraversal()
    {
        Assert.IsFalse(ShareMediaPathSemantics.TryGetRelativePathBelowIncoming(
            @"D:\Applefiles\Incoming",
            @"D:\Applefiles\Other\movie.mkv",
            out _));

        Assert.IsFalse(ShareMediaPathSemantics.TryGetRelativePathBelowIncoming(
            @"D:\Applefiles\Incoming",
            @"D:\Applefiles\Incoming\..\Secret\movie.mkv",
            out _));
    }
}
