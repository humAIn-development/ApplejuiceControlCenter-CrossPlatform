using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ServerReconnectPolicyTests
{
    private const long Now = 2_000_000_000_000;

    [TestMethod]
    public void DisconnectedCore_DoesNotRequireConfirmation()
    {
        ServerReconnectEvaluation result = ServerReconnectPolicy.EvaluateLogin(
            connectedServerId: 0,
            targetServerId: 42,
            connectedSinceUnixMilliseconds: Now - (long)TimeSpan.FromMinutes(5).TotalMilliseconds,
            nowUnixMilliseconds: Now);

        Assert.IsTrue(result.MayProceedWithoutConfirmation);
        Assert.AreEqual(TimeSpan.Zero, result.RecommendedWait);
    }

    [TestMethod]
    public void SameServer_DoesNotRequireConfirmation()
    {
        ServerReconnectEvaluation result = ServerReconnectPolicy.EvaluateLogin(
            connectedServerId: 42,
            targetServerId: 42,
            connectedSinceUnixMilliseconds: Now - (long)TimeSpan.FromMinutes(5).TotalMilliseconds,
            nowUnixMilliseconds: Now);

        Assert.IsFalse(result.RequiresConfirmation);
    }

    [TestMethod]
    public void UnknownConnectedSince_DoesNotRequireConfirmation()
    {
        ServerReconnectEvaluation result = ServerReconnectPolicy.EvaluateLogin(
            connectedServerId: 1,
            targetServerId: 2,
            connectedSinceUnixMilliseconds: 0,
            nowUnixMilliseconds: Now);

        Assert.IsFalse(result.RequiresConfirmation);
    }

    [TestMethod]
    public void DifferentServerInsideWindow_RequiresConfirmationAndReturnsRemainingTime()
    {
        ServerReconnectEvaluation result = ServerReconnectPolicy.EvaluateLogin(
            connectedServerId: 1,
            targetServerId: 2,
            connectedSinceUnixMilliseconds: Now - (long)TimeSpan.FromMinutes(24).TotalMilliseconds,
            nowUnixMilliseconds: Now);

        Assert.IsTrue(result.RequiresConfirmation);
        Assert.AreEqual(TimeSpan.FromMinutes(24), result.ConnectedFor);
        Assert.AreEqual(TimeSpan.FromMinutes(6), result.RecommendedWait);
    }

    [TestMethod]
    public void DifferentServerAfterWindow_DoesNotRequireConfirmation()
    {
        ServerReconnectEvaluation result = ServerReconnectPolicy.EvaluateLogin(
            connectedServerId: 1,
            targetServerId: 2,
            connectedSinceUnixMilliseconds: Now - (long)TimeSpan.FromMinutes(30).TotalMilliseconds,
            nowUnixMilliseconds: Now);

        Assert.IsFalse(result.RequiresConfirmation);
        Assert.AreEqual(TimeSpan.Zero, result.RecommendedWait);
    }

    [TestMethod]
    public void FutureConnectedSince_IsClampedToZeroElapsed()
    {
        ServerReconnectEvaluation result = ServerReconnectPolicy.EvaluateLogin(
            connectedServerId: 1,
            targetServerId: 2,
            connectedSinceUnixMilliseconds: Now + 1_000,
            nowUnixMilliseconds: Now);

        Assert.IsTrue(result.RequiresConfirmation);
        Assert.AreEqual(TimeSpan.Zero, result.ConnectedFor);
        Assert.AreEqual(ServerReconnectPolicy.RestrictionWindow, result.RecommendedWait);
    }

    [DataTestMethod]
    [DataRow("you have to wait 30 minutes until reconnect")]
    [DataRow("WAIT before RECONNECT")]
    [DataRow("reconnect in 12 minuten")]
    [DataRow("until reconnect")]
    public void KnownRestrictionResponses_AreDetected(string response)
    {
        Assert.IsTrue(ServerReconnectPolicy.LooksLikeRestrictionResponse(response));
    }

    [TestMethod]
    public void UnrelatedResponse_IsNotDetectedAsRestriction()
    {
        Assert.IsFalse(ServerReconnectPolicy.LooksLikeRestrictionResponse("OK"));
        Assert.IsFalse(ServerReconnectPolicy.LooksLikeRestrictionResponse(null));
    }

    [DataTestMethod]
    [DataRow("reconnect in 12 minutes", 12)]
    [DataRow("noch 7 minuten bis reconnect", 7)]
    [DataRow("wait 45 minutes until reconnect", 30)]
    public void RestrictionMinutes_AreParsedAndCapped(string response, int expectedMinutes)
    {
        Assert.AreEqual(
            TimeSpan.FromMinutes(expectedMinutes),
            ServerReconnectPolicy.ExtractRestrictionRemaining(response));
    }

    [TestMethod]
    public void MissingRestrictionMinutes_FallsBackToFullWindow()
    {
        Assert.AreEqual(
            ServerReconnectPolicy.RestrictionWindow,
            ServerReconnectPolicy.ExtractRestrictionRemaining("reconnect later"));
        Assert.AreEqual(
            ServerReconnectPolicy.RestrictionWindow,
            ServerReconnectPolicy.ExtractRestrictionRemaining(null));
    }
}
