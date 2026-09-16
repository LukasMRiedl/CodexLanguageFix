using System.Diagnostics.Tracing;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Infrastructure;

// EventCounters werden nur bei angeschlossenem Diagnoseleser abgefragt.
[EventSource(Name = "CodexLanguageFix-Memory")]
internal sealed class MemoryDiagnostics : EventSource
{
    private readonly PollingCounter[] _counters;

    public MemoryDiagnostics(CodexComposerAccessor accessor)
    {
        _counters =
        [
            new PollingCounter("managed-heap-bytes", this, () => GC.GetTotalMemory(false)) { DisplayName = "Managed heap bytes" },
            new PollingCounter("gc-committed-bytes", this, () => GC.GetGCMemoryInfo().TotalCommittedBytes) { DisplayName = "GC committed bytes" },
            new PollingCounter("allocated-bytes", this, () => GC.GetTotalAllocatedBytes()) { DisplayName = "Allocated bytes" },
            new PollingCounter("uia-captures", this, () => accessor.CaptureCount) { DisplayName = "UIA captures" },
            new PollingCounter("layout-captures", this, () => accessor.LayoutCaptureCount) { DisplayName = "Layout captures" }
        ];
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _counters is not null)
            foreach (var counter in _counters) counter.Dispose();
        base.Dispose(disposing);
    }
}
