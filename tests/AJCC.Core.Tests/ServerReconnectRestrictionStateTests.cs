using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ServerReconnectRestrictionStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Mark_CreatesActiveRestrictionWithTarget()
    {
        ServerReconnectRestrictionState state = new();

        state.Mark(TimeSpan.FromMinutes(30), hasExactCountdown: true, targetServerId: 42, nowUtc: Now);

        Assert.IsTrue(state.IsMarked);
        Assert.IsTrue(state.IsActive(Now));
        Assert.IsTrue(state.HasExactCountdown);
        Assert.AreEqual(42L, state.TargetServerId);
        Assert.AreEqual(TimeSpan.FromMinutes(30), state.GetRemaining(Now));
    }

    [TestMethod]
    public void Mark_DoesNotShortenExistingWindow()
    {
        ServerReconnectRestrictionState state = new();
        state.Mark(TimeSpan.FromMinutes(30), hasExactCountdown: true, targetServerId: 42, nowUtc: Now);

        state.Mark(TimeSpan.FromMinutes(10), hasExactCountdown: false, targetServerId: 43, nowUtc: Now.AddMinutes(1));

        Assert.AreEqual(Now.AddMinutes(30), state.UntilUtc);
        Assert.IsFalse(state.HasExactCountdown);
        Assert.AreEqual(43L, state.TargetServerId);
    }

    [TestMethod]
    public void ClearIfConnected_OnlyClearsForRestrictionTarget()
    {
        ServerReconnectRestrictionState state = new();
        state.Mark(TimeSpan.FromMinutes(30), hasExactCountdown: false, targetServerId: 42, nowUtc: Now);

        Assert.IsFalse(state.ClearIfConnected(41));
        Assert.IsTrue(state.IsActive(Now));

        Assert.IsTrue(state.ClearIfConnected(42));
        Assert.IsFalse(state.IsMarked);
        Assert.AreEqual(TimeSpan.Zero, state.GetRemaining(Now));
    }

    [TestMethod]
    public void ClearIfExpired_ClearsElapsedRestriction()
    {
        ServerReconnectRestrictionState state = new();
        state.Mark(TimeSpan.FromMinutes(5), hasExactCountdown: true, targetServerId: 42, nowUtc: Now);

        Assert.IsFalse(state.ClearIfExpired(Now.AddMinutes(4)));
        Assert.IsTrue(state.ClearIfExpired(Now.AddMinutes(5)));
        Assert.IsFalse(state.IsMarked);
    }

    [TestMethod]
    public void NonPositiveMark_IsIgnored()
    {
        ServerReconnectRestrictionState state = new();

        state.Mark(TimeSpan.Zero, hasExactCountdown: true, targetServerId: 42, nowUtc: Now);

        Assert.IsFalse(state.IsMarked);
    }
}
