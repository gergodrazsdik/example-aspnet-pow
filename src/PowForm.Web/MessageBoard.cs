namespace PowForm.Web;

/// <summary>An accepted message.</summary>
public sealed record Accepted(DateTimeOffset At, string Name, string Email, string Message);

/// <summary>The accepted messages, in memory: there is no mail and no database in this example.</summary>
public sealed class MessageBoard(TimeProvider time)
{
    private const int Keep = 1000;

    private readonly Lock _lock = new();
    private readonly List<Accepted> _messages = [];

    public MessageBoard() : this(TimeProvider.System)
    {
    }

    public void Add(string name, string email, string message)
    {
        lock (_lock)
        {
            _messages.Add(new Accepted(time.GetUtcNow(), name.Trim(), email.Trim(), message.Trim()));
            if (_messages.Count > Keep)
            {
                _messages.RemoveAt(0);
            }
        }
    }

    /// <summary>The messages, newest first.</summary>
    public IReadOnlyList<Accepted> Latest()
    {
        lock (_lock)
        {
            return [.. _messages.AsEnumerable().Reverse()];
        }
    }
}
