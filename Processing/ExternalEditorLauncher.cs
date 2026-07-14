using System.Diagnostics;

namespace MgaWwiseImporter.Processing;

/// <summary>
/// 出力 Wave を INI で指定した外部エディタで開く。
/// </summary>
internal static class ExternalEditorLauncher
{
    public static string Open(string wavePath, string executablePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return "外部エディタ起動スキップ: 実行ファイルパスが空です";
            }

            if (!File.Exists(executablePath))
            {
                return $"外部エディタ起動スキップ: 実行ファイルが見つかりません ({executablePath})";
            }

            if (!File.Exists(wavePath))
            {
                return $"外部エディタ起動スキップ: 波形が見つかりません ({wavePath})";
            }

            // UseShellExecute=true detaches from this process / debugger tree.
            // With false, ending the child can tear down the debug session.
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = $"\"{wavePath}\"",
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(wavePath) ?? AppContext.BaseDirectory,
            };

            Process.Start(startInfo)?.Dispose();

            return $"外部エディタ起動: {wavePath}";
        }
        catch (Exception ex)
        {
            return $"外部エディタ起動失敗: {ex.Message}";
        }
    }
}
