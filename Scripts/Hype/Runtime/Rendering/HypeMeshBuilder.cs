using Godot;
using HypeReborn.Hype.Runtime.Textures;

namespace HypeReborn.Hype.Runtime.Rendering;

public static class HypeMeshBuilder
{
    private const uint TextureFlagColorKeyMask = 0x902u;
    private const uint TextureFlagTransparentMask = 0xAu;
    private const uint VisualMaterialTransparentFlag = 1u << 3;

    public static ArrayMesh? BuildArrayMesh(
        string gameRoot,
        HypeResolvedMesh meshData,
        bool flipWinding = false,
        bool invertNormals = false,
        bool preferAuthoredNormals = true)
    {
        if (meshData.Surfaces.Count == 0)
        {
            return null;
        }

        var mesh = new ArrayMesh();
        for (var i = 0; i < meshData.Surfaces.Count; i++)
        {
            var surface = meshData.Surfaces[i];
            if (surface.Vertices.Length == 0 || surface.Indices.Length == 0)
            {
                continue;
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = surface.Vertices;
            arrays[(int)Mesh.ArrayType.TexUV] = BuildSurfaceUvs(surface);
            arrays[(int)Mesh.ArrayType.Normal] = BuildSurfaceNormals(
                surface,
                flipWinding,
                invertNormals,
                preferAuthoredNormals);
            arrays[(int)Mesh.ArrayType.Index] = flipWinding
                ? FlipTriangleWinding(surface.Indices)
                : surface.Indices;

            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            var surfaceIndex = mesh.GetSurfaceCount() - 1;
            var material = BuildSurfaceMaterial(gameRoot, surface);
            if (material != null)
            {
                mesh.SurfaceSetMaterial(surfaceIndex, material);
            }
        }

        return mesh.GetSurfaceCount() > 0 ? mesh : null;
    }

    public static int[] FlipTriangleWinding(int[] source)
    {
        if (source.Length < 3)
        {
            return source;
        }

        var output = new int[source.Length];
        System.Array.Copy(source, output, source.Length);
        for (var i = 0; i + 2 < output.Length; i += 3)
        {
            (output[i + 1], output[i + 2]) = (output[i + 2], output[i + 1]);
        }

        return output;
    }

    public static Vector3[] BuildSurfaceNormals(
        HypeResolvedMeshSurface surface,
        bool flipWinding = false,
        bool invertNormals = false,
        bool preferAuthoredNormals = true)
    {
        Vector3[] normals;
        if (preferAuthoredNormals &&
            surface.Normals != null &&
            surface.Normals.Length == surface.Vertices.Length)
        {
            normals = NormalizeNormals(surface.Normals);
        }
        else
        {
            normals = BuildSmoothNormals(surface.Vertices, surface.Indices);
            if (normals.Length == 0 &&
                surface.Normals != null &&
                surface.Normals.Length == surface.Vertices.Length)
            {
                normals = NormalizeNormals(surface.Normals);
            }
        }

        if (flipWinding)
        {
            normals = InvertNormals(normals);
        }

        if (invertNormals)
        {
            normals = InvertNormals(normals);
        }

        return normals;
    }

    public static StandardMaterial3D? BuildSurfaceMaterial(string gameRoot, HypeResolvedMeshSurface surface)
    {
        var useColorKey = (surface.TextureFlags & TextureFlagColorKeyMask) != 0;
        var wantsTransparency =
            useColorKey ||
            (surface.TextureFlags & TextureFlagTransparentMask) != 0 ||
            (surface.VisualMaterialFlags & VisualMaterialTransparentFlag) != 0;

        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 1f, 1f, 1f),
            Roughness = 1f,
            CullMode = surface.DoubleSided
                ? BaseMaterial3D.CullModeEnum.Disabled
                : BaseMaterial3D.CullModeEnum.Back,
            Transparency = BaseMaterial3D.TransparencyEnum.Disabled,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly
        };
        material.SetFlag(BaseMaterial3D.Flags.UseTextureRepeat, ShouldEnableTextureRepeat(surface));

