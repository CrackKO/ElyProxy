namespace ElyProxy.Helper;
internal static class HelperLifecycleLog
{
    public static void Write(string operation, Exception error)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ElyProxy");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "helper-lifecycle.log"), $"{DateTimeOffset.Now:O} {operation} {error}\n");
        }
        catch { }
    }
}
