using AJCC.Core.Helpers;
using AJCC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ShareDirectoryDraftSemanticsTests
{
    [TestMethod]
    public void Apply_BlocksChildBelowRecursiveUnixParent()
    {
        AjShareDirectory[] current =
        {
            Share("/srv/share", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        ShareDirectoryDraftResult result = ShareDirectoryDraftSemantics.Apply(
            current,
            "/srv/share/music",
            ShareDirectoryDraftSemantics.SingleDirectoryShareMode);

        Assert.IsTrue(result.BlockedByRecursiveAncestor);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual("/srv/share", result.BlockingAncestorPath);
        Assert.AreEqual(1, result.Directories.Count);
    }

    [TestMethod]
    public void Apply_AllowsExactModeSwitchAcrossWindowsSeparators()
    {
        AjShareDirectory[] current =
        {
            Share(@"C:\Share\Movies\", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        ShareDirectoryDraftResult result = ShareDirectoryDraftSemantics.Apply(
            current,
            "c:/share/movies",
            ShareDirectoryDraftSemantics.SingleDirectoryShareMode);

        Assert.IsFalse(result.BlockedByRecursiveAncestor);
        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, result.Directories.Count);
        Assert.AreEqual(ShareDirectoryDraftSemantics.SingleDirectoryShareMode, result.Directories[0].ShareMode);
        Assert.AreEqual(@"C:\Share\Movies\", result.Directories[0].Name);
    }

    [TestMethod]
    public void Apply_RecursiveModeRemovesRedundantDescendants()
    {
        AjShareDirectory[] current =
        {
            Share("/srv/share/music", ShareDirectoryDraftSemantics.SingleDirectoryShareMode),
            Share("/srv/share/music/live", ShareDirectoryDraftSemantics.SingleDirectoryShareMode),
            Share("/srv/other", ShareDirectoryDraftSemantics.SingleDirectoryShareMode)
        };

        ShareDirectoryDraftResult result = ShareDirectoryDraftSemantics.Apply(
            current,
            "/srv/share/music",
            ShareDirectoryDraftSemantics.RecursiveShareMode);

        Assert.IsFalse(result.BlockedByRecursiveAncestor);
        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, result.RemovedRedundantCount);
        Assert.AreEqual(2, result.Directories.Count);
        Assert.IsTrue(result.Directories.Any(directory =>
            directory.Name == "/srv/share/music" &&
            directory.ShareMode == ShareDirectoryDraftSemantics.RecursiveShareMode));
        Assert.IsFalse(result.Directories.Any(directory => directory.Name == "/srv/share/music/live"));
    }

    [TestMethod]
    public void Normalize_DeduplicatesEquivalentPathsAndRecursiveModeWins()
    {
        AjShareDirectory[] current =
        {
            Share(@"C:\Share\Music", ShareDirectoryDraftSemantics.SingleDirectoryShareMode),
            Share("c:/share/music/", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        IReadOnlyList<AjShareDirectory> result = ShareDirectoryDraftSemantics.Normalize(current);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(@"C:\Share\Music", result[0].Name);
        Assert.AreEqual(ShareDirectoryDraftSemantics.RecursiveShareMode, result[0].ShareMode);
    }

    [TestMethod]
    public void Apply_DoesNotTreatSiblingPrefixAsChild()
    {
        AjShareDirectory[] current =
        {
            Share("/srv/share/music", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        ShareDirectoryDraftResult result = ShareDirectoryDraftSemantics.Apply(
            current,
            "/srv/share/musical",
            ShareDirectoryDraftSemantics.SingleDirectoryShareMode);

        Assert.IsFalse(result.BlockedByRecursiveAncestor);
        Assert.AreEqual(2, result.Directories.Count);
    }

    [TestMethod]
    public void Apply_HandlesUncSeparatorsCaseInsensitively()
    {
        AjShareDirectory[] current =
        {
            Share(@"\\Server\Share\Media", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        ShareDirectoryDraftResult result = ShareDirectoryDraftSemantics.Apply(
            current,
            "//server/share/media/movies",
            ShareDirectoryDraftSemantics.SingleDirectoryShareMode);

        Assert.IsTrue(result.BlockedByRecursiveAncestor);
        Assert.AreEqual(@"\\Server\Share\Media", result.BlockingAncestorPath);
    }

    [TestMethod]
    public void Apply_RecursiveUnixRootBlocksDescendants()
    {
        AjShareDirectory[] current =
        {
            Share("/", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        ShareDirectoryDraftResult result = ShareDirectoryDraftSemantics.Apply(
            current,
            "/home/user",
            ShareDirectoryDraftSemantics.SingleDirectoryShareMode);

        Assert.IsTrue(result.BlockedByRecursiveAncestor);
        Assert.AreEqual("/", result.BlockingAncestorPath);
    }

    [TestMethod]
    public void HasSharedDescendant_DetectsWindowsDescendantAcrossSeparatorsAndCase()
    {
        AjShareDirectory[] directories =
        {
            Share(@"C:\Share\Music\Live", ShareDirectoryDraftSemantics.SingleDirectoryShareMode)
        };

        Assert.IsTrue(ShareDirectoryDraftSemantics.HasSharedDescendant(
            directories,
            "c:/share/music"));
    }

    [TestMethod]
    public void HasSharedDescendant_ExactPathAndSiblingPrefixAreNotDescendants()
    {
        AjShareDirectory[] directories =
        {
            Share("/srv/share/music", ShareDirectoryDraftSemantics.SingleDirectoryShareMode),
            Share("/srv/share/musical/live", ShareDirectoryDraftSemantics.SingleDirectoryShareMode)
        };

        Assert.IsFalse(ShareDirectoryDraftSemantics.HasSharedDescendant(
            directories,
            "/srv/share/music"));
    }

    [TestMethod]
    public void HasSharedDescendant_HandlesUnixRoot()
    {
        AjShareDirectory[] directories =
        {
            Share("/home/user/share", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        Assert.IsTrue(ShareDirectoryDraftSemantics.HasSharedDescendant(directories, "/"));
        Assert.IsFalse(ShareDirectoryDraftSemantics.HasSharedDescendant(
            new[] { Share("/", ShareDirectoryDraftSemantics.RecursiveShareMode) },
            "/"));
    }


    [TestMethod]
    public void GetVisualState_DistinguishesDirectSingleAndRecursiveShares()
    {
        AjShareDirectory[] directories =
        {
            Share("/srv/single", ShareDirectoryDraftSemantics.SingleDirectoryShareMode),
            Share("/srv/recursive", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        Assert.AreEqual(
            ShareDirectoryVisualState.Shared,
            ShareDirectoryDraftSemantics.GetVisualState(directories, "/srv/single"));
        Assert.AreEqual(
            ShareDirectoryVisualState.RecursiveShared,
            ShareDirectoryDraftSemantics.GetVisualState(directories, "/srv/recursive"));
    }

    [TestMethod]
    public void GetVisualState_RecursiveAncestorIsInheritedAcrossWindowsSeparators()
    {
        AjShareDirectory[] directories =
        {
            Share(@"C:\Share\Media", ShareDirectoryDraftSemantics.RecursiveShareMode)
        };

        Assert.AreEqual(
            ShareDirectoryVisualState.RecursiveShared,
            ShareDirectoryDraftSemantics.GetVisualState(
                directories,
                "c:/share/media/movies"));
    }

    [TestMethod]
    public void GetVisualState_ContainerOfSeparateShareRemainsNotShared()
    {
        AjShareDirectory[] directories =
        {
            Share("/srv/media/movies", ShareDirectoryDraftSemantics.SingleDirectoryShareMode)
        };

        Assert.AreEqual(
            ShareDirectoryVisualState.NotShared,
            ShareDirectoryDraftSemantics.GetVisualState(directories, "/srv/media"));
        Assert.IsTrue(
            ShareDirectoryDraftSemantics.HasSharedDescendant(directories, "/srv/media"));
    }

    private static AjShareDirectory Share(string path, string mode)
        => new() { Name = path, ShareMode = mode };
}
