using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using TUnit.Playwright;

namespace PowForm.Screens;

/// <summary>The hard cases of the browser side: a failed challenge, a stale answer, no crypto.subtle, the default difficulty.</summary>
public class ProofFlowTests : PageTest
{
    private static App s_app = null!;

    [Before(Class)]
    public static async Task StartApp() => s_app = await App.StartAsync(new Dictionary<string, string> { ["Proof__MaxNumber"] = "20000" });

    [After(Class)]
    public static async Task StopApp() => await s_app.DisposeAsync();

    public override BrowserNewContextOptions ContextOptions(TestContext testContext) => BrowserKit.Context;

    private static async Task FillAsync(IPage page, string message)
    {
        await page.GetByLabel("Name").FillAsync("Ada Lovelace");
        await page.GetByLabel("Email").FillAsync("ada@example.com");
        await page.GetByLabel("Message").FillAsync(message);
    }

    [Test]
    public async Task A_failed_challenge_is_not_kept_and_the_next_send_recovers()
    {
        var violations = BrowserKit.WatchCsp(Page);
        var requests = 0;
        await Page.RouteAsync("**/challenge*", async route =>
        {
            // The first challenge request fails, as a dropped network would.
            if (Interlocked.Increment(ref requests) == 1)
            {
                await route.FulfillAsync(new() { Status = 503, Body = "unavailable" });
            }
            else
            {
                await route.ContinueAsync();
            }
        });

        await Page.GotoAsync(s_app.Url.ToString());
        await FillAsync(Page, "Sent after one failed challenge.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();

        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
        await Assert.That(requests).IsEqualTo(2);
        // The 503 is logged by the browser as a failed resource, which is not a CSP violation.
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task An_answer_older_than_9_minutes_is_solved_again_on_send()
    {
        var violations = BrowserKit.WatchCsp(Page);
        var requests = 0;
        await Page.RouteAsync("**/challenge*", async route =>
        {
            Interlocked.Increment(ref requests);
            await route.ContinueAsync();
        });
        await Page.Clock.InstallAsync();
        await Page.GotoAsync(s_app.Url.ToString());
        await FillAsync(Page, "Sent after lunch.");
        await Expect(Page.Locator("input[name=proof]")).Not.ToHaveValueAsync("");
        var first = await Page.Locator("input[name=proof]").InputValueAsync();

        await Page.Clock.FastForwardAsync(10 * 60 * 1000);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();

        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
        await Assert.That(requests).IsEqualTo(2);
        await Assert.That(first).IsNotEmpty();
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Without_crypto_subtle_the_form_goes_without_an_answer_and_is_refused_clearly()
    {
        await Page.AddInitScriptAsync("Object.defineProperty(window.crypto, 'subtle', { value: undefined });");
        var warnings = new List<string>();
        Page.Console += (_, m) =>
        {
            if (m.Type == "warning")
            {
                warnings.Add(m.Text);
            }
        };

        await Page.GotoAsync(s_app.Url.ToString());
        await FillAsync(Page, "No crypto here.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("secure connection (HTTPS)");
        await Assert.That(warnings.Any(w => w.Contains("secure context", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task On_plain_HTTP_at_a_LAN_address_there_is_no_crypto_subtle()
    {
        // The real thing: a non-loopback address over HTTP is not a secure context.
        var lan = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        if (lan is null)
        {
            Skip.Test("No LAN address on this machine.");
        }

        await using var app = await App.StartAsync(publicHost: lan!.ToString());
        await Page.GotoAsync(app.Url.ToString());
        var secure = await Page.EvaluateAsync<bool>("() => window.isSecureContext");
        var subtle = await Page.EvaluateAsync<string>("() => typeof crypto.subtle");

        await FillAsync(Page, "From the LAN.");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Alert)).ToContainTextAsync("secure connection (HTTPS)");
        await Page.ScreenshotAsync(new() { Path = Repo.Shot("08-insecure-context.png"), Clip = new Clip { X = 0, Y = 0, Width = 1280, Height = 600 } });

        await Assert.That(secure).IsFalse();
        await Assert.That(subtle).IsEqualTo("undefined");
    }

    [Test]
    public async Task The_workers_answer_satisfies_the_server_at_the_default_difficulty_under_the_CSP()
    {
        await using var app = await App.StartAsync(); // the default MaxNumber, 100,000
        var violations = BrowserKit.WatchCsp(Page);
        await Page.GotoAsync(app.Url.ToString());
        await FillAsync(Page, "Default difficulty.");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();

        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(20));
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task The_newsletter_form_works_the_same_way()
    {
        await Page.GotoAsync(new Uri(s_app.Url, "/newsletter").ToString());
        await Page.GetByLabel("Email").FillAsync("ada@example.com");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Subscribe" }).ClickAsync();

        await Expect(Page.GetByText("Thank you, you are on the list.")).ToBeVisibleAsync();
        await Assert.That(Regex.IsMatch(Page.Url, "/newsletter(\\?|$)")).IsTrue();
    }

    [Test]
    public async Task A_double_click_on_Send_posts_once()
    {
        await using var app = await App.StartAsync(new Dictionary<string, string> { ["Proof__MaxNumber"] = "2000" });
        var posts = 0;
        Page.Request += (_, request) =>
        {
            if (request.Method == "POST")
            {
                Interlocked.Increment(ref posts);
            }
        };

        await Page.GotoAsync(app.Url.ToString());
        await FillAsync(Page, "Clicked twice.");
        await Expect(Page.Locator("input[name=proof]")).Not.ToHaveValueAsync("");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync(new() { ClickCount = 2, Delay = 20 });

        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
        await Assert.That(await Page.GetByText("Clicked twice.").CountAsync()).IsEqualTo(1);
        await Assert.That(posts).IsEqualTo(1);
    }

    [Test]
    public async Task Back_after_a_send_does_not_reuse_the_spent_answer()
    {
        await using var app = await App.StartAsync(new Dictionary<string, string> { ["Proof__MaxNumber"] = "2000" });
        await Page.GotoAsync(app.Url.ToString());
        await FillAsync(Page, "First.");
        await Expect(Page.Locator("input[name=proof]")).Not.ToHaveValueAsync("");
        var spent = await Page.Locator("input[name=proof]").InputValueAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();

        await Page.GoBackAsync();

        // Whether the page came from the back/forward cache or was loaded again, the spent answer is gone.
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send" })).ToBeVisibleAsync();
        await Expect(Page.Locator("input[name=proof]")).ToHaveValueAsync("");
        await FillAsync(Page, "Second.");
        await Expect(Page.Locator("input[name=proof]")).Not.ToHaveValueAsync("");
        await Assert.That(await Page.Locator("input[name=proof]").InputValueAsync()).IsNotEqualTo(spent);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Second.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task A_page_restored_from_the_back_forward_cache_drops_its_spent_answer()
    {
        // Playwright's browsers run without the back/forward cache, so the restore is simulated with
        // the event the browser fires: pageshow with persisted = true.
        await Page.GotoAsync(s_app.Url.ToString());
        await FillAsync(Page, "Restored.");
        await Expect(Page.Locator("input[name=proof]")).Not.ToHaveValueAsync("");
        var spent = await Page.Locator("input[name=proof]").InputValueAsync();

        await Page.EvaluateAsync("() => window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }))");
        await Expect(Page.Locator("input[name=proof]")).ToHaveValueAsync("");

        // Typing starts the solver again (the form stays invisible): no need to wait for Send.
        await Page.GetByLabel("Message").FillAsync("Restored, and typing again.");
        await Expect(Page.Locator("input[name=proof]")).Not.ToHaveValueAsync("");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();
        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
        await Assert.That(spent).IsNotEmpty();
    }
}