        if (!string.IsNullOrWhiteSpace(surface.TextureTgaName))
        {
            var textureResult = HypeTextureLookupService.TryGetTextureByTgaNameDetailed(
                gameRoot,
                surface.TextureTgaName,
                surface.TextureFlags,
                surface.TextureAlphaMask,
                forceColorKey: false);
            var texture = textureResult.Texture;
            if (texture != null)
            {
                material.AlbedoTexture = texture;
                var enableTransparency = useColorKey || (wantsTransparency && textureResult.HasAnyTransparency);
                if (enableTransparency)
                {
                    if (useColorKey || !textureResult.HasPartialTransparency)
                    {
                        material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor;
                        material.AlphaScissorThreshold = useColorKey ? 0.5f : 0.01f;
                    }
                    else
                    {
                        material.Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass;
                        material.AlphaScissorThreshold = 0f;
                    }

                    material.DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.OpaqueOnly;
                }
            }
        }

        return material;
    }

    public static Vector2[] BuildSurfaceUvs(HypeResolvedMeshSurface surface)
    {
        // Keep authored UV coordinates untouched. Wrapping/clamping in UV space can
        // collapse tiled coordinates and cause stretched textures.
        return surface.Uvs;
    }

    public static float ApplyUvWrap(float value, bool repeat, bool mirror)
    {
        if (mirror)
        {
            return WrapMirror(value);
        }

        if (repeat)
        {
            return WrapRepeat(value);
        }

        return Mathf.Clamp(value, 0f, 1f);
    }

    public static float WrapRepeat(float value)
    {
        return value - Mathf.Floor(value);
    }

    public static float WrapMirror(float value)
    {
        var wrapped = value - (Mathf.Floor(value / 2f) * 2f);
        if (wrapped < 0f)
        {
            wrapped += 2f;
        }

        return wrapped <= 1f ? wrapped : (2f - wrapped);
    }

    private static Vector3[] InvertNormals(Vector3[] source)
    {
        if (source.Length == 0)
        {
            return source;
        }

        var output = new Vector3[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            output[i] = -source[i];
        }

        return output;
    }

    private static Vector3[] NormalizeNormals(Vector3[] source)
    {
        var output = new Vector3[source.Length];
        for (var i = 0; i < source.Length; i++)
        {
            var normal = source[i];
            output[i] = normal.LengthSquared() > float.Epsilon ? normal.Normalized() : Vector3.Up;
        }

        return output;
    }

    private static Vector3[] BuildSmoothNormals(Vector3[] vertices, int[] indices)
    {
        var normals = new Vector3[vertices.Length];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var i0 = indices[i + 0];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            if (i0 < 0 || i1 < 0 || i2 < 0 ||
                i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
            {
                continue;
            }

            var face = (vertices[i1] - vertices[i0]).Cross(vertices[i2] - vertices[i0]);
            if (face.LengthSquared() <= float.Epsilon)
            {
                continue;
            }

            normals[i0] += face;
            normals[i1] += face;
            normals[i2] += face;
        }

        for (var i = 0; i < normals.Length; i++)
        {
            normals[i] = normals[i].LengthSquared() > float.Epsilon ? normals[i].Normalized() : Vector3.Up;
        }

        return normals;
    }

    private static bool ShouldEnableTextureRepeat(HypeResolvedMeshSurface surface)
    {
        var wrapBits = surface.TextureFlagsByte & 0x0F;
        if (wrapBits != 0)
        {
            return true;
        }

        var uvs = surface.Uvs;
        for (var i = 0; i < uvs.Length; i++)
        {
            var uv = uvs[i];
            if (uv.X < 0f || uv.X > 1f || uv.Y < 0f || uv.Y > 1f)
            {
                return true;
            }
        }

        return false;
    }
}
