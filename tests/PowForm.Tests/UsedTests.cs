using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using PowForm.Web;

namespace PowForm.Tests;

public class UsedTests
{
    private readonly TestTime _time = new();
    private readonly EphemeralDataProtectionProvider _keys = new();

    private ProofOfWork Make(IUsedChallenges store) =>
        new(_keys, store, _time, Options.Create(new ProofOptions { MaxNumber = 1000 }));

    [Test]
    public async Task An_answer_works_once()
    {
        var proof = Make(new InMemoryUsedChallenges(_time));
        var answer = Solver.Solve(proof.Create("contact"))!;

        await Assert.That(await proof.VerifyAsync("contact", answer)).IsTrue();
        await Assert.That(await proof.VerifyAsync("contact", answer)).IsFalse();
    }

    [Test]
    public async Task A_wrong_answer_does_not_use_the_challenge()
    {
        var store = new InMemoryUsedChallenges(_time);
        var proof = Make(store);
        var answer = Solver.Solve(proof.Create("contact"))!;

        await Assert.That(await proof.VerifyAsync("newsletter", answer)).IsFalse();
        await Assert.That(store.Count).IsEqualTo(0);
        await Assert.That(await proof.VerifyAsync("contact", answer)).IsTrue();
    }

    [Test]
    public async Task Two_instances_with_their_own_stores_both_accept_the_same_answer()
    {
        // The failure mode of an in-memory store behind several replicas: A's used answer is fresh on B.
        var a = Make(new InMemoryUsedChallenges(_time));
        var b = Make(new InMemoryUsedChallenges(_time));
        var answer = Solver.Solve(a.Create("contact"))!;

        await Assert.That(await a.VerifyAsync("contact", answer)).IsTrue();
        await Assert.That(await b.VerifyAsync("contact", answer)).IsTrue();
    }

    [Test]
    public async Task Two_instances_with_one_shared_store_accept_it_once()
    {
        var shared = new InMemoryUsedChallenges(_time);
        var a = Make(shared);
        var b = Make(shared);
        var answer = Solver.Solve(a.Create("contact"))!;

        await Assert.That(await a.VerifyAsync("contact", answer)).IsTrue();
        await Assert.That(await b.VerifyAsync("contact", answer)).IsFalse();
    }

    [Test]
    public async Task Of_many_simultaneous_uses_exactly_one_wins()
    {
        var proof = Make(new InMemoryUsedChallenges(_time));
        var answer = Solver.Solve(proof.Create("contact"))!;

        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => proof.VerifyAsync("contact", answer))));

        await Assert.That(results.Count(r => r)).IsEqualTo(1);
    }

    [Test]
    public async Task Expired_entries_are_swept()
    {
        var store = new InMemoryUsedChallenges(_time);
        var proof = Make(store);
        for (var i = 0; i < 5; i++)
        {
            await proof.VerifyAsync("contact", Solver.Solve(proof.Create("contact")));
        }

        await Assert.That(store.Count).IsEqualTo(5);

        // Past their expiry, the next use sweeps them out; only the new entry stays.
        _time.Advance(ProofOfWork.Lifetime + TimeSpan.FromMinutes(2));
        await store.TryUseAsync("fresh", _time.GetUtcNow().AddMinutes(10));

        await Assert.That(store.Count).IsEqualTo(1);
    }

    [Test]
    public async Task An_entry_outlives_its_expiry_by_a_margin()
    {
        var store = new InMemoryUsedChallenges(_time);
        var expires = _time.GetUtcNow().AddMinutes(10);
        await store.TryUseAsync("first", expires);

        // Just past the expiry, a sweep must not drop the entry: a late replay would be accepted.
        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(30));
        await store.TryUseAsync("second", _time.GetUtcNow().AddMinutes(10));

        await Assert.That(store.Count).IsEqualTo(2);
    }

    [Test]
    public async Task An_already_expired_challenge_is_refused_and_not_stored()
    {
        var store = new InMemoryUsedChallenges(_time);

        await Assert.That(await store.TryUseAsync("late", _time.GetUtcNow().AddSeconds(-1))).IsFalse();
        await Assert.That(store.Count).IsEqualTo(0);
    }
}
