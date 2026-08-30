using AJCC.Core.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class AjUploadDisplayTests
{

    [TestMethod]
    public void LoadedPercent_FractionalCoreValueNormalizesToPercent()
    {
        AjUpload upload = new() { Loaded = 0.24 };

        Assert.AreEqual(24.0, upload.LoadedPercent, 0.0001);
        Assert.AreEqual(24.0, upload.ProgressPercent, 0.0001);
        Assert.AreEqual($"{24.0:0.0} %", upload.ProgressPercentText);
        Assert.AreEqual($"{24.0:0.0} %", upload.WatermarkText);
    }

    [TestMethod]
    public void LoadedPercent_AlreadyPercentValuePassesThrough()
    {
        AjUpload upload = new() { Loaded = 24.0 };

        Assert.AreEqual(24.0, upload.LoadedPercent, 0.0001);
        Assert.AreEqual(24.0, upload.ProgressPercent, 0.0001);
    }

    [TestMethod]
    public void LoadedPercent_InvalidAndOutOfRangeValuesAreClamped()
    {
        Assert.AreEqual(0.0, new AjUpload { Loaded = double.NaN }.LoadedPercent, 0.0001);
        Assert.AreEqual(0.0, new AjUpload { Loaded = double.PositiveInfinity }.LoadedPercent, 0.0001);
        Assert.AreEqual(0.0, new AjUpload { Loaded = -0.5 }.LoadedPercent, 0.0001);
        Assert.AreEqual(100.0, new AjUpload { Loaded = 120.0 }.LoadedPercent, 0.0001);
    }

    [TestMethod]
    public void ProgressPercent_TransferRangeStillOverridesLoadedFallback()
    {
        AjUpload upload = new()
        {
            Loaded = 0.90,
            UploadFrom = 0,
            UploadTo = 1000,
            ActualUploadPosition = 250
        };

        Assert.AreEqual(90.0, upload.LoadedPercent, 0.0001);
        Assert.AreEqual(25.0, upload.ProgressPercent, 0.0001);
        Assert.AreEqual($"{90.0:0.0} %", upload.WatermarkText);
    }

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

    [TestMethod]
    public void IsActiveTransfer_RequiresStatusOne()
    {
        AjUpload upload = new()
        {
            Status = 2,
            Speed = 1024,
            UploadFrom = 0,
            UploadTo = 4096,
            ActualUploadPosition = 1024
        };

        Assert.IsFalse(upload.IsActiveTransfer);
    }

    [TestMethod]
    public void IsActiveTransfer_PositiveSpeedWinsEvenIfRangeLooksFinished()
    {
        AjUpload upload = new()
        {
            Status = 1,
            Speed = 1024,
            UploadFrom = 0,
            UploadTo = 4096,
            ActualUploadPosition = 4096
        };

        Assert.IsTrue(upload.IsActiveTransfer);
    }

    [TestMethod]
    public void IsActiveTransfer_ZeroSpeedWithOpenRange_IsActive()
    {
        AjUpload upload = new()
        {
            Status = 1,
            Speed = 0,
            UploadFrom = 100,
            UploadTo = 500,
            ActualUploadPosition = 499
        };

        Assert.IsTrue(upload.IsActiveTransfer);
    }

    [TestMethod]
    public void IsActiveTransfer_ZeroSpeedWithFinishedOrInvalidRange_IsInactive()
    {
        Assert.IsFalse(new AjUpload
        {
            Status = 1,
            Speed = 0,
            UploadFrom = 100,
            UploadTo = 500,
            ActualUploadPosition = 500
        }.IsActiveTransfer);

        Assert.IsFalse(new AjUpload
        {
            Status = 1,
            Speed = 0,
            UploadFrom = 500,
            UploadTo = 500,
            ActualUploadPosition = 0
        }.IsActiveTransfer);
    }
}
