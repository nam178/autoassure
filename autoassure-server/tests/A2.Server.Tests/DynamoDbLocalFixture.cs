using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using Amazon.DynamoDBv2;
using Amazon.Runtime;

namespace A2.Server.Tests;

/// <summary>Downloads, starts, and tears down a single DynamoDB Local (Java) process shared by every
/// integration test class in the "DynamoDbLocal" collection, so tests exercise real DynamoDB API
/// semantics without hitting AWS or Docker. Wipes any pre-existing tables on startup so every test run
/// begins from a clean, empty database. Individual test classes are responsible for creating and deleting
/// their own tables against the shared instance.</summary>
public sealed class DynamoDbLocalFixture : IAsyncLifetime
{
    private const string DownloadUrl =
        "https://s3.us-west-2.amazonaws.com/dynamodb-local/dynamodb_local_latest.tar.gz";

    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".dynamodb-local"
    );

    private readonly HttpClient _httpClient = new();
    private Process? _process;
    private int _port;

    public async Task InitializeAsync()
    {
        await EnsureJarDownloadedAsync();
        _port = GetFreeTcpPort();
        StartProcess();
        await WaitUntilReadyAsync();
        await WipeAllTablesAsync();
    }

    public Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(5000);
        }
        _process?.Dispose();
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Creates a new SDK client pointed at this test run's shared local DynamoDB process.</summary>
    public AmazonDynamoDBClient CreateClient() =>
        new(
            new BasicAWSCredentials("local", "local"),
            new AmazonDynamoDBConfig { ServiceURL = $"http://localhost:{_port}", UseHttp = true }
        );

    // Downloads the official DynamoDB Local distribution once and caches it under the user's home
    // directory so subsequent test runs (and `dotnet clean`) don't re-download ~100MB every time.
    private async Task EnsureJarDownloadedAsync()
    {
        var jarPath = Path.Combine(CacheDirectory, "DynamoDBLocal.jar");
        if (File.Exists(jarPath))
        {
            return;
        }

        Directory.CreateDirectory(CacheDirectory);
        await using var archiveStream = await _httpClient.GetStreamAsync(DownloadUrl);
        await using var gzipStream = new GZipStream(archiveStream, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(gzipStream, CacheDirectory, overwriteFiles: true);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private void StartProcess()
    {
        _process =
            Process.Start(
                new ProcessStartInfo
                {
                    FileName = "java",
                    Arguments =
                        $"-Djava.library.path={CacheDirectory}/DynamoDBLocal_lib "
                        + $"-jar {CacheDirectory}/DynamoDBLocal.jar -inMemory -port {_port}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            )
            ?? throw new InvalidOperationException(
                "Failed to start DynamoDB Local — is `java` on PATH?"
            );
    }

    private async Task WaitUntilReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                throw new InvalidOperationException(
                    $"DynamoDB Local exited early with code {_process.ExitCode}."
                );
            }

            try
            {
                await _httpClient.GetAsync($"http://localhost:{_port}/");
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(200);
            }
        }

        throw new TimeoutException("DynamoDB Local did not become ready within 30 seconds.");
    }

    // Defensive clean slate: a leftover process from a crashed prior run could still be holding tables
    // even though we always start with -inMemory, so we clear the database before every test run.
    private async Task WipeAllTablesAsync()
    {
        using var client = CreateClient();
        var tableNames = (await client.ListTablesAsync()).TableNames;
        foreach (var tableName in tableNames)
        {
            await client.DeleteTableAsync(tableName);
        }
    }
}

/// <summary>Shares one <see cref="DynamoDbLocalFixture"/> across every test class that needs DynamoDB
/// Local. xUnit runs test classes within the same collection sequentially, which keeps table
/// creation/deletion race-free against the single shared process.</summary>
[CollectionDefinition("DynamoDbLocal")]
public sealed class DynamoDbLocalCollection : ICollectionFixture<DynamoDbLocalFixture>;
