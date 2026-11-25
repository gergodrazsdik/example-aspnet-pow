namespace PowForm.Web;

/// <summary>What a form tells the visitor when its proof of work is missing or wrong, and the check that finds out.</summary>
public static class ProofProblems
{
    public const string NoProof =
        "We could not check that you are not a bot. This form needs JavaScript and a secure connection (HTTPS). "
        + "Please turn JavaScript on and send it again.";

    public const string BadProof = "The check expired or was already used. Please send it again.";

    /// <summary>Checks the answer (and uses it up).</summary>
    /// <returns>Null when the answer is good, otherwise what to tell the visitor.</returns>
    public static async Task<string?> ProblemAsync(this ProofOfWork proof, string purpose, string? answer, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(answer) ? NoProof
        : await proof.VerifyAsync(purpose, answer, cancellationToken) ? null
        : BadProof;
}
