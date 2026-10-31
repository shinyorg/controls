using System.Diagnostics;
using Shiny.Controls.BarcodeReader;
using Shiny.Controls.BarcodeReader.Tests;
using SkiaSharp;
using ZXing;
using ZXing.Common;
using ZxFormat = ZXing.BarcodeFormat;

// Long-range engine vs ZXing.Net, same images, same machine.
//
//   synthetic: a sweep of module size (how far away), defocus and noise at random angles on a 1280x720
//              frame, the size a phone analyzes at. Each cell is N scenes; a read counts only if the
//              payload is exactly right, so a misread scores the same as a miss.
//   --images:  every .jpg/.png in a folder, expected payload taken from the file name up to "__"
//              (e.g. 5901234123457__aisle-3m.jpg). This is the number that matters; synthetic scenes
//              are clean in ways a real label is not.

var imagesArg = Array.IndexOf(args, "--images");
if (imagesArg >= 0 && imagesArg + 1 < args.Length)
{
    RunFolder(args[imagesArg + 1]);
    return;
}
RunSynthetic(args.Contains("--quick") ? 4 : 12);

static void RunSynthetic(int perCell)
{
    var cases = new (string Text, ZxFormat Format)[]
    {
        ("5901234123457", ZxFormat.EAN_13),
        ("SHINY-0042-LR", ZxFormat.CODE_128),
    };
    var modules = new[] { 2.5f, 2.0f, 1.6f, 1.3f, 1.1f, 1.0f };
    var blurs = new[] { 0f, 0.4f, 0.7f };

    Console.WriteLine($"Synthetic sweep, 1280x720, noise 4, contrast 120, random angle 0-180°, {perCell} scenes per cell");
    Console.WriteLine("read rate  engine / zxing   (frame time median ms engine / zxing)");
    var engineTimes = new List<double>();
    var zxingTimes = new List<double>();

    foreach (var (text, format) in cases)
    {
        Console.WriteLine();
        Console.WriteLine($"{format} \"{text}\"");
        Console.Write("module px ".PadRight(12));
        foreach (var b in blurs)
            Console.Write($"blur {b:0.0}".PadRight(22));
        Console.WriteLine();

        foreach (var m in modules)
        {
            Console.Write($"{m:0.0}".PadRight(12));
            foreach (var blur in blurs)
            {
                int e = 0, z = 0;
                for (var i = 0; i < perCell; i++)
                {
                    var rng = new Random(i * 7919 + (int)(m * 100) + (int)(blur * 1000));
                    var scene = SyntheticBarcode.Render(text, format, m,
                        angleDegrees: (float)(rng.NextDouble() * 180),
                        blur: blur, noise: 4, contrast: 120, width: 1280, height: 720, seed: i,
                        centerX: 640 + (float)(rng.NextDouble() - 0.5) * 300,
                        centerY: 360 + (float)(rng.NextDouble() - 0.5) * 160);

                    if (Engine(scene.Pixels, scene.Width, scene.Height, engineTimes).Contains(text)) e++;
                    if (ZXingRead(scene.Pixels, scene.Width, scene.Height, zxingTimes).Contains(text)) z++;
                }
                Console.Write($"{100 * e / perCell,3}% / {100 * z / perCell,3}%".PadRight(22));
            }
            Console.WriteLine();
        }
    }

    Console.WriteLine();
    Console.WriteLine($"frame time  engine median {Median(engineTimes):0.0} ms (p90 {P90(engineTimes):0.0})   zxing median {Median(zxingTimes):0.0} ms (p90 {P90(zxingTimes):0.0})");
}

static void RunFolder(string folder)
{
    var files = Directory.EnumerateFiles(folder).Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).OrderBy(f => f).ToList();
    if (files.Count == 0)
    {
        Console.WriteLine($"No images in {folder}");
        return;
    }

    int e = 0, z = 0;
    var et = new List<double>();
    var zt = new List<double>();
    foreach (var file in files)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var expected = name.Split("__")[0];
        using var bmp = SKBitmap.Decode(file);
        var (luma, w, h) = ToLuma(bmp);

        var er = Engine(luma, w, h, et);
        var zr = ZXingRead(luma, w, h, zt);
        var eo = er.Contains(expected);
        var zo = zr.Contains(expected);
        if (eo) e++;
        if (zo) z++;
        Console.WriteLine($"{(eo ? "OK " : "-- ")}{(zo ? "OK " : "-- ")} {Path.GetFileName(file)}  engine=[{string.Join(",", er)}] zxing=[{string.Join(",", zr)}]");
    }
    Console.WriteLine();
    Console.WriteLine($"engine {e}/{files.Count}  median {Median(et):0.0} ms    zxing {z}/{files.Count}  median {Median(zt):0.0} ms");
    Console.WriteLine("(engine = the managed 1D path only; on a device 2D codes and leftovers go to Vision / ML Kit on a crop)");
}

static (byte[] Luma, int W, int H) ToLuma(SKBitmap bmp)
{
    // analyze at 1080p at most, as the camera would deliver it
    var scale = Math.Min(1f, 1920f / Math.Max(bmp.Width, bmp.Height));
    var w = (int)(bmp.Width * scale);
    var h = (int)(bmp.Height * scale);
    using var resized = scale < 1 ? bmp.Resize(new SKImageInfo(w, h), new SKSamplingOptions(SKFilterMode.Linear)) : bmp.Copy();
    var luma = new byte[w * h];
    for (var y = 0; y < h; y++)
    for (var x = 0; x < w; x++)
    {
        var c = resized.GetPixel(x, y);
        luma[y * w + x] = (byte)((c.Red * 77 + c.Green * 150 + c.Blue * 29) >> 8);
    }
    return (luma, w, h);
}

static List<string> Engine(byte[] luma, int w, int h, List<double> times)
{
    var engine = Engines.Value.Value;
    var sw = Stopwatch.StartNew();
    var candidates = engine.Scan(new LumaImage(luma, w, h));
    sw.Stop();
    times.Add(sw.Elapsed.TotalMilliseconds);
    return candidates.Where(c => c.Read != null).Select(c => c.Read!.Text).ToList();
}

static List<string> ZXingRead(byte[] luma, int w, int h, List<double> times)
{
    var reader = new BarcodeReaderGeneric
    {
        AutoRotate = true,
        Options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = [ZxFormat.EAN_13, ZxFormat.UPC_A, ZxFormat.EAN_8, ZxFormat.UPC_E, ZxFormat.CODE_128, ZxFormat.CODE_39, ZxFormat.ITF]
        }
    };
    var sw = Stopwatch.StartNew();
    var results = reader.DecodeMultiple(new PlanarYUVLuminanceSource(luma, w, h, 0, 0, w, h, false));
    sw.Stop();
    times.Add(sw.Elapsed.TotalMilliseconds);
    // ZXing reports UPC-A inside EAN-13 with the leading zero; compare on both spellings
    return results?.SelectMany(r => new[] { r.Text, r.Text.Length == 13 && r.Text[0] == '0' ? r.Text[1..] : r.Text }).ToList() ?? [];
}

static double Median(List<double> v) => v.Count == 0 ? 0 : v.Order().ElementAt(v.Count / 2);
static double P90(List<double> v) => v.Count == 0 ? 0 : v.Order().ElementAt((int)(v.Count * 0.9));

static class Engines
{
    // one engine reused across frames, as the analyzer does
    public static readonly Lazy<BarcodeReaderEngine> Value = new(() => new BarcodeReaderEngine());
}
