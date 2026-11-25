using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace PowForm.Web;

/// <summary>How hard a challenge is. Bound from the "Proof" section of the configuration.</summary>
public sealed class ProofOptions
{
    public const int Smallest = 1;
    public const int Largest = 10_000_000;

    /// <summary>The largest number the browser may have to try. The secret number is random in 0..MaxNumber.</summary>
    public int MaxNumber { get; set; } = 100_000;
}

/// <summary>A challenge for the browser to solve, as <c>GET /challenge</c> sends it.</summary>
/// <param name="Algorithm">The hash: always <c>SHA-256</c>.</param>
/// <param name="Challenge">The hash to match, in lowercase hex: SHA-256 of the salt followed by the secret number.</param>
/// <param name="Salt">What goes before the number: <c>purpose.expiryUnixSeconds.randomHex</c>.</param>
/// <param name="MaxNumber">The largest number to try.</param>
/// <param name="Signature">What the browser sends back, after a colon, with the number it found.</param>
public sealed record ProofChallenge(string Algorithm, string Challenge, string Salt, int MaxNumber, string Signature);

/// <summary>
/// The invisible proof of work, inspired by ALTCHA: the server picks a secret number and sends
/// the SHA-256 of a salt followed by that number; the browser tries 0, 1, 2, ... until the hash
/// matches. The signature is Data Protection over the hash and the salt, so any replica can check an
/// answer without having stored the challenge. The salt carries the purpose and the expiry.
/// </summary>
public sealed class ProofOfWork(IDataProtectionProvider protection, TimeProvider time, IOptions<ProofOptions> options)
{
    /// <summary>The purposes a challenge can be for. An answer works only for the purpose it was issued for.</summary>
    public static readonly IReadOnlySet<string> Purposes = new HashSet<string>(StringComparer.Ordinal) { "contact", "newsletter" };

    /// <summary>How long a challenge can be answered.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IDataProtector _protector = protection.CreateProtector("PowForm.ProofOfWork");

    /// <summary>The largest number a challenge uses, within the allowed range.</summary>
    public int MaxNumber => Math.Clamp(options.Value.MaxNumber, ProofOptions.Smallest, ProofOptions.Largest);

    /// <summary>Makes a challenge. Nothing is stored.</summary>
    /// <param name="purpose">What its answer may be used for: one of <see cref="Purposes"/>.</param>
    public ProofChallenge Create(string purpose)
    {
        if (!Purposes.Contains(purpose))
        {
            throw new ArgumentException($"Unknown purpose '{purpose}'.", nameof(purpose));
        }

        var expires = time.GetUtcNow() + Lifetime;
        var random = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        var salt = string.Create(CultureInfo.InvariantCulture, $"{purpose}.{expires.ToUnixTimeSeconds()}.{random}");
        var number = RandomNumberGenerator.GetInt32(0, MaxNumber + 1);
        var challenge = HashOf(salt, number);
        return new ProofChallenge("SHA-256", challenge, salt, MaxNumber, _protector.Protect($"{challenge}\n{salt}"));
    }

    /// <summary>Checks an answer in one pass.</summary>
    /// <param name="purpose">What the answer is used for.</param>
    /// <param name="answer">The answer: <c>number:signature</c>.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>True when the answer is right, in time and for this purpose.</returns>
    public Task<bool> VerifyAsync(string purpose, string? answer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Check(purpose, answer) is not null);
    }

    // Everything but the once-only check: parse, unprotect, purpose, expiry, hash.
    private (string Challenge, DateTimeOffset Expires)? Check(string purpose, string? answer)
    {
        if (!TryRead(answer, out var number, out var signature) || Unprotect(signature) is not { } payload)
        {
            return null;
        }

        // The payload is what Create signed: "challenge\nsalt". Only the signed salt is used from here on.
        var lines = payload.Split('\n');
        var parts = lines.Length == 2 ? lines[1].Split('.') : [];
        if (parts.Length != 3
            || !string.Equals(parts[0], purpose, StringComparison.Ordinal)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
        {
            return null;
        }

        var expires = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var (challenge, salt) = (lines[0], lines[1]);
        if (expires < time.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(HashOf(salt, number)), Encoding.ASCII.GetBytes(challenge)))
        {
            return null;
        }

        return (challenge, expires);
    }

    private static bool TryRead(string? answer, out int number, out string signature)
    {
        number = 0;
        signature = "";
        var colon = answer?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (answer is null || answer.Length > 1024 || colon <= 0
            || !int.TryParse(answer.AsSpan(0, colon), NumberStyles.None, CultureInfo.InvariantCulture, out number))
        {
            return false;
        }

        signature = answer[(colon + 1)..];
        return signature.Length > 0;
    }

    private string? Unprotect(string signature)
    {
        try
        {
            return _protector.Unprotect(signature);
        }
        catch (CryptographicException)
        {
            // Not ours, altered, or signed with a key we don't have.
            return null;
        }
    }

    /// <summary>The hash of a salt followed by a number, as the browser computes it.</summary>
    /// <returns>Lowercase hex.</returns>
    public static string HashOf(string salt, int number) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(salt + number.ToString(CultureInfo.InvariantCulture))));
}
