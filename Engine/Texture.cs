using Brutal.Numerics;
using Brutal.OpenGlApi;
using Brutal.StbApi;
using static Brutal.OpenGlApi.OpenGl;

namespace BrutalFps.Engine;

/// <summary>What a texture holds, which decides its GPU format.</summary>
public enum TextureKind
{
    /// <summary>sRGB colour (+ alpha).</summary>
    Color,

    /// <summary>Tangent-space normal map: only X and Y are kept (Z is rebuilt in the shader).</summary>
    Normal,

    /// <summary>Linear data, e.g. occlusion / roughness / metalness.</summary>
    Data,
}

/// <summary>
/// GPU textures loaded with BRUTAL's stb bindings (<see cref="Stb"/>): 2D textures with a full CPU-built mip chain,
/// anisotropic filtering and (when the driver supports it) block compression, plus HDR cubemaps for the skies.
/// </summary>
public static unsafe class Texture
{
    // Block-compressed formats (EXT_texture_compression_s3tc / EXT_texture_sRGB, supported by every desktop GPU).
    // The driver compresses on upload, which keeps 2K PBR sets at a quarter of their uncompressed memory.
    private const uint CompressedSrgbAlphaDxt5 = 0x8C4F;
    private const uint CompressedRgbaDxt5 = 0x83F3;
    private const uint CompressedRgRgtc2 = 0x8DBD;

    /// <summary>Set to false (BRUTALFPS_NOCOMPRESS=1) to upload uncompressed textures.</summary>
    public static bool UseCompression = Environment.GetEnvironmentVariable("BRUTALFPS_NOCOMPRESS") != "1";

    private static float _maxAnisotropy = -1;

    public static long BytesUploaded { get; private set; }

    /// <summary>A decoded image with its whole mip chain, ready to upload (built off the GL thread).</summary>
    public sealed class Prepared(string path, TextureKind kind, List<(byte[] Pixels, int Width, int Height)> levels)
    {
        public string Path { get; } = path;
        public TextureKind Kind { get; } = kind;
        public List<(byte[] Pixels, int Width, int Height)> Levels { get; } = levels;
    }

    /// <summary>Loads an image file into a mipmapped, repeating 2D texture.</summary>
    public static GlTexture Load2D(string path, TextureKind kind) => Upload(Prepare(path, kind));

    /// <summary>Decodes an image and builds its mip chain. Thread-safe: no GL calls.</summary>
    public static Prepared Prepare(string path, TextureKind kind)
    {
        var image = Stb.Load(path, 4);
        byte[] pixels;
        int width, height;
        try
        {
            if (image.IsNull())
            {
                Stb.TryGetFailureReason(out var reason);
                throw new IOException($"Couldn't load {path}: {reason}");
            }

            width = image.Width;
            height = image.Height;
            pixels = new byte[width * height * 4];
            image.AsSpan().Slice(0, pixels.Length).CopyTo(pixels);
        }
        finally
        {
            image.Dispose();
        }

        FlipRows(pixels, width, height, 4); // stb gives the top row first; GL (and Blender's UVs) start at the bottom

        // Normal maps keep just X and Y.
        var channels = kind == TextureKind.Normal ? 2 : 4;
        var level = kind == TextureKind.Normal ? Pack2(pixels, width, height) : pixels;
        var levels = new List<(byte[], int, int)>();
        var w = width;
        var h = height;
        while (true)
        {
            levels.Add((level, w, h));
            if (w == 1 && h == 1)
                break;
            level = kind == TextureKind.Color
                ? DownsampleSrgb(level, w, h, channels, out var nw, out var nh)
                : Downsample(level, w, h, channels, out nw, out nh, renormalize: kind == TextureKind.Normal);
            w = nw;
            h = nh;
        }

        return new Prepared(path, kind, levels);
    }

