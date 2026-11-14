using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TaikoDiveLauncher.Models;
using TaikoDiveLauncher.Services;

namespace TaikoDiveLauncher.Tests;

[TestClass]
public sealed class LauncherSignatureTests
{
    private static readonly byte[] Payload = Encoding.UTF8.GetBytes("harmless synthetic launcher bytes; never executed");

    [TestMethod]
    public async Task ValidSignaturePreservesUpdateAndDownloadBehavior()
    {
        using RSA publisher = RSA.Create(3072);
        byte[] bytes = Manifest(Payload);
        using Responses handler = new(bytes, Sign(publisher, bytes), Payload);
        using HttpClient client = new(handler);
        LauncherUpdateService service = Service(client, PublicKey(publisher));
        await service.CheckAsync();
        Assert.AreEqual(LauncherUpdateState.Available, service.State);
        Assert.AreEqual("・署名済みの更新", service.LatestUpdate!.ReleaseNotes);
        Assert.IsTrue(handler.SignatureRequested);

        // These public UI models must not mutate the authenticated install data.
        service.AvailableUpdate!.Sha256 = new string('0', 64);
        service.LatestUpdate!.Revision = new string('f', 40);
        Assert.AreEqual(Convert.ToHexString(SHA256.HashData(Payload)), service.AvailableUpdate!.Sha256);
        Assert.AreEqual(new string('b', 40), service.AvailableUpdate.Revision);

        string directory = Path.Combine(Path.GetTempPath(), "launcher-signature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "synthetic.bin");
        try
        {
            await Download(service, path);
            CollectionAssert.AreEqual(Payload, await File.ReadAllBytesAsync(path));
            int downloads = handler.PayloadRequests;
            await Download(service, path); // Same authenticated hash, cached file.
            Assert.AreEqual(downloads, handler.PayloadRequests);
            File.Delete(path);
            await File.WriteAllBytesAsync(path + ".partial", Payload[..8]);
            await Download(service, path); // Range resume uses the same integrity boundary.
            Assert.IsTrue(handler.RangeRequested);
            CollectionAssert.AreEqual(Payload, await File.ReadAllBytesAsync(path));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("empty")]
    [DataRow("corrupt")]
    [DataRow("attacker-key")]
    [DataRow("substituted-exe-and-hash")]
    [DataRow("changed-revision")]
    [DataRow("changed-json-whitespace")]
    [DataRow("duplicate-field")]
    [DataRow("pkcs1")]
    [DataRow("oversized-signature")]
    [DataRow("oversized-manifest")]
    public async Task UnauthenticatedUpdateNeverReachesInstall(string attack)
    {
        using RSA publisher = RSA.Create(3072);
        byte[] bytes = Manifest(Payload);
        byte[] signature = Sign(publisher, bytes);
        byte[] payload = Payload;
        switch (attack)
        {
            case "empty": signature = []; break;
            case "corrupt": signature[0] ^= 1; break;
            case "attacker-key":
                using (RSA attacker = RSA.Create(3072)) signature = Sign(attacker, bytes);
                break;
            case "substituted-exe-and-hash":
                payload = Encoding.UTF8.GetBytes("attacker's harmless stand-in");
                bytes = Manifest(payload); // Both size and hash now match attacker bytes.
                break;
            case "changed-revision": bytes = Manifest(Payload, new string('c', 40)); break;
            case "changed-json-whitespace": bytes = [.. bytes, (byte)'\n']; break;
            case "duplicate-field":
                bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes)[..^1] + ",\"revision\":\"" + new string('c', 40) + "\"}");
                break;
            case "pkcs1": signature = publisher.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1); break;
            case "oversized-signature": signature = new byte[1025]; break;
            case "oversized-manifest": bytes = new byte[65537]; break;
        }
        using Responses handler = new(bytes, signature, payload) { MissingSignature = attack == "missing" };
        using HttpClient client = new(handler);
        int installBoundaryCalls = 0;
        LauncherUpdateService service = Service(client, PublicKey(publisher), () => { installBoundaryCalls++; return null; });
        await service.CheckAsync();
        Assert.AreEqual(LauncherUpdateState.Failed, service.State);
        Assert.IsNull(service.AvailableUpdate);
        Assert.IsNull(service.LatestUpdate);
        await service.DownloadAndStartInstallerAsync();
        Assert.AreEqual(0, installBoundaryCalls);
        Assert.AreEqual(0, handler.PayloadRequests);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-base64!")]
    [DataRow("AAAA")]
    public async Task MissingOrMalformedPinnedKeyFailsClosed(string key)
    {
        using RSA publisher = RSA.Create(3072);
        byte[] bytes = Manifest(Payload);
        using Responses handler = new(bytes, Sign(publisher, bytes), Payload);
        using HttpClient client = new(handler);
        LauncherUpdateService service = Service(client, key);
        await service.CheckAsync();
        Assert.AreEqual(LauncherUpdateState.Failed, service.State);
        Assert.IsNull(service.AvailableUpdate);
    }

