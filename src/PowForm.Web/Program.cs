using PowForm.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages()
    // The forms use server-side validation only: no jQuery validation script, so no data-val attributes.
    .AddViewOptions(options => options.HtmlHelperOptions.ClientValidationEnabled = false);
builder.Services.AddSingleton<MessageBoard>();

var app = builder.Build();
app.UseStaticFiles();
app.MapRazorPages();
app.Run();

/// <summary>Lets the endpoint tests start the app.</summary>
public partial class Program;
