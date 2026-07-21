using System.Globalization;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// アプリ全体の作業設定（exe 横 INI の [App]）。プロジェクト切替では変わらない。
/// </summary>
internal sealed class AppSettings
{
    public const string Section = "App";

    public bool AlwaysOnTop { get; set; }

    /// <summary>UI／ログの表示言語（既定 ja）。</summary>
    public UiLanguage UiLanguage { get; set; } = UiLanguage.Japanese;

    public static AppSettings Load()
    {
        var values = IniFile.ReadSection(Section);
        var settings = Parse(values);
        if (!HasKnownKeys(values))
        {
            settings.Save();
        }

        return settings;
    }

    public void Save() => WriteValues(ToDictionary());

    public void SaveAlwaysOnTop(bool enabled)
    {
        AlwaysOnTop = enabled;
        Save();
    }

    public void SaveUiLanguage(UiLanguage language)
    {
        UiLanguage = language;
        Save();
    }

    private Dictionary<string, string> ToDictionary() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["AlwaysOnTop"] = AlwaysOnTop ? "1" : "0",
        ["UiLanguage"] = UiStrings.ToIniValue(UiLanguage),
    };

    private static void WriteValues(Dictionary<string, string> values)
    {
        IniFile.WriteSection(Section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AlwaysOnTop"] = values.TryGetValue("AlwaysOnTop", out var alwaysOnTop) ? alwaysOnTop : "0",
            ["UiLanguage"] = values.TryGetValue("UiLanguage", out var language) ? language : "ja",
        });
    }

    private static AppSettings Parse(Dictionary<string, string> values) => new()
    {
        AlwaysOnTop = ReadBool(values, "AlwaysOnTop", defaultValue: false),
        UiLanguage = values.TryGetValue("UiLanguage", out var languageText)
            ? UiStrings.ParseLanguage(languageText)
            : UiLanguage.Japanese,
    };

    private static bool HasKnownKeys(Dictionary<string, string> values) =>
        values.ContainsKey("AlwaysOnTop")
        || values.ContainsKey("UiLanguage");

    private static bool ReadBool(Dictionary<string, string> values, string key, bool defaultValue)
    {
        if (!values.TryGetValue(key, out var text))
        {
            return defaultValue;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number != 0;
        }

        return bool.TryParse(text, out var flag) ? flag : defaultValue;
    }
}
