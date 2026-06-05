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
                    return TorProxyResult.Ok(_socksPort, _torExePath ?? "Tor");
                }

                if (IsPortOpen(DefaultSocksPort))
                {
                    _started = true;
                    _socksPort = DefaultSocksPort;
                    _torExePath = "Existing Tor proxy on 127.0.0.1:" + DefaultSocksPort;
                    return TorProxyResult.Ok(_socksPort, _torExePath);
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
                        return TorProxyResult.Ok(socksPort, component.TorExePath);
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

        /// <summary>
        /// The SOCKS5 port currently in use. 0 if Tor is not running.
        /// BrowserRuntime reads this to build the proxy argument.
        /// </summary>
        public static int ActiveSocksPort => _socksPort > 0 ? _socksPort : DefaultSocksPort;

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

    /// <summary>Simple progress notification sent while Tor is bootstrapping.</summary>
    internal sealed record TorBootstrapProgress(string Stage, string Detail);

    /// <summary>Result of TorComponentManager.ResolveAsync.</summary>
    internal sealed record TorComponentResolution(bool Success, string? TorExePath, string? ErrorMessage)
    {
        public static TorComponentResolution Ok(string torExePath) =>
            new(true, torExePath, null);
        public static TorComponentResolution Failure(string error) =>
            new(false, null, error);
    }

    /// <summary>
    /// Locates tor.exe from Tor Browser or a portable installation next to the app.
    /// Does NOT download or install Tor — just resolves the path.
    /// </summary>
    internal sealed class TorComponentManager
    {
        private static readonly string[] Candidates = new[]
        {
            // Tor Browser — default LocalAppData install
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
            // Tor Browser — Program Files
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
            // Tor Browser — Program Files (x86)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
            // Portable: tor	or.exe next to the app
            Path.Combine(AppContext.BaseDirectory, "tor", "tor.exe"),
            Path.Combine(AppContext.BaseDirectory, "Tor", "tor.exe"),
            Path.Combine(AppContext.BaseDirectory, "tor.exe"),
        };

        public Task<TorComponentResolution> ResolveAsync(
            Action<TorBootstrapProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Invoke(new TorBootstrapProgress("Locating Tor", "Searching for tor.exe..."));

            foreach (var path in Candidates)
            {
                if (File.Exists(path))
                    return Task.FromResult(TorComponentResolution.Ok(path));
            }

            string candidateList = string.Join("\n",
                Path.Combine(AppContext.BaseDirectory, "tor", "tor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"));

            return Task.FromResult(TorComponentResolution.Failure(
                "Could not find tor.exe.\n\n" +
                "Install Tor Browser (https://www.torproject.org/download/) " +
                "or place tor.exe at:\n" + candidateList));
        }
    }

    internal sealed record TorProxyResult(
        bool Success,
        int SocksPort,
        string? TorExePath,
        string? ErrorMessage)
    {
        public static TorProxyResult Ok(int socksPort, string torExePath) =>
            new(true, socksPort, torExePath, null);

        public static TorProxyResult Failure(string errorMessage) =>
            new(false, 0, null, errorMessage);

        public static implicit operator bool(TorProxyResult r) => r.Success;
    }
}
