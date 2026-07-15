using System.Globalization;
using MgaWwiseImporter.Nuendo;
using MgaWwiseImporter.Wave;

if (args.Length < 1)
{
    Console.Error.WriteLine("usage: RegionDiag <wavPath>");
    return 1;
}

var wavPath = Path.GetFullPath(args[0]);
var xmlPath = Path.ChangeExtension(wavPath, ".xml");

var wavInfo = WavFileInfo.Read(wavPath);
var tracklist = NuendoTracklistInfo.Read(xmlPath);
var overlay = WaveformBarOverlayBuilder.Build(tracklist, wavInfo);

Console.WriteLine($"Wave: {wavPath}");
Console.WriteLine($"  sr={wavInfo.SampleRate} frames={wavInfo.FrameCount} timeRef={wavInfo.TimeReferenceSamples} hasIXml={wavInfo.HasIXml}");
Console.WriteLine($"  waveStartPpq={overlay.WaveStartPpq.ToString("0.###", CultureInfo.InvariantCulture)} waveEndPpq={overlay.WaveEndPpq.ToString("0.###", CultureInfo.InvariantCulture)} anacrusis={overlay.HasAnacrusis}");

Console.WriteLine($"Cycles: {overlay.Cycles.Count}");
foreach (var c in overlay.Cycles)
{
    Console.WriteLine($"  [{c.StartSampleOffset,12:N0} .. {c.EndSampleOffset,12:N0}] \"{c.Comment}\"");
}

Console.WriteLine($"Regions: {overlay.Regions.Count}");
foreach (var r in overlay.Regions)
{
    Console.WriteLine($"  [{r.StartSampleOffset,12:N0} .. {r.EndSampleOffset,12:N0}] excluded={r.IsExcluded} suffix=\"{r.NameSuffix}\"");
}

Console.WriteLine($"OutputParts: {overlay.OutputParts.Count}");
foreach (var p in overlay.OutputParts)
{
    Console.WriteLine($"  #{p.Number} [{p.StartSampleOffset,12:N0} .. {p.EndSampleOffset,12:N0}] {p.FileName}");
}

Console.WriteLine($"IgnoredOutside: {overlay.IgnoredOutsideMarks.Count}");
foreach (var i in overlay.IgnoredOutsideMarks)
{
    Console.WriteLine($"  {i.Kind} \"{i.Name}\" ppq=[{i.StartPpq.ToString("0.###", CultureInfo.InvariantCulture)} .. {i.EndPpq.ToString("0.###", CultureInfo.InvariantCulture)}] {i.Reason}");
}

return 0;
