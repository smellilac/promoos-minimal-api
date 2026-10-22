using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using TaskApi.Messaging;
using Testcontainers.PostgreSql;

namespace TaskApi.Tests;

// Boots the real app via WebApplicationFactory<Program> against a throwaway PostgreSQL container
// (ADR 005). Messaging is faked: we don't touch a broker in this test (see HACK on the fake).
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();

    // The fake ITaskEventPublisher the app resolves; the test reads what the endpoint enqueued.
    public FakeTaskEventPublisher Events { get; } = new();

    // xUnit v3: IAsyncLifetime methods return ValueTask. Start the container before the app boots,
    // because Program.cs runs MigrateAsync() on startup and needs a reachable database.
    public async ValueTask InitializeAsync() => await _postgres.StartAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Point the app at the test container instead of the (empty) appsettings connection string.
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
            }));

        // Runs after Program.cs registrations, so these removals win.
        builder.ConfigureServices(services =>
        {
            // Drop the real publisher and its BackgroundService (RabbitMqPublisher is the only
            // hosted service) so the test neither connects to nor publishes to a broker.
            services.RemoveAll<IHostedService>();
            services.RemoveAll<RabbitMqPublisher>();
            services.RemoveAll<ITaskEventPublisher>();

            services.AddSingleton<ITaskEventPublisher>(Events);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

// HACK: не успел, доделал бы так: вместо фейка поднял бы RabbitMQ через Testcontainers.RabbitMq,
// объявил временную очередь и забиндил её на обменник task.events с routing key task.completed,
// затем через BasicConsume проверил бы, что реальное сообщение дошло с правильными body и props
// (ContentType, DeliveryMode). Это проверило бы и сериализацию, и routing key, а не только факт
// вызова Enqueue. Спека явно разрешает заглушить брокер интерфейсом, поэтому для одного теста беру фейк.
public sealed class FakeTaskEventPublisher : ITaskEventPublisher
{
    public ConcurrentQueue<TaskCompletedEvent> Published { get; } = new();

    public void Enqueue(TaskCompletedEvent @event) => Published.Enqueue(@event);
}
