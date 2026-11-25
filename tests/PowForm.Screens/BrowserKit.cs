using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Playwright;

namespace PowForm.Screens;

/// <summary>Helpers shared by the browser tests.</summary>
internal static class BrowserKit
{
    public static BrowserNewContextOptions Context => new()
    {
        ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
        ColorScheme = ColorScheme.Light,
        Locale = "en-US",
    };

    /// <summary>A challenge whose secret number is known, as the page's script receives it.</summary>
    public static Dictionary<string, object> Challenge(int number, int maxNumber)
    {
        var salt = $"contact.4102444800.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12))}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + number.ToString(CultureInfo.InvariantCulture))));
        return new() { ["salt"] = salt, ["challenge"] = hash, ["maxNumber"] = maxNumber };
    }

    /// <summary>Collects Content-Security-Policy violations (page events and console errors, which is where a worker's show up).</summary>
    public static List<string> WatchCsp(IPage page)
    {
        var violations = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase)
                || message.Text.Contains("Content-Security-Policy", StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(message.Text);
            }
        };
        page.PageError += (_, error) => violations.Add(error);
        return violations;
    }
}
