using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnBackdropClick(object? sender, PointerPressedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.CloseOverlays();
    }

    private void OnCloseOverlayClick(object? sender, RoutedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.CloseOverlays();
    }
}
