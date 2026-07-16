namespace Screen_Painter.Models;

public class UpdateCheckResult
{
    public bool UpdateAvailable { get; set; }
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}
