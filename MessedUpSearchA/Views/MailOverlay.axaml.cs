using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA.Views;

public partial class MailOverlay : UserControl
{
    public MailOverlay()
    {
        InitializeComponent();
    }

    /// <summary>Клик по строке получателя — предпросмотр письма именно ему.</summary>
    private void OnRecipientClick(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: MailRecipientItem item } && DataContext is MailViewModel vm)
            vm.PreviewRecipient = item;
    }

    private void OnUseEmailHintClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: MailRecipientItem item } && DataContext is MailViewModel vm)
            vm.UseEmailHint(item);
    }
}
