using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace Wpf_gdRunnerLite.Services;

public sealed record DesktopShortcutResult(string ShortcutPath, bool ReplacedExisting);

public static class DesktopShortcutService
{
    private const string ShortcutFileName = "Grim Dawn Runner Lite.lnk";

    public static DesktopShortcutResult CreateOrReplace()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("桌面快捷方式仅支持 Windows。");
        }

        string desktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrWhiteSpace(desktopDirectory))
        {
            throw new DirectoryNotFoundException("没有找到当前用户的桌面目录。");
        }

        return CreateOrReplaceInDirectory(desktopDirectory);
    }

    public static DesktopShortcutResult CreateOrReplaceInDirectory(string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("快捷方式目录不能为空。", nameof(destinationDirectory));
        }

        destinationDirectory = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationDirectory);
        string targetPath = ResolveLauncherPath();
        string shortcutPath = Path.Combine(destinationDirectory, ShortcutFileName);
        bool replacedExisting = File.Exists(shortcutPath);

        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host 不可用。");

        object? shellObject = null;
        object? shortcutObject = null;
        try
        {
            shellObject = Activator.CreateInstance(shellType)
                ?? throw new InvalidOperationException("创建 Windows Shell 对象失败。");
            dynamic shell = shellObject;
            shortcutObject = shell.CreateShortcut(shortcutPath);
            dynamic shortcut = shortcutObject;
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? AppContext.BaseDirectory;
            shortcut.Description = "Grim Dawn Runner Lite";
            shortcut.IconLocation = $"{targetPath},0";
            shortcut.WindowStyle = 1;
            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcutObject);
            ReleaseComObject(shellObject);
        }

        if (!File.Exists(shortcutPath))
        {
            throw new IOException("桌面快捷方式写入后未找到目标文件。");
        }

        return new DesktopShortcutResult(shortcutPath, replacedExisting);
    }

    private static string ResolveLauncherPath()
    {
        string expectedPath = Path.Combine(AppContext.BaseDirectory, "GrimDawnLauncher.exe");
        if (File.Exists(expectedPath)) return expectedPath;

        string? processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath)) return processPath;

        processPath = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath)) return processPath;

        throw new FileNotFoundException("没有找到当前启动器 EXE。");
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.FinalReleaseComObject(value);
        }
    }
}
