using System.Runtime.InteropServices;

namespace Nuro;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, "Nuro_SingleInstance_Danevi01", out bool first);
        if (!first)
        {
            // Nuro läuft schon: vorhandenes Fenster nach vorne holen
            var h = FindWindow(null, "Nuro");
            if (h != IntPtr.Zero) { ShowWindow(h, 9); SetForegroundWindow(h); }
            return;
        }
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        GC.KeepAlive(mutex);
    }
}
