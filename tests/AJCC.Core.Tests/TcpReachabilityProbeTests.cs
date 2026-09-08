using System.Net;
using System.Net.Sockets;
using AJCC.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AJCC.Core.Tests;

[TestClass]
public sealed class TcpReachabilityProbeTests
{
    [TestMethod]
    public async Task TestAsync_ReturnsTrueForListeningLoopbackEndpoint()
    {
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        bool reachable = await TcpReachabilityProbe.TestAsync(
            IPAddress.Loopback.ToString(),
            port,
            TimeSpan.FromSeconds(2));

        Assert.IsTrue(reachable);
    }

    [TestMethod]
    public async Task TestAsync_ReturnsFalseAfterLoopbackEndpointStopsListening()
    {
        int port;
        using (TcpListener listener = new(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        bool reachable = await TcpReachabilityProbe.TestAsync(
            IPAddress.Loopback.ToString(),
            port,
            TimeSpan.FromMilliseconds(500));

        Assert.IsFalse(reachable);
    }
}
