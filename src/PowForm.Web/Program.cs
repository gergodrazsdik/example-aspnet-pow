using Microsoft.AspNetCore.DataProtection;
using PowForm.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages()
    // The forms use server-side validation only: no jQuery validation script, so no data-val attributes.
    .AddViewOptions(options => options.HtmlHelperOptions.ClientValidationEnabled = false);
builder.Services.AddSingleton<MessageBoard>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ProofOptions>(builder.Configuration.GetSection("Proof"));
builder.Services.AddSingleton<ProofOfWork>();

// The signature on a challenge is Data Protection, so the keys must survive a restart and, in
// production, be shared by every replica (see README). Here they live in a local folder.
// SetApplicationName: replicas must use the same name or they can't read each other's signatures.
var keys = builder.Configuration["DataProtection:KeysPath"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
builder.Services.AddDataProtection()
    .SetApplicationName("PowForm")
    .PersistKeysToFileSystem(new DirectoryInfo(keys));

var app = builder.Build();
app.UseStaticFiles();

// A challenge for a form: nothing is stored; the signature is what lets the server check the answer later.
app.MapGet("/challenge", (string? @for, ProofOfWork proof, HttpContext http) =>
{
    if (@for is null || !ProofOfWork.Purposes.Contains(@for))
    {
        return Results.Problem("Unknown purpose.", statusCode: StatusCodes.Status400BadRequest);
    }

    http.Response.Headers.CacheControl = "no-store";
    return Results.Ok(proof.Create(@for));
});

app.MapRazorPages();
app.Run();

/// <summary>Lets the endpoint tests start the app.</summary>
public partial class Program;
