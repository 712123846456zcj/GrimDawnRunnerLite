using System.IO;
using System.Windows;
using Microsoft.Win32;
using Wpf_gdRunnerLite.Models;

namespace Wpf_gdRunnerLite.Services;

public static class GameLocationService
{
    private const string GameExecutableName = "Grim Dawn.exe";


    public static bool IsValidGameRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;

        return File.Exists(Path.Combine(path, GameExecutableName)) ||
               File.Exists(Path.Combine(path, "x64", GameExecutableName));
    }

    public static string? ResolveRootFromDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath)) return null;

        string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directoryPath));
        string folderName = Path.GetFileName(directory);
        if (folderName.Equals("x64", StringComparison.OrdinalIgnoreCase) ||
            folderName.Equals("x86", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Directory.GetParent(directory)?.FullName;
            if (IsValidGameRoot(parent)) return Path.TrimEndingDirectorySeparator(parent!);
        }

        return IsValidGameRoot(directory) ? directory : null;
    }

    public static string? ResolveRootFromExecutable(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath)) return null;
        if (!string.Equals(Path.GetFileName(executablePath), GameExecutableName, StringComparison.OrdinalIgnoreCase)) return null;

        string? directory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (string.IsNullOrWhiteSpace(directory)) return null;

        string folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        string root = folderName.Equals("x64", StringComparison.OrdinalIgnoreCase) ||
                      folderName.Equals("x86", StringComparison.OrdinalIgnoreCase)
            ? Directory.GetParent(directory)?.FullName ?? directory
            : directory;

        root = Path.TrimEndingDirectorySeparator(root);
        return IsValidGameRoot(root) ? root : null;
    }

    public static string? LoadSavedGameRoot()
    {
        return ResolveRootFromDirectory(SettingsService.Load().GameRoot);
    }

    public static void SaveGameRoot(string gameRoot)
    {
        try
        {
            LauncherSettings settings = SettingsService.Load();
            settings.GameRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameRoot));
            SettingsService.Save(settings);
        }
        catch
        {
            // 路径仍可用于本次运行；持久化失败不会阻止启动器工作。
        }
    }

    public static string? SelectGameRoot(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 Grim Dawn 主程序",
            Filter = "Grim Dawn 主程序 (Grim Dawn.exe)|Grim Dawn.exe|可执行文件 (*.exe)|*.exe",
            FileName = GameExecutableName,
            CheckFileExists = true,
            Multiselect = false
        };

        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == true ? ResolveRootFromExecutable(dialog.FileName) : null;
    }
}