    [TestMethod]
    public async Task SignedCurrentRevisionIsUpToDate()
    {
        using RSA publisher = RSA.Create(3072);
        byte[] bytes = Manifest(Payload, new string('a', 40));
        using Responses handler = new(bytes, Sign(publisher, bytes), Payload);
        using HttpClient client = new(handler);
        LauncherUpdateService service = Service(client, PublicKey(publisher));
        await service.CheckAsync();
        Assert.AreEqual(LauncherUpdateState.UpToDate, service.State);
        Assert.IsNull(service.AvailableUpdate);
        Assert.IsNotNull(service.LatestUpdate);
    }

    [TestMethod]
    public async Task SignedManifestStillRejectsModifiedPayloadAndCache()
    {
        using RSA publisher = RSA.Create(3072);
        byte[] bytes = Manifest(Payload);
        byte[] altered = (byte[])Payload.Clone();
        altered[0] ^= 1;
        using Responses handler = new(bytes, Sign(publisher, bytes), altered);
        using HttpClient client = new(handler);
        LauncherUpdateService service = Service(client, PublicKey(publisher));
        await service.CheckAsync();
        Assert.AreEqual(LauncherUpdateState.Available, service.State);
        string directory = Path.Combine(Path.GetTempPath(), "launcher-signature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "synthetic.bin");
        try
        {
            await File.WriteAllBytesAsync(path, altered);
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => Download(service, path));
            Assert.IsFalse(File.Exists(path));
            Assert.IsFalse(File.Exists(path + ".partial"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public async Task PublishingScriptSignsExactBytesAndRejectsMissingOrMismatchedKey()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "tools", "r2", "Sign-LauncherManifest.ps1")))
            repository = repository.Parent;
        Assert.IsNotNull(repository);
        string script = Path.Combine(repository.FullName, "tools", "r2", "Sign-LauncherManifest.ps1");
        string directory = Path.Combine(Path.GetTempPath(), "launcher-signer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "update-manifest.json");
        using RSA publisher = RSA.Create(3072);
        byte[] bytes = [.. Manifest(Payload), (byte)'\r', (byte)'\n'];
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            async Task<int> Run(string publicKey, string privateKey)
            {
                System.Diagnostics.ProcessStartInfo start = new("pwsh")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = directory,
                };
                foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-File", script, "-ManifestPath", path, "-PublicKey", publicKey })
                    start.ArgumentList.Add(argument);
                start.Environment["LAUNCHER_UPDATE_PRIVATE_KEY"] = privateKey;
                using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start)!;
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch { process.Kill(entireProcessTree: true); throw; }
                await Task.WhenAll(stdout, stderr);
                return process.ExitCode;
            }
            string secret = Convert.ToBase64String(publisher.ExportPkcs8PrivateKey());
            Assert.AreEqual(0, await Run(PublicKey(publisher), secret));
            using Responses handler = new(bytes, await File.ReadAllBytesAsync(path + ".sig"), Payload);
            using HttpClient client = new(handler);
            LauncherUpdateService service = Service(client, PublicKey(publisher));
            await service.CheckAsync();
            Assert.AreEqual(LauncherUpdateState.Available, service.State);
            File.Delete(path + ".sig");
            using RSA wrong = RSA.Create(3072);
            Assert.AreNotEqual(0, await Run(PublicKey(wrong), secret));
            Assert.IsFalse(File.Exists(path + ".sig"));
            Assert.AreNotEqual(0, await Run(PublicKey(publisher), string.Empty));
            Assert.IsFalse(File.Exists(path + ".sig"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task StalledMetadataBodyTimesOutAndAllowsRetry(bool stallSignature)
    {
        using RSA publisher = RSA.Create(3072);
        byte[] bytes = Manifest(Payload);
        using StallingResponses handler = new(bytes, Sign(publisher, bytes), stallSignature);
        using HttpClient client = new(handler) { Timeout = TimeSpan.FromMilliseconds(200) };
        LauncherUpdateService service = Service(client, PublicKey(publisher));
        using CancellationTokenSource cleanup = new();
        Task check = service.CheckAsync(cleanup.Token);
        try
        {
            await handler.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await check.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(LauncherUpdateState.Failed, service.State);
            handler.Stall = false;
            await service.CheckAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(LauncherUpdateState.Available, service.State);
        }
        finally
        {
            cleanup.Cancel();
            await check.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    [TestMethod]
    public async Task CallerCancellationDuringBodyReadReturnsIdleAndAllowsRetry()
    {
        using RSA publisher = RSA.Create(3072);
        byte[] bytes = Manifest(Payload);
        using StallingResponses handler = new(bytes, Sign(publisher, bytes), false);
        using HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(10) };
        LauncherUpdateService service = Service(client, PublicKey(publisher));
        using CancellationTokenSource cancellation = new();
        Task check = service.CheckAsync(cancellation.Token);
        try
        {
            await handler.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
            cancellation.Cancel();
            await check.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(LauncherUpdateState.Idle, service.State);
            handler.Stall = false;
            await service.CheckAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.AreEqual(LauncherUpdateState.Available, service.State);
        }
        finally { cancellation.Cancel(); await check.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    private sealed class StallingResponses(byte[] manifest, byte[] signature, bool stallSignature) : HttpMessageHandler
    {
        public bool Stall { get; set; } = true;
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            bool isSignature = request.RequestUri!.AbsolutePath.EndsWith(".sig", StringComparison.Ordinal);
            HttpContent content = Stall && isSignature == stallSignature
                ? new StreamContent(new StalledStream(ReadStarted))
                : new ByteArrayContent(isSignature ? signature : manifest);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class StalledStream(TaskCompletionSource started) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static LauncherUpdateService Service(HttpClient client, string key, Func<string?>? path = null) => new(
        client, new string('a', 40), new Uri("https://example.test/launcher/update-manifest.json"),
        new Uri("https://example.test/launcher/TaikoDive.Launcher.exe"), path ?? (() => null), publisherPublicKey: key);

    private static byte[] Manifest(byte[] payload, string? revision = null) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        revision = revision ?? new string('b', 40), releaseNotes = "・署名済みの更新",
        sha256 = Convert.ToHexString(SHA256.HashData(payload)), size = payload.Length,
        publishedAt = "2026-01-01T00:00:00Z",
    });

    private static byte[] Sign(RSA key, byte[] bytes) => key.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
    private static string PublicKey(RSA key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    // Exercise the real download verification without ever invoking Process.Start.
    private static Task Download(LauncherUpdateService service, string path) => (Task)typeof(LauncherUpdateService)
        .GetMethod("DownloadAndVerifyAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(service, [path, service.AvailableUpdate!, CancellationToken.None])!;

    private sealed class Responses(byte[] manifest, byte[] signature, byte[] payload) : HttpMessageHandler
    {
        public bool MissingSignature { get; init; }
        public bool SignatureRequested { get; private set; }
        public bool RangeRequested { get; private set; }
        public int PayloadRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;
            if (path.EndsWith(".json.sig", StringComparison.Ordinal))
            {
                SignatureRequested = true;
                response = new(MissingSignature ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new ByteArrayContent(signature) };
            }
            else if (path.EndsWith(".json", StringComparison.Ordinal))
                response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(manifest) };
            else
            {
                PayloadRequests++;
                long? offset = request.Headers.Range?.Ranges.First().From;
                RangeRequested |= offset.HasValue;
                response = new(offset.HasValue ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
                { Content = new ByteArrayContent(payload[(int)(offset ?? 0)..]) };
                if (offset.HasValue)
                    response.Content.Headers.ContentRange = new(offset.Value, payload.Length - 1, payload.Length);
            }
            return Task.FromResult(response);
        }
    }
}