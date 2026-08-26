#!/usr/bin/env dotnet
// Build the Windows .ico from a macOS .iconset directory.
//
// Usage: dotnet run scripts/make-ico.cs -- <iconset-dir> <out.ico>

#:package SixLabors.ImageSharp@3.1.*

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Buffers.Binary;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: make-ico.cs <iconset-dir> <out.ico>");
    return 1;
}

var iconset = args[0];
var output = args[1];

// Sizes Windows asks for. Apple renders 16, 32, 128 and 256 itself; the rest
// are downsampled from 256 rather than re-rendered, which is what the icon
// pipeline did before and keeps the artwork identical.
int[] sizes = [16, 24, 32, 48, 64, 128, 256];

var rendered = new Dictionary<int, string>
{
    [16] = "icon_16x16.png",
    [32] = "icon_16x16@2x.png",
    [128] = "icon_128x128.png",
    [256] = "icon_128x128@2x.png",
};

var basePath = Path.Combine(iconset, rendered[256]);
if (!File.Exists(basePath))
{
    Console.Error.WriteLine($"error: {basePath} not found — was the iconset generated?");
    return 1;
}

using var baseImage = Image.Load(basePath);

// Each entry's payload is a complete PNG. Windows reads PNG-in-ICO at every
// size, and it keeps the file far smaller than the equivalent 32-bit BMPs.
var payloads = new List<(int Size, byte[] Png)>();
foreach (var size in sizes)
{
    using var frame = LoadFrame(size);
    using var buffer = new MemoryStream();
    frame.SaveAsPng(buffer);
    payloads.Add((size, buffer.ToArray()));
}

Image LoadFrame(int size)
{
    if (rendered.TryGetValue(size, out var name))
    {
        var path = Path.Combine(iconset, name);
        if (File.Exists(path)) return Image.Load(path);
    }
    var resized = baseImage.Clone(ctx => ctx.Resize(new ResizeOptions
    {
        Size = new Size(size, size),
        Sampler = KnownResamplers.Lanczos3,
        Mode = ResizeMode.Stretch,
    }));
    return resized;
}

// ── ICO container ────────────────────────────────────────────────────────────
// ICONDIR (6 bytes), then one 16-byte ICONDIRENTRY per image, then the
// payloads. A dimension of 256 is stored as 0, the field being one byte.
const int DirHeader = 6;
const int DirEntry = 16;

using var ico = new MemoryStream();
var header = new byte[DirHeader];
BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0), 0); // reserved
BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2), 1); // 1 = icon
BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), (ushort)payloads.Count);
ico.Write(header);

var offset = DirHeader + DirEntry * payloads.Count;
foreach (var (size, png) in payloads)
{
    var entry = new byte[DirEntry];
    entry[0] = (byte)(size == 256 ? 0 : size); // width
    entry[1] = (byte)(size == 256 ? 0 : size); // height
    entry[2] = 0;                              // palette entries; 0 = truecolour
    entry[3] = 0;                              // reserved
    // Planes and bit count are ignored for a PNG payload — the decoder reads
    // them from the PNG header — so they carry the same values the previous
    // pipeline wrote.
    BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(4), 0);
    BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(6), 32);
    BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(8), png.Length);
    BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(12), offset);
    ico.Write(entry);
    offset += png.Length;
}

foreach (var (_, png) in payloads)
{
    ico.Write(png);
}

File.WriteAllBytes(output, ico.ToArray());
Console.WriteLine($"wrote {output} ({string.Join(", ", payloads.Select(p => $"{p.Size}x{p.Size}"))})");
return 0;
