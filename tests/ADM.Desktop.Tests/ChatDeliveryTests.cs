using ADM.Desktop.Presentation;
using ADM.Domain;

namespace ADM.Desktop.Tests;

public sealed class ChatDeliveryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ConversationMessage Message(string key, string sender = "Sarah", bool system = false) =>
        new(Guid.NewGuid(), Guid.NewGuid(), key, sender, key, Now) { IsSystem = system };

    [Fact]
    public void Replies_wait_but_pilot_and_system_messages_are_immediate()
    {
        var queue = new ChatDeliveryQueue(() => TimeSpan.FromSeconds(3));
        var reply = Message("chat.wellbeing"); var pilot = Message("pilot", "You"); var system = Message("system.service:Water", system: true);
        queue.Enqueue(reply, Now); queue.Enqueue(pilot, Now); queue.Enqueue(system, Now);
        Assert.Equal(new[] { pilot, system }, queue.Drain(Now).Select(item => item.Message));
        Assert.Empty(queue.Drain(Now.AddSeconds(2.99)));
        Assert.Equal(reply, Assert.Single(queue.Drain(Now.AddSeconds(3))).Message);
        Assert.False(queue.HasPendingDispatcher);
    }

    [Fact]
    public void Dispatcher_order_is_preserved_without_adding_delays_together()
    {
        var times = new Queue<int>([5, 1, 3]);
        var queue = new ChatDeliveryQueue(() => TimeSpan.FromSeconds(times.Dequeue()));
        var messages = new[] { Message("first"), Message("second"), Message("third") };
        foreach (var message in messages) queue.Enqueue(message, Now);
        Assert.Empty(queue.Drain(Now.AddSeconds(4)));
        Assert.Equal(messages, queue.Drain(Now.AddSeconds(5)).Select(item => item.Message));
    }

    [Fact]
    public void Clearing_a_sector_drops_undelivered_replies_and_sounds()
    {
        var queue = new ChatDeliveryQueue(() => TimeSpan.FromSeconds(5));
        queue.Enqueue(Message("handover.signoff"), Now);
        queue.Clear();
        Assert.Empty(queue.Drain(Now.AddMinutes(1)));
        Assert.False(queue.HasPendingDispatcher);
        var next = Message("new sector"); queue.Enqueue(next, Now.AddMinutes(1));
        Assert.Equal(next, Assert.Single(queue.Drain(Now.AddMinutes(1).AddSeconds(5))).Message);
    }

    [Fact]
    public void Answered_or_invalidated_questions_are_not_delivered_late()
    {
        var queue = new ChatDeliveryQueue(() => TimeSpan.FromSeconds(2));
        queue.Enqueue(Message("boarding.approval:2::"), Now);
        Assert.Empty(queue.Drain(Now.AddSeconds(3), message => ChatDeliveryQueue.IsRelevant(message, new(QuestionKind.Boarding, 3))));
        Assert.False(queue.HasPendingDispatcher);
        Assert.False(ChatDeliveryQueue.IsRelevant(Message("closure.approval:3::"), null));
        Assert.True(ChatDeliveryQueue.IsRelevant(Message("services.start.gsx:1::"), new(QuestionKind.ServicesStart, 2)));
    }

    [Fact]
    public void Restored_messages_can_be_delivered_without_replaying_sounds()
    {
        var queue = new ChatDeliveryQueue(() => TimeSpan.Zero);
        queue.Enqueue(Message("pilot", "You"), Now, notify: false);
        Assert.False(Assert.Single(queue.Drain(Now)).Notify);
    }

    [Fact]
    public void Production_delays_are_between_one_and_five_seconds()
    {
        var timing = new ChatTimingOptions(true);
        for (int i = 0; i < 100; i++) Assert.InRange(timing.NextDelay(), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.Zero, new ChatTimingOptions(false).NextDelay());
    }
}