    /// <summary>Uploads a prepared image (GL thread).</summary>
    public static GlTexture Upload(Prepared image)
    {
        var kind = image.Kind;
        Span<GlTexture> ids = stackalloc GlTexture[1];
        GenTextures(ids);
        var texture = ids[0];
        BindTexture(GL_TEXTURE_2D, texture.Id);
        PixelStorei(GL_UNPACK_ALIGNMENT, 1);
        // Clear any error left by earlier code, so the check after the first compressed upload only sees its own.
        for (var i = 0; i < 16 && GetError().Id != 0; i++) { }

        var (internalFormat, compressed) = kind switch
        {
            TextureKind.Color => UseCompression ? (CompressedSrgbAlphaDxt5, true) : (GL_SRGB8_ALPHA8, false),
            TextureKind.Normal => UseCompression ? (CompressedRgRgtc2, true) : (GL_RG8, false),
            _ => UseCompression ? (CompressedRgbaDxt5, true) : (GL_RGBA8, false),
        };
        var format = kind == TextureKind.Normal ? GL_RG : GL_RGBA;

        for (var mip = 0; mip < image.Levels.Count; mip++)
        {
            var (pixels, w, h) = image.Levels[mip];
            fixed (byte* p = pixels)
                TexImage2D(GL_TEXTURE_2D, mip, (int)internalFormat, w, h, 0, format, GL_UNSIGNED_BYTE, p);
            BytesUploaded += compressed ? (long)w * h : pixels.LongLength;

            if (mip == 0 && compressed && GetError().Id != 0)
            {
                // The driver refused the compressed format: fall back to plain textures from here on.
                Log.Write("[WARN] Block-compressed textures aren't supported here; using uncompressed textures.");
                UseCompression = false;
                DeleteTextures([texture]);
                return Upload(image);
            }
        }

        SetSampling(image.Levels.Count);
        if (compressed)
            WriteCache(image.Path, kind, internalFormat, image.Levels.Count);
        return texture;
    }

    // ------------------------------------------------------------------------------------ compressed-texture cache
    // Compressing 2K textures in the driver takes a few seconds per run, so the compressed mip chains are read back
    // once and kept in TextureCache next to the exe. A cache file is used only while its source PNG is unchanged.

    private const uint TextureCompressedImageSize = 0x86A0; // GL_TEXTURE_COMPRESSED_IMAGE_SIZE
    private const uint CacheMagic = 0x31544342;             // "BCT1"

    public static string CacheDirectory { get; set; } = System.IO.Path.Combine(AppContext.BaseDirectory, "TextureCache");

    private static string CachePath(string source, TextureKind kind) =>
        System.IO.Path.Combine(CacheDirectory, System.IO.Path.GetFileNameWithoutExtension(source) + "." + FolderHash(source) + "." +
                                               kind.ToString().ToLowerInvariant() + ".bct");

