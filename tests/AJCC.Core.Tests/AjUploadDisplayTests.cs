using AJCC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AjUploadDisplayTests
{
    [TestMethod]
    public void LastConnectionText_Zero_IsDash()
    {
        AjUpload upload = new() { LastConnection = 0 };
        Assert.AreEqual("-", upload.LastConnectionText);
    }

    [TestMethod]
    public void LastConnectionText_Milliseconds_UsesLocalDateTime()
    {
        const long value = 1_700_000_000_000L;
        AjUpload upload = new() { LastConnection = value };
        string expected = DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        Assert.AreEqual(expected, upload.LastConnectionText);
    }

    [TestMethod]
    public void LastConnectionText_Seconds_UsesLocalDateTime()
    {
        const long value = 1_700_000_000L;
        AjUpload upload = new() { LastConnection = value };
        string expected = DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
        Assert.AreEqual(expected, upload.LastConnectionText);
    }

    [TestMethod]
    public void LastConnectionText_SmallValues_AreRelative()
    {
        Assert.AreEqual("gerade eben", new AjUpload { LastConnection = 20 }.LastConnectionText);
        Assert.AreEqual("vor 2 min", new AjUpload { LastConnection = 120 }.LastConnectionText);
        Assert.AreEqual("vor 2 h", new AjUpload { LastConnection = 7200 }.LastConnectionText);
        Assert.AreEqual("vor 2 d", new AjUpload { LastConnection = 172800 }.LastConnectionText);
    }

    [TestMethod]
    public void LastConnectionText_OutOfRange_IsDash()
    {
        AjUpload upload = new() { LastConnection = long.MaxValue };
        Assert.AreEqual("-", upload.LastConnectionText);
    }
}
