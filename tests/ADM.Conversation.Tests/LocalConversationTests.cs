using System.Collections.Immutable;
using ADM.Contracts;
using ADM.Domain;
using ADM.Tests.Shared;

namespace ADM.Conversation.Tests;

public sealed class LocalConversationTests
{
    private readonly PersonaCatalogue _catalogue = PersonaCatalogue.LoadBundled();
    private static OperationalContext Context(PendingQuestion? question = null, ServiceKind? focus = null)
    {
        var state = new OperationalScenario().State;
        return new(state.Session, state.Connection, question, focus);
    }
    [Theory]
    [InlineData(" Skip CATERING ", PilotIntentKind.SkipService, ServiceKind.Catering)]
    [InlineData("cancel cleaning", PilotIntentKind.CancelService, ServiceKind.Cleaning)]
    [InlineData("stop refueling", PilotIntentKind.CancelService, ServiceKind.Refuelling)]
    [InlineData("actually do catering", PilotIntentKind.ResumeService, ServiceKind.Catering)]
    [InlineData("no cleaning needed", PilotIntentKind.SkipService, ServiceKind.Cleaning)]
    [InlineData("resume boarding", PilotIntentKind.ResumeService, ServiceKind.Boarding)]
    public void Service_synonyms_resolve_to_distinct_typed_intents(string text, PilotIntentKind expected, ServiceKind service)
    {
        var intent = LocalIntentParser.Parse(text, Context());
        Assert.Equal(expected, intent.Kind);
        Assert.Equal(service, intent.Service);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("yes please")]
    [InlineData("start boarding")]
    [InlineData("start services")]
    [InlineData("close up and disconnect")]
    [InlineData("skip catering and start boarding")]
    [InlineData("cancel it")]
    [InlineData("do whatever you like")]
    public void Ambiguous_or_unprompted_permission_does_not_authorize(string text) =>
        Assert.Equal(PilotIntentKind.Unknown, LocalIntentParser.Parse(text, Context()).Kind);

    [Theory]
    [InlineData(QuestionKind.Boarding, PilotIntentKind.ApproveBoarding)]
    [InlineData(QuestionKind.Service, PilotIntentKind.ApproveService)]
    [InlineData(QuestionKind.MandatorySkip, PilotIntentKind.ConfirmSkip)]
    [InlineData(QuestionKind.GateCheck, PilotIntentKind.ConfirmGate)]
    [InlineData(QuestionKind.FuelCheck, PilotIntentKind.ConfirmFuel)]
    [InlineData(QuestionKind.ManualRelease, PilotIntentKind.ConfirmRelease)]
    [InlineData(QuestionKind.ServiceCompletion, PilotIntentKind.ConfirmServiceCompletion)]
    [InlineData(QuestionKind.ServicesStart, PilotIntentKind.ApproveServicesStart)]
    public void Yes_is_scoped_to_the_pending_question(QuestionKind question, PilotIntentKind expected) =>
        Assert.Equal(expected, LocalIntentParser.Parse("yes", Context(new(question, 1, ServiceKind.Catering, ReleaseAction.Doors))).Kind);

    [Fact]
    public void Service_completion_confirmation_is_contextual_and_no_declines_without_completing()
    {
        Assert.Equal(PilotIntentKind.Unknown, LocalIntentParser.Parse("Confirm service complete", Context()).Kind);
        var context = Context(new(QuestionKind.ServiceCompletion, 1, ServiceKind.Refuelling));
        Assert.Equal(ServiceKind.Refuelling, LocalIntentParser.Parse("Confirm service complete", context).Service);
        Assert.Equal(PilotIntentKind.Acknowledgement, LocalIntentParser.Parse("No", context).Kind);
        Assert.Contains("Confirm fuel complete", LocalConversationEngine.QuickReplies(context));
    }

