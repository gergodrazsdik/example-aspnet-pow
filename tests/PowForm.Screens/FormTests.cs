using Microsoft.Playwright;
using TUnit.Playwright;

namespace PowForm.Screens;

/// <summary>
/// The form as a visitor uses it: the proof is solved while they type, and the page's pictures are
/// taken on the way. Every test fails on a Content-Security-Policy violation.
/// </summary>
public class FormTests : PageTest
{
    private static App s_app = null!;

    // A small MaxNumber keeps the tests quick: the solver is the same.
    [Before(Class)]
    public static async Task StartApp() => s_app = await App.StartAsync(new Dictionary<string, string> { ["Proof__MaxNumber"] = "20000" });

    [After(Class)]
    public static async Task StopApp() => await s_app.DisposeAsync();

    public override BrowserNewContextOptions ContextOptions(TestContext testContext) => BrowserKit.Context;

    private ILocator Send => Page.GetByRole(AriaRole.Button, new() { Name = "Send" });

    private async Task FillAsync(string name = "Ada Lovelace", string email = "ada@example.com", string message = "Hello from the example.")
    {
        await Page.GetByLabel("Name").FillAsync(name);
        await Page.GetByLabel("Email").FillAsync(email);
        await Page.GetByLabel("Message").FillAsync(message);
    }

    [Test]
    public async Task Step01_the_form()
    {
        var violations = BrowserKit.WatchCsp(Page);
        await Page.GotoAsync(s_app.Url.ToString());
        await Expect(Send).ToBeVisibleAsync();
        await Page.ScreenshotAsync(new() { Path = Repo.Shot("01-form.png"), Clip = new Clip { X = 0, Y = 0, Width = 1280, Height = 600 } });
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Step06_the_proof_is_ready_before_the_visitor_sends()
    {
        var violations = BrowserKit.WatchCsp(Page);
        await Page.GotoAsync(s_app.Url.ToString());
        await Assert.That(await Page.Locator("input[name=proof]").InputValueAsync()).IsEmpty();
        await Page.ScreenshotAsync(new() { Path = Repo.Shot("06-idle.png"), Clip = new Clip { X = 0, Y = 0, Width = 1280, Height = 600 } });

        await FillAsync(message: "Typed while the proof was solved.");

        // Typing started the solver: the hidden field fills by itself, within a moment.
        await Expect(Page.Locator("input[name=proof]")).ToHaveValueAsync(new System.Text.RegularExpressions.Regex(@"^\d+:CfDJ8[\w-]+$"));
        await Send.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/messages"));
        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
        await Expect(Page.GetByText("Typed while the proof was solved.")).ToBeVisibleAsync();
        await Page.ScreenshotAsync(new() { Path = Repo.Shot("06-accepted.png"), Clip = new Clip { X = 0, Y = 0, Width = 1280, Height = 400 } });
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Step06_sending_before_the_answer_waits_for_it()
    {
        var violations = BrowserKit.WatchCsp(Page);

        // Slow the challenge down so the visitor is faster than the solver (the real solve at this
        // MaxNumber takes well under a second).
        await Page.RouteAsync("**/challenge*", async route =>
        {
            await Task.Delay(2500);
            await route.ContinueAsync();
        });
        await Page.GotoAsync(s_app.Url.ToString());
        await FillAsync();
        await Send.ClickAsync();

        var status = Page.Locator("[data-proof-status]");
        await Expect(status).ToHaveTextAsync(new System.Text.RegularExpressions.Regex("Just a moment"));
        await Assert.That(await status.GetAttributeAsync("aria-live")).IsEqualTo("polite");
        await Page.ScreenshotAsync(new() { Path = Repo.Shot("06-waiting.png"), Clip = new Clip { X = 0, Y = 0, Width = 1280, Height = 640 } });

        // The submit goes through by itself once the answer is there.
        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Step06_a_post_without_an_answer_is_refused_with_a_message_and_keeps_the_text()
    {
        // No JavaScript: the form still posts, with an empty proof field.
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            ColorScheme = ColorScheme.Light,
            Locale = "en-US",
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(s_app.Url.ToString());
        await page.GetByLabel("Name").FillAsync("Ada Lovelace");
        await page.GetByLabel("Email").FillAsync("ada@example.com");
        await page.GetByLabel("Message").FillAsync("Hello without JavaScript.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Send" }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("could not check that you are not a bot");
        await Expect(page.GetByLabel("Message")).ToHaveValueAsync("Hello without JavaScript.");
        await page.ScreenshotAsync(new() { Path = Repo.Shot("06-refused.png"), Clip = new Clip { X = 0, Y = 0, Width = 1280, Height = 610 } });
    }

    [Test]
    public async Task Step06_a_post_refused_for_its_text_keeps_its_answer()
    {
        var violations = BrowserKit.WatchCsp(Page);
        await Page.GotoAsync(s_app.Url.ToString());

        // The browser itself would stop an address that isn't one (type=email); switch its validation
        // off, as a bot or an old browser would, so that the server's check is what refuses it.
        await Page.EvaluateAsync("() => { document.forms[0].noValidate = true; }");
        await FillAsync(email: "not-an-address");
        await Expect(Page.Locator("input[name=proof]")).Not.ToHaveValueAsync("");
        var answer = await Page.Locator("input[name=proof]").InputValueAsync();
        await Send.ClickAsync();

        // The server refuses the text first, without spending the answer, and sends the answer back.
        await Expect(Page.GetByText("Please enter an email address, such as you@example.com.")).ToBeVisibleAsync();
        await Assert.That(await Page.Locator("input[name=proof]").InputValueAsync()).IsEqualTo(answer);

        await Page.GetByLabel("Email").FillAsync("ada@example.com");
        await Send.ClickAsync();
        await Expect(Page.GetByText("Thank you, your message was sent.")).ToBeVisibleAsync();
        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Step06_the_form_on_a_phone()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            ColorScheme = ColorScheme.Light,
            Locale = "en-US",
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(s_app.Url.ToString());
        await page.GetByLabel("Name").FillAsync("Ada Lovelace");
        await page.GetByLabel("Email").FillAsync("ada@example.com");
        await page.GetByLabel("Message").FillAsync("Hello from a small screen.");
        await Expect(page.Locator("input[name=proof]")).Not.ToHaveValueAsync("");
        await page.ScreenshotAsync(new() { Path = Repo.Shot("06-form-390.png"), Clip = new Clip { X = 0, Y = 0, Width = 390, Height = 660 } });
    }
}
