using AJCC.Core.Models;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class DownloadPartListAggregatorTests
{
    [TestMethod]
    public void Aggregate_CountsSourcesAndPreservesLoadedRanges()
    {
        List<AjPart> downloadParts =
        [
            new AjPart { FromPosition = 0, Type = 0 },
            new AjPart { FromPosition = 100, Type = -1 }
        ];
        IReadOnlyList<IReadOnlyList<AjPart>> sourcePartLists =
        [
            new List<AjPart>
            {
                new AjPart { FromPosition = 0, Type = 1 },
                new AjPart { FromPosition = 50, Type = 0 }
            },
            new List<AjPart>
            {
                new AjPart { FromPosition = 0, Type = 1 },
                new AjPart { FromPosition = 75, Type = 0 }
            }
        ];

        List<AjPart> result = DownloadPartListAggregator.Aggregate(downloadParts, sourcePartLists, 200);

        Assert.AreEqual(2, TypeAt(result, 25));
        Assert.AreEqual(1, TypeAt(result, 60));
        Assert.AreEqual(0, TypeAt(result, 80));
        Assert.AreEqual(-1, TypeAt(result, 150));
    }

    [TestMethod]
    public void Aggregate_ActiveTransferRangeOverridesAvailability()
    {
        List<AjPart> downloadParts =
        [
            new AjPart { FromPosition = 0, Type = 0 }
        ];
        IReadOnlyList<IReadOnlyList<AjPart>> sourcePartLists =
        [
            new List<AjPart>
            {
                new AjPart { FromPosition = 0, Type = 1 }
            }
        ];
        IReadOnlyList<(long From, long To)> activeRanges =
        [
            (40, 60)
        ];

        List<AjPart> result = DownloadPartListAggregator.Aggregate(
            downloadParts,
            sourcePartLists,
            100,
            activeRanges);

        Assert.AreEqual(1, TypeAt(result, 20));
        Assert.AreEqual(DownloadPartListAggregator.ActiveDownloadPartType, TypeAt(result, 50));
        Assert.AreEqual(1, TypeAt(result, 80));
    }

    private static int TypeAt(IReadOnlyList<AjPart> parts, long position)
    {
        int type = 0;
        foreach (AjPart part in parts.OrderBy(part => part.FromPosition))
        {
            if (part.FromPosition > position)
                break;

            type = part.Type;
        }

        return type;
    }
}
