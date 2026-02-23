using Godot;

namespace HypeReborn.Hype.Runtime.Parsing;

internal static class HypeGeometryReader
{
    public static bool IsInRange(int value, int length)
    {
        return value >= 0 && value < length;
    }

    public static Vector3 ComputeFaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var normal = (b - a).Cross(c - a);
        return normal.LengthSquared() > float.Epsilon ? normal.Normalized() : Vector3.Up;
    }

    public static Vector3? TryGetVertexNormal(Vector3[]? sourceVertexNormals, int index)
    {
        if (sourceVertexNormals == null || index < 0 || index >= sourceVertexNormals.Length)
        {
            return null;
        }

        var normal = sourceVertexNormals[index];
        return normal.LengthSquared() > float.Epsilon ? normal.Normalized() : null;
    }

    public static bool TryGetFaceNormal(Vector3[]? sourceFaceNormals, int triangleIndex, out Vector3 normal)
    {
        normal = Vector3.Zero;
        if (sourceFaceNormals == null || triangleIndex < 0 || triangleIndex >= sourceFaceNormals.Length)
        {
            return false;
        }

        var candidate = sourceFaceNormals[triangleIndex];
        if (candidate.LengthSquared() <= float.Epsilon)
        {
            return false;
        }

        normal = candidate.Normalized();
        return true;
    }

    public static int WrapIndex(int index, int length)
    {
        if (length <= 0)
        {
            return 0;
        }

        var value = index % length;
        if (value < 0)
        {
            value += length;
        }

        return value;
    }

    public static Vector3[] ReadXzyVector3Array(HypeRelocatedAddressSpace space, HypeAddress address, int count)
    {
        var output = new Vector3[count];
        var reader = space.CreateReader(address);
        for (var i = 0; i < count; i++)
        {
            var x = reader.ReadSingle();
            var z = reader.ReadSingle();
            var y = reader.ReadSingle();
            output[i] = new Vector3(x, y, z);
        }

        return output;
    }

    public static int[] ReadInt16Array(HypeRelocatedAddressSpace space, HypeAddress address, int count)
    {
        var output = new int[count];
        var reader = space.CreateReader(address);
        for (var i = 0; i < count; i++)
        {
            output[i] = reader.ReadInt16();
        }

        return output;
    }

    public static ushort[] ReadUInt16Array(HypeRelocatedAddressSpace space, HypeAddress address, int count)
    {
        var output = new ushort[count];
        var reader = space.CreateReader(address);
        for (var i = 0; i < count; i++)
        {
            output[i] = reader.ReadUInt16();
        }

        return output;
    }

    public static Vector2[] ReadVector2Array(HypeRelocatedAddressSpace space, HypeAddress address, int count)
    {
        var output = new Vector2[count];
        var reader = space.CreateReader(address);
        for (var i = 0; i < count; i++)
        {
            output[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        }

        return output;
    }
}
