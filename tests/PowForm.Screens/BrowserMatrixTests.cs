using System.Globalization;
using Microsoft.Playwright;
using TUnit.Playwright;

namespace PowForm.Screens;

/// <summary>
/// The same flow in every browser engine that is installed: the module and the worker load under the
/// strict CSP, the answer satisfies the server, and (with POW_MEASURE=1) the solve times are recorded.
/// A browser that isn't installed is skipped. Install more with playwright.ps1 (see README); WebKit
/// also needs system libraries, so it only runs where those are present.
/// </summary>
public class BrowserMatrixTests : PlaywrightTest
{
    private static App s_app = null!;

    [Before(Class)]
    public static async Task StartApp() => s_app = await App.StartAsync(new Dictionary<string, string> { ["Proof__MaxNumber"] = "20000" });

    [After(Class)]
    public static async Task StopApp() => await s_app.DisposeAsync();

    [Test]
    [Arguments("chromium")]
    [Arguments("firefox")]
    [Arguments("webkit")]
    public async Task The_form_works_in(string engine)
    {
        IBrowser browser;
        try
        {
            var type = engine switch { "firefox" => Playwright.Firefox, "webkit" => Playwright.Webkit, _ => Playwright.Chromium };
            browser = await type.LaunchAsync();
        }
        catch (PlaywrightException)
        {
            Skip.Test($"{engine} is not installed (or lacks system libraries) here.");
            return;
        }

        await using (browser)
        {
            var page = await browser.NewPageAsync();
            var violations = BrowserKit.WatchCsp(page);
            await page.GotoAsync(s_app.Url.ToString());
            await page.GetByLabel("Name").FillAsync("Ada Lovelace");
            await page.GetByLabel("Email").FillAsync("ada@example.com");
            await page.GetByLabel("Message").FillAsync($"Hello from {engine}.");
            await page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
            await Expect(page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
            await Assert.That(violations).IsEmpty();

            if (Environment.GetEnvironmentVariable("POW_MEASURE") == "1")
            {
                await MeasureAsync(engine, browser.Version, page);
            }
        }
    }

    // 100,000 hashes' worth of worker time, 20 runs: the number is uniform in 0..MaxNumber, as the server picks it.
    private static async Task MeasureAsync(string engine, string version, IPage page)
    {
        const int Max = 100_000;
        var lines = new List<string>();
        for (var run = 0; run <= 20; run++)
        {
            var number = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, Max + 1);
            var ms = await page.EvaluateAsync<double>(
                "async (c) => { const m = await import('/proof.js'); const t = performance.now(); await m.work(c); return performance.now() - t; }",
                BrowserKit.Challenge(number, Max));
            if (run > 0)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{engine},{version},{Max},{run},{number},{ms:F1}"));
            }
        }

        Directory.CreateDirectory(Repo.Measurements);
        await File.WriteAllLinesAsync(Path.Combine(Repo.Measurements, $"browser-{engine}.csv"), ["engine,version,maxNumber,run,number,ms", .. lines]);
    }
}
