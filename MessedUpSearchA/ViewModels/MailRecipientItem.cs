using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MessedUpSearchA.ViewModels;

/// <summary>Строка получателя в окне рассылки.</summary>
public partial class MailRecipientItem : ObservableObject
{
    public int ArtistId { get; init; }
    public string Nickname { get; init; } = string.Empty;
    public string AvatarPath { get; init; } = string.Empty;
    public string AvatarColor { get; init; } = "#888888";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasEmail), nameof(CanSelect))]
    private string _email = string.Empty;

    [ObservableProperty] private bool _isSelected;

    /// <summary>Почему снят с галочки: «уже отправлял этот бит 12.09», «писал 3 дн. назад», «нет почты».</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasWarning))] private string _warning = string.Empty;

    /// <summary>Почта из описания профиля — подставить одним кликом, если поле пустое.</summary>
    public List<string> EmailHints { get; init; } = new();

    /// <summary>Письмо, переписанное нейросетью именно под этого артиста; пусто — уйдёт шаблон.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsPersonalized))] private string _customSubject = string.Empty;
    [ObservableProperty] private string _customBody = string.Empty;

    public bool IsPersonalized => CustomSubject.Length > 0;

    // Для «Оживить»: что можно упомянуть об артисте.
    public string Genre { get; init; } = string.Empty;
    public string TopTrack { get; init; } = string.Empty;

    public bool HasEmail => Email.Length > 0;
    public bool CanSelect => HasEmail;
    public bool HasWarning => Warning.Length > 0;
    public bool HasEmailHint => !HasEmail && EmailHints.Count > 0;
    public string EmailHint => EmailHints.Count > 0 ? EmailHints[0] : string.Empty;
}
