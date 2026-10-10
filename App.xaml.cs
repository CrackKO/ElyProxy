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
        // Unhandled exceptions on background threads terminate the process
        // without the dispatcher handler below ever seeing them (this used to
        // look like the app closing for no reason). Record them and show a
        // visible report instead of dying silently.
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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
            string reportPath = WriteCrashReport("DispatcherUnhandledException", args.Exception);
            System.Windows.MessageBox.Show(
                $"Необработанная ошибка:\n{args.Exception.Message}\n\nПодробности: {reportPath}",
                "ElyProxy — Ошибка",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is not Exception exception)
        {
            return;
        }

        string reportPath = WriteCrashReport("UnhandledException", exception);
        try
        {
            System.Windows.MessageBox.Show(
                $"ElyProxy столкнулся с непредвиденной ошибкой и будет закрыт.\n\n{exception.Message}\n\nПодробности: {reportPath}",
                "ElyProxy — Ошибка",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashReport("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    internal static string WriteCrashReport(string source, Exception exception)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ElyProxy");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "crash.log");
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(path, entry);
            return path;
        }
        catch
        {
            return string.Empty;
        }
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
