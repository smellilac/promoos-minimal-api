namespace TaskApi.Messaging;

// Bound from the "RabbitMq" section of appsettings.json. Defaults match a local broker
// (rabbitmq:4-management with the stock guest/guest account) so dotnet run works out of the box.
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = "guest";

    public string Password { get; set; } = "guest";

    // Topic exchange + routing key fixed by the spec.
    public string Exchange { get; set; } = "task.events";

    public string RoutingKey { get; set; } = "task.completed";
}
