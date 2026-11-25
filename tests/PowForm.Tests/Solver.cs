using System.Globalization;
using PowForm.Web;

namespace PowForm.Tests;

/// <summary>Does what the browser does: tries 0, 1, 2, ... until the hash matches.</summary>
public static class Solver
{
    /// <returns>The answer, <c>number:signature</c>, or null when no number up to MaxNumber matches.</returns>
    public static string? Solve(ProofChallenge challenge)
    {
        for (var number = 0; number <= challenge.MaxNumber; number++)
        {
            if (ProofOfWork.HashOf(challenge.Salt, number) == challenge.Challenge)
            {
                return string.Create(CultureInfo.InvariantCulture, $"{number}:{challenge.Signature}");
            }
        }

        return null;
    }
}
