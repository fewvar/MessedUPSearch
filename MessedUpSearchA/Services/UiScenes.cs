#if DEBUG
using System;
using System.Linq;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MessedUpSearchA.ViewModels;

namespace MessedUpSearchA.Services;

/// <summary>
/// Только для отладочной сборки: открыть приложение сразу в нужном состоянии, чтобы
/// снять скриншот без кликов. Переменная MESSEDUP_UI_SCENE, значения ниже.
/// Скрипт снимков лежит в .claude/skills/ui-review.
/// </summary>
public static class UiScenes
{
    public const string Variable = "MESSEDUP_UI_SCENE";

    /// <summary>
    /// MESSEDUP_UI_DELAY=мс — применить сцену не сразу: окно успевает показаться, и серия снимков
    /// ловит саму анимацию появления (иначе она проигрывается раньше, чем скрипт найдёт окно).
    /// </summary>
    public const string DelayVariable = "MESSEDUP_UI_DELAY";

    public static void Apply(MainWindowViewModel main)
    {
        var scene = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(scene))
            return;

        if (int.TryParse(Environment.GetEnvironmentVariable(DelayVariable), out var delay) && delay > 0)
        {
            DispatcherTimer.RunOnce(() => ApplyScene(main, scene), TimeSpan.FromMilliseconds(delay));
            return;
        }

