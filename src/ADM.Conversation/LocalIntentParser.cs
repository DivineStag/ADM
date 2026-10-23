using System.Text.RegularExpressions;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Conversation;

public static partial class LocalIntentParser
{
    private static readonly Dictionary<string, ServiceKind> Services = new(StringComparer.Ordinal)
    {
        ["cleaning"] = ServiceKind.Cleaning,
        ["clean"] = ServiceKind.Cleaning,
        ["catering"] = ServiceKind.Catering,
        ["fuel"] = ServiceKind.Refuelling,
        ["refuel"] = ServiceKind.Refuelling,
        ["refueling"] = ServiceKind.Refuelling,
        ["refuelling"] = ServiceKind.Refuelling,
        ["water"] = ServiceKind.Water,
        ["lavatory"] = ServiceKind.Lavatory,
        ["lavatories"] = ServiceKind.Lavatory,
        ["boarding"] = ServiceKind.Boarding,
        ["board"] = ServiceKind.Boarding
    };
    public static PilotIntent Parse(string message, OperationalContext context)
    {
        string text = Normalize(message);
        if (text is "hi" or "hello" or "hey" or "hi there" or "hello there" or "good morning" or "good afternoon" or "good evening") return new(PilotIntentKind.Greeting, null);
        if (text is "how are you" or "how are you doing" or "hows it going" or "how is it going" or "you alright" or "are you alright" or "how are things" or "all good with you" or "and you" or "what about you") return new(PilotIntentKind.Wellbeing, null);
        if (text is "im good" or "im good thanks" or "good thanks" or "im well" or "im well thanks" or "not bad" or "all good thanks") return new(PilotIntentKind.AllGood, null);
        if (text is "been busy" or "have you been busy" or "busy today" or "are you busy" or "have you had a busy day" or "hows your day" or "how is your day going") return new(PilotIntentKind.Busy, null);
        if (text is "bye" or "goodbye" or "see you" or "see you later" or "cheers bye") return new(PilotIntentKind.Farewell, null);
        text = PoliteRequest().Replace(text, "");
        text = PoliteEnding().Replace(text, "").TrimEnd(' ', ',');
        text = text switch
        {
            "get the services started" or "get services started" or "get the services going" or "get services going" or "start the services" or "start ground services" or "start the ground services" or "lets start services" or "lets get started" or "starting the services" or "starting services" or "get the services underway" => "start services",
            "begin boarding" or "get boarding started" or "lets start boarding" or "starting boarding" => "start boarding",
            "hold off boarding" or "hold off on boarding" or "wait before boarding" => "hold boarding",
            "how are we getting on" or "how are things looking" or "hows it looking" or "give me an update" or "give me a status update" => "are we ready",
            "whats the hold up" or "what are we waiting for" or "whats causing the delay" => "whats the delay",
            _ => text
        };
        if (text is "yes" or "yes please" or "go ahead" or "okay" or "ok" or "yes go ahead") return Answer(context.Question);
        if (text is "no" or "not yet") return context.Question?.Kind switch
        {
            QuestionKind.ServicesStart => new(PilotIntentKind.HoldServicesStart, null),
            QuestionKind.Boarding => new(PilotIntentKind.HoldBoarding, null),
            QuestionKind.Closure => new(PilotIntentKind.HoldClosure, null),
            QuestionKind.Service => new(PilotIntentKind.SkipService, context.Question.Service),
            QuestionKind.MandatorySkip => new(PilotIntentKind.KeepService, context.Question.Service),
            QuestionKind.GateCheck or QuestionKind.FuelCheck or QuestionKind.ManualRelease or QuestionKind.ServiceCompletion => new(PilotIntentKind.Acknowledgement, null),
            _ => Unknown()
        };
        if (text is "are we ready" or "whats left" or "status" or "hows boarding" or "how is boarding") return new(PilotIntentKind.GetStatus, null);
        if (text is "whats the delay" or "whats holding us up" or "why are we waiting") return new(PilotIntentKind.GetDelay, null);
        if (text is "help" or "what can i say") return new(PilotIntentKind.GetHelp, null);
        if (text is "start services" or "yes start services" or "yes, start services" or "ready to start services")
            return context.Question?.Kind == QuestionKind.ServicesStart ? new(PilotIntentKind.ApproveServicesStart, null) : Unknown();
        if (text is "thanks" or "thank you" or "cheers" or "thanks for your help" or "thank you for your help" or "much appreciated") return new(PilotIntentKind.Acknowledgement, null);
        if (text is "skip the gate delay" or "skip gate delay" or "skip simulated disruption" or "skip the simulated delay") return new(PilotIntentKind.SkipSimulatedDelay, null);
        if (text is "hold boarding" or "not yet hold boarding") return new(PilotIntentKind.HoldBoarding, null);
        if (text is "keep connected" or "keep jetway connected" or "hold closure") return new(PilotIntentKind.HoldClosure, null);
        if (text is "start boarding" or "yes start boarding" or "yes, start boarding")
            return context.Question?.Kind == QuestionKind.Boarding ? new(PilotIntentKind.ApproveBoarding, null) :
                context.Session.Holds.Any(hold => hold.IsActive && hold.Kind == HoldKind.ManualPilotHold && hold.Scope == HoldScope.Boarding) ? new(PilotIntentKind.ResumeService, ServiceKind.Boarding) : Unknown();
        if (text is "close the doors" or "close doors" or "close up and disconnect" or "close up" or "disconnect" or "yes close up" or "yes, close up")
            return context.Session.Stage == WorkflowStage.AwaitingClosureApproval ? new(PilotIntentKind.ApproveClosure, null) : Unknown();
        if (text is "gate checked" or "gate ready") return context.Question?.Kind == QuestionKind.GateCheck ? new(PilotIntentKind.ConfirmGate, null) : Unknown();
        if (text is "fuel confirmed" or "required fuel is aboard") return context.Question?.Kind == QuestionKind.FuelCheck ? new(PilotIntentKind.ConfirmFuel, null) : Unknown();
        if (text is "confirm clear" or "confirm release") return context.Question?.Kind == QuestionKind.ManualRelease ? Answer(context.Question) : Unknown();
        if (text == "confirm doors closed") return new(PilotIntentKind.ConfirmRelease, null, ReleaseAction.Doors);
        if (text == "confirm jetway / stairs clear") return new(PilotIntentKind.ConfirmRelease, null, ReleaseAction.JetwayOrStairs);
        if (text == "confirm ground equipment clear") return new(PilotIntentKind.ConfirmRelease, null, ReleaseAction.GroundEquipment);
        if (text is "confirm service complete" or "service complete") return context.Question?.Kind == QuestionKind.ServiceCompletion ? Answer(context.Question) : Unknown();
        if (text == "confirm fuel complete") return new(PilotIntentKind.ConfirmServiceCompletion, ServiceKind.Refuelling);
        if (text == "confirm boarding complete") return new(PilotIntentKind.ConfirmServiceCompletion, ServiceKind.Boarding);
        if (text is "confirm skip" or "skip it" && context.Question?.Kind == QuestionKind.MandatorySkip) return Answer(context.Question);
        var request = ServiceCommand().Match(text);
        if (request.Success)
        {
            string name = request.Groups[2].Value;
            ServiceKind? kind = name == "it" ? ResolvePronoun(context) : Services.GetValueOrDefault(name);
            if (name != "it" && !Services.ContainsKey(name) || kind is null) return Unknown();
            return new(request.Groups[1].Value switch
            { "skip" => PilotIntentKind.SkipService, "cancel" or "stop" => PilotIntentKind.CancelService, _ => PilotIntentKind.ResumeService }, kind);
        }
        var notNeeded = NotNeeded().Match(text);
        if (notNeeded.Success && Services.TryGetValue(notNeeded.Groups[1].Value, out var skipped)) return new(PilotIntentKind.SkipService, skipped);
        return Unknown();
    }
    private static PilotIntent Answer(PendingQuestion? question) => question?.Kind switch
    {
        QuestionKind.ServicesStart => new(PilotIntentKind.ApproveServicesStart, null),
        QuestionKind.Boarding => new(PilotIntentKind.ApproveBoarding, null),
        QuestionKind.Closure => new(PilotIntentKind.ApproveClosure, null),
        QuestionKind.Service => new(PilotIntentKind.ApproveService, question.Service),
        QuestionKind.MandatorySkip => new(PilotIntentKind.ConfirmSkip, question.Service),
        QuestionKind.GateCheck => new(PilotIntentKind.ConfirmGate, null),
        QuestionKind.FuelCheck => new(PilotIntentKind.ConfirmFuel, null),
        QuestionKind.ManualRelease => new(PilotIntentKind.ConfirmRelease, null, question.Release),
        QuestionKind.ServiceCompletion => new(PilotIntentKind.ConfirmServiceCompletion, question.Service),
        _ => Unknown()
    };
    private static ServiceKind? ResolvePronoun(OperationalContext context)
    {
        if (context.FocusService is { } focused) return context.Session.Services[focused].State is ServiceState.Completed or ServiceState.Unknown ? null : focused;
        var active = context.Session.Services.Values.Where(service => service.State is ServiceState.Active or ServiceState.Requested).ToArray();
        return active.Length == 1 ? active[0].Kind : null;
    }
    private static PilotIntent Unknown() => new(PilotIntentKind.Unknown, null);
    private static string Normalize(string message)
    {
        string text = Whitespace().Replace(message.Trim().ToLowerInvariant().Replace('’', '\'').Replace("'", ""), " ").TrimEnd('.', '!', '?');
        if (text is "hi there" or "hello there") return text;
        text = GreetingPrefix().Replace(text, "");
        return text;
    }
    // Anchored wrappers only: negated, conditional and multi-command messages must not become approvals.
    [GeneratedRegex("^(?:(?:hi|hello|hey)(?: there)?[,!]? |(?:good morning|good afternoon|good evening), )", RegexOptions.CultureInvariant)] private static partial Regex GreetingPrefix();
    [GeneratedRegex("^(?:would you mind |(?:(?:please )?(?:can|could|would) (?:you|we) (?:please )?(?:just )?)|please |id like you to |go ahead and )", RegexOptions.CultureInvariant)] private static partial Regex PoliteRequest();
    [GeneratedRegex("(?:,? please|,? thanks|,? thank you| for me| when you can)+$", RegexOptions.CultureInvariant)] private static partial Regex PoliteEnding();
    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)] private static partial Regex Whitespace();
    [GeneratedRegex("^(skip|cancel|stop|resume|restart|start|actually do|do) (?:the )?([a-z]+)$", RegexOptions.CultureInvariant)] private static partial Regex ServiceCommand();
    [GeneratedRegex("^no ([a-z]+) (?:is )?needed$", RegexOptions.CultureInvariant)] private static partial Regex NotNeeded();
}
