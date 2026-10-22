namespace TaskApi.Messaging;

// The endpoint depends on this, not on RabbitMQ directly. Enqueue is fire-and-forget:
// it returns immediately so the HTTP request never waits on the broker (ADR 002, invariant 6).
// Tests can swap in a fake that just records the events.
public interface ITaskEventPublisher
{
    void Enqueue(TaskCompletedEvent @event);
}
