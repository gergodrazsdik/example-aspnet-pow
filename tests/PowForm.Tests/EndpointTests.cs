using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PowForm.Web;

namespace PowForm.Tests;

public class EndpointTests
{
    private static async Task<int> AcceptedCountAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/messages");
        var start = html.IndexOf("class=\"count\">", StringComparison.Ordinal) + "class=\"count\">".Length;
        return int.Parse(html[start..html.IndexOf(' ', start)], System.Globalization.CultureInfo.InvariantCulture);
    }

    [Test]
    public async Task The_challenge_endpoint_sends_json_that_is_not_cached()
    {
        using var app = new TestApp();
        var client = app.NewClient();

        var response = await client.GetAsync("/challenge?for=contact");
        var challenge = await response.Content.ReadFromJsonAsync<ProofChallenge>(JsonSerializerOptions.Web);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl!.NoStore).IsTrue();
        await Assert.That(challenge!.Algorithm).IsEqualTo("SHA-256");
        await Assert.That(challenge.MaxNumber).IsEqualTo(500);
    }

    [Test]
    [Arguments("")]
    [Arguments("?for=admin")]
    public async Task The_challenge_endpoint_refuses_an_unknown_purpose(string query)
    {
        using var app = new TestApp();

        var response = await app.NewClient().GetAsync("/challenge" + query);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    public async Task Every_response_carries_the_strict_CSP()
    {
        using var app = new TestApp();

        var response = await app.NewClient().GetAsync("/");
        var csp = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));

        await Assert.That(csp).Contains("script-src 'self'");
        await Assert.That(csp).Contains("worker-src 'self'");
        await Assert.That(csp.Contains("unsafe-inline", StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task A_post_with_the_answer_is_accepted()
    {
        using var app = new TestApp();
        var client = app.NewClient();

        var response = await TestApp.PostContactAsync(client, proof: await TestApp.AnswerAsync(client, "contact"));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(await AcceptedCountAsync(client)).IsEqualTo(1);
    }

    [Test]
    public async Task A_post_without_an_answer_is_refused_with_a_message_and_keeps_the_text()
    {
        using var app = new TestApp();
        var client = app.NewClient();

        var response = await TestApp.PostContactAsync(client, message: "Hello without proof.");
        var html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(html).Contains(ProofProblems.NoProof);
        await Assert.That(html).Contains("Hello without proof.");
        await Assert.That(await AcceptedCountAsync(client)).IsEqualTo(0);
    }

    [Test]
    public async Task A_garbage_answer_is_refused_not_a_server_error()
    {
        using var app = new TestApp();

        var response = await TestApp.PostContactAsync(app.NewClient(), proof: "12:CfDJ8nonsense");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await response.Content.ReadAsStringAsync()).Contains(ProofProblems.BadProof);
    }

    [Test]
    public async Task An_answer_for_another_form_is_refused()
    {
        using var app = new TestApp();
        var client = app.NewClient();

        var contactAnswer = await TestApp.AnswerAsync(client, "contact");
        var asNewsletter = await client.PostAsync("/newsletter", TestApp.Form(("email", "ada@example.com"), ("proof", contactAnswer)));
        var newsletterAnswer = await TestApp.AnswerAsync(client, "newsletter");
        var asContact = await TestApp.PostContactAsync(client, proof: newsletterAnswer);

        await Assert.That(asNewsletter.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(asContact.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task A_reused_answer_is_refused()
    {
        using var app = new TestApp();
        var client = app.NewClient();
        var answer = await TestApp.AnswerAsync(client, "contact");

        var first = await TestApp.PostContactAsync(client, proof: answer);
        var second = await TestApp.PostContactAsync(client, proof: answer);

        await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
        await Assert.That(await AcceptedCountAsync(client)).IsEqualTo(1);
    }

    [Test]
    public async Task An_expired_answer_is_refused()
    {
        using var app = new TestApp();
        var client = app.NewClient();
        var answer = await TestApp.AnswerAsync(client, "contact");

        app.Time.Advance(ProofOfWork.Lifetime + TimeSpan.FromSeconds(1));
        var response = await TestApp.PostContactAsync(client, proof: answer);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Forbidden);
    }

    [Test]
    public async Task A_post_refused_for_its_text_does_not_spend_the_answer()
    {
        using var app = new TestApp();
        var client = app.NewClient();
        var answer = await TestApp.AnswerAsync(client, "contact");

        // Verify last: the empty message is refused for the message, and the answer comes back in the form.
        var refused = await TestApp.PostContactAsync(client, message: "", proof: answer);
        var html = await refused.Content.ReadAsStringAsync();
        await Assert.That(refused.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        await Assert.That(html).Contains("Please write a message.");
        await Assert.That(html).Contains($"name=\"proof\" value=\"{answer}\"");
        await Assert.That(html.Contains(ProofProblems.BadProof, StringComparison.Ordinal)).IsFalse();

        // The same answer then works with a valid message.
        var accepted = await TestApp.PostContactAsync(client, message: "Now with text.", proof: answer);
        await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
    }

    [Test]
    public async Task At_the_default_difficulty_solving_and_posting_take_well_under_20_seconds()
    {
        // The default MaxNumber (100,000), not the easy test setting: native code needs tens of milliseconds.
        using var app = new TestApp(maxNumber: null);
        var client = app.NewClient();

        var clock = Stopwatch.StartNew();
        var answer = await TestApp.AnswerAsync(client, "contact");
        var response = await TestApp.PostContactAsync(client, proof: answer);
        clock.Stop();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        await Assert.That(clock.Elapsed).IsLessThan(TimeSpan.FromSeconds(20));
    }
}
