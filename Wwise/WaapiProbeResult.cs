using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wwise;

/// <summary>WAAPI 接続確認と、Wwise 上の現在選択（作成先）の結果。</summary>
internal sealed class WaapiProbeResult
{
    public bool Ok { get; init; }
    public string Url { get; init; } = string.Empty;
    public string WwiseVersion { get; init; } = string.Empty;
    public string ProcessPath { get; init; } = string.Empty;
    public string Project { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;

    /// <summary>接続中 Wwise プロジェクトの .wproj フルパス（取得できない場合は空）。</summary>
    public string ProjectFilePath { get; init; } = string.Empty;

    public string SelectedPath { get; init; } = string.Empty;
    public string SelectedName { get; init; } = string.Empty;
    public string SelectedType { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;

    public bool HasSelection => SelectedPath.Length > 0;

    /// <summary>ステータスバー用（localhost / URL は含めない）。</summary>
    public string FormatStatusDetail()
    {
        if (!Ok)
        {
            return Message.Length > 0 ? Message : UiStrings.StatusDisconnected;
        }

        var parts = new List<string>();
        if (WwiseVersion.Length > 0)
        {
            parts.Add(WwiseVersion);
        }

        if (ProjectName.Length > 0)
        {
            parts.Add(ProjectName);
        }

        parts.Add(HasSelection ? SelectedPath : UiStrings.StatusNoneSelected);
        return string.Join("  ·  ", parts);
    }

    /// <summary>エディタログ用。</summary>
    public string FormatLogReport()
    {
        var lines = new List<string>
        {
            UiStrings.LogWaapiHeader,
            $"{UiStrings.KeyStatus} {(Ok ? UiStrings.LogStatusOk : UiStrings.LogStatusNg)}",
        };

        if (Ok)
        {
            if (WwiseVersion.Length > 0)
            {
                lines.Add($"{UiStrings.KeyWwise} {WwiseVersion}");
            }

            if (Project.Length > 0)
            {
                lines.Add($"{UiStrings.KeyProject} {Project}");
            }

            lines.Add(HasSelection
                ? $"{UiStrings.KeyTarget} {SelectedPath}"
                : UiStrings.LogTargetNoneSelected);
            if (SelectedType.Length > 0)
            {
                lines.Add($"{UiStrings.KeyType} {SelectedType}");
            }
        }
        else
        {
            if (Message.Length > 0)
            {
                lines.Add($"{UiStrings.KeyMessage} {Message}");
            }

            if (Detail.Length > 0)
            {
                lines.Add($"{UiStrings.KeyDetail} {Detail}");
            }
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
}
