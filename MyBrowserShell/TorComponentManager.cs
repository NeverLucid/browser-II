#nullable enable
using Org.BouncyCastle.Bcpg.OpenPgp;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MyBrowserShell
{
    internal sealed class TorComponentManager
    {
        internal const string ComponentVersion = "15.0.15";
        internal const string TorVersion = "0.4.9.9";
        internal const string BundleFileName = "tor-expert-bundle-windows-x86_64-15.0.15.tar.gz";
        internal const string SigningKeyFingerprint = "EF6E286DDA85EA2A4BA7DE684E2C6E8793298290";

        private const string BundleUrl =
            "https://archive.torproject.org/tor-package-archive/torbrowser/15.0.15/" + BundleFileName;
        private const string SignatureUrl = BundleUrl + ".asc";
        private const string SigningKeyUrl =
            "https://keys.openpgp.org/vks/v1/by-fingerprint/" + SigningKeyFingerprint;

        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        private readonly HttpClient httpClient;
        private readonly string appBaseDirectory;
        private readonly string cacheRoot;

        public TorComponentManager()
            : this(
                SharedHttpClient,
                AppContext.BaseDirectory,
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MyBrowserShell",
                    "TorComponent"))
        {
        }

        internal TorComponentManager(HttpClient httpClient, string appBaseDirectory, string cacheRoot)
        {
            this.httpClient = httpClient;
            this.appBaseDirectory = appBaseDirectory;
            this.cacheRoot = cacheRoot;
        }

        public async Task<TorComponentResult> ResolveAsync(
            Action<TorBootstrapProgress>? progress,
            CancellationToken cancellationToken)
        {
            progress?.Invoke(new TorBootstrapProgress("Checking Tor", "Looking for bundled or cached Tor..."));

            string? existing = ResolveExistingTorExe();
            if (existing != null)
                return TorComponentResult.Success(existing, downloaded: false);

            try
            {
                string installed = await DownloadVerifyAndInstallAsync(progress, cancellationToken);
                return TorComponentResult.Success(installed, downloaded: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException or InvalidDataException or PgpException)
            {
                return TorComponentResult.Failure("Could not install Tor: " + ex.Message);
            }
        }

        private string? ResolveExistingTorExe()
        {
            return ResolveFirstExistingTorExe(GetCandidatePaths(appBaseDirectory, GetVersionCacheDirectory()), File.Exists);
        }

        private async Task<string> DownloadVerifyAndInstallAsync(
            Action<TorBootstrapProgress>? progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(cacheRoot);
            string versionDirectory = GetVersionCacheDirectory();
            string tempRoot = Path.Combine(cacheRoot, ".download-" + Guid.NewGuid().ToString("N"));
            string archivePath = Path.Combine(tempRoot, BundleFileName);
            string signaturePath = archivePath + ".asc";
            string keyPath = Path.Combine(tempRoot, "torbrowser-signing-key.asc");
            string extractRoot = Path.Combine(tempRoot, "extract");

            try
            {
                Directory.CreateDirectory(tempRoot);
                Directory.CreateDirectory(extractRoot);

                progress?.Invoke(new TorBootstrapProgress("Downloading Tor", "Downloading the Tor Expert Bundle..."));
                await DownloadFileAsync(BundleUrl, archivePath, cancellationToken);
                await DownloadFileAsync(SignatureUrl, signaturePath, cancellationToken);

                progress?.Invoke(new TorBootstrapProgress("Verifying Tor", "Checking the Tor Project signature..."));
                await DownloadFileAsync(SigningKeyUrl, keyPath, cancellationToken);
                await TorSignatureVerifier.VerifyDetachedSignatureAsync(
                    archivePath,
                    signaturePath,
                    keyPath,
                    SigningKeyFingerprint,
                    cancellationToken);

                progress?.Invoke(new TorBootstrapProgress("Extracting Tor", "Installing the Tor client files..."));
                await ExtractTarGzAsync(archivePath, extractRoot, cancellationToken);

                string? torExe = Directory
                    .EnumerateFiles(extractRoot, "tor.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (torExe == null)
                    throw new InvalidDataException("The Tor bundle did not contain tor.exe.");

                if (!Directory.Exists(versionDirectory))
                {
                    Directory.Move(extractRoot, versionDirectory);
                }

                string? installedTorExe = Directory
                    .EnumerateFiles(versionDirectory, "tor.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (installedTorExe == null)
                    throw new InvalidDataException("The installed Tor cache does not contain tor.exe.");

                return installedTorExe;
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(destinationPath);
            await input.CopyToAsync(output, cancellationToken);
        }

        private static async Task ExtractTarGzAsync(
            string archivePath,
            string destinationDirectory,
            CancellationToken cancellationToken)
        {
            await using var file = File.OpenRead(archivePath);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var reader = new TarReader(gzip);

            TarEntry? entry;
            while ((entry = await reader.GetNextEntryAsync(copyData: false, cancellationToken)) != null)
            {
                if (!IsSafeArchiveEntryName(entry.Name))
                    throw new InvalidDataException("The Tor bundle contains an unsafe path: " + entry.Name);

                string destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.Name));
                string destinationRoot = Path.GetFullPath(destinationDirectory);
                if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The Tor bundle contains a path outside the install directory.");

                if (entry.EntryType == TarEntryType.Directory)
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await using var output = File.Create(destinationPath);
                await entry.DataStream!.CopyToAsync(output, cancellationToken);
            }
        }

        private string GetVersionCacheDirectory()
        {
            return Path.Combine(cacheRoot, ComponentVersion);
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch { }
        }

        internal static IReadOnlyList<string> GetCandidatePaths(string appBaseDirectory, string versionCacheDirectory)
        {
            return new[]
            {
                Path.Combine(appBaseDirectory, "Tor", "tor.exe"),
                Path.Combine(appBaseDirectory, "tor", "tor.exe"),
                Path.Combine(appBaseDirectory, "tor.exe"),
                Path.Combine(appBaseDirectory, "Browser", "TorBrowser", "Tor", "tor.exe"),
                Path.Combine(versionCacheDirectory, "Tor", "tor.exe"),
                Path.Combine(versionCacheDirectory, "tor", "tor.exe"),
                Path.Combine(versionCacheDirectory, "tor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Tor Browser", "Browser", "TorBrowser", "Tor", "tor.exe")
            };
        }

        internal static string? ResolveFirstExistingTorExe(
            IEnumerable<string> candidates,
            Func<string, bool> exists)
        {
            foreach (var path in candidates)
            {
                if (exists(path))
                    return path;
            }

            return null;
        }

        internal static bool IsSafeArchiveEntryNameForTests(string entryName)
        {
            return IsSafeArchiveEntryName(entryName);
        }

        private static bool IsSafeArchiveEntryName(string entryName)
        {
            if (string.IsNullOrWhiteSpace(entryName))
                return false;

            if (Path.IsPathRooted(entryName))
                return false;

            string normalized = entryName.Replace('\\', '/');
            return !normalized.Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part == "..");
        }
    }

    internal sealed record TorComponentResult(
        bool IsSuccess,
        string? TorExePath,
        bool Downloaded,
        string? ErrorMessage)
    {
        public static TorComponentResult Success(string torExePath, bool downloaded) =>
            new(true, torExePath, downloaded, null);

        public static TorComponentResult Failure(string errorMessage) =>
            new(false, null, false, errorMessage);
    }

    internal sealed record TorBootstrapProgress(string Stage, string Message);

    internal static class TorSignatureVerifier
    {
        public static async Task VerifyDetachedSignatureAsync(
            string payloadPath,
            string signaturePath,
            string publicKeyPath,
            string expectedFingerprint,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                using var publicKeyInput = File.OpenRead(publicKeyPath);
                using var signatureInput = File.OpenRead(signaturePath);
                using var payloadInput = File.OpenRead(payloadPath);

                VerifyDetachedSignature(
                    payloadInput,
                    signatureInput,
                    publicKeyInput,
                    expectedFingerprint);
            }, cancellationToken);
        }

        private static void VerifyDetachedSignature(
            Stream payloadInput,
            Stream signatureInput,
            Stream publicKeyInput,
            string expectedFingerprint)
        {
            var publicKeys = new PgpPublicKeyRingBundle(PgpUtilities.GetDecoderStream(publicKeyInput));
            if (!ContainsFingerprint(publicKeys, expectedFingerprint))
                throw new PgpException("The Tor signing key fingerprint did not match the pinned fingerprint.");

            PgpSignature signature = ReadSignature(signatureInput);
            PgpPublicKey? signingKey = publicKeys.GetPublicKey(signature.KeyId);
            if (signingKey == null)
                throw new PgpException("The signature was not made by the pinned Tor signing key.");

            signature.InitVerify(signingKey);

            var buffer = new byte[64 * 1024];
            int read;
            while ((read = payloadInput.Read(buffer, 0, buffer.Length)) > 0)
                signature.Update(buffer, 0, read);

            if (!signature.Verify())
                throw new PgpException("The Tor bundle signature is invalid.");
        }

        private static PgpSignature ReadSignature(Stream signatureInput)
        {
            var factory = new PgpObjectFactory(PgpUtilities.GetDecoderStream(signatureInput));
            PgpObject? obj = factory.NextPgpObject();

            if (obj is PgpCompressedData compressed)
            {
                factory = new PgpObjectFactory(compressed.GetDataStream());
                obj = factory.NextPgpObject();
            }

            if (obj is not PgpSignatureList signatures || signatures.Count == 0)
                throw new PgpException("The signature file did not contain a detached PGP signature.");

            return signatures[0];
        }

        private static bool ContainsFingerprint(PgpPublicKeyRingBundle publicKeys, string expectedFingerprint)
        {
            foreach (PgpPublicKeyRing ring in publicKeys.GetKeyRings())
            {
                foreach (PgpPublicKey key in ring.GetPublicKeys())
                {
                    if (string.Equals(ToHex(key.GetFingerprint()), expectedFingerprint, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static string ToHex(byte[] bytes)
        {
            return Convert.ToHexString(bytes);
        }
    }
}
