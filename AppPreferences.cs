using System.Text.Json;

sealed class AppPreferences
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BambuP1SWidget", "preferences.json");
    public static AppPreferences Current { get; private set; } = new();
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "es";
    public bool DesktopWidget { get; set; }
    public int? DesktopLeft { get; set; }
    public int? DesktopTop { get; set; }

    public static AppPreferences Load()
    {
        try
        {
            if (File.Exists(FilePath)) Current = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath)) ?? new AppPreferences();
        }
        catch { Current = new AppPreferences(); }
        Current.Theme = string.Equals(Current.Theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        Current.Language = string.Equals(Current.Language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
        return Current;
    }

    public static void Save(string theme, string language, bool desktopWidget)
    {
        Current.Theme = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        Current.Language = string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "es";
        Current.DesktopWidget = desktopWidget;
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
    }
}
