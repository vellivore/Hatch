using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

int[] sizes = [256, 128, 64, 48, 32, 16];
var pngFiles = new List<(int size, string path)>();

foreach (var sz in sizes)
{
    int renderSize = sz * 4;
    var visual = new DrawingVisual();
    using (var dc = visual.RenderOpen())
    {
        dc.PushTransform(new ScaleTransform(4, 4));
        DrawIcon(dc, sz);
        dc.Pop();
    }

    var rtb = new RenderTargetBitmap(renderSize, renderSize, 96, 96, PixelFormats.Pbgra32);
    rtb.Render(visual);

    var scaled = new TransformedBitmap(rtb, new ScaleTransform(0.25, 0.25));
    var final2 = new FormatConvertedBitmap(scaled, PixelFormats.Pbgra32, null, 0);
    var wb = new WriteableBitmap(final2);
    rtb = new RenderTargetBitmap(sz, sz, 96, 96, PixelFormats.Pbgra32);
    var drawVisual = new DrawingVisual();
    using (var dc2 = drawVisual.RenderOpen())
    {
        dc2.DrawImage(wb, new Rect(0, 0, sz, sz));
    }
    rtb.Render(drawVisual);

    var pngPath = Path.Combine("output", $"icon_{sz}.png");
    Directory.CreateDirectory("output");
    using var fs = new FileStream(pngPath, FileMode.Create);
    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(rtb));
    encoder.Save(fs);
    pngFiles.Add((sz, pngPath));
    Console.WriteLine($"Generated {sz}x{sz}");
}

CreateIco(pngFiles, Path.Combine("output", "app.ico"));
Console.WriteLine("ICO created: output/app.ico");

static void DrawIcon(DrawingContext dc, double sz)
{
    double m = sz * 0.06;
    double r = sz * 0.16;

    // Background rounded rect (white/light gray)
    var bgRect = new Rect(m, m, sz - m * 2, sz - m * 2);
    var bgBrush = new LinearGradientBrush(
        Color.FromRgb(0xF8, 0xF8, 0xF8),
        Color.FromRgb(0xE8, 0xE8, 0xE8),
        90);
    var bgPen = new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0)), sz * 0.008);
    dc.DrawRoundedRectangle(bgBrush, bgPen, bgRect, r, r);

    double cx = sz * 0.5;
    double cy = sz * 0.5;

    // Connection lines (draw before nodes so nodes overlap)
    var linePen = new Pen(new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)), sz * 0.016);
    linePen.StartLineCap = PenLineCap.Round;
    linePen.EndLineCap = PenLineCap.Round;

    // Top node position
    double topX = cx, topY = sz * 0.24;
    // Bottom-left node position
    double blX = sz * 0.26, blY = sz * 0.72;
    // Bottom-right node position
    double brX = sz * 0.74, brY = sz * 0.72;

    // Lines from center to nodes
    dc.DrawLine(linePen, new Point(cx, cy - sz * 0.06), new Point(topX, topY + sz * 0.05));
    dc.DrawLine(linePen, new Point(cx - sz * 0.05, cy + sz * 0.05), new Point(blX + sz * 0.04, blY - sz * 0.04));
    dc.DrawLine(linePen, new Point(cx + sz * 0.05, cy + sz * 0.05), new Point(brX - sz * 0.04, brY - sz * 0.04));

    // Center node (larger, red accent)
    double centerR = sz * 0.10;
    var centerBrush = new SolidColorBrush(Color.FromRgb(0xD0, 0x40, 0x40));
    dc.DrawEllipse(centerBrush, null, new Point(cx, cy), centerR, centerR);

    // Outer nodes (smaller, lighter)
    double nodeR = sz * 0.06;
    var nodeBrush = new SolidColorBrush(Color.FromRgb(0x85, 0x85, 0x85));
    dc.DrawEllipse(nodeBrush, null, new Point(topX, topY), nodeR, nodeR);
    dc.DrawEllipse(nodeBrush, null, new Point(blX, blY), nodeR, nodeR);
    dc.DrawEllipse(nodeBrush, null, new Point(brX, brY), nodeR, nodeR);

    // Rotation arrow on center node (switch indicator)
    double arrowR = sz * 0.065;
    var arrowPen = new Pen(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), sz * 0.014);
    arrowPen.StartLineCap = PenLineCap.Round;
    arrowPen.EndLineCap = PenLineCap.Round;

    // Arc
    var arcGeometry = new PathGeometry();
    var arcFigure = new PathFigure { StartPoint = new Point(cx + arrowR, cy - sz * 0.01), IsClosed = false };
    arcFigure.Segments.Add(new ArcSegment(
        new Point(cx - sz * 0.01, cy - arrowR),
        new Size(arrowR, arrowR), 0, false, SweepDirection.Counterclockwise, true));
    arcGeometry.Figures.Add(arcFigure);
    dc.DrawGeometry(null, arrowPen, arcGeometry);

    // Arrow head
    double ahx = cx - sz * 0.01, ahy = cy - arrowR;
    var arrowHead = new PathGeometry();
    var ahFigure = new PathFigure { StartPoint = new Point(ahx - sz * 0.03, ahy + sz * 0.005), IsClosed = true };
    ahFigure.Segments.Add(new LineSegment(new Point(ahx + sz * 0.005, ahy - sz * 0.028), true));
    ahFigure.Segments.Add(new LineSegment(new Point(ahx + sz * 0.01, ahy + sz * 0.025), true));
    arrowHead.Figures.Add(ahFigure);
    dc.DrawGeometry(new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40)), null, arrowHead);
}

static void CreateIco(List<(int size, string path)> pngs, string outPath)
{
    using var ms = new MemoryStream();
    using var bw = new BinaryWriter(ms);

    bw.Write((short)0);
    bw.Write((short)1);
    bw.Write((short)pngs.Count);

    int headerSize = 6 + pngs.Count * 16;
    var pngDatas = new List<byte[]>();

    foreach (var (size, path) in pngs)
        pngDatas.Add(File.ReadAllBytes(path));

    int offset = headerSize;
    for (int i = 0; i < pngs.Count; i++)
    {
        var sz = pngs[i].size;
        var data = pngDatas[i];
        bw.Write((byte)(sz >= 256 ? 0 : sz));
        bw.Write((byte)(sz >= 256 ? 0 : sz));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((short)1);
        bw.Write((short)32);
        bw.Write(data.Length);
        bw.Write(offset);
        offset += data.Length;
    }

    foreach (var data in pngDatas)
        bw.Write(data);

    File.WriteAllBytes(outPath, ms.ToArray());
}
