using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace PowForm.Web.Pages;

public class MessagesModel(MessageBoard board) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public bool Sent { get; set; }

    /// <summary>The accepted messages, newest first.</summary>
    public IReadOnlyList<Accepted> Latest { get; private set; } = [];

    public void OnGet() => Latest = board.Latest();
}
