using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Conversation;
using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.Conversation.Tests;

public sealed class NaturalChatTests
{
    [Theory]
    [InlineData("Skip to refuelling", ServiceKind.Refuelling)]
    [InlineData("Could you skip to fuel, please?", ServiceKind.Refuelling)]
    [InlineData("Please move straight to boarding", ServiceKind.Boarding)]
    [InlineData("Jump ahead to catering", ServiceKind.Catering)]
    public void Skip_to_requests_select_a_service_without_approving_it(string text, ServiceKind service) =>
        Assert.Equal(new PilotIntent(PilotIntentKind.SkipToService, service), LocalIntentParser.Parse(text, Context()));

    [Theory]
    [InlineData("Don't skip to fuel")]
    [InlineData("Skip to boarding if fuel is done")]
    [InlineData("Skip to fuel and start boarding")]
    [InlineData("Skip to pushback")]
    public void Ambiguous_or_unsupported_shortcuts_do_not_authorize(string text) =>
        Assert.Equal(PilotIntentKind.Unknown, LocalIntentParser.Parse(text, Context()).Kind);

    [Fact]
    public void Shortcut_button_is_brief_and_all_variants_preserve_intent()
    {
        var context = Context();
        string reply = Assert.Single(LocalConversationEngine.QuickReplies(context), text => text.StartsWith("Skip to ", StringComparison.Ordinal));
        Assert.Equal("Skip to fuel", QuickChat.Label(reply));
        for (int turn = 0; turn < 12; turn++)
            Assert.Equal(LocalIntentParser.Parse(reply, context), LocalIntentParser.Parse(QuickChat.Message(reply, context, turn), context));
    }

    private static OperationalContext Context(QuestionKind? question = null)
    {
        var state = new OperationalScenario().State;
        return new(state.Session, state.Connection, question is { } kind ? new(kind, 1, ServiceKind.Refuelling, ReleaseAction.Doors) : null);
    }

    [Theory]
    [InlineData("Please can you start the services?")]
    [InlineData("Could you please start the services for me?")]
    [InlineData("Hi, could you get the services going, please?")]
    [InlineData("Would you mind starting the services?")]
    [InlineData("I'd like you to start ground services, thanks.")]
    [InlineData("Go ahead and start the ground services, thanks.")]
    public void Polite_requests_authorize_only_the_pending_service_start(string text)
    {
        Assert.Equal(PilotIntentKind.ApproveServicesStart, LocalIntentParser.Parse(text, Context(QuestionKind.ServicesStart)).Kind);
        Assert.Equal(PilotIntentKind.Unknown, LocalIntentParser.Parse(text, Context()).Kind);
        Assert.Equal(PilotIntentKind.Unknown, LocalIntentParser.Parse(text, Context(QuestionKind.Boarding)).Kind);
    }

    [Theory]
    [InlineData("Please don't start services")]
    [InlineData("Could you not start services?")]
    [InlineData("Start services if fuel is done")]
    [InlineData("Please skip catering and start boarding")]
    [InlineData("He said start services")]
    [InlineData("Can you start pushback?")]
    [InlineData("How are you? Start boarding")]
    public void Negated_conditional_mixed_and_unsupported_requests_do_not_authorize(string text) =>
        Assert.Equal(PilotIntentKind.Unknown, LocalIntentParser.Parse(text, Context(QuestionKind.ServicesStart)).Kind);

    [Theory]
    [InlineData("How are you?", PilotIntentKind.Wellbeing)]
    [InlineData("Hey, how are you doing?", PilotIntentKind.Wellbeing)]
    [InlineData("Hi how are you?", PilotIntentKind.Wellbeing)]
    [InlineData("Been busy?", PilotIntentKind.Busy)]
    [InlineData("Have you had a busy day?", PilotIntentKind.Busy)]
    [InlineData("Hi there!", PilotIntentKind.Greeting)]
    [InlineData("I'm good thanks", PilotIntentKind.AllGood)]
    [InlineData("Cheers", PilotIntentKind.Acknowledgement)]
    [InlineData("See you later", PilotIntentKind.Farewell)]
    public void Casual_chat_is_separate_from_operational_permission(string text, PilotIntentKind expected) =>
        Assert.Equal(expected, LocalIntentParser.Parse(text, Context(QuestionKind.Boarding)).Kind);

    [Theory]
    [InlineData("Please cancel the catering", PilotIntentKind.CancelService, ServiceKind.Catering)]
    [InlineData("Could you skip catering, please?", PilotIntentKind.SkipService, ServiceKind.Catering)]
    [InlineData("Please restart fuel", PilotIntentKind.ResumeService, ServiceKind.Refuelling)]
    [InlineData("Would you hold off on boarding, please?", PilotIntentKind.HoldBoarding, null)]
    public void Polite_commands_keep_their_original_meaning(string text, PilotIntentKind kind, ServiceKind? service) =>
        Assert.Equal(new PilotIntent(kind, service), LocalIntentParser.Parse(text, Context()));

    [Fact]
    public void Every_quick_reply_variant_preserves_its_contextual_intent()
    {
        foreach (var question in Enum.GetValues<QuestionKind>())
        {
            var context = Context(question);
            foreach (string reply in LocalConversationEngine.QuickReplies(context))
                for (int turn = 0; turn < 12; turn++)
                    Assert.Equal(LocalIntentParser.Parse(reply, context), LocalIntentParser.Parse(QuickChat.Message(reply, context, turn), context));
        }
        var start = Context(QuestionKind.ServicesStart);
        Assert.Equal("Start services", QuickChat.Label("Yes, start services"));
        Assert.Equal(3, Enumerable.Range(0, 3).Select(turn => QuickChat.Message("Yes, start services", start, turn)).Distinct().Count());
        Assert.Equal("Start next sector", QuickChat.Message("Start next sector", start, 0));
    }

    [Fact]
    public void Exhausting_smalltalk_variants_does_not_repeat_the_last_reply()
    {
        var engine = new LocalConversationEngine(PersonaCatalogue.LoadBundled());
        var context = Context(QuestionKind.Boarding);
        string? previous = null;
        for (int turn = 0; turn < 20; turn++)
        {
            var reply = engine.Evaluate(context, new(new OperationalScenario().Meta(), $"chat.wellbeing:{turn}"));
            Assert.False(string.IsNullOrWhiteSpace(reply.Message));
            Assert.NotEqual(previous, reply.Message);
            previous = reply.Message;
            context = context with { RecentMessages = (context.RecentMessages.IsDefault ? ImmutableArray<string>.Empty : context.RecentMessages).Add(reply.Message!).TakeLast(8).ToImmutableArray() };
        }
    }
}
