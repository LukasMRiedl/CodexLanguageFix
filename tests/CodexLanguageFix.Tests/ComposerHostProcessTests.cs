using System.Diagnostics;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

public sealed class ComposerHostProcessTests
{
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\OpenAI.Codex_1.0_x64\app\ChatGPT.exe", ComposerHost.Codex)]
    [InlineData(@"C:\Users\Example\AppData\Local\OpenAI\Codex\ChatGPT.exe", ComposerHost.Codex)]
    [InlineData(@"C:\OPENAI\CODEX\CHATGPT.EXE", ComposerHost.Codex)]
    public void RecognizesExistingHostPaths(string path, ComposerHost expected)
    {
        Assert.Equal(expected, ComposerHostProcess.IdentifyPath(path));
    }

    [Fact]
    public void RejectsHermesAlsoAtItsFormerInstallPaths()
    {
        var paths = new[]
        {
            KnownPath(Environment.SpecialFolder.LocalApplicationData, @"Programs\Hermes\Hermes.exe"),
            KnownPath(Environment.SpecialFolder.LocalApplicationData,
                @"hermes\hermes-agent\apps\desktop\release\win-unpacked\Hermes.exe"),
            KnownPath(Environment.SpecialFolder.ProgramFiles, @"Hermes\Hermes.exe"),
            KnownPath(Environment.SpecialFolder.ProgramFilesX86, @"Hermes\Hermes.exe")
        }.Where(path => path is not null).Cast<string>().ToArray();

        Assert.NotEmpty(paths);
        foreach (var path in paths)
            Assert.Null(ComposerHostProcess.IdentifyPath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(@"C:\Other\ChatGPT.exe")]
    [InlineData(@"C:\Other\Antigravity.exe")]
    [InlineData(@"C:\Other\Hermes.exe")]
    [InlineData(@"C:\Users\Example\AppData\Local\Programs\antigravity\Antigravity.exe")]
    [InlineData(@"C:\PROGRAMS\ANTIGRAVITY\ANTIGRAVITY.EXE")]
    [InlineData(@"C:\Windows\System32\notepad.exe")]
    [InlineData(@"C:\Program Files\Google\Chrome\Application\chrome.exe")]
    [InlineData(@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe")]
    [InlineData(@"C:\OpenAI\Codex\Other.exe")]
    [InlineData(@"C:\OpenAI\Codex\ChatGPT.exe.bak")]
    [InlineData(@"C:\OpenAI\CodexOther\ChatGPT.exe")]
    [InlineData(@"C:\WindowsApps\OpenAI.ChatGPT_1.0\ChatGPT.exe")]
    [InlineData(@"C:\Programs\antigravity-other\Antigravity.exe")]
    [InlineData(@"C:\Programs\antigravity\ChatGPT.exe")]
    [InlineData(@"C:\Programs\Hermes-other\Hermes.exe")]
    [InlineData(@"C:\Program Files\Hermes-other\Hermes.exe")]
    [InlineData(@"C:\Program Files (x86)\Hermes-other\Hermes.exe")]
    [InlineData(@"C:\Programs\Hermes\ChatGPT.exe")]
    [InlineData(@"C:\hermes\hermes-agent\apps\desktop\release\win-unpacked-other\Hermes.exe")]
    [InlineData(@"C:\Temp\Program Files\Hermes\Hermes.exe")]
    [InlineData(@"D:\Fake\Programs\Hermes\Hermes.exe")]
    [InlineData(@"C:\Program Files\Hermes\payload\Hermes.exe")]
    public void RejectsNamesOutsideAllowedHostPaths(string? path)
    {
        Assert.Null(ComposerHostProcess.IdentifyPath(path));
    }

    [Fact]
    public void OwnTestProcessIsNotAComposerHost()
    {
        Assert.Null(ComposerHostProcess.Identify(Environment.ProcessId));
        Assert.Null(CodexComposerAccessor.TryGetComposerHost(Environment.ProcessId));
    }

    [Fact]
    public void ReachableForeignProcessDoesNotFallBackToWindowsHost()
    {
        var start = new ProcessStartInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/q");
        using var foreign = Process.Start(start)!;
        try
        {
            Assert.False(foreign.HasExited);
            Assert.NotEqual(Environment.ProcessId, foreign.Id);
            Assert.True(SpinWait.SpinUntil(() =>
            {
                foreign.Refresh();
                return foreign.MainModule is not null || foreign.HasExited;
            }, TimeSpan.FromSeconds(5)), "The foreign fixture's module must finish loading.");
            Assert.False(foreign.HasExited);
            Assert.Equal("cmd.exe", Path.GetFileName(foreign.MainModule!.FileName), StringComparer.OrdinalIgnoreCase);
            Assert.Null(CodexComposerAccessor.TryGetComposerHost(foreign.Id));
        }
        finally
        {
            foreign.StandardInput.Close();
            if (!foreign.WaitForExit(5000)) { foreign.Kill(); foreign.WaitForExit(); }
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(4)] // Windows System process; access may also be denied.
    [InlineData(int.MaxValue)]
    public void ForeignOrInvalidProcessFailsClosed(int processId)
    {
        Assert.Null(ComposerHostProcess.Identify(processId));
    }

    private static string? KnownPath(Environment.SpecialFolder folder, string relativePath)
    {
        var root = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(root) ? null : Path.Combine(root, relativePath);
    }
}
