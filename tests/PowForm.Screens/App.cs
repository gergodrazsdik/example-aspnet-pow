using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace PowForm.Screens;

/// <summary>
/// The web app, started from its build output as a process on a free port, with the settings a
/// test asks for. A process (not an in-memory server) because the browser needs a real socket.
/// </summary>
public sealed class App : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _keys;

    private App(Process process, Uri url, string keys)
    {
        _process = process;
        Url = url;
        _keys = keys;
    }

    public Uri Url { get; }

    /// <summary>
    /// Starts the app. Settings use the configuration key with "__" for ":" (Proof__MaxNumber).
    /// With a publicHost (a LAN address) it listens on every interface and its Url uses that host.
    /// </summary>
    public static async Task<App> StartAsync(IReadOnlyDictionary<string, string>? settings = null, string? publicHost = null)
    {
        var port = FreePort();
        var url = new Uri($"http://{publicHost ?? "localhost"}:{port}");
        var listen = publicHost is null ? url.ToString() : $"http://0.0.0.0:{port}";
        var keys = Directory.CreateTempSubdirectory("powform-keys-").FullName;
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Path.Combine(Repo.Root, "artifacts", "bin", "PowForm.Web", Repo.Configuration, "PowForm.Web.dll"));
        start.ArgumentList.Add("--urls");
        start.ArgumentList.Add(listen);
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        start.Environment["DataProtection__KeysPath"] = keys;
        foreach (var (key, value) in settings ?? new Dictionary<string, string>())
        {
            start.Environment[key] = value;
        }

        var process = Process.Start(start) ?? throw new InvalidOperationException("The app did not start.");
        var log = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => log.AppendLine(e.Data);
        process.ErrorDataReceived += (_, e) => log.AppendLine(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var http = new HttpClient();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"The app exited:\n{log}");
            }

            try
            {
                using var response = await http.GetAsync(url);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return new App(process, url, keys);
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }

            await Task.Delay(100);
        }

        process.Kill(entireProcessTree: true);
        throw new TimeoutException($"The app did not answer in 10 seconds:\n{log}");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
        Directory.Delete(_keys, recursive: true);
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
