using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PowForm.Web.Pages;

// A second form with its own purpose, to show that an answer is bound to one form: an answer
// solved for the contact form is refused here, and the other way round.
[IgnoreAntiforgeryToken]
public class NewsletterModel(ProofOfWork proof, ILogger<NewsletterModel> log) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Please enter an email address, such as you@example.com.")]
    [EmailAddress(ErrorMessage = "Please enter an email address, such as you@example.com.")]
    public string? Email { get; set; }

    [BindProperty]
    public string? Proof { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Subscribed { get; set; }

    public string? ProofProblem { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
            return Page();
        }

        if (await proof.ProblemAsync("newsletter", Proof, cancellationToken) is { } problem)
        {
            ProofProblem = problem;
            Proof = null;
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return Page();
        }

        log.LogInformation("Subscribed {Email}", Email);
        return RedirectToPage(new { subscribed = true });
    }
}
