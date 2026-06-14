using CanvasNudge;
using Xunit;

namespace CanvasNudge.Tests;

public class ChunkerTests
{
    private static DiscordWebhookPayload Embed(string title, string description) =>
        new(Embeds: new[] { new DiscordEmbed(Title: title, Description: description) });

    [Fact]
    public void Chunk_PassesThrough_WhenWithinLimit()
    {
        var payload = Embed("H", "• one\n• two");

        var result = Chunker.Chunk(payload, maxDescriptionLength: 4096);

        var only = Assert.Single(result);
        Assert.Same(payload, only);
    }

    [Fact]
    public void Chunk_SplitsAtLineBoundaries_NeverMidItem()
    {
        // 5 lines of 8 chars each; maxLen 20 forces ~2 lines per chunk.
        var lines = new[] { "• aaaaaa", "• bbbbbb", "• cccccc", "• dddddd", "• eeeeee" };
        var payload = Embed("HDR", string.Join("\n", lines));

        var result = Chunker.Chunk(payload, maxDescriptionLength: 20);

        Assert.True(result.Count > 1);
        foreach (var p in result)
        {
            var desc = p.Embeds!.Single().Description!;
            Assert.True(desc.Length <= 20, $"chunk too long: {desc.Length}");
            // every line stays intact (no line was cut)
            foreach (var line in desc.Split('\n'))
                Assert.Contains(line, lines);
        }

        // Reassembling all chunk descriptions reproduces every original line in order.
        var reassembled = result.SelectMany(p => p.Embeds!.Single().Description!.Split('\n')).ToArray();
        Assert.Equal(lines, reassembled);

        // First chunk keeps the title; continuations drop it.
        Assert.Equal("HDR", result[0].Embeds!.Single().Title);
        Assert.Null(result[1].Embeds!.Single().Title);
    }

    [Fact]
    public void Chunk_EmitsOversizeLineWhole_RatherThanTruncate()
    {
        var huge = "• " + new string('x', 50);
        var payload = Embed("H", huge);

        var result = Chunker.Chunk(payload, maxDescriptionLength: 20);

        var only = Assert.Single(result);
        Assert.Equal(huge, only.Embeds!.Single().Description);  // never truncated
    }
}
