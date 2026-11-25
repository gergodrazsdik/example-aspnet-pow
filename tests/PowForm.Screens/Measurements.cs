using System.Globalization;
using TUnit.Playwright;

namespace PowForm.Screens;

/// <summary>
/// The raw timings, written to artifacts/measurements (or POW_OUTPUT). They take minutes and depend on the machine, so they run
/// only when asked: POW_MEASURE=1 dotnet test --project tests/PowForm.Screens --treenode-filter "/*/*/Measurements/*"
/// </summary>
public class Measurements : PageTest
{
    private static App s_app = null!;

    [Before(Class)]
    public static async Task StartApp() => s_app = await App.StartAsync();

    [After(Class)]
    public static async Task StopApp() => await s_app.DisposeAsync();

    public override Microsoft.Playwright.BrowserNewContextOptions ContextOptions(TestContext testContext) => BrowserKit.Context;

    private static void RequireOptIn()
    {
        if (Environment.GetEnvironmentVariable("POW_MEASURE") != "1")
        {
            Skip.Test("Set POW_MEASURE=1 to run the measurements.");
        }
    }

    /// <summary>Hashes per second in the worker for batch sizes 1, 16, 64, 256 and 1024: the same 20,000 hashes each time.</summary>
    [Test]
    public async Task Batch_size()
    {
        RequireOptIn();
        await Page.GotoAsync(s_app.Url.ToString());
        const int Hashes = 20_000;
        const int Runs = 15;
        var lines = new List<string> { "batch,run,ms,hashes_per_second" };
        foreach (var batch in new[] { 1, 16, 64, 256, 1024 })
        {
            // One warm-up run, not recorded.
            for (var run = 0; run <= Runs; run++)
            {
                var ms = await Page.EvaluateAsync<double>(
                    "async ([c, b]) => { const m = await import('/proof.js'); const t = performance.now(); await m.work(c, b); return performance.now() - t; }",
                    new object[] { BrowserKit.Challenge(number: Hashes, maxNumber: Hashes), batch });
                if (run > 0)
                {
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"{batch},{run},{ms:F1},{(Hashes + 1) / ms * 1000:F0}"));
                }
            }
        }

        Directory.CreateDirectory(Repo.Measurements);
        await File.WriteAllLinesAsync(Path.Combine(Repo.Measurements, "browser-batch-size.csv"), lines);
    }

    /// <summary>
    /// Solve times in the worker at three sizes, unthrottled and with the CPU slowed 4x through the
    /// DevTools protocol (a stand-in for a phone). Each run solves a fresh challenge whose secret
    /// number is uniform in 0..MaxNumber, as the server picks it.
    /// </summary>
    [Test]
    public async Task Worker_solve_times()
    {
        RequireOptIn();
        await Page.GotoAsync(s_app.Url.ToString());
        var cdp = await Context.NewCDPSessionAsync(Page);
        var lines = new List<string> { "maxNumber,throttle,run,number,ms" };
        foreach (var throttle in new[] { 1, 4 })
        {
            await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = throttle });
            foreach (var (max, runs) in new[] { (10_000, 30), (100_000, 30), (1_000_000, 20) })
            {
                for (var run = 0; run <= runs; run++)
                {
                    var number = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, max + 1);
                    var ms = await Page.EvaluateAsync<double>(
                        "async (c) => { const m = await import('/proof.js'); const t = performance.now(); await m.work(c); return performance.now() - t; }",
                        BrowserKit.Challenge(number, max));
                    if (run > 0)
                    {
                        lines.Add(string.Create(CultureInfo.InvariantCulture, $"{max},{throttle},{run},{number},{ms:F1}"));
                    }
                }
            }
        }

        Directory.CreateDirectory(Repo.Measurements);
        await File.WriteAllLinesAsync(Path.Combine(Repo.Measurements, "browser-worker.csv"), lines);
    }

    /// <summary>
    /// The main-thread fallback with the CDP throttle: the throttle does reach the page's main
    /// thread (unlike the worker, see Worker_solve_times), so this shows it working, and what the fallback costs.
    /// </summary>
    [Test]
    public async Task Main_thread_fallback_times()
    {
        RequireOptIn();
        await Page.GotoAsync(s_app.Url.ToString());
        await Page.EvaluateAsync("() => { delete window.Worker; }");
        var cdp = await Context.NewCDPSessionAsync(Page);
        var lines = new List<string> { "maxNumber,throttle,run,number,ms" };
        foreach (var throttle in new[] { 1, 4 })
        {
            await cdp.SendAsync("Emulation.setCPUThrottlingRate", new Dictionary<string, object> { ["rate"] = throttle });
            const int Max = 100_000;
            for (var run = 0; run <= 20; run++)
            {
                var number = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, Max + 1);
                var ms = await Page.EvaluateAsync<double>(
                    "async (c) => { const m = await import('/proof.js'); const t = performance.now(); await m.work(c); return performance.now() - t; }",
                    BrowserKit.Challenge(number, Max));
                if (run > 0)
                {
                    lines.Add(string.Create(CultureInfo.InvariantCulture, $"{Max},{throttle},{run},{number},{ms:F1}"));
                }
            }
        }

        Directory.CreateDirectory(Repo.Measurements);
        await File.WriteAllLinesAsync(Path.Combine(Repo.Measurements, "browser-main-thread.csv"), lines);
    }

    /// <summary>
    /// A slow-CPU stand-in that does reach the worker: Chromium pinned to one core, shared with 0 or
    /// 3 busy loops, so the worker gets all of that core or a quarter of it.
    /// </summary>
    [Test]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")] // taskset and a shell wrapper
    public async Task Contended_worker_times()
    {
        RequireOptIn();
        var wrapper = Path.Combine(Path.GetTempPath(), $"chromium-pinned-{Environment.ProcessId}.sh");
        await File.WriteAllTextAsync(wrapper, $"#!/bin/sh\nexec taskset -c 3 \"{Playwright.Chromium.ExecutablePath}\" \"$@\"\n");
        File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var burners = new List<System.Diagnostics.Process>();
        try
        {
            var lines = new List<string> { "maxNumber,burners,run,number,ms" };
            foreach (var burnerCount in new[] { 0, 3 })
            {
                for (var i = 0; i < burnerCount; i++)
                {
                    burners.Add(System.Diagnostics.Process.Start("taskset", ["-c", "3", "sh", "-c", "while :; do :; done"])!);
                }

                await using var browser = await Playwright.Chromium.LaunchAsync(new() { ExecutablePath = wrapper });
                var page = await browser.NewPageAsync();
                await page.GotoAsync(s_app.Url.ToString());
                foreach (var (max, runs) in new[] { (10_000, 30), (100_000, 30), (1_000_000, 20) })
                {
                    for (var run = 0; run <= runs; run++)
                    {
                        var number = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, max + 1);
                        var ms = await page.EvaluateAsync<double>(
                            "async (c) => { const m = await import('/proof.js'); const t = performance.now(); await m.work(c); return performance.now() - t; }",
                            BrowserKit.Challenge(number, max));
                        if (run > 0)
                        {
                            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{max},{burnerCount},{run},{number},{ms:F1}"));
                        }
                    }
                }
            }

            Directory.CreateDirectory(Repo.Measurements);
            await File.WriteAllLinesAsync(Path.Combine(Repo.Measurements, "browser-worker-contended.csv"), lines);
        }
        finally
        {
            foreach (var burner in burners)
            {
                burner.Kill();
                burner.Dispose();
            }

            File.Delete(wrapper);
        }
    }
}
