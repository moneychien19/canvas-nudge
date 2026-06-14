using System.Net;
using System.Text;
using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class CanvasFetchTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
    }

    private static CanvasClient ClientReturning(string body) =>
        new(new HttpClient(new StubHandler(body)), "https://canvas.example.edu", "tok",
            (_, _) => Task.CompletedTask);

    [Fact]
    public async Task GetSelfIdAsync_ReturnsId()
    {
        var client = ClientReturning("{\"id\":555,\"name\":\"Me\"}");

        Assert.Equal(555, await client.GetSelfIdAsync());
    }

    [Fact]
    public async Task GetTodoAssignments_KeepsOnlySubmitting()
    {
        var json = """
        [
          { "type": "submitting", "course_id": 100,
            "assignment": { "id": 1, "name": "Cache Lab",
                            "due_at": "2026-06-17T03:59:00Z",
                            "html_url": "https://canvas.example.edu/courses/100/assignments/1" } },
          { "type": "grading", "course_id": 100,
            "assignment": { "id": 9, "name": "Teacher Task", "due_at": null, "html_url": "z" } }
        ]
        """;
        var client = ClientReturning(json);

        var items = await client.GetTodoAssignmentsAsync();

        var item = Assert.Single(items);
        Assert.Equal("submitting", item.Type);
        Assert.Equal(100, item.CourseId);
        Assert.Equal("Cache Lab", item.Assignment!.Name);
        Assert.Equal("https://canvas.example.edu/courses/100/assignments/1", item.Assignment.HtmlUrl);
        Assert.Equal(
            new DateTimeOffset(2026, 6, 17, 3, 59, 0, TimeSpan.Zero),
            item.Assignment.DueAt);
    }

    [Fact]
    public async Task GetTodoAssignments_HandlesNullDueAt()
    {
        var json = """
        [ { "type": "submitting", "course_id": 7,
            "assignment": { "id": 2, "name": "Undated HW", "due_at": null,
                            "html_url": "https://canvas.example.edu/x" } } ]
        """;
        var client = ClientReturning(json);

        var item = Assert.Single(await client.GetTodoAssignmentsAsync());
        Assert.Null(item.Assignment!.DueAt);
        Assert.Equal("Undated HW", item.Assignment.Name);
    }
}
