using System;
using System.IO;
using Godot;
using HypeReborn.Hype.Runtime.Binary;

namespace HypeReborn.Hype.Runtime.Textures;

public static class HypeGfDecoder
{
    public static Image Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new HypeBinaryReader(stream);

        _ = reader.ReadByte(); // version
        var width = reader.ReadUInt32();
        var height = reader.ReadUInt32();

        var channelPixels = width * height;
        var channels = reader.ReadByte();
        var repeatByte = reader.ReadByte();

        var paletteNumColors = reader.ReadUInt16();
        var paletteBytesPerColor = reader.ReadByte();

        _ = reader.ReadByte();
        _ = reader.ReadByte();
        _ = reader.ReadByte();
        _ = reader.ReadUInt32();

        channelPixels = reader.ReadUInt32();
        var montrealType = reader.ReadByte();
        var format = montrealType switch
        {
            5 => 0,
            10 => 565,
            11 => 1555,
            12 => 4444,
            _ => throw new InvalidDataException($"Unsupported Montreal GF format type: {montrealType}")
        };

        byte[]? palette = null;
        if (paletteNumColors > 0 && paletteBytesPerColor > 0)
        {
            palette = reader.ReadBytes(paletteNumColors * paletteBytesPerColor);
        }

        var decodedChannels = ReadChannels(reader, channels, repeatByte, channelPixels);
        var pixelCount = checked((int)(width * height));

        var image = Image.CreateEmpty((int)width, (int)height, false, Image.Format.Rgba8);

        if (channels >= 3)
        {
            for (var i = 0; i < pixelCount; i++)
            {
                var offset = i * channels;
                var b = decodedChannels[offset + 0];
                var g = decodedChannels[offset + 1];
                var r = decodedChannels[offset + 2];
                var a = channels >= 4 ? decodedChannels[offset + 3] : (byte)255;

                image.SetPixel(i % (int)width, i / (int)width, Color.Color8(r, g, b, a));
            }

            return image;
        }

        if (channels == 2)
        {
            for (var i = 0; i < pixelCount; i++)
            {
                var offset = i * 2;
                var pixel = (ushort)(decodedChannels[offset] | (decodedChannels[offset + 1] << 8));
                var color = DecodeTwoChannelPixel(pixel, format);
                image.SetPixel(i % (int)width, i / (int)width, color);
            }

            return image;
        }

        if (channels == 1)
        {
            for (var i = 0; i < pixelCount; i++)
            {
                var value = decodedChannels[i];
                Color color;

                if (palette != null)
                {
                    var baseOffset = value * paletteBytesPerColor;
                    var a = paletteBytesPerColor >= 4 ? palette[baseOffset + 3] : (byte)255;
                    var r = palette[baseOffset + 2];
                    var g = palette[baseOffset + 1];
                    var b = palette[baseOffset + 0];
                    color = Color.Color8(r, g, b, a);
                }
                else
                {
                    color = Color.Color8(value, value, value, 255);
                }

                image.SetPixel(i % (int)width, i / (int)width, color);
            }

            return image;
        }

        throw new InvalidDataException($"Unsupported channel count in GF: {channels}");
    }

    private static byte[] ReadChannels(HypeBinaryReader reader, byte channels, byte repeatByte, uint channelPixels)
    {
        var data = new byte[channels * channelPixels];

        for (var channel = 0; channel < channels; channel++)
        {
            var pixel = 0u;
            while (pixel < channelPixels)
            {
                var value = reader.ReadByte();
                if (value == repeatByte)
                {
                    var repeatedValue = reader.ReadByte();
                    var count = reader.ReadByte();
                    for (var i = 0; i < count && pixel < channelPixels; i++)
                    {
                        data[channel + pixel * channels] = repeatedValue;
                        pixel++;
                    }
                }
                else
                {
                    data[channel + pixel * channels] = value;
                    pixel++;
                }
            }
        }

        return data;
    }

    private static Color DecodeTwoChannelPixel(ushort pixel, int format)
    {
        return format switch
        {
            4444 => Color.Color8(
                (byte)(ExtractBits(pixel, 4, 8) * 17),
                (byte)(ExtractBits(pixel, 4, 4) * 17),
                (byte)(ExtractBits(pixel, 4, 0) * 17),
                (byte)(ExtractBits(pixel, 4, 12) * 17)),
            1555 => Color.Color8(
                (byte)(ExtractBits(pixel, 5, 10) * 255 / 31),
                (byte)(ExtractBits(pixel, 5, 5) * 255 / 31),
                (byte)(ExtractBits(pixel, 5, 0) * 255 / 31),
                (byte)(ExtractBits(pixel, 1, 15) * 255)),
            _ => Color.Color8(
                (byte)(ExtractBits(pixel, 5, 11) * 255 / 31),
                (byte)(ExtractBits(pixel, 6, 5) * 255 / 63),
                (byte)(ExtractBits(pixel, 5, 0) * 255 / 31),
                255)
        };
    }

    private static uint ExtractBits(int number, int count, int offset)
    {
        return (uint)(((1 << count) - 1) & (number >> offset));
    }
}
