using System.Globalization;
using System.Text;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// プロジェクト単位の作業設定（変更時オートセーブ）。exe 横 INI の [Projects] / [Project.*]。
/// </summary>
internal sealed class ProjectProfile
{
    public string Name { get; set; } = ProjectSettingsStore.DefaultName;

    public double FadeInSeconds { get; set; }

    public double FadeOutSeconds { get; set; }

    public PlaylistExitSourceMode ExitSourceAt { get; set; } = PlaylistExitSourceMode.Immediate;

    public MarkerGridOverrideMode GridOverride { get; set; } = MarkerGridOverrideMode.Bar;

    public int CommentDigits { get; set; } = 3;

    public bool CommentZeroPad { get; set; } = true;

    public bool CommentPrefixEnabled { get; set; }

    public string CommentPrefix { get; set; } = string.Empty;

    public bool CommentSuffixEnabled { get; set; }

    public string CommentSuffix { get; set; } = string.Empty;

    public bool CommentJoinerEnabled { get; set; }

    public string CommentJoiner { get; set; } = string.Empty;

    public bool CommentResetPerPart { get; set; } = true;

    public bool CompactFileNumbers { get; set; }

    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Music Track のストリーミング有効（既定オン）。</summary>
    public bool StreamEnabled { get; set; } = true;

    /// <summary>2 番目以降のセグメントの Look-ahead time（ms）。</summary>
    public int LookAheadMs { get; set; } = 500;

    /// <summary>Playlist 先頭セグメント先頭トラックの Prefetch Length（ms）。</summary>
    public int PrefetchLengthMs { get; set; } = 500;

    /// <summary>EXPORT 時のラウドネス正規化（既定オフ）。</summary>
    public bool LoudnessNormalizeEnabled { get; set; }

    /// <summary>正規化ターゲット（LKFS。既定 −24）。</summary>
    public double LoudnessTargetLkfs { get; set; } = -24.0;

    /// <summary>グループ内の相対バランスを保って正規化するか（既定オン）。</summary>
    public bool LoudnessPreserveGroupBalance { get; set; } = true;

    /// <summary>正規化ゲインの逆を Music Playlist へ戻すか（既定オン）。</summary>
    public bool AutoVolumeEnabled { get; set; } = false;

    /// <summary>Auto Volume の書き戻し先（既定 Make-Up Gain）。</summary>
    public AutoVolumeTarget AutoVolumeTarget { get; set; } = AutoVolumeTarget.MakeUpGain;

    /// <summary>More Options パネルを開いた状態にするか（既定オン）。</summary>
    public bool MoreOptionsExpanded { get; set; } = true;

    /// <summary>起動時／プロジェクト復帰時に最後のセッションを復元するか（既定オン）。</summary>
    public bool KeepLastSession { get; set; } = true;

    /// <summary>最後に正常に読み込んだ波形のフルパス。</summary>
    public string LastWavePath { get; set; } = string.Empty;

    /// <summary>Wwise 作成先パスをこのプロジェクトで固定するか（既定オフ）。</summary>
    public bool KeepTarget { get; set; }

    /// <summary>固定中の Wwise オブジェクトパス。</summary>
    public string KeptTargetPath { get; set; } = string.Empty;

