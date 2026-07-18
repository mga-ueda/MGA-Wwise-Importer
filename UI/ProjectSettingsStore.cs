using System.Globalization;
using System.Text;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// プロジェクト単位の作業設定（手動 SAVE）。exe 横 INI の [Projects] / [Project.*]。
/// </summary>
internal sealed class ProjectProfile
{
    public string Name { get; set; } = ProjectSettingsStore.DefaultName;

    public double FadeInSeconds { get; set; }

    public double FadeOutSeconds { get; set; }

    public PlaylistExitSourceMode ExitSourceAt { get; set; } = PlaylistExitSourceMode.NextBar;

    public MarkerGridOverrideMode GridOverride { get; set; } = MarkerGridOverrideMode.Default;

    public int CommentDigits { get; set; } = 3;

    public bool CommentZeroPad { get; set; } = true;

    public bool CommentPrefixEnabled { get; set; } = true;

    public string CommentPrefix { get; set; } = string.Empty;

    public bool CommentSuffixEnabled { get; set; }

    public string CommentSuffix { get; set; } = string.Empty;

    public bool CommentJoinerEnabled { get; set; }

    public string CommentJoiner { get; set; } = "_";

    public bool CommentResetPerPart { get; set; } = true;

    public bool CompactFileNumbers { get; set; } = true;

    public bool AlwaysOnTop { get; set; }

    public string OutputDirectory { get; set; } = string.Empty;

    public ProjectProfile Clone() => new()
    {
        Name = Name,
        FadeInSeconds = FadeInSeconds,
        FadeOutSeconds = FadeOutSeconds,
        ExitSourceAt = ExitSourceAt,
        GridOverride = GridOverride,
        CommentDigits = CommentDigits,
        CommentZeroPad = CommentZeroPad,
        CommentPrefixEnabled = CommentPrefixEnabled,
        CommentPrefix = CommentPrefix,
        CommentSuffixEnabled = CommentSuffixEnabled,
        CommentSuffix = CommentSuffix,
        CommentJoinerEnabled = CommentJoinerEnabled,
        CommentJoiner = CommentJoiner,
        CommentResetPerPart = CommentResetPerPart,
        CompactFileNumbers = CompactFileNumbers,
        AlwaysOnTop = AlwaysOnTop,
        OutputDirectory = OutputDirectory,
    };

    public void CopyMarkerInto(MarkerSettings markers)
    {
        markers.GridOverride = GridOverride;
        markers.CommentDigits = Math.Clamp(
            CommentDigits,
            MarkerSettings.CommentDigitsMin,
            MarkerSettings.CommentDigitsMax);
        markers.CommentZeroPad = CommentZeroPad;
        markers.CommentPrefixEnabled = CommentPrefixEnabled;
        markers.CommentPrefix = CommentPrefix;
        markers.CommentSuffixEnabled = CommentSuffixEnabled;
        markers.CommentSuffix = CommentSuffix;
        markers.CommentJoinerEnabled = CommentJoinerEnabled;
        markers.CommentJoiner = CommentJoiner;
        markers.CommentResetPerPart = CommentResetPerPart;
    }

    public void CopyMarkerFrom(MarkerSettings markers)
    {
        GridOverride = markers.GridOverride;
        CommentDigits = markers.CommentDigits;
        CommentZeroPad = markers.CommentZeroPad;
        CommentPrefixEnabled = markers.CommentPrefixEnabled;
        CommentPrefix = markers.CommentPrefix;
        CommentSuffixEnabled = markers.CommentSuffixEnabled;
        CommentSuffix = markers.CommentSuffix;
        CommentJoinerEnabled = markers.CommentJoinerEnabled;
        CommentJoiner = markers.CommentJoiner;
        CommentResetPerPart = markers.CommentResetPerPart;
    }
}

/// <summary>
/// プロジェクト一覧と Active の読み書き。オートセーブは行わない。
/// </summary>
internal sealed class ProjectSettingsStore
{
    public const string DefaultName = "Default";
    public const string NewProjectMenuItem = "+ New Project";
    public const string IndexSection = "Projects";

    private readonly List<string> _names = [];
    private readonly Dictionary<string, ProjectProfile> _profiles =
        new(StringComparer.OrdinalIgnoreCase);

