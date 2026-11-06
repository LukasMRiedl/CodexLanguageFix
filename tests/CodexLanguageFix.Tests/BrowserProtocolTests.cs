using System.Buffers.Binary;
using System.Text.Json;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;
public sealed class BrowserProtocolTests
{
    private static BrowserMessage Valid(string type = "correct") => new(1, type, "request", "document", 0, "field", 1, "Testtext");
    [Fact] public void SupportedRequestIsAccepted() => Assert.True(BrowserProtocol.IsValid(Valid()));
    [Theory][InlineData("state")][InlineData("cancel")]
    public void StateAndCancelDoNotRequireText(string type) => Assert.True(BrowserProtocol.IsValid(Valid(type) with { Text = null }));
    [Fact] public void RejectsUnboundedOrUnknownRequests()
    {
        Assert.False(BrowserProtocol.IsValid(Valid() with { Type = "execute" }));
        Assert.False(BrowserProtocol.IsValid(Valid() with { Version = 2 }));
        Assert.False(BrowserProtocol.IsValid(Valid() with { FrameId = -1 }));
        Assert.False(BrowserProtocol.IsValid(Valid() with { Revision = -1 }));
        Assert.False(BrowserProtocol.IsValid(Valid() with { RequestId = "" }));
        Assert.False(BrowserProtocol.IsValid(Valid() with { FieldId = new string('a', 129) }));
        Assert.False(BrowserProtocol.IsValid(Valid() with { Text = new string('a', 20_001) }));
        Assert.False(BrowserProtocol.IsValid(Valid() with { Text = "  " }));
    }
    [Fact] public async Task FramingPreservesUnicodeAndEnvelope()
    {
        using var stream = new MemoryStream();
        var value = Valid() with { Text = "Grüße 😀\nZweite Zeile" };
        await BrowserProtocol.WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(value, BrowserProtocol.Json), default);
        stream.Position = 0;
        var body = await BrowserProtocol.ReadAsync(stream, default);
        Assert.Equal(value, JsonSerializer.Deserialize<BrowserMessage>(body!, BrowserProtocol.Json));
        Assert.Null(await BrowserProtocol.ReadAsync(stream, default));
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(262145)]
    public async Task RejectsInvalidLengthBeforeAllocating(int length)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, length);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => BrowserProtocol.ReadAsync(stream, default));
    }
    [Fact] public async Task TruncatedMessageIsRejected()
    {
        using var stream = new MemoryStream(new byte[] { 5, 0, 0, 0, 65 });
        await Assert.ThrowsAsync<EndOfStreamException>(() => BrowserProtocol.ReadAsync(stream, default));
    }
    [Fact] public void TargetIncludesDocumentFrameFieldAndRevision()
    {
        var a = Valid();
        Assert.True(BrowserBridgeServer.SameTarget(a, a with { RequestId = "new" }));
        Assert.False(BrowserBridgeServer.SameTarget(a, a with { DocumentId = "other" }));
        Assert.False(BrowserBridgeServer.SameTarget(a, a with { FrameId = 2 }));
        Assert.False(BrowserBridgeServer.SameTarget(a, a with { FieldId = "other" }));
        Assert.False(BrowserBridgeServer.SameTarget(a, a with { Revision = 2 }));
    }
}
