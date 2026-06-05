#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyBrowserShell
{
    /// <summary>
    /// Locates, installs, or launches the Tor daemon and waits until its SOCKS5 port is ready.
    /// </summary>
    internal static class TorProxy
    {
        public const int DefaultSocksPort = 9050;

        private static Process? _torProcess;
        private static bool _started;
        private static int _socksPort;
        private static string? _torExePath;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        public static async Task<TorProxyResult> EnsureRunningAsync(
            Action<TorBootstrapProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await _lock.WaitAsync(cancellationToken);
            try
            {
                if (_started && _socksPort > 0 && IsPortOpen(_socksPort))
                {
                    return TorProxyResult.Success(_socksPort, _torExePath ?? "Tor");
                }

                if (IsPortOpen(DefaultSocksPort))
                {
                    _started = true;
                    _socksPort = DefaultSocksPort;
                    _torExePath = "Existing Tor proxy on 127.0.0.1:" + DefaultSocksPort;
                    return TorProxyResult.Success(_socksPort, _torExePath);
                }

                var componentManager = new TorComponentManager();
                var component = await componentManager.ResolveAsync(progress, cancellationToken);
                if (!component.Success || component.TorExePath == null)
                {
                    string message = component.ErrorMessage ?? "Tor could not be found or installed.";
                    ShowTorErrorDialog(message);
                    return TorProxyResult.Failure(message);
                }

                int socksPort = SelectAvailableSocksPort();
                string torDataDir = Path.Combine(Path.GetTempPath(), "MyBrowserShell", "TorData", socksPort.ToString());
                Directory.CreateDirectory(torDataDir);

                progress?.Invoke(new TorBootstrapProgress("Connecting to Tor", "Starting the Tor network proxy..."));

                var psi = new ProcessStartInfo(component.TorExePath)
                {
                    Arguments = $"--SocksPort 127.0.0.1:{socksPort} --DataDirectory \"{torDataDir}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };

                _torProcess = Process.Start(psi);
                if (_torProcess == null)
                {
                    const string message = "Failed to start the Tor process.";
                    ShowTorErrorDialog(message);
                    return TorProxyResult.Failure(message);
                }

                var deadline = DateTime.UtcNow.AddSeconds(45);
                while (DateTime.UtcNow < deadline)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (_torProcess.HasExited)
                    {
                        string message = "Tor exited before the SOCKS5 proxy became available.";
                        ShowTorErrorDialog(message);
                        return TorProxyResult.Failure(message);
                    }

                    if (IsPortOpen(socksPort))
                    {
                        _started = true;
                        _socksPort = socksPort;
                        _torExePath = component.TorExePath;
                        return TorProxyResult.Success(socksPort, component.TorExePath);
                    }

                    await Task.Delay(500, cancellationToken);
                }

                Shutdown();

                const string timeoutMessage =
                    "Tor started but the SOCKS5 port did not become available within 45 seconds.\n" +
                    "Check that Tor is not blocked by your firewall.";
                ShowTorErrorDialog(timeoutMessage);
                return TorProxyResult.Failure(timeoutMessage);
            }
            catch (OperationCanceledException)
            {
                const string message = "Tor startup was cancelled.";
                return TorProxyResult.Failure(message);
            }
            finally
            {
                _lock.Release();
            }
        }

        public static void Shutdown()
        {
            try { _torProcess?.Kill(true); } catch { }
            _torProcess = null;
            _started = false;
            _socksPort = 0;
            _torExePath = null;
        }

        internal static int SelectAvailableSocksPortForTests(params int[] preferredPorts)
        {
            return SelectAvailableSocksPort(preferredPorts);
        }

        private static int SelectAvailableSocksPort(params int[] preferredPorts)
        {
            foreach (int port in preferredPorts)
            {
                if (!IsPortOpen(port))
                    return port;
            }

            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int selectedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return selectedPort;
        }

        private static bool IsPortOpen(int port)
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.Connect(IPAddress.Loopback, port);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void ShowTorErrorDialog(string message)
        {
            MessageBox.Show(
                message,
                "Tor Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    internal sealed record TorProxyResult(
        bool Success,
        int SocksPort,
        string? TorExePath,
        string? ErrorMessage)
    {
        public static TorProxyResult Success(int socksPort, string torExePath) =>
            new(true, socksPort, torExePath, null);

        public static TorProxyResult Failure(string errorMessage) =>
            new(false, 0, null, errorMessage);
    }
}
