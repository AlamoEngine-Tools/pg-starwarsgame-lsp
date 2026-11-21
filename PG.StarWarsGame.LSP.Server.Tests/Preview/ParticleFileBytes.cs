// Copyright (c) Alamo Engine Tools and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for details.

using System.Text;

namespace PG.StarWarsGame.LSP.Server.Tests.Preview;

/// <summary>The smallest particle files that parse, for the tests that need one as bytes.</summary>
internal static class ParticleFileBytes
{
    public static byte[] ParticleSystem(params byte[][] emitters)
    {
        return ParticleSystemWith([], emitters);
    }

    /// <param name="extra">More chunks at the system's own level, after the emitter list.</param>
    /// <param name="emitters">The emitters, as <see cref="Emitter" /> builds them.</param>
    public static byte[] ParticleSystemWith(byte[] extra, params byte[][] emitters)
    {
        return Chunk(0x0900, true, Concat(
            Chunk(0x0000, false, Str("p_test")),
            Chunk(0x0001, false, I32(0)),
            Chunk(0x0800, true, Concat(emitters)),
            extra));
    }

    /// <param name="extra">More chunks inside the emitter, after the five every emitter carries.</param>
    /// <param name="properties">The property block's mini-chunks; none by default.</param>
    public static byte[] Emitter(byte[]? extra = null, params byte[][] properties)
    {
        var volume = Chunk(0x1100, true, Chunk(0x1101, false, new byte[64]));
        var parts = new List<byte[]>
        {
            Chunk(0x0002, true, Concat(properties)),
            Chunk(0x0003, false, Str("p_particle_master.tga")),
            Chunk(0x0016, false, Str("spark")),
            Chunk(0x0029, true, Concat(volume, volume, volume)),
            Chunk(0x0001, true, Tracks())
        };
        if (extra is not null)
            parts.Add(extra);
        return Chunk(0x0700, true, Concat([.. parts]));
    }

    private static byte[] Tracks()
    {
        var parts = new List<byte[]>();
        for (var i = 0; i < 7; i++)
        {
            var byteValued = i < 4;
            var endpoint = byteValued ? new byte[] { 255 } : F32(1f);
            parts.Add(Chunk(0x0000, true, Concat(Mini(0x02, endpoint), Mini(0x03, endpoint), Mini(0x04, I32(0)))));
            parts.Add(Chunk(0x0001, true, Concat()));
        }

        return Concat([.. parts]);
    }

    public static byte[] Chunk(uint type, bool container, params byte[][] body)
    {
        var data = Concat(body);
        var header = new byte[8];
        BitConverter.GetBytes(type).CopyTo(header, 0);
        BitConverter.GetBytes((uint)data.Length | (container ? 0x80000000u : 0u)).CopyTo(header, 4);
        return Concat(header, data);
    }

    public static byte[] Mini(byte type, params byte[][] body)
    {
        var data = Concat(body);
        return Concat([type, (byte)data.Length], data);
    }

    public static byte[] Concat(params byte[][] parts)
    {
        return parts.SelectMany(p => p).ToArray();
    }

    public static byte[] Str(string value)
    {
        return Concat(Encoding.ASCII.GetBytes(value), [0]);
    }

    public static byte[] I32(int value)
    {
        return BitConverter.GetBytes(value);
    }

    public static byte[] F32(float value)
    {
        return BitConverter.GetBytes(value);
    }
}