        ApplyScene(main, scene);
    }

    private static void ApplyScene(MainWindowViewModel main, string scene)
    {
        switch (scene.Trim().ToLowerInvariant())
        {
            case "beats":
                break;

            case "beat-editor":
                if (main.BeatsVm.Beats.FirstOrDefault() is { } beat)
                    main.BeatsVm.BeginEditBeat(beat);
                break;

            case "beat-analyze":
                if (main.BeatsVm.Beats.FirstOrDefault() is { } analyzed)
                {
                    main.BeatsVm.BeginEditBeat(analyzed);
                    main.BeatsVm.AnalyzeSimilarityCommand.Execute(null);
                }
                break;

            case "player":
                if (main.BeatsVm.Beats.FirstOrDefault() is { } first)
                {
                    main.Player.PlayBeat(first);
                    // Даём загрузиться и посчитать волну, потом ставим на паузу посередине —
                    // на снимке видны и сыгранная, и оставшаяся часть.
                    DispatcherTimer.RunOnce(() =>
                    {
                        main.Player.TogglePlay();
                        main.Player.SeekToCommand.Execute(0.4);
                    }, TimeSpan.FromSeconds(2));
                }
                break;

            case "artists":
                main.IsBeatsSelected = false;
                break;

            case "artist-editor":
                main.IsBeatsSelected = false;
                if (main.ArtistsVm.Artists.FirstOrDefault() is { } artist)
                    main.ArtistsVm.BeginEditArtist(artist);
                break;

            case "artist-contacts":
                main.IsBeatsSelected = false;
                if (main.ArtistsVm.Artists.FirstOrDefault() is { } withBio)
                {
                    main.ArtistsVm.BeginEditArtist(withBio);
                    main.ArtistsVm.EditEmail = "booking@label.com";
                    main.ArtistsVm.ShowBioForScene("biz: beats (at) proton (dot) me · tg: @artist_mgr · instagram.com/artist.ig");
                }
                break;

            // Низ настроек: «О программе» и «Сообщить о проблеме».
            case "report":
                main.IsSettingsOpen = true;
                DispatcherTimer.RunOnce(() =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime is
                        Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
                    {
                        foreach (var scroll in Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(window)
                                     .OfType<Avalonia.Controls.ScrollViewer>()
                                     .Where(s => s.FindAncestorOfType<Views.SettingsOverlay>() is not null))
                            scroll.ScrollToEnd();
                    }
                }, TimeSpan.FromMilliseconds(600));
                break;

            // Окно рассылки: первый бит, первые пять артистов (почта у них — из демо-копии базы).
            case "mail":
                if (main.BeatsVm.Beats.FirstOrDefault() is { } mailed)
                    main.MailVm.Open(mailed.Id, main.ArtistsVm.Artists.Take(5).Select(a => a.Id).ToList());
                break;

            // Живая отправка на тестовый SMTP (127.0.0.1:2525): кадр ловит отсчёт в футере.
            case "mail-send":
                SecretStore.Set(Mail.MailQueue.PasswordKey("test@local.test"), "secret");
                if (main.BeatsVm.Beats.FirstOrDefault() is { } sending)
                {
                    main.MailVm.Open(sending.Id, main.ArtistsVm.Artists.Take(5).Select(a => a.Id).ToList());
                    main.MailVm.SendCommand.Execute(null);
                }
                break;

            case "beat-analyze-pick":
                if (main.BeatsVm.Beats.FirstOrDefault() is { } picked)
                {
                    main.BeatsVm.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(BeatsViewModel.IsAnalyzing) || main.BeatsVm.IsAnalyzing)
                            return;
                        foreach (var item in main.BeatsVm.SimilarArtists.Take(3))
                            item.IsPicked = true;
                        main.BeatsVm.NotifyPickedChanged();
                    };
                    main.BeatsVm.BeginEditBeat(picked);
                    main.BeatsVm.AnalyzeSimilarityCommand.Execute(null);
                }
                break;

            case "parser":
                main.IsParserOpen = true;
                break;

            case "settings":
                main.IsSettingsOpen = true;
                break;

            // Футер во время поиска (без сети: только состояние).
            case "parser-running":
                main.ParserVm.IsSearching = true;
                break;

            // Все звуки интерфейса по очереди — проверка, что движок их отдаёт (ошибки — в app.log).
            case "sounds":
                var delay = 0.0;
                foreach (var sound in Enum.GetValues<MessedUpSearchA.Services.Audio.UiSound>())
                {
                    var s = sound;
                    DispatcherTimer.RunOnce(() => MessedUpSearchA.Services.Audio.UiSounds.Play(s), TimeSpan.FromSeconds(delay += 0.8));
                }
                break;

            case "reminders":
                main.IsReminderOpen = true;
                break;

            // Звезда: вращается 2 с (как во время поиска), потом доворачивается до четверти оборота.
            case "spin":
                main.ParserVm.IsSearching = true;
                DispatcherTimer.RunOnce(() => main.ParserVm.IsSearching = false, TimeSpan.FromSeconds(2));
                break;

            case "toast":
                Toasts.Show("Отправлено ✓");
                break;

            case "sold-flash":
                if (main.BeatsVm.Beats.FirstOrDefault() is { } sold)
                    main.BeatsVm.JustSoldBeatId = sold.Id;
                break;

            // Редактор с сохранённой выдачей; через секунду первому артисту — «добавлен» (✓ со щелчком).
            case "import-check":
                if (main.BeatsVm.Beats.FirstOrDefault() is { } withResults)
                {
                    main.BeatsVm.BeginEditBeat(withResults);
                    DispatcherTimer.RunOnce(() =>
                    {
                        if (main.BeatsVm.SimilarArtists.FirstOrDefault(a => a.CanImport) is { } item)
                        {
                            item.ArtistId = -1;
                            item.JustImported = true;
                        }
                    }, TimeSpan.FromSeconds(1.2));
                }
                break;

            case "sort-bpm":
                main.BeatsVm.SortBy("Bpm");
                break;

            case "sort-plays":
                main.IsBeatsSelected = false;
                main.ArtistsVm.SortBy("Plays");
                main.ArtistsVm.SortBy("Plays");   // второй клик — по возрастанию
                break;

            // Прерывание анимации: открыть, через 120 мс закрыть, ещё через 90 мс снова открыть.
            case "settings-bounce":
                main.IsSettingsOpen = true;
                DispatcherTimer.RunOnce(() => main.IsSettingsOpen = false, TimeSpan.FromMilliseconds(120));
                DispatcherTimer.RunOnce(() => main.IsSettingsOpen = true, TimeSpan.FromMilliseconds(210));
                break;

            case "crm":
                main.ShowCrm();
                break;
        }
    }
}
#endif
