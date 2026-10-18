using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace SmartRail.Api.Tests.Catering;

// The real RailBites service (Node + SQLite) as a child process, so these tests cross the actual
// HTTP boundary. It needs services/railbites built (npm run build). If the build is missing the
// tests fail with that instruction rather than being skipped: a skipped boundary test proves nothing.
public sealed class RailBitesProcess : IAsyncDisposable
{
    public const string ServiceKey = "test-railbites-service-key";
    public const string CallbackSecret = "test-railbites-callback-secret";
    public const string PolgahawelaKioskKey = "kiosk-key-polgahawela";
    public const string PeradeniyaKioskKey = "kiosk-key-peradeniya";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"railbites-it-{Guid.NewGuid():N}");
    private Process? _process;

    public RailBitesProcess()
    {
        Port = FreePort();
        Directory.CreateDirectory(_directory);
        Http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(15) };
        Http.DefaultRequestHeaders.Add("X-RailBites-Service-Key", ServiceKey);
    }

    public int Port { get; }

    public string BaseUrl => $"http://127.0.0.1:{Port}";

    public HttpClient Http { get; }

    // Extra environment for the child, for example the SmartRail address for callbacks.
    public Dictionary<string, string> Environment { get; } = [];

    public static string ServiceFolder([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "services", "railbites"));

    public async Task StartAsync()
    {
        var folder = ServiceFolder();
        var entry = Path.Combine(folder, "dist", "index.js");
        if (!File.Exists(entry))
        {
            throw new InvalidOperationException(
                $"RailBites is not built: {entry} is missing. Run `npm --prefix services/railbites ci` and `npm --prefix services/railbites run build` first.");
        }

        var start = new ProcessStartInfo("node", "dist/index.js")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["PORT"] = Port.ToString();
        start.Environment["NODE_ENV"] = "test";
        start.Environment["RAILBITES_DB_PATH"] = Path.Combine(_directory, "railbites.db");
        start.Environment["RAILBITES_SERVICE_KEY"] = ServiceKey;
        start.Environment["RAILBITES_KIOSK_KEYS"] = $"polgahawela-junction={PolgahawelaKioskKey},peradeniya-junction={PeradeniyaKioskKey}";
        start.Environment["RAILBITES_TEST_CLOCK"] = "1";
        foreach (var (name, value) in Environment)
        {
            start.Environment[name] = value;
        }

        var output = new StringBuilder();
        _process = new Process { StartInfo = start };
        _process.OutputDataReceived += (_, e) => output.AppendLine(e.Data);
        _process.ErrorDataReceived += (_, e) => output.AppendLine(e.Data);
        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException($"RailBites exited at start-up:{System.Environment.NewLine}{output}");
            }

            try
            {
                if ((await Http.GetAsync("/health/live")).IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"RailBites did not become healthy:{System.Environment.NewLine}{output}");
    }

    // Stops the process but keeps its database, so StartAsync brings the same data back.
    public async Task StopAsync()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process?.Dispose();
        _process = null;
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    // Pins RailBites' clock to the same instant as SmartRail's test clock.
    public async Task SetNowAsync(DateTimeOffset now)
    {
        var response = await Http.PutAsync(
            "/internal/test/clock",
            new StringContent($$"""{"now":"{{now.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.fff'Z'}}"}""", Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> KioskAsync(string collectionPoint, string kioskKey, HttpMethod method, string suffix, string? json = null)
    {
        var request = new HttpRequestMessage(method, $"/v1/kiosk/collection-points/{collectionPoint}/orders{suffix}");
        request.Headers.Add("X-RailBites-Kiosk-Key", kioskKey);
        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return await Http.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        Http.Dispose();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
