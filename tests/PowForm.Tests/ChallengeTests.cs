using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PowForm.Web;

namespace PowForm.Tests;

public partial class ChallengeTests
{
    private static ProofOfWork Make(int maxNumber = 1000, TimeProvider? time = null) =>
        new(new EphemeralDataProtectionProvider(), time ?? new TestTime(), Options.Create(new ProofOptions { MaxNumber = maxNumber }));

    [Test]
    public async Task A_challenge_has_the_scheme_s_shape()
    {
        var time = new TestTime();
        var challenge = Make(1000, time).Create("contact");

        await Assert.That(challenge.Algorithm).IsEqualTo("SHA-256");
        await Assert.That(challenge.MaxNumber).IsEqualTo(1000);

        // salt = purpose.expiryUnixSeconds.randomHex: three dot-separated parts.
        var parts = challenge.Salt.Split('.');
        await Assert.That(parts.Length).IsEqualTo(3);
        await Assert.That(parts[0]).IsEqualTo("contact");
        await Assert.That(long.Parse(parts[1])).IsEqualTo(time.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds());
        await Assert.That(LowerHex24().IsMatch(parts[2])).IsTrue();

        // challenge = SHA-256 hex of salt + number, for some number in 0..MaxNumber.
        await Assert.That(challenge.Challenge.Length).IsEqualTo(64);
        var found = Enumerable.Range(0, challenge.MaxNumber + 1).Any(n => ProofOfWork.HashOf(challenge.Salt, n) == challenge.Challenge);
        await Assert.That(found).IsTrue();
    }

    [Test]
    public async Task The_signature_is_base64url_and_safe_after_a_colon()
    {
        var signature = Make().Create("contact").Signature;

        await Assert.That(Base64Url().IsMatch(signature)).IsTrue();
        await Assert.That(signature.Contains(':')).IsFalse();
    }

    [Test]
    public async Task Every_challenge_is_different()
    {
        var proof = Make();
        var a = proof.Create("contact");
        var b = proof.Create("contact");

        await Assert.That(a.Salt).IsNotEqualTo(b.Salt);
        await Assert.That(a.Signature).IsNotEqualTo(b.Signature);
    }

    [Test]
    public async Task An_unknown_purpose_is_refused()
    {
        await Assert.That(() => Make().Create("admin")).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0, 1)]
    [Arguments(-5, 1)]
    [Arguments(50_000_000, 10_000_000)]
    public async Task MaxNumber_stays_in_range(int configured, int used)
    {
        await Assert.That(Make(configured).MaxNumber).IsEqualTo(used);
    }

    [GeneratedRegex("^[0-9a-f]{24}$")]
    private static partial Regex LowerHex24();

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex Base64Url();
}
