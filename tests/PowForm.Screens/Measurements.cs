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
}
