using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// Wwise Project Explorer 上のオブジェクト選択を WAAPI で操作する。
/// </summary>
internal static class WaapiSelection
{
    private const string FindInProjectExplorerCommand = "FindInProjectExplorerSyncGroup1";

    /// <summary>
    /// 指定パスのオブジェクトを Project Explorer で選択する。
    /// オブジェクトが無い／失敗時は false。
    /// </summary>
    public static async Task<bool> TrySelectAsync(
        WaapiSettings settings,
        string objectPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectPath))
        {
            return false;
        }

        var path = objectPath.Trim();
        if (!await WaapiObjectUtil.ExistsAsync(settings, path, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        using var client = new WaapiHttpClient(
            settings.Url,
            TimeSpan.FromMilliseconds(settings.TimeoutMs));

        await client.CallAsync(
                "ak.wwise.ui.commands.execute",
                new Dictionary<string, object?>
                {
                    ["command"] = FindInProjectExplorerCommand,
                    ["objects"] = new[] { path },
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Keep Target 用に記憶したパスを、同一プロジェクト上であれば再選択する。
    /// </summary>
    public static async Task<(bool Applied, string Path, string Message)> TryRestoreKeptTargetAsync(
        WaapiSettings settings,
        string keptTargetPath,
        string keptTargetProjectFilePath,
        string currentProjectFilePath,
        CancellationToken cancellationToken = default)
    {
        var keptPath = keptTargetPath.Trim();
        if (keptPath.Length == 0)
        {
            return (false, string.Empty, UiStrings.LogKeepTargetMemoryEmpty);
        }

        var keptProject = keptTargetProjectFilePath.Trim();
        var currentProject = currentProjectFilePath.Trim();
        if (keptProject.Length > 0
            && currentProject.Length > 0
            && !string.Equals(keptProject, currentProject, StringComparison.OrdinalIgnoreCase))
        {
            return (
                false,
                keptPath,
                UiStrings.LogKeepTargetOtherProject);
        }

        try
        {
            var ok = await TrySelectAsync(settings, keptPath, cancellationToken).ConfigureAwait(false);
            return ok
                ? (true, keptPath, UiStrings.LogKeepTargetReselectOk(keptPath))
                : (false, keptPath, UiStrings.LogKeepTargetObjectMissing(keptPath));
        }
        catch (Exception ex)
        {
            return (false, keptPath, UiStrings.LogKeepTargetReselectFailed(ex.Message));
        }
    }
}
