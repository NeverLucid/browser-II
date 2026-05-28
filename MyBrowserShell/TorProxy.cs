#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MyBrowserShell
{
    /// <summary>
    /// Locates or launches the Tor daemon and waits until its SOCKS5 port is ready.
    /// </summary>
    internal static class TorProxy
    {
        public const int DefaultSocksPort = 9050;

        // Common locations for tor.exe — checked in order
        private static readonly string[] TorExeCandidates =
        {
            // Tor Browser (default install)
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
            // Standalone / portable tor.exe next to the app
            Path.Combine(AppContext.BaseDirectory, "tor", "tor.exe"),
            Path.Combine(AppContext.BaseDirectory, "Tor", "tor.exe"),
            Path.Combine(AppContext.BaseDirectory, "tor.exe"),
        };

        private static Process? _torProcess;
        private static bool _started;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        /// <summary>
        /// Ensures the Tor SOCKS5 proxy is running on <see cref="DefaultSocksPort"/>.
        /// Returns true on success. Shows a user-friendly error dialog on failure.
        /// </summary>
        public static async Task<bool> EnsureRunningAsync()
        {
            await _lock.WaitAsync();
            try
            {
                // Already up?
                if (_started && IsPortOpen(DefaultSocksPort))
                    return true;

                // Maybe Tor Browser / system Tor is already running
                if (IsPortOpen(DefaultSocksPort))
                {
                    _started = true;
                    return true;
                }

                // Try to launch tor.exe ourselves
                string? torPath = FindTorExe();
                if (torPath == null)
                {
                    ShowTorNotFoundDialog();
                    return false;
                }

                string torDataDir = Path.Combine(Path.GetTempPath(), "MyBrowserShell", "TorData");
                Directory.CreateDirectory(torDataDir);

                var psi = new ProcessStartInfo(torPath)
                {
                    Arguments        = $"--SocksPort {DefaultSocksPort} --DataDirectory \"{torDataDir}\"",
                    CreateNoWindow   = true,
                    UseShellExecute  = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                };

                _torProcess = Process.Start(psi);
                if (_torProcess == null)
                {
                    MessageBox.Show(
                        "Failed to start the Tor process.",
                        "Tor Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }

                // Wait up to 30 s for the SOCKS port to open
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (DateTime.UtcNow < deadline)
                {
                    if (IsPortOpen(DefaultSocksPort))
                    {
                        _started = true;
                        return true;
                    }
                    await Task.Delay(500);
                }

                _torProcess.Kill(true);
                _torProcess = null;

                MessageBox.Show(
                    "Tor started but the SOCKS5 port did not become available within 30 seconds.\n" +
                    "Check that Tor is not blocked by your firewall.",
                    "Tor Timeout", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
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
        }

        private static bool IsPortOpen(int port)
        {
            try
            {
                using var tcp = new TcpClient();
                tcp.Connect("127.0.0.1", port);
                return true;
            }
            catch { return false; }
        }

        private static string? FindTorExe()
        {
            foreach (var path in TorExeCandidates)
                if (File.Exists(path)) return path;
            return null;
        }

        private static void ShowTorNotFoundDialog()
        {
            MessageBox.Show(
                "Could not find tor.exe.\n\n" +
                "Please install Tor Browser (https://www.torproject.org/download/) " +
                "or place tor.exe in one of these locations:\n\n" +
                $"• {Path.Combine(AppContext.BaseDirectory, "tor", "tor.exe")}\n" +
                $"• {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe")}\n" +
                $"• {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe")}",
                "Tor Not Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
