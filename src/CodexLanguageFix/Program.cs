using System.Runtime.CompilerServices;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Do not construct Application or load its resources in the headless worker.
        if (args.FirstOrDefault() == "--composer-worker")
            return args is [_, var value] && int.TryParse(value, out var parentId) && parentId > 0
                ? ComposerProcessServer.RunNative(parentId) : 2;
        return RunDesktop();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunDesktop()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
