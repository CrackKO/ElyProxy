using System.IO;

namespace ElyProxy.Updater;

/// <summary>Parsed updater command line: all three values are required.</summary>
public sealed record UpdaterOptions(string InstallDirectory, string PackagePath, int WaitPid);

/// <summary>
/// Command-line parsing for ElyProxy.Updater, split out of Program so the
/// argument contract is covered by tests.
/// </summary>
public static class UpdaterArguments
{
    public static UpdaterOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index + 1 < args.Length; index += 2)
        {
            values[args[index]] = args[index + 1];
        }

        if (!values.TryGetValue("--install-dir", out var installDirectory)
            || !values.TryGetValue("--package", out var package)
            || !values.TryGetValue("--wait-pid", out var processText)
            || !int.TryParse(processText, out var processId)
            || processId <= 0)
        {
            throw new ArgumentException("Недопустимые параметры обновления.");
        }

        return new UpdaterOptions(installDirectory, package, processId);
    }
}

/// <summary>
/// Pre-flight validation of an update request, before any file in the install
/// directory is touched. Never destructive; throws on invalid input.
/// </summary>
public static class InstallPreflight
{
    public static (string InstallDirectory, string PackagePath) Validate(string installDirectory, string packagePath)
    {
        var install = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(install, Path.GetPathRoot(install), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Недопустимая папка установки.");
        }

        if (!File.Exists(Path.Combine(install, "ElyProxy.exe")))
        {
            throw new FileNotFoundException("Не найден ElyProxy.exe в папке установки.");
        }

        var package = Path.GetFullPath(packagePath);
        if (!File.Exists(package))
        {
            throw new FileNotFoundException("Пакет обновления не найден.", package);
        }

        return (install, package);
    }
}
