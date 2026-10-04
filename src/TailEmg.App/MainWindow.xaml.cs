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
        Close();
    }
}
