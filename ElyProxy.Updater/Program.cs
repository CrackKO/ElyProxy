using System.Diagnostics;
using System.Windows.Forms;

namespace ElyProxy.Updater;

internal static class Program
{
    private const int WaitForClientTimeoutMs = 45_000;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = UpdaterArguments.Parse(args);
            WaitForClientToExit(options.WaitPid);
            Install(options);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Не удалось установить обновление. Резервная копия сохранена рядом с папкой установки.\n\n{ex.Message}",
                "ElyProxy — обновление",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void Install(UpdaterOptions options)
    {
        var (installDirectory, packagePath) = InstallPreflight.Validate(options.InstallDirectory, options.PackagePath);

        var parent = Directory.GetParent(installDirectory)?.FullName
            ?? throw new InvalidOperationException("Не удалось определить родительскую папку установки.");
        var name = Path.GetFileName(installDirectory);
        var staging = Path.Combine(parent, $".{name}-staging-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $"{name}-backup-{DateTime.UtcNow:yyyyMMddHHmmss}");
        var backupCreated = false;

        try
        {
            PackageExtractor.ExtractVerified(packagePath, staging);
            var stagedExecutable = Path.Combine(staging, "ElyProxy.exe");
            var stagedUpdater = Path.Combine(staging, "updater", "ElyProxy.Updater.exe");
            if (!File.Exists(stagedExecutable) || !File.Exists(stagedUpdater))
                throw new InvalidDataException("Пакет обновления не содержит полный набор файлов ElyProxy.");

            // Keep the installation directory itself in place. Moving that directory is
            // denied for some locations even after ElyProxy.exe has exited (for example,
            // a folder opened by Explorer). A content-level replacement avoids that
            // fragile operation and leaves a full rollback copy outside the app folder.
            CopyDirectoryContents(installDirectory, backup);
            backupCreated = true;

            // Copy the new files over the old ones, then drop leftovers. Files
            // locked by a running helper service (installed from the app folder
            // by older versions) are skipped instead of aborting the update.
            var skipped = new List<string>();
            skipped.AddRange(DirectoryReplacer.CopyOver(staging, installDirectory));
            skipped.AddRange(DirectoryReplacer.RemoveStale(installDirectory, staging));
            if (!File.Exists(Path.Combine(installDirectory, "ElyProxy.exe")))
            {
                throw new InvalidDataException("ElyProxy.exe не удалось установить: файл заблокирован другим процессом.");
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(installDirectory, "ElyProxy.exe"),
                    UseShellExecute = true,
                    WorkingDirectory = installDirectory
                });
                if (process is null)
                    throw new InvalidOperationException("Новая версия не запустилась.");
            }
            catch
            {
                RestoreBackup(installDirectory, backup);
                backupCreated = false;
                throw;
            }

            TryDelete(packagePath);
            WriteUpdateLog(skipped);
        }
        catch
        {
            if (backupCreated && Directory.Exists(backup))
                RestoreBackup(installDirectory, backup);
            throw;
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
    }

    private static void CopyDirectoryContents(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                CopyDirectory(entry, target);
            }
            else
            {
                File.Copy(entry, target, overwrite: true);
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static void RestoreBackup(string installDirectory, string backup)
    {
        DirectoryReplacer.Clear(installDirectory);
        DirectoryReplacer.CopyOver(backup, installDirectory);
    }

    private static void WriteUpdateLog(IReadOnlyList<string> skipped)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElyProxy");
            Directory.CreateDirectory(directory);
            var lines = new List<string>
            {
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Обновление установлено. Пропущено заблокированных файлов: {skipped.Count}"
            };
            lines.AddRange(skipped.Select(path => "  " + path));
            File.AppendAllLines(Path.Combine(directory, "updater.log"), lines);
        }
        catch
        {
        }
    }

    private static void WaitForClientToExit(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.WaitForExit(WaitForClientTimeoutMs))
                throw new TimeoutException("ElyProxy не завершился вовремя. Закройте его и повторите обновление.");
        }
        catch (ArgumentException)
        {
            // The client has already exited before the updater reached this point.
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
