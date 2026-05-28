namespace MessedUpSearchA.Models;

public class Beat
{
    public int Id { get; set; }
    public string BeatName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int Bpm { get; set; }
    public string AiTags { get; set; } = string.Empty;
    public string StatusColor { get; set; } = string.Empty;
    public string LicenseType { get; set; } = string.Empty;
    public bool IsSold { get; set; }
}
