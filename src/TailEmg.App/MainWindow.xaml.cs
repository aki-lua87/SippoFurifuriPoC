using System.ComponentModel;
using System.Windows;

namespace TailEmg.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();
    private bool _shutdownDone;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        // Charts draw directly (no binding per point), so repaint when the view model added points.
        _vm.ChartUpdated += (_, _) =>
        {
            RawChart.InvalidateVisual();
            OutputChart.InvalidateVisual();
        };
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_shutdownDone)
        {
            return;
        }

        // Let the ramp-to-zero + final 0.0 OSC message finish, then close for real.
        e.Cancel = true;
        await _vm.ShutdownAsync();
        _shutdownDone = true;

        // ShutdownAsync can complete synchronously (nothing running), i.e. while we are still inside the
        // Closing event. Calling Close() there throws, so queue it until this handler has returned.
        _ = Dispatcher.BeginInvoke(Close);
    }
}
