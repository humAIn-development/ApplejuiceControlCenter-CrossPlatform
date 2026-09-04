using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class ServerReconnectRestrictionSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Capture_UnmarkedState_ReturnsUnmarkedSnapshot()
    {
        ServerReconnectRestrictionState state = new();

        ServerReconnectRestrictionSnapshot snapshot = ServerReconnectRestrictionSnapshots.Capture(state);

        Assert.IsFalse(snapshot.IsMarked);
        Assert.AreEqual(0L, snapshot.TargetServerId);
    }

    [TestMethod]
    public void Capture_MarkedState_PreservesAbsoluteState()
    {
        ServerReconnectRestrictionState state = new();
        state.Mark(TimeSpan.FromMinutes(17), hasExactCountdown: true, targetServerId: 42, nowUtc: Now);

        ServerReconnectRestrictionSnapshot snapshot = ServerReconnectRestrictionSnapshots.Capture(state);

        Assert.IsTrue(snapshot.IsMarked);
        Assert.AreEqual(Now.AddMinutes(17), snapshot.UntilUtc);
        Assert.IsTrue(snapshot.HasExactCountdown);
        Assert.AreEqual(42L, snapshot.TargetServerId);
    }

    [TestMethod]
    public void Restore_FutureSnapshot_ReconstructsRemainingWindow()
    {
        ServerReconnectRestrictionState state = new();
        ServerReconnectRestrictionSnapshot snapshot = new(
            Now.AddMinutes(12),
            HasExactCountdown: false,
            TargetServerId: 99);

        bool restored = ServerReconnectRestrictionSnapshots.Restore(state, snapshot, Now.AddMinutes(2));

        Assert.IsTrue(restored);
        Assert.IsTrue(state.IsActive(Now.AddMinutes(2)));
        Assert.AreEqual(TimeSpan.FromMinutes(10), state.GetRemaining(Now.AddMinutes(2)));
        Assert.IsFalse(state.HasExactCountdown);
        Assert.AreEqual(99L, state.TargetServerId);
    }

    [TestMethod]
    public void Restore_ExpiredSnapshot_ClearsExistingState()
    {
        ServerReconnectRestrictionState state = new();
        state.Mark(TimeSpan.FromMinutes(30), hasExactCountdown: true, targetServerId: 7, nowUtc: Now);
        ServerReconnectRestrictionSnapshot snapshot = new(
            Now.AddMinutes(-1),
            HasExactCountdown: false,
            TargetServerId: 8);

        bool restored = ServerReconnectRestrictionSnapshots.Restore(state, snapshot, Now);

        Assert.IsFalse(restored);
        Assert.IsFalse(state.IsMarked);
        Assert.AreEqual(0L, state.TargetServerId);
    }

    [TestMethod]
    public void Restore_DefaultSnapshot_ClearsExistingState()
    {
        ServerReconnectRestrictionState state = new();
        state.Mark(TimeSpan.FromMinutes(30), hasExactCountdown: true, targetServerId: 7, nowUtc: Now);

        bool restored = ServerReconnectRestrictionSnapshots.Restore(state, default, Now);

        Assert.IsFalse(restored);
        Assert.IsFalse(state.IsMarked);
    }
}
