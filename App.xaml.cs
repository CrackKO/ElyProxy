namespace ElyProxy;

public partial class App : System.Windows.Application
{
    public static bool LaunchElyTunOnStartup { get; private set; }

    public static int? ElyTunHandoffParentProcessId { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        LaunchElyTunOnStartup = Services.ElyTunElevationService.TryGetLaunchRequest(
            e.Args,
            out int? parentProcessId);
        ElyTunHandoffParentProcessId = parentProcessId;

        base.OnStartup(e);

        Services.ThemeService.Current.PreloadFromDisk();

        DispatcherUnhandledException += (_, args) =>
        {
            System.Windows.MessageBox.Show(
                $"Необработанная ошибка:\n{args.Exception.Message}",
                "ElyProxy — Ошибка",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
