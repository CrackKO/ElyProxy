using System.IO;

namespace ElyProxy.Updater;

/// <summary>
/// Content-level directory replacement for the updater. Files locked by a
/// running helper service are skipped instead of aborting the whole update:
/// the previous strict clear-then-move logic deleted the client first and
/// then failed to restore, leaving a broken installation.
/// </summary>
public static class DirectoryReplacer
{
    /// <summary>Copies every file from the staging directory over the install directory; returns skipped targets.</summary>
    public static IReadOnlyList<string> CopyOver(string source, string destination)
    {
        var skipped = new List<string>();
        CopyOverCore(source, destination, skipped);
        return skipped;
    }

    /// <summary>Deletes install-directory entries that are not part of the new package; returns skipped paths.</summary>
    public static IReadOnlyList<string> RemoveStale(string installDirectory, string stagingDirectory)
    {
        var skipped = new List<string>();
        RemoveStaleCore(installDirectory, stagingDirectory, skipped);
        return skipped;
    }

    /// <summary>Best-effort recursive delete; returns paths that could not be removed.</summary>
    public static IReadOnlyList<string> Clear(string directory)
    {
        var skipped = new List<string>();
        ClearCore(directory, skipped);
        return skipped;
    }

    private static void CopyOverCore(string source, string destination, List<string> skipped)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            try
            {
                File.Copy(file, target, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(target);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyOverCore(directory, Path.Combine(destination, Path.GetFileName(directory)), skipped);
        }
    }

    private static void RemoveStaleCore(string installDirectory, string stagingDirectory, List<string> skipped)
    {
        if (!Directory.Exists(installDirectory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(installDirectory))
        {
            var relative = Path.GetRelativePath(installDirectory, file);
            if (File.Exists(Path.Combine(stagingDirectory, relative)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(file);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(installDirectory))
        {
            var relative = Path.GetRelativePath(installDirectory, directory);
            var staged = Path.Combine(stagingDirectory, relative);
            if (Directory.Exists(staged))
            {
                RemoveStaleCore(directory, staged, skipped);
                continue;
            }

            ClearCore(directory, skipped);
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void ClearCore(string directory, List<string> skipped)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(file);
            }
        }

        foreach (var subDirectory in Directory.EnumerateDirectories(directory))
        {
            ClearCore(subDirectory, skipped);
            try
            {
                if (!Directory.EnumerateFileSystemEntries(subDirectory).Any())
                {
                    Directory.Delete(subDirectory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
