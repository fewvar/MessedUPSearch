using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MessedUpSearchA.Services.Parsing;

public static class AudioKinds
{
    public const string Full = "Full";
    public const string Preview = "Preview";
}

/// <summary>
/// Откуда качать звук трека. Обычно одна ссылка; у HLS без progressive — список
/// кусков, которые склеиваются подряд (mp3-сегменты склеиваются без перекодирования).
/// </summary>
public class ResolvedAudio
{
    public IReadOnlyList<string> Urls { get; init; } = [];
    public string AudioKind { get; init; } = AudioKinds.Full;
}

/// <summary>
/// Площадка, которая умеет отдать звук своего трека. Ссылка получается заново
/// перед каждым скачиванием: у SoundCloud и Deezer она подписана и быстро протухает.
/// </summary>
public interface IAudioResolver
{
    string Platform { get; }

    /// <summary>null — звука нет (сниппет, трек удалён, стрим закрыт). Это не ошибка.</summary>
    Task<ResolvedAudio?> ResolveAsync(string trackId, string trackUrl, CancellationToken ct);
}
