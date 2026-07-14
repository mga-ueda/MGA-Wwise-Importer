using System.Globalization;

namespace MgaWwiseImporter.UI;

/// <summary>
/// 開発者向け設定（exe 横の MgaWwiseImporter.ini [Developer]）。
/// </summary>
internal sealed class DeveloperSettings
{
    public const string Section = "Developer";

    /// <summary>
    /// 不足キー追記時の外部エディタ既定パス（空なら起動しない。各環境で INI に設定する）。
    /// </summary>
    public const string DefaultExternalEditorPath = "";

    public string ExternalEditorPath { get; init; } = DefaultExternalEditorPath;

    /// <summary>出力後に外部エディタで開くか。既定はオン。</summary>
    public bool OpenInExternalEditor { get; init; } = true;

    /// <summary>起動時に自動ドロップ扱いする波形。空ならスキップ。</summary>
    public string AutoLoadWavePath { get; init; } = string.Empty;

    /// <summary>ウィンドウを最前面に固定するか。既定はオフ。</summary>
    public bool TopMost { get; init; }

    public static DeveloperSettings Load()
    {
        EnsureDefaultsWritten();

        var values = IniFile.ReadSection(Section);
        return new DeveloperSettings
        {
            ExternalEditorPath = ResolveExternalEditorPath(values),
            OpenInExternalEditor = ResolveOpenInExternalEditor(values),
            AutoLoadWavePath = values.TryGetValue("AutoLoadWavePath", out var wave)
                ? wave
                : string.Empty,
            TopMost = values.TryGetValue("TopMost", out var topMost)
                && ParseBool(topMost, defaultValue: false),
        };
    }

    /// <summary>
    /// 不足キーがあれば現状の既定値で書き足す（既存値は維持）。
    /// 旧キー名があれば新キーへ移行する。
    /// </summary>
    public static void EnsureDefaultsWritten()
    {
        var values = IniFile.ReadSection(Section);
        var changed = false;

        if (MigrateLegacyKeys(values))
        {
            changed = true;
        }

        if (!values.ContainsKey("ExternalEditorPath"))
        {
            values["ExternalEditorPath"] = DefaultExternalEditorPath;
            changed = true;
        }

        if (!values.ContainsKey("OpenInExternalEditor"))
        {
            values["OpenInExternalEditor"] = "1";
            changed = true;
        }

        if (!values.ContainsKey("AutoLoadWavePath"))
        {
            values["AutoLoadWavePath"] = ResolveDefaultAutoLoadWavePath();
            changed = true;
        }

        if (!values.ContainsKey("TopMost"))
        {
            // 製品既定はオフだが、デバッグ中は不足キー追記時にオンにしておく
            values["TopMost"] = "1";
            changed = true;
        }

        if (changed)
        {
            // 挿入順を固定（旧キーは書き戻さない）
            IniFile.WriteSection(Section, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExternalEditorPath"] = values["ExternalEditorPath"],
                ["OpenInExternalEditor"] = values["OpenInExternalEditor"],
                ["AutoLoadWavePath"] = values["AutoLoadWavePath"],
                ["TopMost"] = values["TopMost"],
            });
        }
    }

    private static bool MigrateLegacyKeys(Dictionary<string, string> values)
    {
        const string legacyEditorPathKey = "SoundForgePath";
        const string legacyOpenKey = "OpenInSoundForge";
        var changed = false;

        if (!values.ContainsKey("ExternalEditorPath")
            && values.TryGetValue(legacyEditorPathKey, out var legacyPath)
            && legacyPath.Length > 0)
        {
            values["ExternalEditorPath"] = legacyPath;
            changed = true;
        }

        if (!values.ContainsKey("OpenInExternalEditor")
            && values.TryGetValue(legacyOpenKey, out var legacyOpen))
        {
            values["OpenInExternalEditor"] = legacyOpen;
            changed = true;
        }

        if (values.Remove(legacyEditorPathKey) | values.Remove(legacyOpenKey))
        {
            changed = true;
        }

        return changed;
    }

    private static string ResolveExternalEditorPath(Dictionary<string, string> values)
    {
        if (values.TryGetValue("ExternalEditorPath", out var path) && path.Length > 0)
        {
            return path;
        }

        return DefaultExternalEditorPath;
    }

    private static bool ResolveOpenInExternalEditor(Dictionary<string, string> values)
    {
        if (values.TryGetValue("OpenInExternalEditor", out var open))
        {
            return ParseBool(open, defaultValue: true);
        }

        return true;
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
