using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace HypeReborn.Hype.Runtime;

public static class HypeHeuristicTransformScanner
{
    private const int FloatByteSize = 4;
    private const int Matrix4x4FloatCount = 16;
    private const int TransformBlockByteSize = Matrix4x4FloatCount * FloatByteSize;

    public static IReadOnlyList<Transform3D> Scan(string filePath, int maxCount)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || maxCount <= 0)
        {
            return Array.Empty<Transform3D>();
        }

        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < TransformBlockByteSize)
        {
            return Array.Empty<Transform3D>();
        }

        var result = new List<Transform3D>(Math.Min(maxCount, 256));
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var offset = 0; offset <= bytes.Length - TransformBlockByteSize && result.Count < maxCount; offset += FloatByteSize)
        {
            if (!TryReadTransform(bytes, offset, out var transform))
            {
                continue;
            }

            var key = QuantizedKey(transform.Origin);
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(transform);
        }

        return result;
    }

    private static bool TryReadTransform(byte[] bytes, int offset, out Transform3D transform)
    {
        transform = Transform3D.Identity;

        var f = new float[Matrix4x4FloatCount];
        for (var i = 0; i < Matrix4x4FloatCount; i++)
        {
            f[i] = BitConverter.ToSingle(bytes, offset + (i * FloatByteSize));
        }

        if (!AreFinite(f))
        {
            return false;
        }

        var rowMajorBasis = new Basis(
            new Vector3(f[0], f[1], f[2]),
            new Vector3(f[4], f[5], f[6]),
            new Vector3(f[8], f[9], f[10]));

        var columnMajorBasis = new Basis(
            new Vector3(f[0], f[4], f[8]),
            new Vector3(f[1], f[5], f[9]),
            new Vector3(f[2], f[6], f[10]));

        var rowScore = ScoreBasis(rowMajorBasis);
        var colScore = ScoreBasis(columnMajorBasis);
        if (rowScore <= 0f && colScore <= 0f)
        {
            return false;
        }

        var basis = rowScore >= colScore ? rowMajorBasis : columnMajorBasis;

        // Montreal-era files are inconsistent: translation can be in either
        // [3,7,11] or [12,13,14] slots depending on producer path.
        var tA = new Vector3(f[3], f[7], f[11]);
        var tB = new Vector3(f[12], f[13], f[14]);
        var t = SelectBestTranslation(tA, tB);

        if (!IsLikelyTranslation(t))
        {
            return false;
        }

        transform = new Transform3D(basis, t);
        return true;
    }

    private static float ScoreBasis(Basis basis)
    {
        var x = basis.X;
        var y = basis.Y;
        var z = basis.Z;

        var lx = x.Length();
        var ly = y.Length();
        var lz = z.Length();
        if (lx < 0.2f || ly < 0.2f || lz < 0.2f || lx > 4f || ly > 4f || lz > 4f)
        {
            return -1f;
        }

        var dxy = Mathf.Abs(x.Dot(y));
        var dxz = Mathf.Abs(x.Dot(z));
        var dyz = Mathf.Abs(y.Dot(z));
        if (dxy > 1.2f || dxz > 1.2f || dyz > 1.2f)
        {
            return -1f;
        }

        var det = x.Dot(y.Cross(z));
        if (Mathf.Abs(det) <= 0.05f || Mathf.Abs(det) >= 40f)
        {
            return -1f;
        }

        var orthPenalty = dxy + dxz + dyz;
        return 10f - orthPenalty;
    }

    private static Vector3 SelectBestTranslation(Vector3 a, Vector3 b)
    {
        var magA = a.Length();
        var magB = b.Length();

        var aValid = IsLikelyTranslation(a);
        var bValid = IsLikelyTranslation(b);

        if (bValid && (!aValid || magB > (magA * 1.2f)))
        {
            return b;
        }

        if (aValid)
        {
            return a;
        }

        return bValid ? b : a;
    }

    private static bool IsLikelyTranslation(Vector3 t)
    {
        const float maxAbs = 50000f;
        return Mathf.Abs(t.X) < maxAbs && Mathf.Abs(t.Y) < maxAbs && Mathf.Abs(t.Z) < maxAbs;
    }

    private static bool AreFinite(params float[] values)
    {
        foreach (var value in values)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }
        }

        return true;
    }

    private static string QuantizedKey(Vector3 v)
    {
        var x = Mathf.RoundToInt(v.X * 10f);
        var y = Mathf.RoundToInt(v.Y * 10f);
        var z = Mathf.RoundToInt(v.Z * 10f);
        return $"{x}:{y}:{z}";
    }
}
