using System.Diagnostics;
using System.Windows.Automation;

namespace CodexLanguageFix.Tests;

internal static class LiveDesktopAppProcess
{
    // The native worker shares the executable name but never owns an overlay.
    internal static Process FindOverlayOwner(Process[] processes, string buttonName)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            var owners = processes.Where(process => !process.HasExited
                && AutomationElement.RootElement.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id),
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.NameProperty, buttonName),
                    new PropertyCondition(AutomationElement.IsOffscreenProperty, false))) is not null).ToArray();
            if (owners.Length > 0) return Assert.Single(owners);
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("Kein eindeutig sichtbarer Textkorrektur-Knopf gefunden.");
    }
}
