using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace TaskApi.Messaging;

// One component plays two roles:
//  * ITaskEventPublisher — the endpoint enqueues events (non-blocking).
//  * BackgroundService    — a single reader drains the queue and publishes to RabbitMQ.
// A single reader is deliberate: IChannel is not thread-safe for concurrent publishes,
// so funnelling everything through one loop avoids locks and channel pooling (ADR 002).
public sealed class RabbitMqPublisher : BackgroundService, ITaskEventPublisher
{
    private const int QueueCapacity = 10_000;
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(5);

    // Message contract: camelCase names, Priority as a string.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Channel<TaskCompletedEvent> _queue;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqPublisher(IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Bounded so a long broker outage can't grow memory without limit. DropOldest keeps the
        // newest events and discards the stalest; the itemDropped callback logs what we lost.
        _queue = Channel.CreateBounded<TaskCompletedEvent>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            },
            dropped => _logger.LogWarning(
                "Task event queue full, dropped event for TaskId {TaskId}", dropped.TaskId));
    }

    // Returns immediately (invariant 6). With DropOldest, TryWrite always succeeds.
    public void Enqueue(TaskCompletedEvent @event) => _queue.Writer.TryWrite(@event);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Connect once at startup. If the broker is unreachable we log and carry on with a null
        // channel: the drain loop below then logs each event as lost and drops it, so completing
        // a task never fails because of RabbitMQ (invariant 5).
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
            };

            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            // Durable topic exchange, created if absent so the app is self-sufficient.
            await _channel.ExchangeDeclareAsync(
                exchange: _options.Exchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                cancellationToken: stoppingToken);
        }
        catch (Exception ex)
        {
            // HACK: не успел, доделал бы так: переподключение с backoff через Polly
            // (ResiliencePipeline с WaitAndRetryForever/экспоненциальной задержкой) — чтобы при
            // кратковременной недоступности брокера на старте не терять все события, а дождаться
            // его подъёма и переустановить соединение/канал.
            _logger.LogError(ex, "Failed to connect to RabbitMQ; task-completed events will be dropped");
        }

        // IMPORTANT: no stoppingToken here. On shutdown it is cancelled immediately, and passing it
        // would abort ReadAllAsync mid-drain, losing queued events. The loop instead ends when
        // StopAsync calls Writer.Complete(); the 5s drain budget is enforced there (invariant 7).
        await foreach (var @event in _queue.Reader.ReadAllAsync())
        {
            await PublishAsync(@event);
        }
    }

    private async Task PublishAsync(TaskCompletedEvent @event)
    {
        if (_channel is null)
        {
            // Broker never came up — honour invariant 5: log with TaskId, drop the message.
            _logger.LogError(
                "RabbitMQ unavailable, dropping task-completed event for TaskId {TaskId}", @event.TaskId);
            return;
        }

        try
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(@event, SerializerOptions);

            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
            };

            // HACK: не успел, доделал бы так: включил бы publisher confirms
            // (CreateChannelOptions { PublisherConfirmationsEnabled = true } + ожидание ack/nack),
            // чтобы точно знать, что брокер принял сообщение, а не просто отправил в сокет.
            await _channel.BasicPublishAsync(
                exchange: _options.Exchange,
                routingKey: _options.RoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body);
        }
        catch (Exception ex)
        {
            // Invariant 5: a broker failure must never surface to the client (the HTTP response
            // was already sent). Log with TaskId and drop the message.
            _logger.LogError(ex, "Failed to publish task-completed event for TaskId {TaskId}", @event.TaskId);
        }
    }

    // Graceful shutdown (ADR 004, invariant 7).
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 1. Refuse new events; the reader loop finishes once the queue is empty.
        _queue.Writer.Complete();

        // 2. Let the reader drain what's already queued, but cap the wait at 5s.
        if (ExecuteTask is { } drainTask)
        {
            await Task.WhenAny(drainTask, Task.Delay(DrainTimeout, cancellationToken));
        }

        // 3. Close broker resources in order: channel, then connection.
        try
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync(cancellationToken);
                await _channel.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.CloseAsync(cancellationToken);
                await _connection.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing RabbitMQ channel/connection on shutdown");
        }

        await base.StopAsync(cancellationToken);
    }
}
