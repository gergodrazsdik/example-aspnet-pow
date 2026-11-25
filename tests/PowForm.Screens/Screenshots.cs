using Microsoft.Playwright;
using TUnit.Playwright;

namespace PowForm.Screens;

/// <summary>The article's pictures, one per step where something is visible; each also checks what it shows.</summary>
public class Screenshots : PageTest
{
    private static App s_app = null!;

    [Before(Class)]
    public static async Task StartApp() => s_app = await App.StartAsync();

    [After(Class)]
    public static async Task StopApp() => await s_app.DisposeAsync();

    public override BrowserNewContextOptions ContextOptions(TestContext testContext) => new()
    {
        ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
        ColorScheme = ColorScheme.Light,
        Locale = "en-US",
    };

    [Test]
    public async Task Step01_the_form()
    {
        await Page.GotoAsync(s_app.Url.ToString());
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Send" })).ToBeVisibleAsync();
        await Page.ScreenshotAsync(new() { Path = Repo.Shot("01-form.png"), Clip = new Clip { X = 0, Y = 0, Width = 1280, Height = 600 } });
    }

    [Test]
    public async Task Step01_the_flood()
    {
        // What a bot does: 100 posts, no browser. Unprotected, every one is accepted.
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = s_app.Url };
        await Parallel.ForEachAsync(Enumerable.Range(1, 100), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (i, ct) =>
        {
            using var response = await http.PostAsync("/contact", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["name"] = $"Bot {i}",
                ["email"] = $"bot{i}@example.com",
                ["message"] = $"Buy cheap watches, offer {i}",
            }), ct);
            await Assert.That((int)response.StatusCode).IsEqualTo(302);
        });

        await Page.GotoAsync(new Uri(s_app.Url, "/messages").ToString());
        await Expect(Page.GetByText("100 accepted so far")).ToBeVisibleAsync();
        await Page.ScreenshotAsync(new() { Path = Repo.Shot("01-flood.png"), Clip = new Clip { X = 0, Y = 0, Width = 1280, Height = 620 } });
    }
}
