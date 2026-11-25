using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PowForm.Web.Pages;

// Antiforgery is off: the form has no session or cookie to forge a request against, and the
// example is about the proof of work, not CSRF.
[IgnoreAntiforgeryToken]
public class ContactModel(ProofOfWork proof, MessageBoard board, ILogger<ContactModel> log) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Please enter your name.")]
    [StringLength(100, ErrorMessage = "Please keep your name under 100 characters.")]
    public string? Name { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Please enter an email address, such as you@example.com.")]
    [EmailAddress(ErrorMessage = "Please enter an email address, such as you@example.com.")]
    public string? Email { get; set; }

    [BindProperty]
    [Required(ErrorMessage = "Please write a message.")]
    [StringLength(2000, ErrorMessage = "Please keep your message under 2,000 characters.")]
    public string? Message { get; set; }

    /// <summary>The proof of work's answer, <c>number:signature</c>, from the form's hidden field.</summary>
    [BindProperty]
    public string? Proof { get; set; }

    /// <summary>Why the proof was refused, for the visitor.</summary>
    public string? ProofProblem { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        // Fields first: a post refused for its text keeps its answer (verifying uses it up).
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Page();
        }

        // The proof last, just before saving.
        if (await proof.ProblemAsync("contact", Proof, cancellationToken) is { } problem)
        {
            ProofProblem = problem;
            Proof = null;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Page();
        }

        board.Add(Name!, Email!, Message!);
        log.LogInformation("Message from {Name} <{Email}>: {Message}", Name, Email, Message);
        return RedirectToPage("/Messages", new { sent = true });
    }
}
