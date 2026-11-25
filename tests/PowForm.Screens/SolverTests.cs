using Microsoft.Playwright;
using TUnit.Playwright;

namespace PowForm.Screens;

/// <summary>The browser's solver: finds the number, under the strict CSP.</summary>
public class SolverTests : PageTest
{
    private static App s_app = null!;

    [Before(Class)]
    public static async Task StartApp() => s_app = await App.StartAsync();

    [After(Class)]
    public static async Task StopApp() => await s_app.DisposeAsync();

    public override BrowserNewContextOptions ContextOptions(TestContext testContext) => BrowserKit.Context;

    [Test]
    public async Task The_worker_finds_the_number_under_the_CSP()
    {
        var violations = BrowserKit.WatchCsp(Page);
        var response = await Page.GotoAsync(s_app.Url.ToString());
        await Assert.That(response!.Headers["content-security-policy"]).Contains("worker-src 'self'");

        var number = await Page.EvaluateAsync<int?>(
            "async (c) => (await import('/proof.js')).work(c)",
            BrowserKit.Challenge(number: 4321, maxNumber: 10_000));

        await Assert.That(number).IsEqualTo(4321);
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Without_Worker_the_main_thread_finds_it()
    {
        var violations = BrowserKit.WatchCsp(Page);
        await Page.GotoAsync(s_app.Url.ToString());

        // Hide Worker before the module loads, as an old browser or a blocking policy would.
        await Page.EvaluateAsync("() => { delete window.Worker; }");
        var number = await Page.EvaluateAsync<int?>(
            "async (c) => (await import('/proof.js')).work(c)",
            BrowserKit.Challenge(number: 777, maxNumber: 2_000));

        await Assert.That(number).IsEqualTo(777);
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task A_number_that_does_not_exist_gives_null()
    {
        await Page.GotoAsync(s_app.Url.ToString());
        var challenge = BrowserKit.Challenge(number: 5000, maxNumber: 5000);
        challenge["maxNumber"] = 100;

        var number = await Page.EvaluateAsync<int?>("async (c) => (await import('/proof.js')).work(c)", challenge);

        await Assert.That(number).IsNull();
    }
}
