using System;
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

        // Tunnel, а не обычный KeyDown: иначе пробел раньше нас съест кнопка в фокусе
        // и нажмёт себя — например, «Добавить бит».
        AddHandler(KeyDownEvent, OnPlayerKeys, RoutingStrategies.Tunnel);
    }

    private void OnPlayerKeys(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { Player: { HasTrack: true } player })
            return;

        // В поле ввода пробел и стрелки нужны для текста.
        if (FocusManager?.GetFocusedElement() is TextBox)
            return;

        switch (e.Key)
        {
            case Key.Space:
                player.TogglePlay();
                e.Handled = true;
                break;
            case Key.Right:
                player.SeekForward();
                e.Handled = true;
                break;
            case Key.Left:
                player.SeekBackward();
                e.Handled = true;
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as MainWindowViewModel)?.Player.Dispose();
        base.OnClosed(e);
    }
}
