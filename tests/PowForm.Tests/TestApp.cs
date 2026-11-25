using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PowForm.Web;
using TUnit.AspNetCore;

namespace PowForm.Tests;

/// <summary>
/// The app in memory, with a clock the test moves, its own Data Protection keys folder, and an easy
/// proof (MaxNumber 500 unless a test asks for the default): the tests exercise the logic, not the
/// hashing speed.
/// </summary>
public sealed class TestApp(int? maxNumber = 500) : TestWebApplicationFactory<Program>
{
    private readonly string _keys = Directory.CreateTempSubdirectory("powform-test-keys-").FullName;

    public TestTime Time { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("DataProtection:KeysPath", _keys);
        if (maxNumber is { } max)
        {
            builder.UseSetting("Proof:MaxNumber", max.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time)));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_keys))
        {
            Directory.Delete(_keys, recursive: true);
        }
    }

    public HttpClient NewClient() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Gets a challenge and solves it, as the browser does.</summary>
    public static async Task<string> AnswerAsync(HttpClient client, string purpose)
    {
        var challenge = await client.GetFromJsonAsync<ProofChallenge>($"/challenge?for={purpose}", JsonSerializerOptions.Web);
        return Solver.Solve(challenge!) ?? throw new InvalidOperationException("No number matches.");
    }

    public static Task<HttpResponseMessage> PostContactAsync(HttpClient client, string? message = "Hello.", string? proof = null) =>
        client.PostAsync("/contact", Form(("name", "Ada"), ("email", "ada@example.com"), ("message", message), ("proof", proof)));

    public static FormUrlEncodedContent Form(params (string Key, string? Value)[] fields) =>
        new(fields.Where(f => f.Value is not null).Select(f => KeyValuePair.Create(f.Key, f.Value!)));
}