    /// <summary>固定時の Wwise プロジェクトファイルパス（不一致なら再選択しない）。</summary>
    public string KeptTargetProjectFilePath { get; set; } = string.Empty;

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
        OutputDirectory = OutputDirectory,
        StreamEnabled = StreamEnabled,
        LookAheadMs = LookAheadMs,
        PrefetchLengthMs = PrefetchLengthMs,
        LoudnessNormalizeEnabled = LoudnessNormalizeEnabled,
        LoudnessTargetLkfs = LoudnessTargetLkfs,
        LoudnessPreserveGroupBalance = LoudnessPreserveGroupBalance,
        AutoVolumeEnabled = AutoVolumeEnabled,
        AutoVolumeTarget = AutoVolumeTarget,
        MoreOptionsExpanded = MoreOptionsExpanded,
        KeepLastSession = KeepLastSession,
        LastWavePath = LastWavePath,
        KeepTarget = KeepTarget,
        KeptTargetPath = KeptTargetPath,
        KeptTargetProjectFilePath = KeptTargetProjectFilePath,
    };

    public void CopyMarkerInto(MarkerSettings markers)
    {
        markers.GridOverride = GridOverride;
        markers.CommentDigits = Math.Clamp(
            CommentDigits,
            MarkerSettings.CommentDigitsMin,
            MarkerSettings.CommentDigitsMax);
        markers.CommentZeroPad = CommentZeroPad;
        markers.CommentPrefix = CommentPrefixEnabled ? CommentPrefix : string.Empty;
        markers.CommentSuffix = CommentSuffixEnabled ? CommentSuffix : string.Empty;
        markers.CommentJoiner = CommentJoinerEnabled ? CommentJoiner : string.Empty;
        markers.CommentResetPerPart = CommentResetPerPart;
        markers.SyncCommentOptionalEnabledFlags();
    }

    public void CopyMarkerFrom(MarkerSettings markers)
    {
        markers.SyncCommentOptionalEnabledFlags();
        GridOverride = markers.GridOverride;
        CommentDigits = markers.CommentDigits;
        CommentZeroPad = markers.CommentZeroPad;
        CommentPrefix = markers.CommentPrefix;
        CommentSuffix = markers.CommentSuffix;
        CommentJoiner = markers.CommentJoiner;
        CommentPrefixEnabled = markers.CommentPrefixEnabled;
        CommentSuffixEnabled = markers.CommentSuffixEnabled;
        CommentJoinerEnabled = markers.CommentJoinerEnabled;
        CommentResetPerPart = markers.CommentResetPerPart;
    }
}

/// <summary>
/// プロジェクト一覧と Active の読み書き。変更時はプロファイルを即時保存する。
/// </summary>
internal sealed class ProjectSettingsStore
{
    public const string DefaultName = "Default";
    public const string IndexSection = "Projects";

