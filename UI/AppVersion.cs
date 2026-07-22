using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// アプリ版の単一ソース。csproj の <c>Version</c>（SemVer）を表示・比較・GitHub タグ照合に共通利用する。
/// </summary>
internal static class AppVersion
{
    public const string GitHubOwner = "mga-ueda";
    public const string GitHubRepo = "MGA-Wwise-IMImporter";
    public const string RepositoryUrl = "https://github.com/" + GitHubOwner + "/" + GitHubRepo;
    public const string ReleasesApiUrl =
        "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases";

    private static readonly Lazy<string> CurrentLazy = new(ReadCurrent);

    /// <summary>表示・比較・ログ共通の版（例: <c>1.0.4-beta</c>）。</summary>
    public static string Current => CurrentLazy.Value;

    public static string FormTitle =>
        "MGA Wwise IMImporter - Version " + Current;

    /// <summary>
    /// <paramref name="remoteSemVer"/> がローカルより新しいとき true。
    /// パース不能なときは比較せず false。
    /// </summary>
    public static bool IsRemoteNewer(string? remoteSemVer) =>
        CompareSemVer(remoteSemVer, Current) > 0;

    /// <summary>
    /// SemVer 風文字列を比較する。left &gt; right なら正、等しいなら 0、left &lt; right なら負。
    /// パース不能な側は「より古い」扱い（比較不能時は 0）。
    /// </summary>
    public static int CompareSemVer(string? left, string? right)
    {
        var leftOk = TryParse(left, out var leftParsed);
        var rightOk = TryParse(right, out var rightParsed);
        if (!leftOk && !rightOk)
        {
            return 0;
        }

        if (!leftOk)
        {
            return -1;
        }

        if (!rightOk)
        {
            return 1;
        }

        return Compare(leftParsed, rightParsed);
    }

    /// <summary>タグや版文字列から先頭の <c>v</c> を除いた SemVer 風文字列を返す。</summary>
    public static string NormalizeTag(string? tagOrVersion)
    {
        var text = (tagOrVersion ?? string.Empty).Trim();
        if (text.Length >= 2
            && (text[0] is 'v' or 'V')
            && char.IsDigit(text[1]))
        {
            return text[1..];
        }

        return text;
    }

    private static string ReadCurrent()
    {
        foreach (var meta in Assembly.GetExecutingAssembly()
                     .GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (meta.Key.Equals("AppVersion", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(meta.Value))
            {
                return NormalizeTag(meta.Value);
            }
        }

        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?.Trim();
        if (!string.IsNullOrEmpty(informational))
        {
            var plus = informational.IndexOf('+');
            var value = plus >= 0 ? informational[..plus] : informational;
            var normalized = NormalizeTag(value);
            if (TryParse(normalized, out _))
            {
                return normalized;
            }
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
        if (assemblyVersion is not null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}");
        }

        return "0.0.0";
    }

    private static bool TryParse(string? text, out ParsedVersion parsed)
    {
        parsed = default;
        var normalized = NormalizeTag(text);
        if (normalized.Length == 0)
        {
            return false;
        }

        // 1.0.2 / 1.0.2-beta / 1.0.2-beta.2
        var match = Regex.Match(
            normalized,
            @"^(?<maj>\d+)\.(?<min>\d+)\.(?<pat>\d+)(?:-(?<pre>[0-9A-Za-z\.-]+))?$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return false;
        }

        parsed = new ParsedVersion(
            int.Parse(match.Groups["maj"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["pat"].Value, CultureInfo.InvariantCulture),
            match.Groups["pre"].Success ? match.Groups["pre"].Value : null);
        return true;
    }

    /// <summary>SemVer 風: 同じ数値なら、プレリリース無しが新しい。</summary>
    private static int Compare(ParsedVersion left, ParsedVersion right)
    {
        var core = left.Major.CompareTo(right.Major);
        if (core != 0)
        {
            return core;
        }

        core = left.Minor.CompareTo(right.Minor);
        if (core != 0)
        {
            return core;
        }

        core = left.Patch.CompareTo(right.Patch);
        if (core != 0)
        {
            return core;
        }

        var leftPre = left.Prerelease;
        var rightPre = right.Prerelease;
        if (leftPre is null && rightPre is null)
        {
            return 0;
        }

        if (leftPre is null)
        {
            return 1;
        }

        if (rightPre is null)
        {
            return -1;
        }

        return string.Compare(leftPre, rightPre, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ParsedVersion(
        int Major,
        int Minor,
        int Patch,
        string? Prerelease);
}
