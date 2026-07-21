using System.Text.Json;
using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// WAAPI 接続確認と、Wwise 上の現在選択（オブジェクト作成先）の取得。
/// </summary>
internal static class WaapiStartupProbe
{
    private static readonly object SelectedReturnOptions = new Dictionary<string, object>
    {
        ["return"] = new[] { "id", "name", "type", "path" },
    };

    public static async Task<WaapiProbeResult> RunAsync(
        WaapiSettings settings,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new WaapiHttpClient(settings.Url, TimeSpan.FromMilliseconds(settings.TimeoutMs));
            var info = await client.CallAsync("ak.wwise.core.getInfo", cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var projectText = string.Empty;
            var projectName = string.Empty;
            var projectFilePath = string.Empty;
            try
            {
                var project = await client.CallAsync("ak.wwise.core.getProjectInfo", cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                projectText = FormatProject(project);
                if (TryGetString(project, "name", out var name))
                {
                    projectName = name;
                }

                projectFilePath = ReadProjectFilePath(project);
            }
            catch
            {
                projectText = UiStrings.StatusNoProject;
            }

            TryGetString(info, "processPath", out var processPath);
            var (selectedPath, selectedName, selectedType) = await ReadSelectionAsync(client, cancellationToken)
                .ConfigureAwait(false);

            return new WaapiProbeResult
            {
                Ok = true,
                Url = settings.Url,
                WwiseVersion = FormatWwiseVersion(info),
                ProcessPath = processPath,
                Project = projectText,
                ProjectName = projectName,
                ProjectFilePath = projectFilePath,
                SelectedPath = selectedPath,
                SelectedName = selectedName,
                SelectedType = selectedType,
            };
        }
        catch (TaskCanceledException)
        {
            return Fail(settings.Url, UiStrings.LogWaapiTimeout);
        }
        catch (HttpRequestException ex)
        {
            return Fail(
                settings.Url,
                UiStrings.LogWaapiConnectFailed,
                ex.Message);
        }
        catch (Exception ex)
        {
            return Fail(settings.Url, ex.Message);
        }
    }

    /// <summary>接続維持中に選択だけ更新する。</summary>
    public static async Task<(string Path, string Name, string Type)> RefreshSelectionAsync(
        WaapiSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var client = new WaapiHttpClient(settings.Url, TimeSpan.FromMilliseconds(settings.TimeoutMs));
        return await ReadSelectionAsync(client, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(string Path, string Name, string Type)> ReadSelectionAsync(
        WaapiHttpClient client,
        CancellationToken cancellationToken)
    {
        var selected = await client.CallAsync(
                "ak.wwise.ui.getSelectedObjects",
                options: SelectedReturnOptions,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (selected.ValueKind != JsonValueKind.Object
            || !selected.TryGetProperty("objects", out var objects)
            || objects.ValueKind != JsonValueKind.Array
            || objects.GetArrayLength() == 0)
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        // 複数選択時は先頭を作成先として扱う
        var first = objects[0];
        TryGetString(first, "path", out var path);
        TryGetString(first, "name", out var name);
        TryGetString(first, "type", out var type);
        return (path, name, type);
    }

    private static WaapiProbeResult Fail(string url, string message, string detail = "") =>
        new()
        {
            Ok = false,
            Url = url,
            Message = message,
            Detail = detail,
        };

    private static string FormatWwiseVersion(JsonElement info)
    {
        var displayName = TryGetString(info, "displayName", out var name) ? name : UiStrings.LabelWwise;
        if (info.TryGetProperty("version", out var version)
            && TryGetString(version, "displayName", out var versionName))
        {
            return $"{displayName} {versionName}";
        }

        return displayName;
    }

    private static string FormatProject(JsonElement project)
    {
        var name = TryGetString(project, "name", out var n) ? n : UiStrings.LabelUnnamedProject;
        var path = ReadProjectFilePath(project);
        return path.Length > 0 ? $"{name} ({path})" : name;
    }

    private static string ReadProjectFilePath(JsonElement project)
    {
        if (TryGetString(project, "path", out var path))
        {
            return path;
        }

        if (TryGetString(project, "filePath", out var filePath))
        {
            return filePath;
        }

        return string.Empty;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length > 0;
    }
}
