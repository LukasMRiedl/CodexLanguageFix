using System.IO;
using System.Text.Json;
using CodexLanguageFix.Core;
using CodexLanguageFix.Contracts;

namespace CodexLanguageFix.Infrastructure;

public sealed class DiagnosticLogger
{
    private readonly string _logDirectory;
    private readonly object _gate = new();

    public DiagnosticLogger(string applicationDirectory)
    {
        _logDirectory = Path.Combine(applicationDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);
        RemoveExpiredLogs();
    }

    public void Write(
        string eventName,
        int? characterCount = null,
        int? matchCount = null,
        int? statusCode = null,
        double? elapsedMilliseconds = null,
        CorrectionProviderKind? provider = null,
        ComposerHost? host = null,
        string? fieldCategory = null,
        string? reason = null)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.Now,
            eventName,
            provider = provider?.ToString(),
            characterCount,
            matchCount,
            statusCode,
            elapsedMilliseconds,
            host = host?.ToString(),
            fieldCategory,
            reason
        };

        var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        var path = Path.Combine(_logDirectory, $"diagnostic-{DateTime.Now:yyyy-MM-dd}.jsonl");
        try
        {
            lock (_gate)
            {
                File.AppendAllText(path, line);
            }
        }
        catch (IOException)
        {
            // Diagnosedaten dürfen die Korrekturfunktion niemals beeinträchtigen.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnosedaten dürfen die Korrekturfunktion niemals beeinträchtigen.
        }
    }

    public void WriteFailure(string eventName, Exception exception)
    {
        Write(eventName, statusCode: exception.HResult, reason: exception.GetType().FullName);
    }

    private void RemoveExpiredLogs()
    {
        foreach (var file in Directory.EnumerateFiles(_logDirectory, "diagnostic-*.jsonl"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Eine gesperrte alte Protokolldatei wird beim nächsten Start erneut geprüft.
            }
        }
    }
}
