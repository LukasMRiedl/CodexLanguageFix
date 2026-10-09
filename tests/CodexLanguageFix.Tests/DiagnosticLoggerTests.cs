using System.IO;
using System.Text.Json;
using CodexLanguageFix.Infrastructure;

namespace CodexLanguageFix.Tests;

public sealed class DiagnosticLoggerTests
{
    [Fact]
    public void Failure_RecordsTypeAndErrorCodeWithoutExceptionContents()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LanguageFixDiagnosticTest-" + Guid.NewGuid());
        try
        {
            var logger = new DiagnosticLogger(directory);
            var exception = new InvalidOperationException("PRIVATE-DRAFT-TEXT", new Exception("PRIVATE-INNER-TEXT"));
            logger.WriteFailure("application_dispatcher_failure", exception);
            var line = File.ReadAllText(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs"))));
            using var entry = JsonDocument.Parse(line);
            Assert.Equal("application_dispatcher_failure", entry.RootElement.GetProperty("eventName").GetString());
            Assert.Equal(typeof(InvalidOperationException).FullName, entry.RootElement.GetProperty("reason").GetString());
            Assert.Equal(exception.HResult, entry.RootElement.GetProperty("statusCode").GetInt32());
            Assert.DoesNotContain("PRIVATE", line);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Failure_WhenLogFileIsLocked_DoesNotThrow()
    {
        var directory = Path.Combine(Path.GetTempPath(), "LanguageFixDiagnosticTest-" + Guid.NewGuid());
        try
        {
            var logger = new DiagnosticLogger(directory);
            logger.Write("application_started");
            using var lockedFile = File.Open(Assert.Single(Directory.GetFiles(Path.Combine(directory, "logs"))),
                FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            logger.WriteFailure("application_runtime_failure", new Exception("PRIVATE-DRAFT-TEXT"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}