    /// <summary>A stable (FNV-1a) hash of the source's folder: same-named textures in different folders get their own cache file.</summary>
    private static string FolderHash(string source)
    {
        var folder = System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(source) ?? "").ToLowerInvariant();
        var hash = 2166136261u;
        foreach (var c in folder)
            hash = (hash ^ c) * 16777619u;
        return hash.ToString("x8");
    }

    /// <summary>A cached, already-compressed texture read from disk (thread-safe; no GL calls).</summary>
    public sealed record Cached(uint Format, List<(byte[] Data, int Width, int Height)> Levels);

    /// <summary>Reads the cache for <paramref name="source"/> if it's there and still matches the PNG.</summary>
    public static Cached? TryReadCache(string source, TextureKind kind)
    {
        if (!UseCompression)
            return null;
        var path = CachePath(source, kind);
        if (!File.Exists(path))
            return null;
        try
        {
            var info = new FileInfo(source);
            using var reader = new BinaryReader(File.OpenRead(path));
            if (reader.ReadUInt32() != CacheMagic)
                return null;
            var format = reader.ReadUInt32();
            if (reader.ReadInt64() != info.LastWriteTimeUtc.Ticks || reader.ReadInt64() != info.Length)
                return null;
            var count = reader.ReadInt32();
            if (count > 20)
                return null;   // corrupt (a 2K texture has 12 levels)
            var levels = new List<(byte[], int, int)>(count);
            for (var i = 0; i < count; i++)
            {
                var w = reader.ReadInt32();
                var h = reader.ReadInt32();
                var size = reader.ReadInt32();
                if (w <= 0 || h <= 0 || size <= 0)
                    return null;   // corrupt
                var data = reader.ReadBytes(size);
                if (data.Length != size)
                    return null;   // truncated
                levels.Add((data, w, h));
            }
            if (count <= 0)
                return null;

            return new Cached(format, levels);
        }
        catch (Exception e) when (e is IOException or EndOfStreamException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Uploads a cached compressed texture (GL thread).</summary>
    public static GlTexture UploadCached(Cached cached)
    {
        Span<GlTexture> ids = stackalloc GlTexture[1];
        GenTextures(ids);
        var texture = ids[0];
        BindTexture(GL_TEXTURE_2D, texture.Id);
        for (var mip = 0; mip < cached.Levels.Count; mip++)
        {
            var (data, w, h) = cached.Levels[mip];
            fixed (byte* p = data)
                CompressedTexImage2D(GL_TEXTURE_2D, mip, cached.Format, w, h, 0, data.Length, p);
            BytesUploaded += data.Length;
        }

        SetSampling(cached.Levels.Count);
        return texture;
    }

    /// <summary>Reads back the compressed mip chain of the bound texture and saves it for next time.</summary>
    private static void WriteCache(string source, TextureKind kind, uint format, int levels)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var info = new FileInfo(source);
            // Write to a temporary file and move it into place when complete, so a crash part-way through can't
            // leave a truncated cache file that would be read back on every later run.
            var final = CachePath(source, kind);
            var temp = final + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temp)))
                WriteCacheBody(writer, info, format, levels);
            File.Move(temp, final, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Write($"[WARN] Couldn't write the texture cache ({e.Message}).");
        }
    }

    private static void WriteCacheBody(BinaryWriter writer, FileInfo info, uint format, int levels)
    {
        writer.Write(CacheMagic);
        writer.Write(format);
        writer.Write(info.LastWriteTimeUtc.Ticks);
        writer.Write(info.Length);
        writer.Write(levels);
        Span<int> value = stackalloc int[1];
        for (var mip = 0; mip < levels; mip++)
        {
            GetTexLevelParameteriv(GL_TEXTURE_2D, mip, GL_TEXTURE_WIDTH, value);
            var w = value[0];
            GetTexLevelParameteriv(GL_TEXTURE_2D, mip, GL_TEXTURE_HEIGHT, value);
            var h = value[0];
            GetTexLevelParameteriv(GL_TEXTURE_2D, mip, TextureCompressedImageSize, value);
            var data = new byte[value[0]];
            fixed (byte* p = data)
                GetCompressedTexImage(GL_TEXTURE_2D, mip, p);
            writer.Write(w);
            writer.Write(h);
            writer.Write(data.Length);
            writer.Write(data);
        }
    }

    private static void SetSampling(int levels)
    {
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MAX_LEVEL, levels - 1);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, (int)GL_REPEAT);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, (int)GL_REPEAT);
        TexParameterFloat(GL_TEXTURE_2D, GL_TEXTURE_MAX_ANISOTROPY, MaxAnisotropy);
    }

    /// <summary>
    /// Loads six HDR faces (<c>{prefix}_px.hdr</c> ... <c>{prefix}_nz.hdr</c>, in OpenGL cubemap layout) into a
    /// mipmapped RGB16F cubemap. Returns the texture and the number of mip levels.
    /// </summary>
    public static (GlTexture Texture, int Levels) LoadCubemapHdr(string prefix)
    {
        ReadOnlySpan<string> faces = ["px", "nx", "py", "ny", "pz", "nz"];
        Span<GlTexture> ids = stackalloc GlTexture[1];
        GenTextures(ids);
        var texture = ids[0];
        BindTexture(GL_TEXTURE_CUBE_MAP, texture.Id);
        PixelStorei(GL_UNPACK_ALIGNMENT, 1);

        var size = 0;
        for (var i = 0; i < faces.Length; i++)
        {
            var path = $"{prefix}_{faces[i]}.hdr";
            using var image = Stb.LoadF(path, 3);
            if (image.IsNull())
            {
                Stb.TryGetFailureReason(out var reason);
                throw new IOException($"Couldn't load {path}: {reason}");
            }

            size = image.Width;
            var data = image.AsSpan().Slice(0, image.Width * image.Height * 3);
            fixed (float* p = data)
                TexImage2D(GL_TEXTURE_CUBE_MAP_POSITIVE_X + (uint)i, 0, (int)GL_RGB16F, image.Width, image.Height, 0, GL_RGB, GL_FLOAT, p);
        }

        GenerateMipmap(GL_TEXTURE_CUBE_MAP);
        TexParameterFloat(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_MIN_FILTER, (int)GL_LINEAR_MIPMAP_LINEAR);
        TexParameterFloat(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_MAG_FILTER, (int)GL_LINEAR);
        TexParameterFloat(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_WRAP_S, (int)GL_CLAMP_TO_EDGE);
        TexParameterFloat(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_WRAP_T, (int)GL_CLAMP_TO_EDGE);
        TexParameterFloat(GL_TEXTURE_CUBE_MAP, GL_TEXTURE_WRAP_R, (int)GL_CLAMP_TO_EDGE);
        BytesUploaded += (long)size * size * 6 * 6 * 4 / 3;
        return (texture, (int)float.Log2(size) + 1);
    }

    private static float MaxAnisotropy
    {
        get
        {
            if (_maxAnisotropy < 0)
            {
                Span<float> value = stackalloc float[1];
                GetFloatv(GL_MAX_TEXTURE_MAX_ANISOTROPY, value);
                _maxAnisotropy = float.Clamp(value[0], 1f, 16f);
                GetError(); // ignore if the query isn't supported
            }

            return _maxAnisotropy;
        }
    }

    private static void FlipRows(byte[] pixels, int width, int height, int channels)
    {
        var stride = width * channels;
        var row = new byte[stride];
        for (var y = 0; y < height / 2; y++)
        {
            var a = pixels.AsSpan(y * stride, stride);
            var b = pixels.AsSpan((height - 1 - y) * stride, stride);
            a.CopyTo(row);
            b.CopyTo(a);
            row.CopyTo(b);
        }
    }

    private static byte[] Pack2(byte[] rgba, int width, int height)
    {
        var rg = new byte[width * height * 2];
        for (int i = 0, j = 0; i < rg.Length; i += 2, j += 4)
        {
            rg[i] = rgba[j];
            rg[i + 1] = rgba[j + 1];
        }

        return rg;
    }

    // Box-filtered mip levels. Colour is averaged in linear light (so mips don't darken), alpha linearly.
    private static readonly float[] SrgbToLinear = Enumerable.Range(0, 256)
        .Select(i => i / 255f <= 0.04045f ? i / 255f / 12.92f : MathF.Pow((i / 255f + 0.055f) / 1.055f, 2.4f)).ToArray();

    private static byte LinearToSrgb(float v)
    {
        v = float.Clamp(v, 0f, 1f);
        var s = v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;
        return (byte)(s * 255f + 0.5f);
    }

    private static byte[] DownsampleSrgb(byte[] src, int w, int h, int channels, out int nw, out int nh)
    {
        nw = int.Max(1, w / 2);
        nh = int.Max(1, h / 2);
        var dst = new byte[nw * nh * channels];
        var width = nw;
        Parallel.For(0, nh, y =>
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    var x0 = int.Min(x * 2, w - 1); var x1 = int.Min(x * 2 + 1, w - 1);
                    var y0 = int.Min(y * 2, h - 1); var y1 = int.Min(y * 2 + 1, h - 1);
                    int A(int xx, int yy) => src[(yy * w + xx) * channels + c];
                    if (c == 3)
                        dst[(y * width + x) * channels + c] = (byte)((A(x0, y0) + A(x1, y0) + A(x0, y1) + A(x1, y1) + 2) / 4);
                    else
                        dst[(y * width + x) * channels + c] = LinearToSrgb(
                            (SrgbToLinear[A(x0, y0)] + SrgbToLinear[A(x1, y0)] + SrgbToLinear[A(x0, y1)] + SrgbToLinear[A(x1, y1)]) * 0.25f);
                }
            }
        });
        return dst;
    }

    private static byte[] Downsample(byte[] src, int w, int h, int channels, out int nw, out int nh, bool renormalize)
    {
        nw = int.Max(1, w / 2);
        nh = int.Max(1, h / 2);
        var dst = new byte[nw * nh * channels];
        var width = nw;
        Parallel.For(0, nh, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var x0 = int.Min(x * 2, w - 1); var x1 = int.Min(x * 2 + 1, w - 1);
                var y0 = int.Min(y * 2, h - 1); var y1 = int.Min(y * 2 + 1, h - 1);
                var sum = new float4(0);
                for (var c = 0; c < channels; c++)
                    sum[c] = (src[(y0 * w + x0) * channels + c] + src[(y0 * w + x1) * channels + c]
                            + src[(y1 * w + x0) * channels + c] + src[(y1 * w + x1) * channels + c]) * 0.25f;

                if (renormalize)
                {
                    // Average the XY of the normals and push the result back towards unit length (keeps mips crisp).
                    var nx = sum[0] / 127.5f - 1f;
                    var ny = sum[1] / 127.5f - 1f;
                    var len2 = nx * nx + ny * ny;
                    if (len2 > 1f)
                    {
                        var inv = 1f / MathF.Sqrt(len2);
                        nx *= inv;
                        ny *= inv;
                    }

                    sum[0] = (nx + 1f) * 127.5f;
                    sum[1] = (ny + 1f) * 127.5f;
                }

                for (var c = 0; c < channels; c++)
                    dst[(y * width + x) * channels + c] = (byte)float.Clamp(sum[c] + 0.5f, 0f, 255f);
            }
        });
        return dst;
    }
}
