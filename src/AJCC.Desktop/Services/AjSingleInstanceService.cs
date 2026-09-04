using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace AJCC.Desktop.Services;

internal sealed class AjSingleInstanceService : IDisposable
{
    private const string MutexNameBase = "ApplejuiceControlCenter.CrossPlatform.SingleInstance.v1";
    private const string PipeNameBase = "ApplejuiceControlCenter.CrossPlatform.ImportPipe.v1";
    private static readonly string MutexName = BuildUserScopedName(MutexNameBase);
    private static readonly string PipeName = BuildUserScopedName(PipeNameBase);

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private CancellationTokenSource? _serverCts;

    private AjSingleInstanceService(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public bool IsPrimaryInstance => _ownsMutex;
    public event Action<string[]>? ArgumentsReceived;

    public static AjSingleInstanceService Create()
    {
        Mutex mutex = new(false, MutexName);
        bool ownsMutex;
        try
        {
            ownsMutex = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            ownsMutex = true;
        }

        StartupDiagnostics.WriteState(
            "Single-instance state resolved; primary=" + ownsMutex);
        return new AjSingleInstanceService(mutex, ownsMutex);
    }

    public static async Task<bool> TryForwardArgumentsAsync(
        string[]? args,
        int timeoutMs = 10000)
    {
        string[] safeArgs = args ?? Array.Empty<string>();

        try
        {
            using NamedPipeClientStream pipe = new(
                ".",
                PipeName,
                PipeDirection.Out,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using CancellationTokenSource cts = new(timeoutMs);
            await pipe.ConnectAsync(cts.Token).ConfigureAwait(false);

            string payload = EncodeArguments(safeArgs);
            await using StreamWriter writer =
                new(pipe, new UTF8Encoding(false)) { AutoFlush = true };
            await writer.WriteLineAsync(payload).ConfigureAwait(false);

            StartupDiagnostics.WriteState(
                "Secondary arguments forwarded to primary instance",
                safeArgs);
            return true;
        }
        catch (Exception ex)
        {
            StartupDiagnostics.WriteException(
                "SingleInstance.TryForwardArgumentsAsync",
                ex);
            return false;
        }
    }

    public void StartServer()
    {
        if (!IsPrimaryInstance || _serverCts is not null)
            return;

        _serverCts = new CancellationTokenSource();
        _ = Task.Run(() => ServerLoopAsync(_serverCts.Token));
    }

    private async Task ServerLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using NamedPipeServerStream pipe = new(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);

                using StreamReader reader = new(pipe, Encoding.UTF8);
                string? payload = await reader.ReadLineAsync(token).ConfigureAwait(false);
                string[] args = DecodeArguments(payload);
                StartupDiagnostics.WriteState(
                    "Primary instance received secondary arguments",
                    args);
                ArgumentsReceived?.Invoke(args);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                StartupDiagnostics.WriteException(
                    "SingleInstance.ServerLoop",
                    ex);
                try
                {
                    await Task.Delay(250, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static string BuildUserScopedName(string baseName)
    {
        string userScopeRoot =
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(userScopeRoot))
        {
            userScopeRoot =
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        string securityScope = OperatingSystem.IsWindows()
            ? (Environment.IsPrivilegedProcess ? "windows-privileged" : "windows-standard")
            : "user";

        return BuildUserScopedName(
            baseName,
            Environment.UserName,
            userScopeRoot,
            securityScope);
    }

    internal static string BuildUserScopedName(
        string baseName,
        string? userName,
        string? userScopeRoot,
        string? securityScope)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            throw new ArgumentException("IPC-Basisname fehlt.", nameof(baseName));

        string identity =
            (userName ?? string.Empty).Trim()
            + "\n"
            + (userScopeRoot ?? string.Empty).Trim()
            + "\n"
            + (securityScope ?? string.Empty).Trim();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        string token =
            Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
        return baseName.Trim() + "." + token;
    }

    private static string EncodeArguments(IEnumerable<string> args)
        => Convert.ToBase64String(
            Encoding.UTF8.GetBytes(string.Join("\n", args)));

    private static string[] DecodeArguments(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return Array.Empty<string>();

        try
        {
            string text = Encoding.UTF8.GetString(
                Convert.FromBase64String(payload.Trim()));
            return text.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? serverCts = _serverCts;
        _serverCts = null;

        try
        {
            serverCts?.Cancel();
            serverCts?.Dispose();
        }
        catch
        {
        }

        try
        {
            if (_ownsMutex)
                _mutex.ReleaseMutex();
        }
        catch
        {
        }

        _mutex.Dispose();
    }
}
