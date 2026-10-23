using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Domain;

namespace ADM.Conversation;

/// <summary>Local conversational wording. These replies never make operational claims or issue commands.</summary>
internal static class ChatPhrases
{
    public static ImmutableArray<string> For(string key, OperationalContext context)
    {
        bool formal = context.Session.Dispatcher.Persona.Tone == "FormalConcise";
        return key switch
        {
            "handover.signoff" => ["Have a safe flight! That's my part done. Pushback's over to you.", "That's ADM finished for this stop. Have a safe flight!"],
            "boarding.approval.gate" => ["Fuel's complete. If you're ready at the gate, shall I start boarding?", "Fuelling's finished. Happy to start boarding, with the gate ready your end?"],
            "boarding.approval.gate-no-fuel" => ["The preboarding services are sorted. If you're ready at the gate, shall I start boarding?", "Preboarding services are finished. Ready at the gate for boarding?"],
            "chat.greeting" => formal
                ? ["Hello again. What can I help you with?", "Hello. I'm here if you need anything.", "Good to hear from you. How can I help?"]
                : ["Hi there! How's it going up front?", "Hello again! What can I do for you?", "Hey! I'm here if you need anything."],
            "chat.wellbeing" => formal
                ? ["Very well, thank you. How are things in the flight deck?", "All well here, thank you. How are you?", "Doing well, thank you for asking."]
                : ["All good here, thanks! How are things up front?", "Doing well, thanks for asking. How about you?", "I'm good, thanks! Hope things are going smoothly your end."],
            "chat.busy" => formal
                ? ["I'm focused on this turnaround. Let me know if you need an update.", "Keeping an eye on our ground handling. How are your preparations going?", "This turnaround has my attention. How are things your end?"]
                : ["Keeping an eye on this turnaround! How's the setup going up front?", "Just keeping tabs on things here. How about you?", "You've got my attention! How are your preparations going?"],
            "chat.thanks" => formal
                ? ["You're welcome.", "My pleasure.", "Of course. Let me know if you need anything else."]
                : ["You're welcome!", "No problem at all.", "Any time!", "Happy to help."],
            "chat.wait" => ["No problem. I'll wait for your confirmation.", "Understood. Let me know once you've checked."],
            "chat.all-good" => ["Good to hear! Let me know if you need anything.", "Glad to hear it. I'm here if anything comes up.", "Good stuff. Just give me a shout if you need me."],
            "chat.farewell" => ["Speak soon. I'm here if you need anything else.", "Catch you later!", "Take care. You can message me whenever you need."],
            "chat.unknown" => context.Question is null
                ? ["I didn't quite catch that. Try asking how we're getting on, or name the service you'd like to change.", "I'm not sure what you meant. You can ask for an update, or tell me which service you need help with.", "Could you put that another way? A service name helps if you'd like me to change something."]
                : ["I didn't quite catch that. The quick replies below can help, or tell me which service you mean.", "Could you put that another way? I'll leave the current question open for you.", "I'm not sure what you meant. You can use a quick reply below or name the service you'd like to change."],
            _ => []
        };
    }
}

/// <summary>Brief button labels with varied, explicit pilot messages through the same parser as typed chat.</summary>
public static class QuickChat
{
    public static string Label(string command) => command switch
    {
        "Yes, start services" => "Start services",
        "Yes, start boarding" => "Start boarding",
        "Yes, close up" => "Close doors",
        "Are we ready?" => "Status",
        "What's the delay?" => "Any delay?",
        "Skip the gate delay" => "Skip delay",
        "Confirm fuel complete" => "Fuel complete",
        "Confirm boarding complete" => "Boarding complete",
        "Confirm jetway / stairs clear" => "Access clear",
        "Confirm ground equipment clear" => "Equipment clear",
        "Confirm doors closed" => "Doors closed",
        _ => command
    };

    public static string Message(string command, OperationalContext context, int turn)
    {
        string[] variants = command.StartsWith("Skip to ", StringComparison.Ordinal) ?
            [$"Could you skip to {command[8..]}, please?", $"Please move straight to {command[8..]}.", $"Skip ahead to {command[8..]}, thanks."] : command switch
            {
                "Yes, start services" => ["Please can you start the services?", "Could you get the services going, please?", "Go ahead and start the ground services, thanks."],
                "Yes, start boarding" => ["Please start boarding.", "Could you begin boarding, please?", "Go ahead and start boarding, thanks."],
                "Yes, close up" => ["Please close the doors.", "Could you close the doors, please?", "Go ahead and close the doors, thanks."],
                "Not yet" => ["Not yet, please.", "Not yet, thanks."],
                "Hold boarding" => ["Please hold off on boarding.", "Could you hold boarding, please?"],
                "Keep connected" => ["Please keep connected.", "Could you keep connected, please?"],
                "Are we ready?" => ["How are we getting on?", "Could you give me an update, please?", "How are things looking?"],
                "What's the delay?" => ["What are we waiting for?", "What's holding us up?", "What's causing the delay?"],
                "Skip the gate delay" => ["Please skip the gate delay.", "Could you skip the simulated delay, please?"],
                "Yes" => ["Yes please.", "Go ahead, thanks."],
                _ => [$"{command}, please.", $"{command}, thanks."]
            };
        int index = (int)((uint)turn % (uint)variants.Length);
        var intent = LocalIntentParser.Parse(command, context);
        // Never change the meaning of an approval, manual confirmation or recovery command.
        return intent.Kind != PilotIntentKind.Unknown && LocalIntentParser.Parse(variants[index], context) == intent ? variants[index] : command;
    }
}
