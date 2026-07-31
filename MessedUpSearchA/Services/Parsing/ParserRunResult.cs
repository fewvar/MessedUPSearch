using System.Collections.Generic;
using System.Linq;

namespace MessedUpSearchA.Services.Parsing;

public class ParserRunResult
{
    public List<ArtistCandidate> Candidates { get; } = new();
    public List<SourceFailure> Failures { get; } = new();

    public bool HasFailures => Failures.Count > 0;

    public string Summary
    {
        get
        {
            var platforms = Candidates.Select(c => c.Platform).Distinct().Count();
            var line = $"Найдено {Plural(Candidates.Count, "артист", "артиста", "артистов")} " +
                       $"с {Plural(platforms, "площадки", "площадок", "площадок")}";
            return HasFailures ? $"{line}, отвалилось: {Failures.Count}" : line;
        }
    }

    private static string Plural(int count, string one, string few, string many)
    {
        var mod100 = count % 100;
        var mod10 = count % 10;

        var word = (mod100 is >= 11 and <= 14) ? many
            : mod10 == 1 ? one
            : mod10 is >= 2 and <= 4 ? few
            : many;

        return $"{count} {word}";
    }
}

public class SourceFailure
{
    public string Platform { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;

    public int CollectedBefore { get; init; }

    public string Line => CollectedBefore > 0
        ? $"{Platform}: отвалилась, успели собрать {CollectedBefore} — {Message}"
        : $"{Platform}: не ответила — {Message}";
}
