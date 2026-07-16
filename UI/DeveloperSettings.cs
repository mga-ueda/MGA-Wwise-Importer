using System.Globalization;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 開発者向け設定（exe 横の MgaWwiseIMImporter.ini [Developer]）。
/// </summary>
internal sealed class DeveloperSettings
{
    public const string Section = "Developer";

    /// <summary>起動時に自動ドロップ扱いする波形。パス自体は <see cref="AutoLoadOnStartup"/> と独立。</summary>
    public string AutoLoadWavePath { get; init; } = string.Empty;

    /// <summary>起動時に <see cref="AutoLoadWavePath"/> を自動読み込みするか。既定はオン。</summary>
    public bool AutoLoadOnStartup { get; init; } = true;

    /// <summary>ウィンドウを最前面に固定するか。既定はオフ。</summary>
    public bool TopMost { get; init; }

    public static DeveloperSettings Load()
    {
        EnsureDefaultsWritten();

        var values = IniFile.ReadSection(Section);
        return new DeveloperSettings
        {
            AutoLoadWavePath = values.TryGetValue("AutoLoadWavePath", out var wave)
                ? wave
                : string.Empty,
            AutoLoadOnStartup = values.TryGetValue("AutoLoadOnStartup", out var autoLoad)
                ? ParseBool(autoLoad, defaultValue: true)
                : true,
            TopMost = values.TryGetValue("TopMost", out var topMost)
                && ParseBool(topMost, defaultValue: false),
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

        if (!values.ContainsKey("AutoLoadWavePath"))
        {
            values["AutoLoadWavePath"] = ResolveDefaultAutoLoadWavePath();
            changed = true;
        }

        if (!values.ContainsKey("AutoLoadOnStartup"))
        {
            values["AutoLoadOnStartup"] = "1";
            changed = true;
        }

        if (!values.ContainsKey("TopMost"))
        {
            values["TopMost"] = "0";
            changed = true;
        }

        if (changed)
        {
            IniFile.WriteSection(Section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AutoLoadWavePath"] = values["AutoLoadWavePath"],
                ["AutoLoadOnStartup"] = values["AutoLoadOnStartup"],
                ["TopMost"] = values["TopMost"],
            });
        }
    }

    /// <summary>[Developer] TopMost だけ更新する（他キーは維持）。</summary>
    public static void SaveTopMost(bool topMost)
    {
        EnsureDefaultsWritten();
        var values = IniFile.ReadSection(Section);
        values["TopMost"] = topMost ? "1" : "0";
        IniFile.WriteSection(Section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AutoLoadWavePath"] = values.TryGetValue("AutoLoadWavePath", out var wave)
                ? wave
                : ResolveDefaultAutoLoadWavePath(),
            ["AutoLoadOnStartup"] = values.TryGetValue("AutoLoadOnStartup", out var autoLoad)
                ? autoLoad
                : "1",
            ["TopMost"] = values["TopMost"],
        });
    }

    private static bool MigrateLegacyKeys(Dictionary<string, string> values)
    {
        // 旧プレビュー以前のキーは削除する（値は使わない）
        string[] obsolete =
        [
            "ExternalEditorPath",
            "OpenInExternalEditor",
            "SoundForgePath",
            "OpenInSoundForge",
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

    public static string ResolveDefaultAutoLoadWavePath()
    {
        var testDirectory = FindTestDirectory();
        if (testDirectory is null)
        {
            return string.Empty;
        }

        return Path.Combine(testDirectory, "a.wav");
    }

    public string? ResolveAutoLoadWavePath()
    {
        var configured = AutoLoadWavePath.Trim().Trim('"');
        if (configured.Length == 0)
        {
            return null;
        }

        if (Path.IsPathRooted(configured))
        {
            return Path.GetFullPath(configured);
        }

        // 相対パス: exe 基準 → 見つからなければリポジトリ探索
        var fromBase = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, configured));
        if (File.Exists(fromBase))
        {
            return fromBase;
        }

        var testDirectory = FindTestDirectory();
        if (testDirectory is not null)
        {
            var fromTestRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testDirectory)!, configured));
            if (File.Exists(fromTestRoot))
            {
                return fromTestRoot;
            }

            var fileName = Path.GetFileName(configured);
            var besideTest = Path.Combine(testDirectory, fileName);
            if (File.Exists(besideTest))
            {
                return besideTest;
            }
        }

        return fromBase;
    }

    private static string? FindTestDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "test");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
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
