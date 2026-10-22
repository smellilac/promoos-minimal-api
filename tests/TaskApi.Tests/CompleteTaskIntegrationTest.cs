using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskApi.Tests;

public sealed class CompleteTaskIntegrationTest(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    // Match the app's JSON contract (invariant 9): camelCase + enums as strings.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Complete_publishes_one_event_then_second_complete_returns_409()
    {
        var client = fixture.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        // 1. Create a task.
        var createResponse = await client.PostAsJsonAsync(
            "/tasks",
            new CreateTaskRequest("Buy milk", Priority.High),
            JsonOptions,
            ct);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<TaskResponse>(JsonOptions, ct);
        Assert.NotNull(created);

        // 2. Complete it → 200.
        var completeResponse = await client.PutAsync($"/tasks/{created!.Id}/complete", content: null, ct);
        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        // 3. The endpoint enqueued exactly one event with the right payload.
        Assert.True(fixture.Events.Published.TryDequeue(out var published));
        Assert.Empty(fixture.Events.Published);
        Assert.Equal(created.Id, published!.TaskId);
        Assert.Equal("Buy milk", published.Title);
        Assert.Equal(Priority.High, published.Priority);
        Assert.NotEqual(default, published.CompletedAt);

        // 4. Completing again → 409 (invariant 4: already completed).
        var secondComplete = await client.PutAsync($"/tasks/{created.Id}/complete", content: null, ct);
        Assert.Equal(HttpStatusCode.Conflict, secondComplete.StatusCode);
    }
}
