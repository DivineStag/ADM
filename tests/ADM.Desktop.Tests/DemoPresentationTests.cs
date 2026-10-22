using ADM.Contracts;
using ADM.Conversation;
using ADM.Desktop.Presentation;
using ADM.Domain;

namespace ADM.Desktop.Tests;

public sealed class DemoPresentationTests
{
    private sealed class Clock : IOperationalClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
    private static DemoSessionViewModel Create(Action? notify = null)
    {
        var personas = PersonaCatalogue.LoadBundled();
        return new(new Clock(), notify ?? (() => { }), new LocalConversationEngine(personas), personas);
    }
    private static ServiceRow Row(DemoSessionViewModel demo, ServiceKind kind) => demo.Services.Single(row => row.Kind == kind);

    [Fact]
    public void Happy_path_requires_two_separate_approvals_and_observed_release()
    {
        int notifications = 0;
        var demo = Create(() => notifications++);
        demo.Send("yes");
        Assert.Equal(DemoStage.Preparation, demo.Stage);
        Assert.Empty(demo.Approvals);
        demo.AdvanceCommand.Execute(null);
        Assert.Equal(DemoStage.BoardingApproval, demo.Stage);
        Assert.Equal(ServiceState.Planned, Row(demo, ServiceKind.Boarding).State);
        Assert.False(demo.AdvanceCommand.CanExecute(null));
        demo.QuickReplyCommand.Execute("Yes, start boarding");
        Assert.Equal(DemoStage.Boarding, demo.Stage);
        Assert.Single(demo.Approvals);
        demo.Send("yes");
        Assert.Single(demo.Approvals);
        demo.AdvanceCommand.Execute(null);
        Assert.Equal(DemoStage.ClosureApproval, demo.Stage);
        Assert.False(demo.AdvanceCommand.CanExecute(null));
        demo.Send("Close up and disconnect");
        Assert.Equal(DemoStage.Releasing, demo.Stage);
        Assert.Equal(2, demo.Approvals.Count);
        demo.AdvanceCommand.Execute(null);
        Assert.Equal(DemoStage.Complete, demo.Stage);
        Assert.Contains("pushback", demo.Messages.Last().Text, StringComparison.OrdinalIgnoreCase);
        Assert.All(demo.Approvals, approval => Assert.Equal(demo.DemoId, approval.DemoId));
        Assert.Equal(3, notifications);
        Assert.Empty(demo.QuickReplies);
    }

    [Fact]
    public void Hold_does_not_record_approval_or_start_boarding()
    {
        var demo = Create();
        demo.AdvanceCommand.Execute(null);
        demo.Send("Hold boarding");
        Assert.Equal(DemoStage.Preparation, demo.Stage);
        Assert.Empty(demo.Approvals);
        Assert.Equal(ServiceState.Planned, Row(demo, ServiceKind.Boarding).State);
        demo.Send("Start boarding");
        Assert.Equal(DemoStage.BoardingApproval, demo.Stage);
        demo.Send("Yes");
        Assert.Equal(DemoStage.Boarding, demo.Stage);
    }

    [Fact]
    public void Cancellation_is_not_confirmed_by_the_request_and_requires_explicit_observation()
    {
        var demo = Create();
        var fuel = Row(demo, ServiceKind.Refuelling);
        Assert.False(demo.SkipCommand.CanExecute(fuel));
        demo.CancelCommand.Execute(fuel);
        Assert.Equal(ServiceState.Active, fuel.State);
        Assert.True(demo.HasPendingAction);
        demo.ConfirmActionCommand.Execute(null);
        Assert.Equal(ServiceState.CancelPending, fuel.State);
        Assert.True(demo.CanAdvance); // Other concurrent services can still finish.
        demo.ObserveCancellationCommand.Execute(fuel);
        Assert.Equal(ServiceState.Cancelled, fuel.State);
        demo.AdvanceCommand.Execute(null);
        Assert.False(demo.CanAdvance);
        demo.ResumeCommand.Execute(fuel);
        demo.ConfirmActionCommand.Execute(null);
        Assert.Equal(ServiceState.Active, fuel.State);
        Assert.False(demo.SkipCommand.CanExecute(fuel));
    }

