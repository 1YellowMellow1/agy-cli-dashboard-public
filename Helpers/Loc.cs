namespace AgyCliDashboard.Helpers;

public static class Loc
{
    public static string Tr(string key, string? fallback = null) => Services.LocalizationService.Instance.Get(key, fallback);
    public static string Format(string key, params object[] args) => Services.LocalizationService.Instance.Format(key, args);
}
