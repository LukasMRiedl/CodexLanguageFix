using System.Buffers.Binary;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexLanguageFix.Infrastructure;

internal static class BrowserProtocol
{
    internal const int MaximumBytes = 256 * 1024;
#if BROWSER_INTEGRATION_FIXTURE
    internal const string HostName = "com.codexlanguagefix.chrome.test";
    internal static string PipeName => "CodexLanguageFix.Browser." + WindowsIdentity.GetCurrent().User!.Value + ".Test";
#else
    internal const string HostName = "com.codexlanguagefix.chrome";
    internal static string PipeName => "CodexLanguageFix.Browser." + WindowsIdentity.GetCurrent().User!.Value;
#endif
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static bool IsValid(BrowserMessage m) => m.Version == 1
        && m.Type is "state" or "prepare" or "correct" or "cancel"
        && ValidId(m.RequestId) && ValidId(m.DocumentId) && ValidId(m.FieldId)
        && m.FrameId >= 0 && m.Revision >= 0
        && (m.Type is not ("prepare" or "correct") || (!string.IsNullOrWhiteSpace(m.Text) && m.Text.Length <= 20_000));
    private static bool ValidId(string? value) => value is { Length: > 0 and <= 128 } && !value.Any(char.IsControl);
    internal static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellation)
    {
        var header = new byte[4];
        var first = await stream.ReadAsync(header.AsMemory(0, 1), cancellation);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellation);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid message size.");
        var body = new byte[size];
        await stream.ReadExactlyAsync(body, cancellation);
        return body;
    }
    internal static async Task WriteAsync(Stream stream, byte[] body, CancellationToken cancellation)
    {
        if (body.Length is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid message size.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, cancellation);
        await stream.WriteAsync(body, cancellation);
        await stream.FlushAsync(cancellation);
    }
}
internal sealed record BrowserMessage(
    [property: JsonRequired] int Version,
    [property: JsonRequired] string Type,
    [property: JsonRequired] string RequestId,
    [property: JsonRequired] string DocumentId,
    [property: JsonRequired] int FrameId,
    [property: JsonRequired] string FieldId,
    [property: JsonRequired] long Revision,
    string? Text = null);
internal sealed record BrowserState(bool Enabled, bool Prepare, uint ShortcutModifiers, uint ShortcutVirtualKey);
