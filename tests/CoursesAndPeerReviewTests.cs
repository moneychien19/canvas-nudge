using System.Net;
using System.Text;
using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class CoursesAndPeerReviewTests
{
    /// <summary>Single canned 200 response (for single-endpoint tests).</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StubHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(_body, Encoding.UTF8, "application/json") });
    }

    private static CanvasClient Client(HttpMessageHandler handler) =>
        new(new HttpClient(handler), "https://canvas.example.edu", "tok", (_, _) => Task.CompletedTask);

    // --- 3.1 courses + label map ---

    [Fact]
    public async Task GetActiveCourses_BuildsLabelMap_NameFallsBackToCode()
    {
        var json = """
        [ { "id": 100, "name": "Intro to Systems", "course_code": "15-503" },
          { "id": 101, "name": null, "course_code": "17-214" } ]
        """;
        var courses = await Client(new StubHandler(json)).GetActiveCoursesAsync();
        var map = CanvasCourse.BuildLabelMap(courses);

        Assert.Equal("Intro to Systems", map[100]);
        Assert.Equal("17-214", map[101]);   // null name -> course_code
    }

    // --- 3.2 peer reviews via GraphQL (REST index can't do this for students) ---

    // Real Canvas GraphQL response shape (captured from canvas.cmu.edu).
    private const string GraphQlBody = """
    { "data": { "course": { "assignmentsConnection": { "nodes": [
        { "_id": "1", "name": "WA1", "dueAt": "2026-06-14T23:59:59-04:00",
          "assessmentRequestsForCurrentUser": [
            { "workflowState": "assigned" }, { "workflowState": "assigned" },
            { "workflowState": "assigned" } ] },
        { "_id": "2", "name": "WA2", "dueAt": "2026-05-29T23:59:59-04:00",
          "assessmentRequestsForCurrentUser": [
            { "workflowState": "completed" }, { "workflowState": "completed" } ] },
        { "_id": "3", "name": "WA3 undated", "dueAt": null,
          "assessmentRequestsForCurrentUser": [ { "workflowState": "assigned" } ] }
    ] } } } }
    """;

    [Fact]
    public async Task GetPendingPeerReviews_GroupsAssignedOnly_OmitsCompleted()
    {
        var groups = await Client(new StubHandler(GraphQlBody))
            .GetPendingPeerReviewsAsync(new long[] { 100 });

        Assert.Equal(2, groups.Count);  // WA2 (all completed) omitted

        var wa1 = groups.Single(g => g.AssignmentName == "WA1");
        Assert.Equal(100, wa1.CourseId);
        Assert.Equal(3, wa1.PendingCount);
        Assert.Equal(new DateTimeOffset(2026, 6, 14, 23, 59, 59, TimeSpan.FromHours(-4)), wa1.DueAt);

        var wa3 = groups.Single(g => g.AssignmentName == "WA3 undated");
        Assert.Equal(1, wa3.PendingCount);
        Assert.Null(wa3.DueAt);
    }

    [Fact]
    public async Task PostGraphQl_Throws_OnGraphQlErrors()
    {
        var body = """{ "errors": [ { "message": "Invalid course" } ], "data": null }""";

        await Assert.ThrowsAsync<CanvasException>(() =>
            Client(new StubHandler(body)).PostGraphQlAsync("query{}", null));
    }
}
