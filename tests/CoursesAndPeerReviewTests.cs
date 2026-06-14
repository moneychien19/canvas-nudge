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

    /// <summary>Routes by first matching URL substring (order most-specific first).</summary>
    private sealed class RoutingHandler : HttpMessageHandler
    {
        private readonly (string Match, string Body)[] _routes;
        public RoutingHandler(params (string, string)[] routes) => _routes = routes;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var url = r.RequestUri!.ToString();
            foreach (var (match, body) in _routes)
            {
                if (url.Contains(match))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
            throw new InvalidOperationException($"No route for {url}");
        }
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

    // --- 3.2 peer reviews ---

    [Fact]
    public async Task GetPeerReviewAssignments_KeepsOnlyEnabledAndPublished()
    {
        var json = """
        [ { "id": 1, "name": "Design Doc", "peer_reviews": true,  "published": true  },
          { "id": 2, "name": "Draft",      "peer_reviews": true,  "published": false },
          { "id": 3, "name": "Quiz",       "peer_reviews": false, "published": true  } ]
        """;
        var result = await Client(new StubHandler(json)).GetPeerReviewAssignmentsAsync(100);

        var a = Assert.Single(result);
        Assert.Equal("Design Doc", a.Name);
    }

    [Fact]
    public async Task GetPendingPeerReviewCount_CountsOnlySelfAssigned()
    {
        var json = """
        [ { "assessor_id": 555, "workflow_state": "assigned"  },
          { "assessor_id": 555, "workflow_state": "assigned"  },
          { "assessor_id": 555, "workflow_state": "completed" },
          { "assessor_id": 999, "workflow_state": "assigned"  } ]
        """;
        var count = await Client(new StubHandler(json))
            .GetPendingPeerReviewCountAsync(100, 1, selfId: 555);

        Assert.Equal(2, count);  // self + assigned only
    }

    [Fact]
    public async Task GetPendingPeerReviews_GroupsByAssignment_AndOmitsZero()
    {
        var handler = new RoutingHandler(
            ("/assignments/1/peer_reviews", """
                [ { "assessor_id": 555, "workflow_state": "assigned" },
                  { "assessor_id": 555, "workflow_state": "assigned" },
                  { "assessor_id": 555, "workflow_state": "assigned" } ]
                """),
            ("/assignments/9/peer_reviews", """
                [ { "assessor_id": 555, "workflow_state": "completed" } ]
                """),
            ("/courses/100/assignments", """
                [ { "id": 1, "name": "Design Doc", "peer_reviews": true, "published": true,
                    "due_at": "2026-06-14T03:59:00Z" },
                  { "id": 9, "name": "Done Review", "peer_reviews": true, "published": true,
                    "due_at": null } ]
                """));

        var groups = await Client(handler).GetPendingPeerReviewsAsync(new long[] { 100 }, selfId: 555);

        var g = Assert.Single(groups);  // assignment 9 has 0 pending -> omitted
        Assert.Equal(100, g.CourseId);
        Assert.Equal("Design Doc", g.AssignmentName);
        Assert.Equal(3, g.PendingCount);
        Assert.Equal(new DateTimeOffset(2026, 6, 14, 3, 59, 0, TimeSpan.Zero), g.DueAt);
    }
}
