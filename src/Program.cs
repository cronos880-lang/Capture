using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Capture
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Directory.SetCurrentDirectory(Path.GetTempPath());
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\Capture.Windows.Desktop", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show("Capture ya está abierto. Pulsa Ctrl+Alt+R o usa su icono junto al reloj.",
                        "Capture", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                try
                {
                    using (var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                    using (var app = new CaptureApplication(icon ?? SystemIcons.Application))
                        Application.Run(app);
                }
                catch (Exception error)
                {
                    MessageBox.Show("No se pudo ejecutar Capture. " + error.Message, "Capture",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }

    sealed class CaptureApplication : ApplicationContext
    {
        readonly EditorForm editor;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly CaptureHotKey hotKey;
        readonly System.Windows.Forms.Timer captureTimer;
        RegionSelector selector;
        bool restoreEditor;
        bool finishingCapture;

        public CaptureApplication(Icon icon)
        {
            editor = new EditorForm(icon);
            editor.CaptureRequested += RequestCapture;
            editor.FormClosed += delegate { ExitThread(); };

            captureTimer = new System.Windows.Forms.Timer { Interval = 140 };
            captureTimer.Tick += BeginSelection;
            hotKey = new CaptureHotKey();
            hotKey.Pressed += RequestCapture;

            menu = new ContextMenuStrip();
            var captureItem = menu.Items.Add("Nueva captura");
            captureItem.Click += RequestCapture;
            if (hotKey.Registered)
                ((ToolStripMenuItem)captureItem).ShortcutKeyDisplayString = "Ctrl+Alt+R";
            var open = menu.Items.Add("Abrir editor");
            open.Click += delegate { if (!Busy) ShowEditor(true); };
            menu.Opening += delegate { open.Available = editor.HasCapture; };
            menu.Items.Add("Salir").Click += delegate
            {
                if (Busy) return;
                if (editor.IsHandleCreated) editor.Close();
                else ExitThread();
            };

            tray = new NotifyIcon { Icon = icon, ContextMenuStrip = menu, Visible = true };
            tray.Text = hotKey.Registered ? "Capture · Ctrl+Alt+R" : "Capture";
            tray.DoubleClick += delegate
            {
                if (Busy) return;
                if (editor.HasCapture) ShowEditor(true);
                else RequestCapture(this, EventArgs.Empty);
            };
            editor.SetShortcutAvailable(hotKey.Registered);
        }

        bool Busy { get { return selector != null || captureTimer.Enabled || finishingCapture; } }

        void RequestCapture(object sender, EventArgs e)
        {
            if (Busy || editor.IsDisposed || editor.IsEditing ||
                (editor.IsHandleCreated && !NativeWindowEnabled(editor.Handle))) return;
            restoreEditor = editor.Visible;
            if (restoreEditor)
            {
                ExcludeEditorFromCapture(true);
                editor.Hide();
            }
            captureTimer.Start();
        }

        void BeginSelection(object sender, EventArgs e)
        {
            captureTimer.Stop();
            try
            {
                DwmFlush();
                if (editor.IsHandleCreated && IsWindowVisible(editor.Handle))
                    throw new InvalidOperationException("La ventana del editor aún está visible.");
                var bounds = SystemInformation.VirtualScreen;
                var desktop = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppRgb);
                try
                {
                    using (var graphics = Graphics.FromImage(desktop))
                        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    selector = new RegionSelector(bounds, desktop);
                    desktop = null; // The selector now owns the frozen desktop.
                    selector.FormClosed += SelectionClosed;
                    selector.Open();
                }
                finally { if (desktop != null) desktop.Dispose(); }
            }
            catch (Exception error)
            {
                if (selector != null)
                {
                    selector.FormClosed -= SelectionClosed;
                    selector.Dispose();
                    selector = null;
                }
                RestoreEditor();
                var owner = editor.Visible ? editor : null;
                MessageBox.Show(owner, "No se pudo mostrar el recorte. " + error.Message, "Capture",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void SelectionClosed(object sender, FormClosedEventArgs e)
        {
            finishingCapture = true;
            var closedSelector = selector;
            selector = null;
            Bitmap capture = closedSelector.TakeResult();
            try
            {
                if (capture == null)
                {
                    RestoreEditor();
                    return;
                }
                if (editor.HasUnsavedChanges) ShowEditor(true);
                if (editor.ConfirmDiscard())
                {
                    editor.SetCapture(capture);
                    capture = null;
                }
                ShowEditor(true);
            }
            finally
            {
                if (capture != null) capture.Dispose();
                finishingCapture = false;
            }
        }

        void ShowEditor(bool activate)
        {
            if (editor.IsDisposed) return;
            ExcludeEditorFromCapture(false);
            if (activate && editor.WindowState == FormWindowState.Minimized)
                editor.WindowState = FormWindowState.Normal;
            editor.Show();
            if (activate) editor.Activate();
        }

        void RestoreEditor()
        {
            if (restoreEditor) ShowEditor(editor.WindowState != FormWindowState.Minimized);
        }

        void ExcludeEditorFromCapture(bool exclude)
        {
            // WDA_EXCLUDEFROMCAPTURE was introduced in Windows 10, version 2004.
            if (editor.IsHandleCreated && Environment.OSVersion.Version.Build >= 19041)
                SetWindowDisplayAffinity(editor.Handle, exclude ? 0x11u : 0u);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                captureTimer.Dispose();
                tray.Dispose();
                menu.Dispose();
                hotKey.Dispose();
                if (selector != null)
                {
                    selector.FormClosed -= SelectionClosed;
                    selector.Dispose();
                }
                if (!editor.IsDisposed) editor.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmFlush();

        [DllImport("user32.dll", EntryPoint = "IsWindowEnabled")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool NativeWindowEnabled(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
    }

    sealed class CaptureHotKey : NativeWindow, IDisposable
    {
        const int Id = 1;
        const int HotKeyMessage = 0x0312;
        const uint CtrlAltNoRepeat = 0x4003;
        public event EventHandler Pressed;
        public bool Registered { get; private set; }

        public CaptureHotKey()
        {
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
            Registered = RegisterHotKey(Handle, Id, CtrlAltNoRepeat, (uint)Keys.R);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == HotKeyMessage && m.WParam.ToInt32() == Id)
            {
                var handler = Pressed;
                if (handler != null) handler(this, EventArgs.Empty);
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Registered)
            {
                UnregisterHotKey(Handle, Id);
                Registered = false;
            }
            DestroyHandle();
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr window, int id);
    }
}
