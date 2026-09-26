using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using MessedUpSearchA.Data;
using MessedUpSearchA.Services.Ml;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA;

public partial class App : Application
{
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
        }

        // Треки, которые не успели послушать в прошлый раз, — дослушиваем в фоне.
        TrackEmbeddingQueue.Instance.Kick();

        base.OnFrameworkInitializationCompleted();
    }
}
