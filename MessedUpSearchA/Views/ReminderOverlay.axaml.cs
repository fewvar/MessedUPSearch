using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA.Views;

public partial class ReminderOverlay : UserControl
{
    public ReminderOverlay()
    {
        InitializeComponent();
    }

    private MainWindowViewModel? Vm => (TopLevel.GetTopLevel(this) as Window)?.DataContext as MainWindowViewModel;

    private void OnBackdropClick(object? sender, PointerPressedEventArgs e) => Vm?.CloseReminders();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Vm?.CloseReminders();

    private void OnOpenCrmClick(object? sender, RoutedEventArgs e) => Vm?.OpenCrmFromReminder();
}
