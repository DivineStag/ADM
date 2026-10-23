using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Conversation;

public sealed class LocalConversationEngine(PersonaCatalogue catalogue) : IConversationEngine
{
    public PilotIntent Parse(string message, OperationalContext context) => LocalIntentParser.Parse(message, context);
    public ConversationDecision Evaluate(OperationalContext context, DomainEvent domainEvent)
    {
        string key = domainEvent.Name.Split(':')[0];
        if (key == "services.start.gsx" && domainEvent.Detail?.Contains("planned load figures", StringComparison.Ordinal) == true) key = "services.start.gsx-load";
        var persona = catalogue.Get(context.Session.Dispatcher.Persona.Id);
        string? message = domainEvent.Detail;
        var variants = persona.Templates.TryGetValue(key, out var configured) ? configured :
            ChatPhrases.For(key, context).Select(text => new PhraseVariant(text, 1)).ToImmutableArray();
        if (!variants.IsDefaultOrEmpty)
        {
            var recent = context.RecentMessages.IsDefault ? ImmutableArray<string>.Empty : context.RecentMessages;
            var available = variants.Where(variant => !recent.TakeLast(8).Contains(variant.Text)).ToArray();
            if (available.Length == 0)
            {
                int oldest = variants.Min(variant => recent.LastIndexOf(variant.Text));
                available = variants.Where(variant => recent.LastIndexOf(variant.Text) == oldest).ToArray();
            }
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{context.Session.Id}/{persona.Id}/{domainEvent.Name}"));
            int pick = (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(hash) % available.Sum(variant => variant.Weight));
            foreach (var variant in available)
            {
                pick -= variant.Weight;
                if (pick < 0) { message = variant.Text; break; }
            }
        }
        return new(domainEvent.Name, message, QuickReplies(context));
    }

    public static ImmutableArray<string> QuickReplies(OperationalContext context)
    {
        var replies = BaseQuickReplies(context);
        if (context.Connection != ConnectionState.Connected || context.Session.Stage == WorkflowStage.Completed ||
            context.Question is { Kind: not QuestionKind.Service }) return replies;
        bool unresolved(ServiceKind kind)
        {
            var state = context.Session.Services[kind].State;
            if (state is ServiceState.Completed or ServiceState.NotRequired) return false;
            if (context.Session.Plan.Services[kind].Rule.Required) return true;
            return state is not (ServiceState.Skipped or ServiceState.Cancelled or ServiceState.CycleEnded);
        }
        string? shortcut = ServiceSequence.Ordered(context.Session.Plan)
            .Where(kind => (kind is ServiceKind.Refuelling or ServiceKind.Catering or ServiceKind.Boarding) &&
                context.Session.Services[kind].State is ServiceState.Planned or ServiceState.Eligible)
            .Where(kind => ServiceSequence.Ordered(context.Session.Plan).TakeWhile(earlier => earlier != kind).Any(unresolved) &&
                !ServiceSequence.Ordered(context.Session.Plan).TakeWhile(earlier => earlier != kind).Any(earlier => unresolved(earlier) && context.Session.Plan.Services[earlier].Rule.Required))
            .Select(kind => "Skip to " + (kind == ServiceKind.Refuelling ? "fuel" : kind.ToString().ToLowerInvariant())).FirstOrDefault();
        return shortcut is null ? replies : [.. replies, shortcut];
    }

    private static ImmutableArray<string> BaseQuickReplies(OperationalContext context) => context.Question?.Kind switch
    {
        _ when context.Session.Stage == WorkflowStage.Completed => [],
        QuestionKind.ServicesStart => ["Yes, start services", "Not yet"],
        QuestionKind.Boarding => ["Yes, start boarding", "Hold boarding"],
        QuestionKind.Closure => ["Yes, close up", "Not yet"],
        QuestionKind.Service => ["Yes", $"Skip {context.Question.Service!.Value.ToString().ToLowerInvariant()}"],
        QuestionKind.MandatorySkip => ["Confirm skip", "Are we ready?"],
        QuestionKind.GateCheck => ["Gate checked"],
        QuestionKind.FuelCheck => ["Fuel confirmed"],
        QuestionKind.ManualRelease => [context.Question.Release switch
        {
            ReleaseAction.Doors => "Confirm doors closed",
            ReleaseAction.JetwayOrStairs => "Confirm jetway / stairs clear",
            _ => "Confirm ground equipment clear"
        }],
        QuestionKind.ServiceCompletion => [context.Question.Service == ServiceKind.Refuelling ? "Confirm fuel complete" : "Confirm boarding complete", context.Question.Service == ServiceKind.Refuelling ? "Restart fuel" : "Restart boarding", "What's the delay?"],
        _ when context.Session.Holds.Any(hold => hold.IsActive && hold.Kind == HoldKind.ManualPilotHold && hold.Scope == HoldScope.Boarding) => ["Resume boarding", "What's the delay?"],
        _ when context.Session.Holds.Any(hold => hold.IsActive && hold.IsSimulated) || context.Session.Gate is { State: GateReadinessState.Preparing, Provenance: GateProvenance.Simulation }
            => ["Skip the gate delay", "What's the delay?"],
        _ => ["Are we ready?", "What's the delay?"]
    };
}
