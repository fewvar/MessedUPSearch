using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA.Views;

public partial class ParserOverlay : UserControl
{
    public ParserOverlay()
    {
        InitializeComponent();
    }

    private void OnBackdropClick(object? sender, PointerPressedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        (owner?.DataContext as MainWindowViewModel)?.CloseOverlays();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        var owner = TopLevel.GetTopLevel(this) as Window;
        (owner?.DataContext as MainWindowViewModel)?.CloseOverlays();
    }

}
