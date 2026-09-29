using System;
using System.IO;
using System.Linq;
using MessedUpSearchA.Data;

namespace MessedUpSearchA.Services.Ml;

/// <summary>
/// Где лежат файлы подбора по звуку. Модель (EffNet, 18 МБ), голова, ориентиры «звучит как»
/// и фон для поправки на хабы едут внутри приложения (Assets/Models) — анализ работает сразу,
/// без скачивания. Отдельно качается только большой индекс артистов (<see cref="IndexStore"/>):
/// он обновляется чаще приложения.
/// </summary>
public static class MlAssets
{
    private static string AssetsDir => Path.Combine(AppContext.BaseDirectory, "Assets", "Models");

    public static string ModelPath => Path.Combine(AssetsDir, "effnet_style.onnx");
    public static string HeadPath => Path.Combine(AssetsDir, "effnet_head.bin");

    /// <summary>«Звучит как»: инструменталы 30 крупных артистов (Deezer + SoundCloud).</summary>
    public static string ReferencesPath => Path.Combine(AssetsDir, "references_v3.bin");

    /// <summary>Type beat'ы — фон для поправки на хабы.</summary>
    public static string BackgroundPath => Path.Combine(AssetsDir, HubCorrection.FileName);

    /// <summary>Скачиваемое (индекс артистов) — рядом с базой пользователя.</summary>
    public static string DownloadDirectory
    {
        get
        {
            var dir = Path.Combine(AppPaths.DataDir, "models");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static bool IsReady() =>
        new[] { ModelPath, HeadPath, ReferencesPath }.All(File.Exists);
}
