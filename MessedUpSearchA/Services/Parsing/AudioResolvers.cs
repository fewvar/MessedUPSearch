using System;
using System.Collections.Generic;
using System.Linq;

namespace MessedUpSearchA.Services.Parsing;

/// <summary>Все площадки, которые умеют отдать звук, по имени площадки.</summary>
public static class AudioResolvers
{
    public static IReadOnlyDictionary<string, IAudioResolver> CreateAll() =>
        new IAudioResolver[] { new Sources.SoundCloudSource(), new Sources.AudiusSource(), new Sources.DeezerSource() }
            .ToDictionary(r => r.Platform, StringComparer.OrdinalIgnoreCase);
}