    [Theory]
    [InlineData("What's the delay?", PilotIntentKind.GetDelay)]
    [InlineData("What’s holding us up?", PilotIntentKind.GetDelay)]
    [InlineData("Are we ready?", PilotIntentKind.GetStatus)]
    [InlineData("Skip the gate delay", PilotIntentKind.SkipSimulatedDelay)]
    public void Natural_status_and_delay_phrases_are_local(string text, PilotIntentKind kind) => Assert.Equal(kind, LocalIntentParser.Parse(text, Context()).Kind);

    [Fact]
    public void Pronoun_only_uses_unambiguous_context()
    {
        Assert.Equal(ServiceKind.Catering, LocalIntentParser.Parse("cancel it", Context(focus: ServiceKind.Catering)).Service);
        Assert.Equal(PilotIntentKind.Unknown, LocalIntentParser.Parse("cancel it", Context()).Kind);
    }

    [Fact]
    public void Persona_selection_is_stable_and_has_multiple_valid_variants()
    {
        Guid sessionId = Guid.NewGuid();
        Assert.Equal(_catalogue.Select(sessionId), _catalogue.Select(sessionId));
        foreach (string id in new[] { "sample-sarah", "alex-concise", "james-formal" })
        {
            var persona = _catalogue.Get(id);
            foreach (string key in PersonaCatalogue.RequiredKeys) Assert.True(persona.Templates[key].Length >= 2);
        }
    }

    [Fact]
    public void Essential_phrase_selection_is_repeatable_and_avoids_recent_variant()
    {
        var engine = new LocalConversationEngine(_catalogue);
        var scenario = new OperationalScenario();
        var context = new OperationalContext(scenario.State.Session, ConnectionState.Connected);
        var evt = new DomainEvent(scenario.Meta(), "boarding.approval:3");
        var first = engine.Evaluate(context, evt);
        Assert.Equal(first.Message, engine.Evaluate(context, evt).Message);
        var next = engine.Evaluate(context with { RecentMessages = [first.Message!] }, evt with { Name = "boarding.approval:4" });
        Assert.NotEqual(first.Message, next.Message);
        Assert.Contains("?", next.Message);
    }

    [Fact]
    public void Persona_templates_cannot_override_operational_receipt_or_failure_clarifications()
    {
        var persona = _catalogue.Get("sample-sarah");
        Assert.Throws<ArgumentException>(() => new PersonaCatalogue([persona with
        { Templates = persona.Templates.Add("reply", [new("Cancellation succeeded", 1), new("Everything completed", 1)]) }]));
    }

    [Theory]
    [InlineData(QuestionKind.Service, PilotIntentKind.SkipService)]
    [InlineData(QuestionKind.MandatorySkip, PilotIntentKind.KeepService)]
    [InlineData(QuestionKind.GateCheck, PilotIntentKind.Acknowledgement)]
    [InlineData(QuestionKind.ManualRelease, PilotIntentKind.Acknowledgement)]
    [InlineData(QuestionKind.ServicesStart, PilotIntentKind.HoldServicesStart)]
    public void No_is_scoped_and_never_attests_completion(QuestionKind question, PilotIntentKind expected) =>
        Assert.Equal(expected, LocalIntentParser.Parse("no", Context(new(question, 1, ServiceKind.Catering, ReleaseAction.Doors))).Kind);

    [Fact]
    public void Malformed_personas_and_unknown_versions_fail_validation()
    {
        var persona = _catalogue.Get("sample-sarah");
        Assert.Throws<ArgumentException>(() => new PersonaCatalogue([persona, persona]));
        Assert.Throws<ArgumentException>(() => new PersonaCatalogue([persona with { Tone = "Unsafe" }]));
        Assert.Throws<ArgumentException>(() => new PersonaCatalogue([persona with { Templates = ImmutableDictionary<string, ImmutableArray<PhraseVariant>>.Empty }]));
        Assert.Throws<ArgumentException>(() => PersonaCatalogue.FromJson("{\"schemaVersion\":99,\"personas\":[]}"));
    }
}
