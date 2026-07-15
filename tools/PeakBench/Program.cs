using System.Diagnostics;
using MgaWwiseImporter.Wave;

if (args.Length < 1)
{
    Console.WriteLine("usage: PeakBench <wav>");
    return;
}

var info = WavFileInfo.Read(args[0]);
Console.WriteLine($"frames={info.FrameCount:N0} ch={info.Channels} bits={info.BitsPerSample} rate={info.SampleRate}");

var sw = Stopwatch.StartNew();
var overview = WavPeakReader.Read(info, peakCount: 2400);
sw.Stop();
Console.WriteLine($"overview Read(2400)          : {sw.ElapsedMilliseconds} ms  buckets={overview.Mins.Length}");

sw.Restart();
var pyramid = WavPeakPyramid.Build(info);
sw.Stop();
Console.WriteLine($"pyramid Build (full scan)    : {sw.ElapsedMilliseconds} ms  baseBucketFrames={pyramid.BaseBucketFrames}");

// ズーム操作の代表ケース: 表示窓を狭めながら pyramid.ReadRange を回す
sw.Restart();
var iterations = 0;
for (var zoom = 1.0; zoom <= 8192.0; zoom *= 1.09050773267)
{
    var span = 1.0 / zoom;
    var start = (0.5 - span / 2) * pyramid.FrameCount;
    var s = (long)Math.Max(0, start);
    var e = (long)Math.Min(pyramid.FrameCount, start + span * pyramid.FrameCount);
    _ = pyramid.ReadRange(s, e, 1600);
    iterations++;
}

sw.Stop();
Console.WriteLine($"pyramid ReadRange x{iterations,3}       : {sw.ElapsedMilliseconds} ms total ({sw.Elapsed.TotalMilliseconds / iterations:0.00} ms/step)");

// 深いズームで生サンプル読みにフォールバックするケース
var deepSpanFrames = (long)(1600L * pyramid.BaseBucketFrames * 0.5);
var deepStart = pyramid.FrameCount / 2;
sw.Restart();
var raw = WavPeakReader.ReadRange(info, deepStart, deepStart + deepSpanFrames, 1600);
sw.Stop();
Console.WriteLine($"raw ReadRange (deep zoom)    : {sw.ElapsedMilliseconds} ms  buckets={raw.Mins.Length}  frames={deepSpanFrames:N0}");

// 検証: pyramid と raw の全体概要が概ね一致するか
var check = pyramid.ReadRange(0, pyramid.FrameCount, 2400);
double maxDiff = 0;
for (var i = 0; i < check.Mins.Length; i++)
{
    maxDiff = Math.Max(maxDiff, Math.Abs(check.Maxs[i] - overview.Maxs[i]));
}

Console.WriteLine($"overview vs pyramid maxDiff  : {maxDiff:0.0000} (サンプリング近似なので多少の差は正常)");
