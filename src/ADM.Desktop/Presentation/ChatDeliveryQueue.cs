using ADM.Domain;

namespace ADM.Desktop.Presentation;

public sealed record ChatTimingOptions(bool Enabled)
{
    public TimeSpan NextDelay() => Enabled ? TimeSpan.FromSeconds(Random.Shared.Next(1, 6)) : TimeSpan.Zero;
}

public sealed record DeliveredChatMessage(ConversationMessage Message, bool Notify);

/// <summary>Presentation delay only. It never delays a reducer, adapter, journal or service command.</summary>
public sealed class ChatDeliveryQueue(Func<TimeSpan> nextDelay)
{
    private readonly List<(ConversationMessage Message, DateTimeOffset Due, bool Notify)> _pending = [];
    private DateTimeOffset _lastDispatcherDue;
    public bool HasPendingDispatcher => _pending.Any(item => !item.Message.IsSystem && item.Message.Sender != "You");
    public void Enqueue(ConversationMessage message, DateTimeOffset now, bool notify = true)
    {
        bool dispatcher = !message.IsSystem && message.Sender != "You";
        var due = now;
        if (dispatcher)
        {
            var delay = nextDelay();
            if (delay < TimeSpan.Zero || delay > TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(nextDelay));
            due = now + delay;
            if (_lastDispatcherDue > due) due = _lastDispatcherDue;
            _lastDispatcherDue = due;
        }
        _pending.Add((message, due, notify));
    }
    public IReadOnlyList<DeliveredChatMessage> Drain(DateTimeOffset now, Func<ConversationMessage, bool>? stillRelevant = null)
    {
        var result = new List<DeliveredChatMessage>();
        foreach (var item in _pending.ToArray())
        {
            if (stillRelevant is not null && !stillRelevant(item.Message)) { _pending.Remove(item); continue; }
            if (item.Due > now) continue;
            result.Add(new(item.Message, item.Notify)); _pending.Remove(item);
        }
        return result;
    }
    public void Clear() { _pending.Clear(); _lastDispatcherDue = default; }

    public static bool IsRelevant(ConversationMessage message, PendingQuestion? question)
    {
        string key = message.SemanticKey.Split(':')[0];
        QuestionKind? kind = key switch
        {
            "services.start.approval" or "services.start.gsx" => QuestionKind.ServicesStart,
            "boarding.approval" or "boarding.approval.no-fuel" or "boarding.approval.gate" or "boarding.approval.gate-no-fuel" => QuestionKind.Boarding,
            "closure.approval" => QuestionKind.Closure,
            "service.approval" or "service.unsupported" => QuestionKind.Service,
            "skip.confirm" => QuestionKind.MandatorySkip,
            "service.completion.check" => QuestionKind.ServiceCompletion,
            "fuel.check" => QuestionKind.FuelCheck,
            _ => null
        };
        if (kind is null) return true;
        if (question?.Kind != kind) return false;
        if (kind == QuestionKind.ServicesStart) return true;
        var parts = message.SemanticKey.Split(':');
        return parts.Length > 1 && int.TryParse(parts[1], out int revision) && revision == question.Revision &&
            (kind is not (QuestionKind.Service or QuestionKind.ServiceCompletion or QuestionKind.MandatorySkip) || parts.Length > 2 && parts[2] == question.Service?.ToString());
    }
}