    [Fact]
    public void Skipping_optional_planned_catering_leaves_no_completion_claim()
    {
        var personas = PersonaCatalogue.LoadBundled();
        var demo = new DemoSessionViewModel(new Clock(), () => { }, new LocalConversationEngine(personas), personas)
        { NextSequence = () => ServiceSequence.DefaultPhases.SetItem(ServiceKind.Catering, 4) };
        demo.Reset();
        var catering = Row(demo, ServiceKind.Catering);
        demo.SkipCommand.Execute(catering);
        demo.ConfirmActionCommand.Execute(null);
        demo.AdvanceCommand.Execute(null);
        Assert.Equal(ServiceState.Skipped, catering.State);
        Assert.Equal(DemoStage.BoardingApproval, demo.Stage);
    }

    [Fact]
    public void Cancelled_boarding_needs_observation_resume_and_fresh_approval()
    {
        var demo = Create();
        demo.AdvanceCommand.Execute(null);
        demo.Send("Start boarding");
        var boarding = Row(demo, ServiceKind.Boarding);
        demo.CancelCommand.Execute(boarding);
        demo.ConfirmActionCommand.Execute(null);
        Assert.Equal(DemoStage.Boarding, demo.Stage);
        Assert.Empty(demo.Approvals);
        Assert.False(demo.CanAdvance);
        demo.ObserveCancellationCommand.Execute(boarding);
        Assert.False(demo.CanAdvance);
        demo.ResumeCommand.Execute(boarding);
        demo.ConfirmActionCommand.Execute(null);
        demo.AdvanceCommand.Execute(null);
        Assert.Equal(DemoStage.BoardingApproval, demo.Stage);
        Assert.Empty(demo.Approvals);
    }

    [Fact]
    public void Disconnect_invalidates_approvals_and_restoration_cannot_replay()
    {
        var demo = Create();
        demo.AdvanceCommand.Execute(null);
        demo.Send("Start boarding");
        demo.PreviewConnectionLoss(true);
        Assert.Empty(demo.Approvals);
        Assert.Equal(ServiceState.Unknown, Row(demo, ServiceKind.Boarding).State);
        Assert.Empty(demo.QuickReplies);
        demo.PreviewConnectionLoss(false);
        demo.Send("yes");
        demo.AdvanceCommand.Execute(null);
        Assert.Equal(DemoStage.NeedsReview, demo.Stage);
        Assert.Empty(demo.Approvals);
        Assert.False(demo.CanAdvance);
        demo.Reset();
        Assert.Equal(DemoStage.Preparation, demo.Stage);
        Assert.False(demo.ShowRecovery);
    }

    [Fact]
    public void Old_demo_service_rows_cannot_modify_a_new_demo()
    {
        var demo = Create();
        var oldFuel = Row(demo, ServiceKind.Refuelling);
        var oldCatering = Row(demo, ServiceKind.Catering);
        demo.Reset();
        demo.CancelCommand.Execute(oldFuel);
        demo.SkipCommand.Execute(oldCatering);
        Assert.False(demo.HasPendingAction);
        Assert.Equal(ServiceState.Active, Row(demo, ServiceKind.Refuelling).State);
        Assert.Equal(ServiceState.Active, Row(demo, ServiceKind.Catering).State);
    }

    [Fact]
    public void Reset_clears_approvals_and_creates_an_independent_demo_identity()
    {
        var demo = Create();
        Guid initialId = demo.DemoId;
        demo.AdvanceCommand.Execute(null);
        demo.Send("yes");
        demo.Reset();
        Assert.NotEqual(initialId, demo.DemoId);
        Assert.Empty(demo.Approvals);
        Assert.Single(demo.Messages, message => !message.IsSystem);
        Assert.Contains(demo.Messages, message => message.IsSystem && message.Text.EndsWith("started", StringComparison.Ordinal));
        Assert.Equal(ServiceState.Planned, Row(demo, ServiceKind.Boarding).State);
    }
}