    public string ActiveName { get; private set; } = DefaultName;

    public IReadOnlyList<string> Names => _names;

    public static ProjectProfile CreateAppDefaults(string name = DefaultName) => new()
    {
        Name = name,
        FadeInSeconds = 0d,
        FadeOutSeconds = 0d,
        ExitSourceAt = PlaylistExitSourceMode.NextBar,
        GridOverride = MarkerGridOverrideMode.Default,
        CommentDigits = 3,
        CommentZeroPad = true,
        CommentPrefixEnabled = true,
        CommentPrefix = string.Empty,
        CommentSuffixEnabled = false,
        CommentSuffix = string.Empty,
        CommentJoinerEnabled = false,
        CommentJoiner = "_",
        CommentResetPerPart = true,
        CompactFileNumbers = true,
        AlwaysOnTop = false,
        OutputDirectory = string.Empty,
    };

    public static ProjectSettingsStore Load()
    {
        var store = new ProjectSettingsStore();
        var index = IniFile.ReadSection(IndexSection);
        if (index.Count == 0 || !index.ContainsKey("Names"))
        {
            store.MigrateFromLegacyAndCreateDefault();
            store.WriteAll();
            return store;
        }

        foreach (var name in ParseNames(index.TryGetValue("Names", out var namesText) ? namesText : string.Empty))
        {
            store._names.Add(name);
            store._profiles[name] = ReadProfile(name);
        }

        if (store._names.Count == 0)
        {
            store.EnsureDefaultExists();
            store.WriteAll();
            return store;
        }

        var active = index.TryGetValue("Active", out var activeText)
            ? activeText.Trim()
            : DefaultName;
        store.ActiveName = store._profiles.ContainsKey(active)
            ? store._names.First(n => string.Equals(n, active, StringComparison.OrdinalIgnoreCase))
            : store._names[0];
        return store;
    }

    public ProjectProfile GetActive() => GetRequired(ActiveName);

    public ProjectProfile GetRequired(string name)
    {
        if (_profiles.TryGetValue(name, out var profile))
        {
            return profile.Clone();
        }

        throw new InvalidOperationException($"プロジェクトが見つかりません: {name}");
    }

    public bool ContainsName(string name) =>
        !string.IsNullOrWhiteSpace(name) && _profiles.ContainsKey(name.Trim());

