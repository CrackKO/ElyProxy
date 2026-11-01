using System.Windows.Threading;
using ElyProxy.Controls;
using ElyProxy.ViewModels;
using Xunit;

namespace ElyProxy.Tests;

public class ModeSelectorTests
{
    [Fact]
    public void DeferredSelectionAndFlagUpdatesNeverRequestNetworkChanges()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                int requests = 0;
                var selector = new ModeSelector { ModeRequestCommand = new RelayCommand(_ => requests++) };
                for (int i = 0; i < 100; i++)
                {
                    selector.ProxyLiteEnabled = true;
                    selector.SelectedIndex = 2;
                    Assert.False(selector.ProxyProEnabled);
                    selector.ProxyLiteEnabled = false;
                    selector.ElyTunEnabled = true;
                    selector.ElyTunEnabled = false;
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                }
                Assert.Equal(0, requests);
                Assert.False(selector.ProxyLiteEnabled);
                Assert.False(selector.ProxyProEnabled);
                Assert.False(selector.ElyTunEnabled);
            }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "Selector updates did not settle");
        if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
