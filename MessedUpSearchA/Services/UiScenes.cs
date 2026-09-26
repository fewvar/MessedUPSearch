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

    public static void Apply(MainWindowViewModel main)
    {
        var scene = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(scene))
            return;

        switch (scene.Trim().ToLowerInvariant())
        {
            case "beats":
                break;

            case "beat-editor":
                if (main.BeatsVm.Beats.FirstOrDefault() is { } beat)
                    main.BeatsVm.BeginEditBeat(beat);
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

            case "crm":
                main.ShowCrm();
                break;
        }
    }
}
#endif