    public static string NewProjectMenuItem => UiStrings.ProjectNewProjectMenuItem;

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
        ExitSourceAt = PlaylistExitSourceMode.Immediate,
        GridOverride = MarkerGridOverrideMode.Bar,
        CommentDigits = 3,
        CommentZeroPad = true,
        CommentPrefixEnabled = false,
        CommentPrefix = string.Empty,
        CommentSuffixEnabled = false,
        CommentSuffix = string.Empty,
        CommentJoinerEnabled = false,
        CommentJoiner = string.Empty,
        CommentResetPerPart = true,
        CompactFileNumbers = false,
        OutputDirectory = string.Empty,
        StreamEnabled = true,
        LookAheadMs = 500,
        PrefetchLengthMs = 500,
        LoudnessNormalizeEnabled = false,
        LoudnessTargetLkfs = -24.0,
        LoudnessPreserveGroupBalance = true,
        AutoVolumeEnabled = false,
        AutoVolumeTarget = AutoVolumeTarget.MakeUpGain,
        MoreOptionsExpanded = true,
        KeepLastSession = true,
        LastWavePath = string.Empty,
        KeepTarget = false,
        KeptTargetPath = string.Empty,
        KeptTargetProjectFilePath = string.Empty,
    };

    public static ProjectSettingsStore Load()
    {
        var store = new ProjectSettingsStore();
        var index = IniFile.ReadSection(IndexSection);
        if (index.Count == 0 || !index.ContainsKey("Names"))
        {
            store.EnsureDefaultExists();
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

        store.MigrateLegacyKeepTargetFromApp();
        return store;
    }

    public ProjectProfile GetActive() => GetRequired(ActiveName);

    public ProjectProfile GetRequired(string name)
    {
        if (_profiles.TryGetValue(name, out var profile))
        {
            return profile.Clone();
        }

        throw new InvalidOperationException(UiStrings.ErrProjectNotFound(name));
    }

    public bool ContainsName(string name) =>
        !string.IsNullOrWhiteSpace(name) && _profiles.ContainsKey(name.Trim());

    public void SetActive(string name)
    {
        var trimmed = name.Trim();
        if (!_profiles.ContainsKey(trimmed))
        {
            throw new InvalidOperationException(UiStrings.ErrProjectNotFound(trimmed));
        }

        ActiveName = _names.First(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
        WriteIndex();
    }

    /// <summary>終了時など、Active 名だけ更新する（プロファイルは書かない）。</summary>
    public void SaveActiveNameOnly()
    {
        WriteIndex();
    }

    public void SaveStreaming(
        string name,
        bool streamEnabled,
        int lookAheadMs,
        int prefetchLengthMs)
    {
        if (!_profiles.TryGetValue(name.Trim(), out var profile))
        {
            return;
        }

        profile.StreamEnabled = streamEnabled;
        profile.LookAheadMs = Math.Clamp(lookAheadMs, 0, 9999);
        profile.PrefetchLengthMs = Math.Clamp(prefetchLengthMs, 0, 9999);
        WriteProfile(name, profile);
    }

    public void SaveLoudness(
        string name,
        bool enabled,
        double targetLkfs,
        bool preserveGroupBalance,
        bool autoVolumeEnabled = true,
        AutoVolumeTarget autoVolumeTarget = AutoVolumeTarget.MakeUpGain)
    {
        if (!_profiles.TryGetValue(name.Trim(), out var profile))
        {
            return;
        }

        profile.LoudnessNormalizeEnabled = enabled;
        profile.LoudnessTargetLkfs = Math.Clamp(targetLkfs, -70.0, 0.0);
        profile.LoudnessPreserveGroupBalance = preserveGroupBalance;
        profile.AutoVolumeEnabled = autoVolumeEnabled;
        profile.AutoVolumeTarget = autoVolumeTarget;
        WriteProfile(name, profile);
    }

    public void SaveMoreOptionsExpanded(string name, bool expanded)
    {
        if (!_profiles.TryGetValue(name.Trim(), out var profile))
        {
            return;
        }

        profile.MoreOptionsExpanded = expanded;
        WriteProfile(name, profile);
    }

    public void SaveKeepLastSession(string name, bool enabled)
    {
        if (!_profiles.TryGetValue(name.Trim(), out var profile))
        {
            return;
        }

        profile.KeepLastSession = enabled;
        WriteProfile(name, profile);
    }

    public void SaveLastWavePath(string name, string path)
    {
        if (!_profiles.TryGetValue(name.Trim(), out var profile))
        {
            return;
        }

        profile.LastWavePath = path?.Trim() ?? string.Empty;
        WriteProfile(name, profile);
    }

    public void SaveKeepTarget(
        string name,
        bool enabled,
        string keptTargetPath,
        string keptTargetProjectFilePath)
    {
        if (!_profiles.TryGetValue(name.Trim(), out var profile))
        {
            return;
        }

        profile.KeepTarget = enabled;
        profile.KeptTargetPath = keptTargetPath?.Trim() ?? string.Empty;
        profile.KeptTargetProjectFilePath = keptTargetProjectFilePath?.Trim() ?? string.Empty;
        WriteProfile(name, profile);
    }

    public void SaveMarkers(string name, MarkerSettings markers)
    {
        if (!_profiles.TryGetValue(name.Trim(), out var profile))
        {
            return;
        }

        profile.CopyMarkerFrom(markers);
        WriteProfile(name, profile);
    }

    public void SaveLastWaveSession(string name, LastWaveSessionState state)
    {
        var trimmed = name.Trim();
        if (!_profiles.ContainsKey(trimmed))
        {
            return;
        }

        try
        {
            var path = LastWaveSessionState.SidecarPath(trimmed);
            File.WriteAllText(path, state.ToJson());
        }
        catch
        {
            // オートセーブ失敗は作業を止めない。
        }
    }

    public bool TryReadLastWaveSession(string name, out LastWaveSessionState? state)
    {
        state = null;
        var trimmed = name.Trim();
        if (!_profiles.ContainsKey(trimmed))
        {
            return false;
        }

        var path = LastWaveSessionState.SidecarPath(trimmed);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            return LastWaveSessionState.TryParse(json, out state);
        }
        catch
        {
            return false;
        }
    }

    public static void DeleteLastWaveSessionFile(string projectName)
    {
        try
        {
            var path = LastWaveSessionState.SidecarPath(projectName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 削除失敗は無視する。
        }
    }

    private static void RenameLastWaveSessionFile(string oldName, string newName)
    {
        try
        {
            var oldPath = LastWaveSessionState.SidecarPath(oldName);
            var newPath = LastWaveSessionState.SidecarPath(newName);
            if (!File.Exists(oldPath))
            {
                return;
            }

            if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(newPath))
            {
                File.Delete(newPath);
            }

            File.Move(oldPath, newPath);
        }
        catch
        {
            // 改名失敗は無視する。
        }
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
            throw new InvalidOperationException(UiStrings.ErrProjectNameRequired);
        }

        if (string.Equals(trimmedNew, NewProjectMenuItem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(UiStrings.ErrProjectNameReserved);
        }

        if (creatingNew)
        {
            if (_profiles.ContainsKey(trimmedNew))
            {
                throw new InvalidOperationException(UiStrings.ErrProjectNameExists(trimmedNew));
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
            throw new InvalidOperationException(UiStrings.ErrProjectNotFound(trimmedCurrent));
        }

        var rename = !string.Equals(trimmedCurrent, trimmedNew, StringComparison.OrdinalIgnoreCase);
        if (rename && _profiles.ContainsKey(trimmedNew))
        {
            throw new InvalidOperationException(UiStrings.ErrProjectNameExists(trimmedNew));
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
            RenameLastWaveSessionFile(trimmedCurrent, trimmedNew);
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
            throw new InvalidOperationException(UiStrings.ErrProjectNotFound(trimmed));
        }

        _names.RemoveAll(n => string.Equals(n, trimmed, StringComparison.OrdinalIgnoreCase));
        _profiles.Remove(trimmed);
        IniFile.RemoveSection(ToSectionName(trimmed));
        DeleteLastWaveSessionFile(trimmed);

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
        var baseName = UiStrings.ProjectNewProjectBaseName;
        if (!_profiles.ContainsKey(baseName))
        {
            return baseName;
        }

        for (var i = 2; i < 10_000; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!_profiles.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} {DateTime.Now:yyyyMMddHHmmss}";
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
            ["OutputDirectory"] = profile.OutputDirectory,
            ["StreamEnabled"] = profile.StreamEnabled ? "1" : "0",
            ["LookAheadMs"] = Math.Clamp(profile.LookAheadMs, 0, 9999)
                .ToString(CultureInfo.InvariantCulture),
            ["PrefetchLengthMs"] = Math.Clamp(profile.PrefetchLengthMs, 0, 9999)
                .ToString(CultureInfo.InvariantCulture),
            ["LoudnessNormalizeEnabled"] = profile.LoudnessNormalizeEnabled ? "1" : "0",
            ["LoudnessTargetLkfs"] = Math.Clamp(profile.LoudnessTargetLkfs, -70.0, 0.0)
                .ToString("0.###", CultureInfo.InvariantCulture),
            ["LoudnessPreserveGroupBalance"] = profile.LoudnessPreserveGroupBalance ? "1" : "0",
            ["AutoVolumeEnabled"] = profile.AutoVolumeEnabled ? "1" : "0",
            ["AutoVolumeTarget"] = profile.AutoVolumeTarget.ToString(),
            ["MoreOptionsExpanded"] = profile.MoreOptionsExpanded ? "1" : "0",
            ["KeepLastSession"] = profile.KeepLastSession ? "1" : "0",
            ["LastWavePath"] = profile.LastWavePath,
            ["KeepTarget"] = profile.KeepTarget ? "1" : "0",
            ["KeptTargetPath"] = profile.KeptTargetPath,
            ["KeptTargetProjectFilePath"] = profile.KeptTargetProjectFilePath,
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
        profile.CommentResetPerPart = ReadBool(values, "CommentResetPerPart", profile.CommentResetPerPart);
        profile.CompactFileNumbers = ReadBool(values, "CompactFileNumbers", profile.CompactFileNumbers);

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

        profile.CommentPrefixEnabled = profile.CommentPrefix.Length > 0;
        profile.CommentSuffixEnabled = profile.CommentSuffix.Length > 0;
        profile.CommentJoinerEnabled = profile.CommentJoiner.Length > 0;

        if (values.TryGetValue("OutputDirectory", out var output))
        {
            profile.OutputDirectory = output;
        }

        profile.StreamEnabled = ReadBool(values, "StreamEnabled", defaultValue: true);

        if (values.TryGetValue("LookAheadMs", out var lookAheadText)
            && int.TryParse(lookAheadText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lookAheadMs))
        {
            profile.LookAheadMs = Math.Clamp(lookAheadMs, 0, 9999);
        }

        if (values.TryGetValue("PrefetchLengthMs", out var prefetchText)
            && int.TryParse(prefetchText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var prefetchMs))
        {
            profile.PrefetchLengthMs = Math.Clamp(prefetchMs, 0, 9999);
        }

        profile.LoudnessNormalizeEnabled = ReadBool(
            values,
            "LoudnessNormalizeEnabled",
            defaultValue: false);
        if (values.TryGetValue("LoudnessTargetLkfs", out var loudnessTargetText)
            && double.TryParse(
                loudnessTargetText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var loudnessTarget))
        {
            profile.LoudnessTargetLkfs = Math.Clamp(loudnessTarget, -70.0, 0.0);
        }

        profile.LoudnessPreserveGroupBalance = ReadBool(
            values,
            "LoudnessPreserveGroupBalance",
            defaultValue: true);

        profile.AutoVolumeEnabled = ReadBool(
            values,
            "AutoVolumeEnabled",
            defaultValue: false);
        if (values.TryGetValue("AutoVolumeTarget", out var autoVolumeTargetText)
            && Enum.TryParse<AutoVolumeTarget>(autoVolumeTargetText, ignoreCase: true, out var autoVolumeTarget))
        {
            profile.AutoVolumeTarget = autoVolumeTarget;
        }

        profile.MoreOptionsExpanded = ReadBool(
            values,
            "MoreOptionsExpanded",
            defaultValue: true);

        profile.KeepLastSession = ReadBool(
            values,
            "KeepLastSession",
            defaultValue: true);
        if (values.TryGetValue("LastWavePath", out var lastWave))
        {
            profile.LastWavePath = lastWave.Trim().Trim('"');
        }

        profile.KeepTarget = ReadBool(
            values,
            "KeepTarget",
            defaultValue: false);
        if (values.TryGetValue("KeptTargetPath", out var keptTargetPath))
        {
            profile.KeptTargetPath = keptTargetPath.Trim().Trim('"');
        }

        if (values.TryGetValue("KeptTargetProjectFilePath", out var keptTargetProject))
        {
            profile.KeptTargetProjectFilePath = keptTargetProject.Trim().Trim('"');
        }

        return profile;
    }

    /// <summary>
    /// 旧 [App] KeepTarget 系を Active プロジェクトへ一度だけ移し、[App] から除去する。
    /// プロジェクト側に既に Keep Target がある場合は App 側だけ消す。
    /// </summary>
    private void MigrateLegacyKeepTargetFromApp()
    {
        var appValues = IniFile.ReadSection(AppSettings.Section);
        var hasLegacy = appValues.ContainsKey("KeepTarget")
            || appValues.ContainsKey("KeptTargetPath")
            || appValues.ContainsKey("KeptTargetProjectFilePath");
        if (!hasLegacy)
        {
            return;
        }

        if (_profiles.TryGetValue(ActiveName, out var profile)
            && !profile.KeepTarget
            && string.IsNullOrWhiteSpace(profile.KeptTargetPath)
            && string.IsNullOrWhiteSpace(profile.KeptTargetProjectFilePath))
        {
            profile.KeepTarget = ReadBool(appValues, "KeepTarget", defaultValue: false);
            if (appValues.TryGetValue("KeptTargetPath", out var keptPath))
            {
                profile.KeptTargetPath = keptPath.Trim().Trim('"');
            }

            if (appValues.TryGetValue("KeptTargetProjectFilePath", out var keptProject))
            {
                profile.KeptTargetProjectFilePath = keptProject.Trim().Trim('"');
            }

            WriteProfile(ActiveName, profile);
        }

        // [App] を Keep Target 無しで書き直し（他キーは AppSettings 経由で維持）。
        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (appValues.TryGetValue("AlwaysOnTop", out var alwaysOnTop))
        {
            cleaned["AlwaysOnTop"] = alwaysOnTop;
        }

        if (appValues.TryGetValue("UiLanguage", out var language))
        {
            cleaned["UiLanguage"] = language;
        }

        if (cleaned.Count == 0)
        {
            cleaned["AlwaysOnTop"] = "0";
            cleaned["UiLanguage"] = "ja";
        }

        IniFile.WriteSection(AppSettings.Section, cleaned);
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
