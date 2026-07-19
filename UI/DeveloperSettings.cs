using System.Globalization;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 開発者向け設定（exe 横の MgaWwiseIMImporter.ini [Developer]）。
/// </summary>
internal sealed class DeveloperSettings
{
    public const string Section = "Developer";

    /// <summary>Playlist／再生エンジンの詳細診断ログを出すか。既定はオン。</summary>
    public bool DetailedPlaybackLog { get; init; } = true;

    public static DeveloperSettings Load()
    {
        EnsureDefaultsWritten();

        var values = IniFile.ReadSection(Section);
        return new DeveloperSettings
        {
            DetailedPlaybackLog = values.TryGetValue("DetailedPlaybackLog", out var detailedLog)
                ? ParseBool(detailedLog, defaultValue: true)
                : true,
        };
    }

    /// <summary>
    /// 不足キーがあれば現状の既定値で書き足す（既存値は維持）。
    /// 旧キー名があれば除去する。
    /// </summary>
    public static void EnsureDefaultsWritten()
    {
        var values = IniFile.ReadSection(Section);
        var changed = false;

        if (MigrateLegacyKeys(values))
        {
            changed = true;
        }

        if (!values.ContainsKey("DetailedPlaybackLog"))
        {
            values["DetailedPlaybackLog"] = "1";
            changed = true;
        }

        if (changed)
        {
            WriteSection(values);
        }
    }

    /// <summary>[Developer] DetailedPlaybackLog だけ更新する（他キーは維持）。</summary>
    public static void SaveDetailedPlaybackLog(bool enabled)
    {
        EnsureDefaultsWritten();
        var values = IniFile.ReadSection(Section);
        values["DetailedPlaybackLog"] = enabled ? "1" : "0";
        WriteSection(values);
    }

    private static void WriteSection(Dictionary<string, string> values)
    {
        IniFile.WriteSection(Section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DetailedPlaybackLog"] = values.TryGetValue("DetailedPlaybackLog", out var detailedLog)
                ? detailedLog
                : "1",
        });
    }

    private static bool MigrateLegacyKeys(Dictionary<string, string> values)
    {
        // 旧キーは削除する（TopMost だけはプロジェクト形式の初回移行時に参照される）
        string[] obsolete =
        [
            "ExternalEditorPath",
            "OpenInExternalEditor",
            "SoundForgePath",
            "OpenInSoundForge",
            "TopMost",
            "AutoLoadWavePath",
            "AutoLoadOnStartup",
        ];

        var changed = false;
        foreach (var key in obsolete)
        {
            if (values.Remove(key))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool ParseBool(string text, bool defaultValue)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number != 0;
        }

        if (bool.TryParse(text, out var flag))
        {
            return flag;
        }

        if (string.Equals(text, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(text, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return defaultValue;
    }
}
