using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Capture
{
    struct LaunchMode
    {
        public const string CaptureArgument = "--recortar";
        public const string StartupArgument = "--al-iniciar";

        public bool CaptureOnStart { get; private set; }
        public bool ForwardIfRunning { get; private set; }

        public static LaunchMode Parse(string[] arguments)
        {
            bool atLogin = Array.IndexOf(arguments, StartupArgument) >= 0;
            return new LaunchMode
            {
                CaptureOnStart = !atLogin && Array.IndexOf(arguments, CaptureArgument) >= 0,
                ForwardIfRunning = !atLogin
            };
        }
    }

    static class WindowsShortcuts
    {
        const string StartupLinkName = "Capture (inicio automático).lnk";

        public static bool StartsWithWindows
        {
            get
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup, Environment.SpecialFolderOption.DoNotVerify);
                return folder.Length > 0 && File.Exists(Path.Combine(folder, StartupLinkName));
            }
        }

        public static void SetStartup(bool enabled)
        {
            var path = StartupPath();
            if (enabled) SaveShortcut(path, LaunchMode.StartupArgument, "Inicia Capture junto al reloj");
            else File.Delete(path);
        }

        public static void CreateStartMenuShortcut()
        {
            var programs = ShortcutFolder(Environment.SpecialFolder.Programs);
            var path = Path.Combine(programs, "Capture", "Capture.lnk");
            SaveShortcut(path, LaunchMode.CaptureArgument, "Selecciona un área de la pantalla con Capture");
        }

        static string StartupPath()
        {
            return Path.Combine(ShortcutFolder(Environment.SpecialFolder.Startup), StartupLinkName);
        }

        static string ShortcutFolder(Environment.SpecialFolder id)
        {
            var path = Environment.GetFolderPath(id, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(path))
                throw new DirectoryNotFoundException("Windows no ha facilitado la carpeta de accesos de tu sesión.");
            return path;
        }

        static void SaveShortcut(string path, string arguments, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            object shortcut = null;
            try
            {
                shortcut = Invoke(shell, "CreateShortcut", BindingFlags.InvokeMethod, path);
                Invoke(shortcut, "TargetPath", BindingFlags.SetProperty, Application.ExecutablePath);
                Invoke(shortcut, "Arguments", BindingFlags.SetProperty, arguments);
                Invoke(shortcut, "Description", BindingFlags.SetProperty, description);
                Invoke(shortcut, "WorkingDirectory", BindingFlags.SetProperty, Path.GetTempPath());
                Invoke(shortcut, "IconLocation", BindingFlags.SetProperty, Application.ExecutablePath + ",0");
                Invoke(shortcut, "Save", BindingFlags.InvokeMethod);
            }
            finally
            {
                if (shortcut != null) Marshal.ReleaseComObject(shortcut);
                Marshal.ReleaseComObject(shell);
            }
        }

        static object Invoke(object target, string member, BindingFlags operation, params object[] values)
        {
            return target.GetType().InvokeMember(member, operation, null, target, values);
        }
    }
}
