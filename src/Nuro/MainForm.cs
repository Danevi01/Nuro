using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Nuro;

public sealed class MainForm : Form
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private readonly WebView2 _web;
    private LocalServer? _server;

    public MainForm()
    {
        Text = "Nuro";
        try { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath); } catch { }
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 880);
        MinimumSize = new Size(720, 500);
        BackColor = Color.FromArgb(30, 29, 27);

        _web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(30, 29, 27) };
        Controls.Add(_web);
        Load += async (_, _) => await StartAsync();
        FormClosed += (_, _) => _server?.Stop();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int dark = 1;
        try { DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)); } catch { }
    }

    private async Task StartAsync()
    {
        try
        {
            _ = Task.Run(OllamaHelper.EnsureRunningAsync);
            _server = new LocalServer(this, Config.Load());
            _server.Start();

            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Config.Dir, "webview"));
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsZoomControlEnabled = true;
            // Links in neuen Fenstern im normalen Browser öffnen
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
            };
            _web.Source = new Uri(_server.Url);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show(this,
                "Für Nuro wird die Microsoft-Edge-WebView2-Laufzeit benötigt.\n\nSie ist bei Windows 10/11 normalerweise vorinstalliert. " +
                "Du kannst sie kostenlos bei Microsoft herunterladen: https://developer.microsoft.com/microsoft-edge/webview2/",
                "Nuro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Nuro konnte nicht starten:\n\n" + ex.Message, "Nuro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    /// <summary>Ordnerauswahl-Dialog auf dem UI-Thread anzeigen.</summary>
    public Task<string?> PickFolderAsync()
    {
        var tcs = new TaskCompletionSource<string?>();
        BeginInvoke(() =>
        {
            try
            {
                using var dlg = new FolderBrowserDialog
                {
                    Description = "Ordner für Nuro freigeben (lesen und schreiben)",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = true
                };
                tcs.SetResult(dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null);
            }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    /// <summary>App beenden (für das Selbst-Update).</summary>
    public void Shutdown() => BeginInvoke(new Action(Close));
}
