using System.IO.Compression;

namespace ElyProxy.Updater;

/// <summary>
/// Safe ZIP extraction for update packages. The BCL already rejects most
/// path-traversal entries, but every entry is validated explicitly so a
/// crafted package can never write outside the staging directory.
/// </summary>
public static class PackageExtractor
{
    public static void ExtractVerified(string packagePath, string destinationDirectory)
    {
        var destination = Path.GetFullPath(destinationDirectory);
        using (var archive = ZipFile.OpenRead(packagePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (!IsEntrySafe(entry.FullName, destination))
                {
                    throw new InvalidDataException($"Пакет обновления содержит недопустимый путь: {entry.FullName}");
                }
            }
        }

        ZipFile.ExtractToDirectory(packagePath, destination);
    }

    public static bool IsEntrySafe(string entryName, string destinationDirectory)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            return false;
        }

        var destination = Path.GetFullPath(destinationDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = destination + Path.DirectorySeparatorChar;

        string target;
        try
        {
            target = Path.GetFullPath(Path.Combine(destination, entryName));
        }
        catch (Exception)
        {
            return false;
        }

        return target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
