using System.IO.Pipes;
using System.Text.Json;
using CodexLanguageFix.Infrastructure;

// This process only transports bounded messages; the desktop app owns all credentials and corrections.
try
{
    if (args.Length == 0) return 2;
    using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, BrowserProtocol.HostName + ".json")));
    var allowed = manifest.RootElement.GetProperty("allowed_origins").EnumerateArray().Select(x => x.GetString());
    if (!args[0].StartsWith("chrome-extension://", StringComparison.Ordinal) || !allowed.Contains(args[0], StringComparer.Ordinal)) return 2;
    using var pipe = new NamedPipeClientStream(".", BrowserProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    using var stop = new CancellationTokenSource();
    await pipe.ConnectAsync(3000, stop.Token);
    async Task Copy(Stream input, Stream output, bool validate)
    {
        while (await BrowserProtocol.ReadAsync(input, stop.Token) is { } body)
        {
            if (validate)
            {
                var message = JsonSerializer.Deserialize<BrowserMessage>(body, BrowserProtocol.Json);
                if (message is null || !BrowserProtocol.IsValid(message)) throw new InvalidDataException("Invalid request.");
            }
            await BrowserProtocol.WriteAsync(output, body, stop.Token);
        }
    }
    var toApp = Copy(Console.OpenStandardInput(), pipe, true);
    var toBrowser = Copy(pipe, Console.OpenStandardOutput(), false);
    await await Task.WhenAny(toApp, toBrowser);
    stop.Cancel();
    return 0;
}
catch { return 1; }
