namespace MessedUpSearchA.Services.Parsing;

public class ArtistSearchQuery
{

    public string GenreTag { get; init; } = string.Empty;

    public int? PlaysMin { get; init; }
    public int? PlaysMax { get; init; }

    public int? FreshWithinDays { get; init; }

    public string Country { get; init; } = string.Empty;

    public int MaxArtists { get; init; } = 50;
    public int TracksPerArtist { get; init; } = 10;

    /// <summary>
    /// Есть ли отсев, который применяется уже ПОСЛЕ поиска (по трекам артиста).
    /// Источники должны игнорировать MaxArtists как предел глубины поиска, пока это true —
    /// иначе первые найденные профили срежет фильтр, и добирать станет неоткуда.
    /// </summary>
    public bool HasPostSearchFilter =>
        PlaysMin.HasValue || PlaysMax.HasValue || FreshWithinDays.HasValue || !string.IsNullOrWhiteSpace(Country);
}
