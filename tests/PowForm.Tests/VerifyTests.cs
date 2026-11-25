using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PowForm.Web;

namespace PowForm.Tests;

public class VerifyTests
{
    private readonly TestTime _time = new();
    private readonly EphemeralDataProtectionProvider _keys = new();

    private ProofOfWork Make(IDataProtectionProvider? keys = null, IUsedChallenges? store = null) =>
        new(keys ?? _keys, store ?? new InMemoryUsedChallenges(_time), _time, Options.Create(new ProofOptions { MaxNumber = 1000 }));

    private static string Answer(ProofChallenge challenge) =>
        Solver.Solve(challenge) ?? throw new InvalidOperationException("No number matches.");

    [Test]
    public async Task The_right_answer_is_accepted()
    {
        var proof = Make();
        var answer = Answer(proof.Create("contact"));

        await Assert.That(await proof.VerifyAsync("contact", answer)).IsTrue();
    }

    [Test]
    public async Task Another_instance_with_the_same_keys_accepts_it()
    {
        // The point of signing with Data Protection: a second instance never saw the challenge.
        var answer = Answer(Make().Create("contact"));

        await Assert.That(await Make().VerifyAsync("contact", answer)).IsTrue();
    }

    [Test]
    public async Task A_tampered_signature_is_refused()
    {
        var proof = Make();
        var answer = Answer(proof.Create("contact"));
        var last = answer[^1];
        var tampered = answer[..^1] + (last == 'A' ? 'B' : 'A');

        await Assert.That(await proof.VerifyAsync("contact", tampered)).IsFalse();
    }

    [Test]
    public async Task A_tampered_number_is_refused()
    {
        var proof = Make();
        var answer = Answer(proof.Create("contact"));
        var colon = answer.IndexOf(':', StringComparison.Ordinal);
        var other = int.Parse(answer[..colon], System.Globalization.CultureInfo.InvariantCulture) + 1;

        await Assert.That(await proof.VerifyAsync("contact", $"{other}{answer[colon..]}")).IsFalse();
    }

    [Test]
    public async Task An_answer_for_another_purpose_is_refused()
    {
        var proof = Make();
        var answer = Answer(proof.Create("contact"));

        await Assert.That(await proof.VerifyAsync("newsletter", answer)).IsFalse();
    }

    [Test]
    public async Task An_expired_answer_is_refused()
    {
        var proof = Make();
        var answer = Answer(proof.Create("contact"));

        _time.Advance(ProofOfWork.Lifetime - TimeSpan.FromSeconds(30));
        await Assert.That(await proof.VerifyAsync("contact", answer)).IsTrue();

        _time.Advance(TimeSpan.FromMinutes(1));
        await Assert.That(await proof.VerifyAsync("contact", answer)).IsFalse();
    }

    [Test]
    public async Task A_signature_from_other_keys_is_refused()
    {
        var answer = Answer(Make().Create("contact"));

        await Assert.That(await Make(new EphemeralDataProtectionProvider()).VerifyAsync("contact", answer)).IsFalse();
    }

    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments(":")]
    [Arguments("12")]
    [Arguments("12:")]
    [Arguments(":abc")]
    [Arguments("abc:def")]
    [Arguments("-1:abc")]
    [Arguments("99999999999999999999:abc")]
    [Arguments("12:not-a-signature")]
    [Arguments("12:CfDJ8AAAA")]
    public async Task Garbage_is_refused_without_throwing(string? garbage)
    {
        await Assert.That(await Make().VerifyAsync("contact", garbage)).IsFalse();
    }

    [Test]
    public async Task An_oversized_answer_is_refused()
    {
        await Assert.That(await Make().VerifyAsync("contact", "1:" + new string('A', 5000))).IsFalse();
    }
}
