#if DEBUG
using System;
using System.Linq;
using Avalonia.Threading;
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

            case "parser":
                main.IsParserOpen = true;
                break;

            case "settings":
                main.IsSettingsOpen = true;
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
