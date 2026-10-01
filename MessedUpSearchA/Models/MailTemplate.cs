namespace MessedUpSearchA.Models;

/// <summary>Шаблон письма: тема и текст с подстановками {artist} {beat} {bpm} {key} {link} {my_name}.</summary>
public class MailTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>MailKinds.Pitch или MailKinds.FollowUp.</summary>
    public string Kind { get; set; } = MailKinds.Pitch;

    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
}

public static class MailKinds
{
    public const string Pitch = "pitch";
    public const string FollowUp = "followup";
}
