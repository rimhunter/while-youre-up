using System;
using System.IO;
using System.IO.Compression;

namespace WyuTestRunner
{
    public sealed class Png
    {
        public int Width { get; }
        public int Height { get; }
        readonly byte[] rgba;

        Png(int width, int height, byte[] rgba) {
            Width  = width;
            Height = height;
            this.rgba = rgba;
        }

        public (byte r, byte g, byte b) Pixel(int x, int y) {
            var i = (y * Width + x) * 4;
            return (rgba[i], rgba[i + 1], rgba[i + 2]);
        }

        static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static bool IsComplete(byte[] data) {
            if (data.Length < Signature.Length + 12) return false;
            for (var i = 0; i < Signature.Length; i++)
                if (data[i] != Signature[i]) return false;
            var end = data.Length - 8;
            return data[end] == (byte)'I' && data[end + 1] == (byte)'E' && data[end + 2] == (byte)'N' && data[end + 3] == (byte)'D';
        }

        static int BigEndian(byte[] d, int at) => (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3];

        public static Png Decode(byte[] data) {
            if (!IsComplete(data)) throw new InvalidDataException("not a complete PNG file");
            int width = 0, height = 0, colorType = -1;
            var idat = new MemoryStream();
            var pos = Signature.Length;
            while (pos < data.Length) {
                var length = BigEndian(data, pos);
                var type   = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
                var body   = pos + 8;
                if (type == "IHDR") {
                    width  = BigEndian(data, body);
                    height = BigEndian(data, body + 4);
                    var bitDepth = data[body + 8];
                    colorType    = data[body + 9];
                    var interlace = data[body + 12];
                    if (bitDepth != 8 || (colorType != 2 && colorType != 6) || interlace != 0)
                        throw new InvalidDataException($"unsupported PNG: bit depth {bitDepth}, color type {colorType}, interlace {interlace}");
                }
                else if (type == "IDAT") idat.Write(data, body, length);
                else if (type == "IEND") break;
                pos = body + length + 4;
            }
            var channels = colorType == 6 ? 4 : 3;
            var stride   = width * channels;
            var raw      = new byte[(stride + 1) * height];
            idat.Position = 2;
            using (var inflate = new DeflateStream(idat, CompressionMode.Decompress)) {
                var read = 0;
                while (read < raw.Length) {
                    var n = inflate.Read(raw, read, raw.Length - read);
                    if (n == 0) throw new InvalidDataException("PNG image data ended early");
                    read += n;
                }
            }
            var pixels = new byte[width * height * 4];
            var prev   = new byte[stride];
            var cur    = new byte[stride];
            for (var y = 0; y < height; y++) {
                var filter = raw[y * (stride + 1)];
                Buffer.BlockCopy(raw, y * (stride + 1) + 1, cur, 0, stride);
                for (var i = 0; i < stride; i++) {
                    int a = i >= channels ? cur[i - channels] : 0, b = prev[i], c = i >= channels ? prev[i - channels] : 0;
                    cur[i] = (byte)(cur[i] + filter switch {
                        0 => 0,
                        1 => a,
                        2 => b,
                        3 => (a + b) / 2,
                        4 => Paeth(a, b, c),
                        _ => throw new InvalidDataException("unknown PNG filter " + filter),
                    });
                }
                for (var x = 0; x < width; x++) {
                    var o = (y * width + x) * 4;
                    pixels[o]     = cur[x * channels];
                    pixels[o + 1] = cur[x * channels + 1];
                    pixels[o + 2] = cur[x * channels + 2];
                    pixels[o + 3] = channels == 4 ? cur[x * channels + 3] : (byte)255;
                }
                (prev, cur) = (cur, prev);
            }
            return new Png(width, height, pixels);
        }

        static int Paeth(int a, int b, int c) {
            int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }
}