    public void SetActive(string name)
    {
        var trimmed = name.Trim();
        if (!_profiles.ContainsKey(trimmed))
        {
            throw new InvalidOperationException($"プロジェクトが見つかりません: {trimmed}");
        }

        ActiveName = _names.First(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
        WriteIndex();
    }

    /// <summary>終了時など、Active 名だけ更新する（プロファイルは書かない）。</summary>
    public void SaveActiveNameOnly()
    {
        WriteIndex();
    }

    /// <summary>
    /// 現在の UI 状態を保存する。newName が異なれば改名（旧名セクションは削除）。
    /// creatingNew のときは新規追加。
    /// </summary>
    public string SaveProfile(
        string currentName,
        string newName,
        ProjectProfile profile,
        bool creatingNew)
    {
        var trimmedNew = NormalizeName(newName);
        if (trimmedNew.Length == 0)
        {
            throw new InvalidOperationException("プロジェクト名を入力してください。");
        }

        if (string.Equals(trimmedNew, NewProjectMenuItem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("この名前は予約されています。");
        }

        if (creatingNew)
        {
            if (_profiles.ContainsKey(trimmedNew))
            {
                throw new InvalidOperationException($"同じ名前のプロジェクトが既にあります: {trimmedNew}");
            }

            profile.Name = trimmedNew;
            _names.Add(trimmedNew);
            _profiles[trimmedNew] = profile.Clone();
            ActiveName = trimmedNew;
            WriteProfile(trimmedNew, _profiles[trimmedNew]);
            WriteIndex();
            return trimmedNew;
        }

        var trimmedCurrent = currentName.Trim();
        if (!_profiles.ContainsKey(trimmedCurrent))
        {
            throw new InvalidOperationException($"プロジェクトが見つかりません: {trimmedCurrent}");
        }

        var rename = !string.Equals(trimmedCurrent, trimmedNew, StringComparison.OrdinalIgnoreCase);
        if (rename && _profiles.ContainsKey(trimmedNew))
        {
            throw new InvalidOperationException($"同じ名前のプロジェクトが既にあります: {trimmedNew}");
        }

        profile.Name = trimmedNew;
        if (rename)
        {
            var index = _names.FindIndex(n =>
                string.Equals(n, trimmedCurrent, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _names[index] = trimmedNew;
            }

            _profiles.Remove(trimmedCurrent);
            IniFile.RemoveSection(ToSectionName(trimmedCurrent));
        }

        _profiles[trimmedNew] = profile.Clone();
        ActiveName = trimmedNew;
        WriteProfile(trimmedNew, _profiles[trimmedNew]);
        WriteIndex();
        return trimmedNew;
    }

    public ProjectProfile Delete(string name)
    {
        var trimmed = name.Trim();
        if (!_profiles.ContainsKey(trimmed))
        {
            throw new InvalidOperationException($"プロジェクトが見つかりません: {trimmed}");
        }

        _names.RemoveAll(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
        _profiles.Remove(trimmed);
        IniFile.RemoveSection(ToSectionName(trimmed));

        if (_names.Count == 0)
        {
            EnsureDefaultExists();
            WriteAll();
            return GetActive();
        }

        if (string.Equals(ActiveName, trimmed, StringComparison.OrdinalIgnoreCase))
        {
            ActiveName = _names[0];
        }

        WriteIndex();
        return GetActive();
    }

    public string SuggestNewProjectName()
    {
        for (var i = 2; i < 10_000; i++)
        {
            var candidate = $"Project {i}";
            if (!_profiles.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return $"Project {DateTime.Now:yyyyMMddHHmmss}";
    }

    private void MigrateFromLegacyAndCreateDefault()
    {
        var profile = CreateAppDefaults();
        var markers = MarkerSettings.Load();
        profile.CopyMarkerFrom(markers);
        var developer = DeveloperSettings.Load();
        profile.AlwaysOnTop = developer.TopMost;
        _names.Clear();
        _profiles.Clear();
        _names.Add(DefaultName);
        _profiles[DefaultName] = profile;
        ActiveName = DefaultName;
    }

    private void EnsureDefaultExists()
    {
        _names.Clear();
        _profiles.Clear();
        var profile = CreateAppDefaults();
        _names.Add(DefaultName);
        _profiles[DefaultName] = profile;
        ActiveName = DefaultName;
    }

    private void WriteAll()
    {
        foreach (var name in _names)
        {
            WriteProfile(name, _profiles[name]);
        }

        WriteIndex();
    }

    private void WriteIndex()
    {
        IniFile.WriteSection(IndexSection, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Active"] = ActiveName,
            ["Names"] = string.Join("|", _names),
        });
    }

    private static void WriteProfile(string name, ProjectProfile profile)
    {
        IniFile.WriteSection(ToSectionName(name), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Name"] = profile.Name,
            ["FadeInSeconds"] = profile.FadeInSeconds.ToString(CultureInfo.InvariantCulture),
            ["FadeOutSeconds"] = profile.FadeOutSeconds.ToString(CultureInfo.InvariantCulture),
            ["ExitSourceAt"] = profile.ExitSourceAt.ToString(),
            ["GridOverride"] = profile.GridOverride.ToString(),
            ["CommentDigits"] = Math.Clamp(
                    profile.CommentDigits,
                    MarkerSettings.CommentDigitsMin,
                    MarkerSettings.CommentDigitsMax)
                .ToString(CultureInfo.InvariantCulture),
            ["CommentZeroPad"] = profile.CommentZeroPad ? "1" : "0",
            ["CommentPrefixEnabled"] = profile.CommentPrefixEnabled ? "1" : "0",
            ["CommentPrefix"] = profile.CommentPrefix,
            ["CommentSuffixEnabled"] = profile.CommentSuffixEnabled ? "1" : "0",
            ["CommentSuffix"] = profile.CommentSuffix,
            ["CommentJoinerEnabled"] = profile.CommentJoinerEnabled ? "1" : "0",
            ["CommentJoiner"] = profile.CommentJoiner,
            ["CommentResetPerPart"] = profile.CommentResetPerPart ? "1" : "0",
            ["CompactFileNumbers"] = profile.CompactFileNumbers ? "1" : "0",
            ["AlwaysOnTop"] = profile.AlwaysOnTop ? "1" : "0",
            ["OutputDirectory"] = profile.OutputDirectory,
        });
    }

    private static ProjectProfile ReadProfile(string name)
    {
        var values = IniFile.ReadSection(ToSectionName(name));
        var profile = CreateAppDefaults(name);
        if (values.TryGetValue("Name", out var storedName) && storedName.Trim().Length > 0)
        {
            profile.Name = storedName.Trim();
        }

        if (values.TryGetValue("FadeInSeconds", out var fadeInText)
            && double.TryParse(fadeInText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fadeIn))
        {
            profile.FadeInSeconds = fadeIn;
        }

        if (values.TryGetValue("FadeOutSeconds", out var fadeOutText)
            && double.TryParse(fadeOutText, NumberStyles.Float, CultureInfo.InvariantCulture, out var fadeOut))
        {
            profile.FadeOutSeconds = fadeOut;
        }

        if (values.TryGetValue("ExitSourceAt", out var exitText)
            && Enum.TryParse<PlaylistExitSourceMode>(exitText, ignoreCase: true, out var exitMode))
        {
            profile.ExitSourceAt = exitMode;
        }

        if (values.TryGetValue("GridOverride", out var gridText)
            && Enum.TryParse<MarkerGridOverrideMode>(gridText, ignoreCase: true, out var gridMode))
        {
            profile.GridOverride = gridMode;
        }

        if (values.TryGetValue("CommentDigits", out var digitsText)
            && int.TryParse(digitsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits))
        {
            profile.CommentDigits = Math.Clamp(
                digits,
                MarkerSettings.CommentDigitsMin,
                MarkerSettings.CommentDigitsMax);
        }

        profile.CommentZeroPad = ReadBool(values, "CommentZeroPad", profile.CommentZeroPad);
        profile.CommentPrefixEnabled = ReadBool(values, "CommentPrefixEnabled", profile.CommentPrefixEnabled);
        profile.CommentSuffixEnabled = ReadBool(values, "CommentSuffixEnabled", profile.CommentSuffixEnabled);
        profile.CommentJoinerEnabled = ReadBool(values, "CommentJoinerEnabled", profile.CommentJoinerEnabled);
        profile.CommentResetPerPart = ReadBool(values, "CommentResetPerPart", profile.CommentResetPerPart);
        profile.CompactFileNumbers = ReadBool(values, "CompactFileNumbers", profile.CompactFileNumbers);
        profile.AlwaysOnTop = ReadBool(values, "AlwaysOnTop", profile.AlwaysOnTop);

        if (values.TryGetValue("CommentPrefix", out var prefix))
        {
            profile.CommentPrefix = prefix;
        }

        if (values.TryGetValue("CommentSuffix", out var suffix))
        {
            profile.CommentSuffix = suffix;
        }

        if (values.TryGetValue("CommentJoiner", out var joiner))
        {
            profile.CommentJoiner = joiner;
        }

        if (values.TryGetValue("OutputDirectory", out var output))
        {
            profile.OutputDirectory = output;
        }

        return profile;
    }

    private static IEnumerable<string> ParseNames(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var part in text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0
                && !string.Equals(part, NewProjectMenuItem, StringComparison.OrdinalIgnoreCase))
            {
                yield return part;
            }
        }
    }

    private static string NormalizeName(string name) => name.Trim();

    internal static string ToSectionName(string projectName)
    {
        // セクション名に使えない文字を避けつつ、表示名は Name キーに保持する。
        var sb = new StringBuilder(projectName.Length);
        foreach (var ch in projectName)
        {
            if (ch is '[' or ']' or '\r' or '\n' or '=')
            {
                sb.Append('_');
            }
            else
            {
                sb.Append(ch);
            }
        }

        var safe = sb.ToString().Trim();
        if (safe.Length == 0)
        {
            safe = "Unnamed";
        }

        return "Project." + safe;
    }

    private static bool ReadBool(
        Dictionary<string, string> values,
        string key,
        bool defaultValue)
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
