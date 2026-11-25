namespace PowForm.Screens;

/// <summary>Step 1's flood again, against the protected form: every post without an answer is refused.</summary>
public class FloodTests
{
    [Test]
    public async Task Step06_the_flood_is_refused()
    {
        await using var app = await App.StartAsync();
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = app.Url };

        var statuses = new System.Collections.Concurrent.ConcurrentBag<int>();
        await Parallel.ForEachAsync(Enumerable.Range(1, 100), new ParallelOptions { MaxDegreeOfParallelism = 20 }, async (i, ct) =>
        {
            using var response = await http.PostAsync("/contact", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["name"] = $"Bot {i}",
                ["email"] = $"bot{i}@example.com",
                ["message"] = $"Buy cheap watches, offer {i}",
            }), ct);
            statuses.Add((int)response.StatusCode);
        });

        await Assert.That(statuses.All(s => s == 403)).IsTrue();
        var messages = await http.GetStringAsync("/messages");
        await Assert.That(messages).Contains("0 accepted so far");
    }
}
