using System.Net.Sockets;

namespace AJCC.Core.Services;

public static class TcpReachabilityProbe
{
    public static async Task<bool> TestAsync(string host, int port, TimeSpan timeout)
    {
        try
        {
            using TcpClient tcpClient = new();
            Task connectTask = tcpClient.ConnectAsync(host, port);
            Task completed = await Task.WhenAny(connectTask, Task.Delay(timeout)).ConfigureAwait(false);
            if (!ReferenceEquals(completed, connectTask))
                return false;

            await connectTask.ConfigureAwait(false);
            return tcpClient.Connected;
        }
        catch
        {
            return false;
        }
    }
}
