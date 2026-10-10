namespace ElyProxy;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activationSignal;
    private RegisteredWaitHandle? _activationRegistration;

    public static event Action? ActivationRequested;

    public static bool LaunchElyTunOnStartup { get; private set; }

    public static int? ElyTunHandoffParentProcessId { get; private set; }

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        LaunchElyTunOnStartup = Services.ElyTunElevationService.TryGetLaunchRequest(
            e.Args,
            out int? parentProcessId);
        ElyTunHandoffParentProcessId = parentProcessId;

        if (!LaunchElyTunOnStartup && !TryAcquireSingleInstance())
        {
            Shutdown();
            return;
        }

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

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _activationRegistration?.Unregister(null);
        _activationSignal?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "Local\\ElyProxy.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            try
            {
                using EventWaitHandle existing = EventWaitHandle.OpenExisting("Local\\ElyProxy.Activate");
                existing.Set();
            }
            catch
            {
            }
            return false;
        }

        _activationSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\ElyProxy.Activate");
        _activationRegistration = ThreadPool.RegisterWaitForSingleObject(
            _activationSignal,
            (_, _) => Dispatcher.InvokeAsync(() => ActivationRequested?.Invoke()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
        return true;
    }
}
