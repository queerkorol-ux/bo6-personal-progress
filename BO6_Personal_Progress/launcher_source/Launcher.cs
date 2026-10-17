using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

[assembly: AssemblyTitle("BO6 Personal Progress Launcher")]
[assembly: AssemblyDescription("Persistent x64 entry point for the unchanged v024 progression package")]
[assembly: AssemblyVersion("0.3.5.0")]
[assembly: AssemblyFileVersion("0.3.5.0")]

public static class BO6LauncherCore {
    public static string Quote(string value) {
        if (value == null) throw new ArgumentNullException("value");
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value) {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
            else { result.Append('\\', slashes); result.Append(c); }
            slashes = 0;
        }
        result.Append('\\', slashes * 2); result.Append('"');
        return result.ToString();
    }
    public static ProcessStartInfo ElevatedStart(string executable, string package) {
        return new ProcessStartInfo {
            FileName = executable, Arguments = "--elevated-child", WorkingDirectory = package,
            UseShellExecute = true, Verb = "runas"
        };
    }
    public static ProcessStartInfo BridgeStart(string powershell, string adapter, string package) {
        return new ProcessStartInfo {
            FileName = powershell,
            Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(adapter),
            WorkingDirectory = package, UseShellExecute = false, CreateNoWindow = false
        };
    }
    public static int WaitForChild(ProcessStartInfo info, Action<string> log) {
        using (var process = new Process()) {
            process.StartInfo = info;
            if (!process.Start()) throw new InvalidOperationException("Не получен процесс дочернего запускателя.");
            IntPtr retainedHandle = process.Handle;
            if (retainedHandle == IntPtr.Zero) throw new InvalidOperationException("Не удалось сохранить дескриптор дочернего процесса.");
            if (log != null) log("child_started pid=" + process.Id + " file=" + info.FileName);
            process.WaitForExit();
            int code = process.ExitCode;
            if (log != null) log("child_exited code=" + code);
            return code;
        }
    }
}

static class BO6Launcher {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int MessageBox(IntPtr owner, string text, string title, uint flags);
    static string logPath;
    static void Log(string line) {
        try { File.AppendAllText(logPath, DateTime.UtcNow.ToString("o") + " " + line + Environment.NewLine, new UTF8Encoding(false)); }
        catch { }
    }
    static void ShowError(string text) {
        MessageBox(IntPtr.Zero, text + "\r\n\r\nЛог запускателя: " + (logPath ?? "не создан") +
            "\r\nЛоги Personal Progress: BO6_Personal_Progress_Data\\logs рядом с папкой игры.",
            "BO6 Personal Progress — запуск", 0x10);
    }
    [STAThread]
    static int Main(string[] args) {
        try {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BO6_Personal_Progress_Launcher");
            Directory.CreateDirectory(logDir);
            logPath = Path.Combine(logDir, DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N") + ".log");
            string executable = Path.GetFullPath(Assembly.GetExecutingAssembly().Location);
            string package = Path.GetDirectoryName(executable);
            bool elevatedChild = args.Length == 1 && args[0] == "--elevated-child";
            if (args.Length != 0 && !elevatedChild) throw new ArgumentException("Запускай EXE без дополнительных аргументов.");
            if (IntPtr.Size != 8) throw new InvalidOperationException("Для этого запускателя требуется64-битный Windows.");
            bool admin;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            Log("launcher_started pid=" + Process.GetCurrentProcess().Id + " admin=" + admin + " elevated_child=" + elevatedChild + " package=" + package);
            if (!admin) {
                if (elevatedChild) throw new InvalidOperationException("Повышенный запуск не получил права администратора. Игра не запущена.");
                // This original asInvoker process stays alive across account-changing UAC,
                // waiting on the retained handle for the elevated copy of this same EXE.
                return BO6LauncherCore.WaitForChild(BO6LauncherCore.ElevatedStart(executable, package), Log);
            }
            string adapter = Path.Combine(package, "launcher_source", "Start_From_Exe.ps1");
            if (!File.Exists(adapter)) throw new FileNotFoundException("Не найден Start_From_Exe.ps1. Распакуй весь комплект запускателя в папку Personal Progress.", adapter);
            string powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32\\WindowsPowerShell\\v1.0\\powershell.exe");
            if (!File.Exists(powershell)) throw new FileNotFoundException("Не найден64-битный Windows PowerShell.", powershell);
            int result = BO6LauncherCore.WaitForChild(BO6LauncherCore.BridgeStart(powershell, adapter, package), Log);
            if (result != 0) ShowError("Personal Progress завершился с ошибкой (код " + result + "). Проверь консоль и LOG ZIP; сохранения оставь на месте.");
            return result;
        } catch (Win32Exception e) {
            Log("launcher_error " + e);
            if (e.NativeErrorCode == 1223) {
                ShowError("Запрос прав администратора отменён. Игра не запущена, файлы игры и прогресс не изменены.");
                return 1223;
            }
            ShowError(e.Message); return 1;
        } catch (Exception e) {
            Log("launcher_error " + e); ShowError(e.Message); return 1;
        }
    }
}
