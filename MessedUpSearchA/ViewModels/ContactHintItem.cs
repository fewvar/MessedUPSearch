using MessedUpSearchA.Services.Localization;
using MessedUpSearchA.Services.Parsing;

namespace MessedUpSearchA.ViewModels;

/// <summary>Подсказка «нашли в профиле» в карточке артиста.</summary>
public record ContactHintItem(ContactKind Kind, string Value)
{
    public string Label => Kind switch
    {
        ContactKind.Email => Localizer.Instance["ArtistEditor.Email"],
        ContactKind.Telegram => "TELEGRAM",
        _ => "INSTAGRAM"
    };
}
