using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.EntityFrameworkCore;
using MessedUpSearchA.Data;
using MessedUpSearchA.Services;
using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindowViewModel? _main;
    private TrayIcon? _tray;
    private bool _quitting;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {

        using (var db = new AppDbContext())
        {
            db.Database.Migrate();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            _main = new MainWindowViewModel();
            _main.BackgroundModeChanged += ApplyBackgroundMode;

            // Автозапуск с системой: стартуем свёрнутыми в трей, окно — по клику на иконку.
            var hidden = desktop.Args?.Contains(Autostart.BackgroundArg) == true && _main.DawTracking;
            if (!hidden)
                desktop.MainWindow = CreateWindow();

            ApplyBackgroundMode(_main.DawTracking);

#if DEBUG
            UiScenes.Apply(_main);
#endif
        }


        base.OnFrameworkInitializationCompleted();
    }

    private MainWindow CreateWindow()
    {
        var window = new MainWindow { DataContext = _main };

        // Фоновый режим: крестик прячет окно, приложение остаётся в трее (там же «Выйти»).
        window.Closing += (_, e) =>
        {
            if (_main?.DawTracking == true && !_quitting)
            {
                e.Cancel = true;
                window.Hide();
            }
        };
        return window;
    }

    /// <summary>Иконка в трее / строке меню и поведение при закрытии окна.</summary>
    private void ApplyBackgroundMode(bool on)
    {
        if (_desktop is null)
            return;

        _desktop.ShutdownMode = on ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose;

        if (on && _tray is null)
        {
            var menu = new NativeMenu();
            var open = new NativeMenuItem(Localizer.Instance["Tray.Open"]);
            open.Click += (_, _) => ShowWindow();
            var quit = new NativeMenuItem(Localizer.Instance["Tray.Quit"]);
            quit.Click += (_, _) => Quit();
            menu.Items.Add(open);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quit);

            _tray = new TrayIcon
            {
                Icon = new WindowIcon(new Bitmap(AssetLoader.Open(new Uri("avares://MessedUpSearchA/Assets/Icons/logo.png")))),
                ToolTipText = "MessedUpSearch",
                Menu = menu
            };
            _tray.Clicked += (_, _) => ShowWindow();
            TrayIcon.SetIcons(this, [_tray]);
        }
        else if (!on && _tray is not null)
        {
            TrayIcon.SetIcons(this, []);
            _tray.Dispose();
            _tray = null;

            // Окно было спрятано, а фоновый режим выключили — вернуть его, иначе приложение не закрыть.
            if (_desktop.MainWindow is null || !_desktop.MainWindow.IsVisible)
                ShowWindow();
        }
    }

    private void ShowWindow()
    {
        if (_desktop is null)
            return;

        _desktop.MainWindow ??= CreateWindow();
        _desktop.MainWindow.Show();
        _desktop.MainWindow.Activate();
    }

    private void Quit()
    {
        _quitting = true;
        _desktop?.Shutdown();
    }
}
