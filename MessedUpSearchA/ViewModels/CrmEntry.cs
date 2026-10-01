using System.Collections.Generic;

namespace MessedUpSearchA.ViewModels;

public class CrmEntry
{
    public string Nickname { get; init; } = string.Empty;
    public string CrmStatus { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public List<CrmBeatItem> Beats { get; init; } = new();

    /// <summary>«ответил 03.10: „Йо, кидай ещё“» — последний ответ из почты.</summary>
    public string ReplyLine { get; init; } = string.Empty;
    public bool HasReply => ReplyLine.Length > 0;

    /// <summary>3+ писем без ответа.</summary>
    public bool IsSilent { get; init; }

    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasBeats => Beats.Count > 0;

    public int SentCount => Beats.FindAll(b => b.IsSent).Count;
    public string SentCountLabel => Services.Localization.Localizer.Instance.Format("Crm.SentCount", SentCount, Beats.Count);
}

public class CrmBeatItem
{
    public int LogId { get; init; }
    public string BeatName { get; init; } = string.Empty;
    public string StatusColor { get; init; } = "#888888";
    public bool IsSent { get; init; }

    public bool IsPending => !IsSent;
}

public class ReminderEntry
{
    public string Nickname { get; init; } = string.Empty;
    public List<string> BeatNames { get; init; } = new();

    public string Summary => BeatNames.Count == 1
        ? "1 бит ждёт отправки"
        : $"{BeatNames.Count} битов ждут отправки";

    public string BeatsLine => string.Join(", ", BeatNames);
}
