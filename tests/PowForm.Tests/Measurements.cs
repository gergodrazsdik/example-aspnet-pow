using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using PowForm.Web;

namespace PowForm.Tests;

/// <summary>
/// Native solve times, written to artifacts/measurements (or POW_OUTPUT). They depend on the machine and take about a minute,
/// so they run only when asked: POW_MEASURE=1 dotnet test --project tests/PowForm.Tests --treenode-filter "/*/*/Measurements/*"
/// </summary>
public class Measurements
{
    private static readonly string Folder = Path.Combine(Environment.GetEnvironmentVariable("POW_OUTPUT") is { Length: > 0 } output ? Path.GetFullPath(output, FindRoot()) : Path.Combine(FindRoot(), "artifacts"), "measurements");

    [Test]
    public async Task Native_solve_times()
    {
        if (Environment.GetEnvironmentVariable("POW_MEASURE") != "1")
        {
            Skip.Test("Set POW_MEASURE=1 to run the measurements.");
        }

        var simple = new List<string> { "solver,maxNumber,run,number,hashes,ms" };
        foreach (var (max, runs) in new[] { (10_000, 200), (100_000, 200), (1_000_000, 100) })
        {
            var proof = new ProofOfWork(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider(),
                new InMemoryUsedChallenges(TimeProvider.System),
                TimeProvider.System,
                Microsoft.Extensions.Options.Options.Create(new ProofOptions { MaxNumber = max }));

            // The test project's Solver (strings and hex, one thread), then the fast one on one core and on all.
            Time(proof, max, runs, "simple", c => Solver.Solve(c) is { } a ? int.Parse(a[..a.IndexOf(':', StringComparison.Ordinal)], CultureInfo.InvariantCulture) : null, simple);
            Time(proof, max, runs, "fast-1-core", c => FastSolver.Solve(c.Salt, c.Challenge, c.MaxNumber, allCores: false), simple);
            Time(proof, max, runs, $"fast-{Environment.ProcessorCount}-cores", c => FastSolver.Solve(c.Salt, c.Challenge, c.MaxNumber, allCores: true), simple);
        }

        Directory.CreateDirectory(Folder);
        await File.WriteAllLinesAsync(Path.Combine(Folder, "native-dotnet.csv"), simple);
    }

    private static void Time(ProofOfWork proof, int max, int runs, string solver, Func<ProofChallenge, int?> solve, List<string> lines)
    {
        // Warm up the JIT before timing.
        solve(proof.Create("contact"));
        for (var run = 1; run <= runs; run++)
        {
            var challenge = proof.Create("contact");
            var clock = Stopwatch.StartNew();
            var number = solve(challenge) ?? throw new InvalidOperationException("No number found.");
            clock.Stop();
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{solver},{max},{run},{number},{number + 1},{clock.Elapsed.TotalMilliseconds:F3}"));
        }
    }

    private static string FindRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "PowForm.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("PowForm.slnx not found.");
    }
}
