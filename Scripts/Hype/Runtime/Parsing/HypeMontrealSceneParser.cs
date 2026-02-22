using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;

namespace HypeReborn.Hype.Runtime.Parsing;

public static class HypeMontrealSceneParser
{
    private const uint VisualMaterialFlagBackfaceCulling = 1u << 10;
    private const int MaxChildChain = 20000;

    public static IReadOnlyList<HypeResolvedEntity> ParseLevel(
        string gameRoot,
        HypeLevelRecord level,
        IReadOnlyList<HypeAnimationRecord> animations)
    {
        var diagnostics = new List<string>();
        var entities = new List<HypeResolvedEntity>();

        if (!TryBuildContext(level, out var context, diagnostics))
        {
            EmitDiagnostics(level.LevelName, diagnostics);
            AddAnimationAnchors(entities, animations);
            return entities;
        }

        try
        {
            var roots = ReadWorldRoots(context.Space, context.LevelGptAddress, diagnostics);
            var decoder = new HypeSceneDecoder(context.Space, diagnostics);
            entities.AddRange(decoder.Decode(roots));
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Scene parse failed: {ex.Message}");
        }

        AddAnimationAnchors(entities, animations);
        EmitDiagnostics(level.LevelName, diagnostics);
        return entities;
    }

    private static IReadOnlyList<HypeAddress> ReadWorldRoots(
        HypeRelocatedAddressSpace space,
        HypeAddress levelGptAddress,
        List<string> diagnostics)
    {
        var roots = new List<HypeAddress>(3);
        try
        {
            var reader = space.CreateReader(levelGptAddress);
            _ = reader.ReadPointer();
            _ = reader.ReadPointer();
            _ = reader.ReadPointer();
            _ = reader.ReadUInt32();

            var actualWorld = reader.ReadPointer();
            var dynamicWorld = reader.ReadPointer();
            var fatherSector = reader.ReadPointer();
            _ = reader.ReadUInt32();

            AddUnique(roots, actualWorld);
            AddUnique(roots, dynamicWorld);
            AddUnique(roots, fatherSector);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to read GPT world roots: {ex.Message}");
        }

        return roots;
    }

    private static void AddUnique(List<HypeAddress> list, HypeAddress? address)
    {
        if (address == null)
        {
            return;
        }

        if (!list.Contains(address.Value))
        {
            list.Add(address.Value);
        }
    }

    private static void AddAnimationAnchors(
        List<HypeResolvedEntity> entities,
        IReadOnlyList<HypeAnimationRecord> animations)
    {
        var animationOffset = 0;
        foreach (var animation in animations)
        {
            entities.Add(new HypeResolvedEntity
            {
                Id = animation.Id,
                Name = Path.GetFileNameWithoutExtension(animation.SourceFile),
                Kind = HypeResolvedEntityKind.AnimationAnchor,
                Transform = new Transform3D(
                    Basis.Identity,
                    new Vector3((animationOffset % 12) * 1.5f, 2f, (animationOffset / 12) * 1.5f)),
                SourceFile = animation.SourceFile
            });
            animationOffset++;
        }
    }

    private static void EmitDiagnostics(string levelName, IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0)
        {
            return;
        }

        foreach (var line in diagnostics)
        {
            GD.PrintErr($"[HypeSceneParser:{levelName}] {line}");
        }
    }

    private static bool TryBuildContext(
        HypeLevelRecord level,
        out HypeParseContext context,
        List<string> diagnostics)
    {
        context = default;

        var levelsRoot = Directory.GetParent(level.LevelDirectoryPath)?.FullName;
        if (string.IsNullOrWhiteSpace(levelsRoot))
        {
            diagnostics.Add("Could not resolve levels root from level directory.");
            return false;
        }

        var fixSnaPath = FindPath(levelsRoot, "fix.sna");
        var fixRtbPath = FindPath(levelsRoot, "fix.rtb");
        var fixRtpPath = FindPath(levelsRoot, "fix.rtp");
        var fixRttPath = FindPath(levelsRoot, "fix.rtt");
        var fixGptPath = FindPath(levelsRoot, "fix.gpt");
        var fixPtxPath = FindPath(levelsRoot, "fix.ptx");

        var lvlSnaPath = GetLevelCoreFile(level, ".sna");
        var lvlRtbPath = GetLevelCoreFile(level, ".rtb");
        var lvlRtpPath = GetLevelCoreFile(level, ".rtp");
        var lvlRttPath = GetLevelCoreFile(level, ".rtt");
        var lvlGptPath = GetLevelCoreFile(level, ".gpt");
        var lvlPtxPath = GetLevelCoreFile(level, ".ptx");
        var fixLvlRtbPath = GetNamedCoreFile(level, "fixlvl.rtb");

        var fixSna = TryLoadSna(fixSnaPath, "fix.sna", diagnostics);
        var lvlSna = TryLoadSna(lvlSnaPath, $"{level.LevelName}.sna", diagnostics);
        var fixRtb = TryLoadRtb(fixRtbPath, "fix.rtb", diagnostics);
        var fixLvlRtb = TryLoadRtb(fixLvlRtbPath, "fixlvl.rtb", diagnostics);
        var lvlRtb = TryLoadRtb(lvlRtbPath, $"{level.LevelName}.rtb", diagnostics);
        var fixRtp = TryLoadRtb(fixRtpPath, "fix.rtp", diagnostics);
        var lvlRtp = TryLoadRtb(lvlRtpPath, $"{level.LevelName}.rtp", diagnostics);
        var fixRtt = TryLoadRtb(fixRttPath, "fix.rtt", diagnostics);
        var lvlRtt = TryLoadRtb(lvlRttPath, $"{level.LevelName}.rtt", diagnostics);
        var fixGpt = TryLoadBytes(fixGptPath, "fix.gpt", diagnostics);
        var lvlGpt = TryLoadBytes(lvlGptPath, $"{level.LevelName}.gpt", diagnostics);
        var fixPtx = TryLoadBytes(fixPtxPath, "fix.ptx", diagnostics);
        var lvlPtx = TryLoadBytes(lvlPtxPath, $"{level.LevelName}.ptx", diagnostics);

        if (lvlSna == null || lvlRtb == null || lvlGpt == null)
        {
            diagnostics.Add("Level is missing required parse files (SNA/RTB/GPT).");
            return false;
        }

        var mergedFixRtb = HypeRelocationTable.Merge(fixRtb, fixLvlRtb);
        var space = new HypeRelocatedAddressSpace();
        space.AddSnaBlocks(fixSna, mergedFixRtb, "fix");
        space.AddSnaBlocks(lvlSna, lvlRtb, "lvl");
        space.AddPointerFile("fix_gpt", fixGpt, fixRtp);
        space.AddPointerFile("lvl_gpt", lvlGpt, lvlRtp);
        space.AddPointerFile("fix_ptx", fixPtx, fixRtt);
        space.AddPointerFile("lvl_ptx", lvlPtx, lvlRtt);

        if (!space.TryGetSegmentStart("lvl_gpt", out var levelGptAddress))
        {
            diagnostics.Add("Could not map level GPT segment.");
            return false;
        }

        context = new HypeParseContext
        {
            Space = space,
            LevelGptAddress = levelGptAddress
        };

        return true;
    }

    private static HypeSnaImage? TryLoadSna(string? path, string tag, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return HypeSnaImage.Load(path, snaCompression: true);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to parse {tag}: {ex.Message}");
            return null;
        }
    }

    private static HypeRelocationTable? TryLoadRtb(string? path, string tag, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return HypeRelocationTable.Load(path, snaCompression: true);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to parse {tag}: {ex.Message}");
            return null;
        }
    }

    private static byte[]? TryLoadBytes(string? path, string tag, List<string> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            diagnostics.Add($"Failed to read {tag}: {ex.Message}");
            return null;
        }
    }

    private static string? GetLevelCoreFile(HypeLevelRecord level, string extension)
    {
        return GetNamedCoreFile(level, $"{level.LevelName}{extension}");
    }

    private static string? GetNamedCoreFile(HypeLevelRecord level, string fileName)
    {
        if (level.CoreFiles.TryGetValue(fileName, out var path) && !string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        return null;
    }

    private static string? FindPath(string directory, string fileName)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private readonly struct HypeParseContext
    {
        public required HypeRelocatedAddressSpace Space { get; init; }
        public required HypeAddress LevelGptAddress { get; init; }
    }

    private sealed class HypeSceneDecoder
    {
        private readonly HypeRelocatedAddressSpace _space;
        private readonly List<string> _diagnostics;
        private readonly Dictionary<HypeAddress, ParsedSuperObject> _superObjects = new();
        private readonly Dictionary<HypeAddress, HypeResolvedMesh?> _ipoMeshByAddress = new();
        private readonly Dictionary<HypeAddress, HypeResolvedMesh?> _physicalMeshByAddress = new();
        private readonly Dictionary<HypeAddress, HypeResolvedMesh?> _geoMeshByAddress = new();
        private readonly Dictionary<HypeAddress, ParsedVisualMaterial> _visualMaterials = new();
        private readonly Dictionary<HypeAddress, ParsedTextureInfo> _textureInfos = new();
        private readonly Dictionary<HypeAddress, ParsedGameMaterial> _gameMaterials = new();

        public HypeSceneDecoder(HypeRelocatedAddressSpace space, List<string> diagnostics)
        {
            _space = space;
            _diagnostics = diagnostics;
        }

        public IReadOnlyList<HypeResolvedEntity> Decode(IReadOnlyList<HypeAddress> roots)
        {
            var entities = new List<HypeResolvedEntity>();
            var traversalStack = new HashSet<HypeAddress>();

            foreach (var root in roots)
            {
                DecodeSuperObjectRecursive(root, Transform3D.Identity, entities, traversalStack);
            }

            return entities;
        }

        private void DecodeSuperObjectRecursive(
            HypeAddress superObjectAddress,
            Transform3D parentTransform,
            List<HypeResolvedEntity> entities,
            HashSet<HypeAddress> traversalStack)
        {
            if (!traversalStack.Add(superObjectAddress))
            {
                return;
            }

            try
            {
                var superObject = ParseSuperObject(superObjectAddress);
                if (superObject == null)
                {
                    return;
                }

                var worldTransform = parentTransform * superObject.LocalTransform;
                if (superObject.Mesh != null && superObject.Mesh.Surfaces.Count > 0)
                {
                    var flipWinding = worldTransform.Basis.Determinant() < 0f;
                    entities.Add(new HypeResolvedEntity
                    {
                        Id = $"geo:{superObjectAddress}",
                        Name = superObject.Name,
                        Kind = HypeResolvedEntityKind.Geometry,
                        Transform = worldTransform,
                        FlipWinding = flipWinding,
                        Mesh = superObject.Mesh
                    });
                }

                var visitedChildren = new HashSet<HypeAddress>();
                var nextChild = superObject.FirstChild;
                var remaining = (int)Math.Min(superObject.ChildCount, MaxChildChain);

                while (nextChild.HasValue && remaining-- > 0 && visitedChildren.Add(nextChild.Value))
                {
                    DecodeSuperObjectRecursive(nextChild.Value, worldTransform, entities, traversalStack);

                    var child = ParseSuperObject(nextChild.Value);
                    nextChild = child?.NextBrother;
                }
            }
            finally
            {
                traversalStack.Remove(superObjectAddress);
            }
        }

        private ParsedSuperObject? ParseSuperObject(HypeAddress address)
        {
            if (_superObjects.TryGetValue(address, out var cached))
            {
                return cached;
            }

            try
            {
                var reader = _space.CreateReader(address);

                var typeCode = reader.ReadUInt32();
                var offData = reader.ReadPointer();
                var offChildHead = reader.ReadPointer();
                _ = reader.ReadPointer();
                var childCount = reader.ReadUInt32();
                var offBrotherNext = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                var offMatrix = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadPointer();

                var localTransform = Transform3D.Identity;
                if (offMatrix.HasValue)
                {
                    localTransform = ReadLegacyTransform(offMatrix.Value);
                }

                var superType = GetMontrealSuperObjectType(typeCode);
                HypeResolvedMesh? mesh = null;
                if ((superType == MontrealSuperObjectType.IPO || superType == MontrealSuperObjectType.IPO2) && offData.HasValue)
                {
                    mesh = ParseIpoMesh(offData.Value);
                }

                var parsed = new ParsedSuperObject
                {
                    Name = $"{superType}_{address.Offset:X8}",
                    Type = superType,
                    TypeCode = typeCode,
                    FirstChild = offChildHead,
                    ChildCount = childCount,
                    NextBrother = offBrotherNext,
                    LocalTransform = localTransform,
                    Mesh = mesh
                };

                _superObjects[address] = parsed;
                return parsed;
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse SuperObject {address}: {ex.Message}");
                return null;
            }
        }

        private HypeResolvedMesh? ParseIpoMesh(HypeAddress ipoAddress)
        {
            if (_ipoMeshByAddress.TryGetValue(ipoAddress, out var cached))
            {
                return cached;
            }

            HypeResolvedMesh? mesh = null;
            try
            {
                var reader = _space.CreateReader(ipoAddress);
                var offPhysicalObject = reader.ReadPointer();
                _ = reader.ReadPointer();

                if (offPhysicalObject.HasValue)
                {
                    mesh = ParsePhysicalObjectMesh(offPhysicalObject.Value);
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse IPO {ipoAddress}: {ex.Message}");
            }

            _ipoMeshByAddress[ipoAddress] = mesh;
            return mesh;
        }

        private HypeResolvedMesh? ParsePhysicalObjectMesh(HypeAddress physicalObjectAddress)
        {
            if (_physicalMeshByAddress.TryGetValue(physicalObjectAddress, out var cached))
            {
                return cached;
            }

            HypeResolvedMesh? mesh = null;
            try
            {
                var reader = _space.CreateReader(physicalObjectAddress);
                var offVisualSet = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadUInt32();

                if (!offVisualSet.HasValue)
                {
                    _physicalMeshByAddress[physicalObjectAddress] = null;
                    return null;
                }

                var visualSetReader = _space.CreateReader(offVisualSet.Value);
                _ = visualSetReader.ReadUInt32();
                var lodCount = visualSetReader.ReadUInt16();
                var visualSetType = visualSetReader.ReadUInt16();
                _ = visualSetReader.ReadPointer();
                var offLodDataOffsets = visualSetReader.ReadPointer();
                _ = visualSetReader.ReadUInt32();

                if (lodCount == 0 || !offLodDataOffsets.HasValue || visualSetType != 0)
                {
                    _physicalMeshByAddress[physicalObjectAddress] = null;
                    return null;
                }

                HypeAddress? selectedLodAddress = null;
                var lodAddressReader = _space.CreateReader(offLodDataOffsets.Value);
                for (var i = 0; i < lodCount; i++)
                {
                    var lodAddr = lodAddressReader.ReadPointer();
                    if (selectedLodAddress == null && lodAddr.HasValue)
                    {
                        selectedLodAddress = lodAddr;
                    }
                }

                if (selectedLodAddress.HasValue)
                {
                    mesh = ParseGeometricObjectMesh(selectedLodAddress.Value);
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse PhysicalObject {physicalObjectAddress}: {ex.Message}");
            }

            _physicalMeshByAddress[physicalObjectAddress] = mesh;
            return mesh;
        }

        private HypeResolvedMesh? ParseGeometricObjectMesh(HypeAddress geometricObjectAddress)
        {
            if (_geoMeshByAddress.TryGetValue(geometricObjectAddress, out var cached))
            {
                return cached;
            }

            HypeResolvedMesh? mesh = null;
            try
            {
                var reader = _space.CreateReader(geometricObjectAddress);

                var vertexCount = (int)reader.ReadUInt32();
                var offVertices = reader.ReadPointer();
                var offNormals = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadInt32();
                var elementCount = (int)reader.ReadUInt32();
                var offElementTypes = reader.ReadPointer();
                var offElements = reader.ReadPointer();
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();
                _ = reader.ReadSingle();

                if (vertexCount <= 0 || elementCount <= 0 || !offVertices.HasValue || !offElementTypes.HasValue || !offElements.HasValue)
                {
                    _geoMeshByAddress[geometricObjectAddress] = null;
                    return null;
                }

                var vertices = ReadXzyVector3Array(offVertices.Value, vertexCount);
                var vertexNormals = offNormals.HasValue
                    ? ReadXzyVector3Array(offNormals.Value, vertexCount)
                    : null;
                var elementTypes = ReadUInt16Array(offElementTypes.Value, elementCount);

                var surfaces = new List<HypeResolvedMeshSurface>();
                for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
                {
                    if (elementTypes[elementIndex] != 1)
                    {
                        continue;
                    }

                    var elementPointerReader = _space.CreateReader(offElements.Value.Add(elementIndex * 4));
                    var offElement = elementPointerReader.ReadPointer();
                    if (!offElement.HasValue)
                    {
                        continue;
                    }

                    var surface = ParseTriangleElement(offElement.Value, vertices, vertexNormals);
                    if (surface != null)
                    {
                        surfaces.Add(surface);
                    }
                }

                if (surfaces.Count > 0)
                {
                    mesh = new HypeResolvedMesh { Surfaces = surfaces };
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse GeometricObject {geometricObjectAddress}: {ex.Message}");
            }

            _geoMeshByAddress[geometricObjectAddress] = mesh;
            return mesh;
        }

        private HypeResolvedMeshSurface? ParseTriangleElement(
            HypeAddress elementAddress,
            Vector3[] sourceVertices,
            Vector3[]? sourceVertexNormals)
        {
            try
            {
                var reader = _space.CreateReader(elementAddress);
                var offMaterial = reader.ReadPointer();
                var triangleCount = (int)reader.ReadUInt16();
                var uvCount = (int)reader.ReadUInt16();
                var offTriangles = reader.ReadPointer();
                var offMappingUvs = reader.ReadPointer();
                var offNormals = reader.ReadPointer();
                var offUvs = reader.ReadPointer();
                _ = reader.ReadUInt32();
                _ = reader.ReadPointer();
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt32();

                if (triangleCount <= 0 || uvCount <= 0 || !offTriangles.HasValue || !offMappingUvs.HasValue || !offUvs.HasValue)
                {
                    return null;
                }

                var gameMaterial = offMaterial.HasValue ? ParseGameMaterial(offMaterial.Value) : null;
                var backfaceCulling = gameMaterial != null && (gameMaterial.VisualFlags & VisualMaterialFlagBackfaceCulling) != 0;

                var mappingUvs = ReadInt16Array(offMappingUvs.Value, triangleCount * 3);
                var sourceUvs = ReadVector2Array(offUvs.Value, uvCount);
                var sourceTriangles = ReadInt16Array(offTriangles.Value, triangleCount * 3);
                var sourceNormals = offNormals.HasValue ? ReadXzyVector3Array(offNormals.Value, triangleCount) : null;

                var vertices = new List<Vector3>(triangleCount * 3);
                var uvs = new List<Vector2>(triangleCount * 3);
                var normals = new List<Vector3>(triangleCount * 3);
                var indices = new List<int>(triangleCount * 3);

                for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
                {
                    var baseTri = triangleIndex * 3;
                    var i0 = sourceTriangles[baseTri + 0];
                    var i1 = sourceTriangles[baseTri + 1];
                    var i2 = sourceTriangles[baseTri + 2];
                    if (!IsInRange(i0, sourceVertices.Length) ||
                        !IsInRange(i1, sourceVertices.Length) ||
                        !IsInRange(i2, sourceVertices.Length))
                    {
                        continue;
                    }

                    var m0 = mappingUvs[baseTri + 0];
                    var m1 = mappingUvs[baseTri + 1];
                    var m2 = mappingUvs[baseTri + 2];

                    // Some surfaces contain mixed triangle winding. Use the authored per-face
                    // normal stream as reference and swap winding when needed.
                    if (TryGetFaceNormal(sourceNormals, triangleIndex, out var authoredFaceNormal))
                    {
                        var sourceFace = ComputeFaceNormal(sourceVertices[i0], sourceVertices[i1], sourceVertices[i2]);
                        if (sourceFace.Dot(authoredFaceNormal) < 0f)
                        {
                            (i1, i2) = (i2, i1);
                            (m1, m2) = (m2, m1);
                        }
                    }

                    var v0 = sourceVertices[i0];
                    var v1 = sourceVertices[i1];
                    var v2 = sourceVertices[i2];

                    var uv0 = sourceUvs[WrapIndex(m0, sourceUvs.Length)];
                    var uv1 = sourceUvs[WrapIndex(m1, sourceUvs.Length)];
                    var uv2 = sourceUvs[WrapIndex(m2, sourceUvs.Length)];
                    var fallbackFaceNormal = sourceNormals != null && triangleIndex < sourceNormals.Length
                        ? sourceNormals[triangleIndex]
                        : ComputeFaceNormal(v0, v2, v1);
                    var n0 = TryGetVertexNormal(sourceVertexNormals, i0) ?? fallbackFaceNormal;
                    var n1 = TryGetVertexNormal(sourceVertexNormals, i1) ?? fallbackFaceNormal;
                    var n2 = TryGetVertexNormal(sourceVertexNormals, i2) ?? fallbackFaceNormal;

                    var vertexBase = vertices.Count;
                    vertices.Add(v0);
                    vertices.Add(v1);
                    vertices.Add(v2);
                    uvs.Add(uv0);
                    uvs.Add(uv1);
                    uvs.Add(uv2);
                    normals.Add(n0);
                    normals.Add(n1);
                    normals.Add(n2);

                    indices.Add(vertexBase + 0);
                    indices.Add(vertexBase + 2);
                    indices.Add(vertexBase + 1);
                }

                if (vertices.Count == 0 || indices.Count == 0)
                {
                    return null;
                }

                return new HypeResolvedMeshSurface
                {
                    Vertices = vertices.ToArray(),
                    Uvs = uvs.ToArray(),
                    Normals = normals.ToArray(),
                    Indices = indices.ToArray(),
                    DoubleSided = !backfaceCulling,
                    TextureTgaName = gameMaterial?.TextureTgaName,
                    VisualMaterialFlags = gameMaterial?.VisualFlags ?? 0,
                    TextureFlags = gameMaterial?.TextureFlags ?? 0,
                    TextureFlagsByte = gameMaterial?.TextureFlagsByte ?? 0,
                    TextureAlphaMask = gameMaterial?.TextureAlphaMask ?? 0
                };
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse triangle element {elementAddress}: {ex.Message}");
                return null;
            }
        }

        private ParsedGameMaterial? ParseGameMaterial(HypeAddress address)
        {
            if (_gameMaterials.TryGetValue(address, out var cached))
            {
                return cached;
            }

            try
            {
                var reader = _space.CreateReader(address);
                var offVisualMaterial = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadUInt32();
                _ = reader.ReadPointer();

                var visualMaterial = offVisualMaterial.HasValue ? ParseVisualMaterial(offVisualMaterial.Value) : null;
                var parsed = new ParsedGameMaterial
                {
                    VisualFlags = visualMaterial?.Flags ?? 0,
                    TextureTgaName = visualMaterial?.TextureTgaName,
                    TextureFlags = visualMaterial?.TextureFlags ?? 0,
                    TextureFlagsByte = visualMaterial?.TextureFlagsByte ?? 0,
                    TextureAlphaMask = visualMaterial?.TextureAlphaMask ?? 0
                };

                _gameMaterials[address] = parsed;
                return parsed;
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse GameMaterial {address}: {ex.Message}");
                return null;
            }
        }

        private ParsedVisualMaterial? ParseVisualMaterial(HypeAddress address)
        {
            if (_visualMaterials.TryGetValue(address, out var cached))
            {
                return cached;
            }

            try
            {
                var reader = _space.CreateReader(address);
                var flags = reader.ReadUInt32();
                for (var i = 0; i < 16; i++)
                {
                    _ = reader.ReadSingle();
                }

                _ = reader.ReadUInt32();
                var offTexture = reader.ReadPointer();
                var textureInfo = offTexture.HasValue ? ParseTextureInfo(offTexture.Value) : null;

                var parsed = new ParsedVisualMaterial
                {
                    Flags = flags,
                    TextureTgaName = textureInfo?.Name,
                    TextureFlags = textureInfo?.Flags ?? 0,
                    TextureFlagsByte = textureInfo?.FlagsByte ?? 0,
                    TextureAlphaMask = textureInfo?.AlphaMask ?? 0
                };

                _visualMaterials[address] = parsed;
                return parsed;
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse VisualMaterial {address}: {ex.Message}");
                return null;
            }
        }

        private ParsedTextureInfo? ParseTextureInfo(HypeAddress address)
        {
            if (_textureInfos.TryGetValue(address, out var cached))
            {
                return cached;
            }

            try
            {
                var reader = _space.CreateReader(address);
                _ = reader.ReadUInt32(); // field0
                _ = reader.ReadUInt32(); // field4/field8
                var flags = reader.ReadUInt32(); // flags
                _ = reader.ReadUInt32(); // height_
                _ = reader.ReadUInt32(); // width_
                _ = reader.ReadUInt32(); // field14
                _ = reader.ReadUInt32(); // field18
                _ = reader.ReadUInt32(); // field1C
                _ = reader.ReadUInt32(); // field20
                _ = reader.ReadUInt32(); // field24
                _ = reader.ReadUInt32(); // field28
                var alphaMask = reader.ReadUInt32(); // field2C (chroma key in older material paths)
                _ = reader.ReadUInt32(); // height
                _ = reader.ReadUInt32(); // width
                for (var i = 0; i < 11; i++)
                {
                    _ = reader.ReadUInt32();
                }
                var name = reader.ReadFixedString(0x50);
                _ = reader.ReadByte();
                var flagsByte = reader.ReadByte();

                var parsed = new ParsedTextureInfo
                {
                    Flags = flags,
                    FlagsByte = flagsByte,
                    AlphaMask = alphaMask,
                    Name = NormalizeTextureName(name)
                };

                _textureInfos[address] = parsed;
                return parsed;
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse TextureInfo {address}: {ex.Message}");
                return null;
            }
        }

        private static string? NormalizeTextureName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = value.Trim().Replace('/', '\\');
            normalized = normalized.TrimStart('\\');
            if (normalized.StartsWith("gamedata\\", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized["gamedata\\".Length..];
            }
            else if (normalized.StartsWith("game\\gamedata\\", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized["game\\gamedata\\".Length..];
            }

            if (normalized.EndsWith(".gf", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[..^3] + ".tga";
            }

            return normalized;
        }

        private static bool IsInRange(int value, int length)
        {
            return value >= 0 && value < length;
        }

        private static Vector3 ComputeFaceNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = (b - a).Cross(c - a);
            return normal.LengthSquared() > float.Epsilon ? normal.Normalized() : Vector3.Up;
        }

        private static Vector3? TryGetVertexNormal(Vector3[]? sourceVertexNormals, int index)
        {
            if (sourceVertexNormals == null || index < 0 || index >= sourceVertexNormals.Length)
            {
                return null;
            }

            var normal = sourceVertexNormals[index];
            return normal.LengthSquared() > float.Epsilon ? normal.Normalized() : null;
        }

        private static bool TryGetFaceNormal(Vector3[]? sourceFaceNormals, int triangleIndex, out Vector3 normal)
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

        private static int WrapIndex(int index, int length)
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

        private Vector3[] ReadXzyVector3Array(HypeAddress address, int count)
        {
            var output = new Vector3[count];
            var reader = _space.CreateReader(address);
            for (var i = 0; i < count; i++)
            {
                var x = reader.ReadSingle();
                var z = reader.ReadSingle();
                var y = reader.ReadSingle();
                output[i] = new Vector3(x, y, z);
            }

            return output;
        }

        private int[] ReadInt16Array(HypeAddress address, int count)
        {
            var output = new int[count];
            var reader = _space.CreateReader(address);
            for (var i = 0; i < count; i++)
            {
                output[i] = reader.ReadInt16();
            }

            return output;
        }

        private ushort[] ReadUInt16Array(HypeAddress address, int count)
        {
            var output = new ushort[count];
            var reader = _space.CreateReader(address);
            for (var i = 0; i < count; i++)
            {
                output[i] = reader.ReadUInt16();
            }

            return output;
        }

        private Vector2[] ReadVector2Array(HypeAddress address, int count)
        {
            var output = new Vector2[count];
            var reader = _space.CreateReader(address);
            for (var i = 0; i < count; i++)
            {
                output[i] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            }

            return output;
        }

        private Transform3D ReadLegacyTransform(HypeAddress matrixAddress)
        {
            var reader = _space.CreateReader(matrixAddress);
            _ = reader.ReadUInt32();

            var posX = reader.ReadSingle();
            var posY = reader.ReadSingle();
            var posZ = reader.ReadSingle();

            var rotCol0 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var rotCol1 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var rotCol2 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

            var scaleCol0 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var scaleCol1 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var scaleCol2 = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

            var position = new Vector3(posX, posZ, posY);
            var scale = new Vector3(
                SignedScaleComponent(scaleCol0, rotCol0),
                SignedScaleComponent(scaleCol2, rotCol2),
                SignedScaleComponent(scaleCol1, rotCol1));
            var rotation = ConvertRotationToGodot(rotCol0, rotCol1, rotCol2);

            var basis = new Basis(rotation).Scaled(scale);
            return new Transform3D(basis, position);
        }

        private static float SignedScaleComponent(Vector3 scaleColumn, Vector3 rotationColumn)
        {
            var magnitude = scaleColumn.Length();
            if (magnitude <= float.Epsilon)
            {
                return 0f;
            }

            var dot = scaleColumn.Dot(rotationColumn);
            if (MathF.Abs(dot) <= float.Epsilon)
            {
                return magnitude;
            }

            return dot < 0f ? -magnitude : magnitude;
        }

        private static Quaternion ConvertRotationToGodot(Vector3 col0, Vector3 col1, Vector3 col2)
        {
            var m00 = col0.X;
            var m01 = col1.X;
            var m02 = col2.X;
            var m10 = col0.Y;
            var m11 = col1.Y;
            var m12 = col2.Y;
            var m20 = col0.Z;
            var m21 = col1.Z;
            var m22 = col2.Z;

            float t;
            Quaternion q;
            if (m22 < 0f)
            {
                if (m00 > m11)
                {
                    t = 1f + m00 - m11 - m22;
                    q = new Quaternion(t, m01 + m10, m20 + m02, m12 - m21);
                }
                else
                {
                    t = 1f - m00 + m11 - m22;
                    q = new Quaternion(m01 + m10, t, m12 + m21, m20 - m02);
                }
            }
            else
            {
                if (m00 < -m11)
                {
                    t = 1f - m00 - m11 + m22;
                    q = new Quaternion(m20 + m02, m12 + m21, t, m01 - m10);
                }
                else
                {
                    t = 1f + m00 + m11 + m22;
                    q = new Quaternion(m12 - m21, m20 - m02, m01 - m10, t);
                }
            }

            if (t <= float.Epsilon)
            {
                return Quaternion.Identity;
            }

            var factor = 0.5f / MathF.Sqrt(t);
            q = new Quaternion(q.X * factor, q.Y * factor, q.Z * factor, q.W * -factor);
            return new Quaternion(q.X, q.Z, q.Y, -q.W).Normalized();
        }

        private enum MontrealSuperObjectType
        {
            Unknown,
            World,
            Perso,
            Sector,
            IPO,
            IPO2
        }

        private static MontrealSuperObjectType GetMontrealSuperObjectType(uint typeCode)
        {
            return typeCode switch
            {
                0x0 => MontrealSuperObjectType.World,
                0x4 => MontrealSuperObjectType.Perso,
                0x8 => MontrealSuperObjectType.Sector,
                0xD => MontrealSuperObjectType.IPO,
                0x15 => MontrealSuperObjectType.IPO2,
                _ => MontrealSuperObjectType.Unknown
            };
        }

        private sealed class ParsedSuperObject
        {
            public required string Name { get; init; }
            public required uint TypeCode { get; init; }
            public required MontrealSuperObjectType Type { get; init; }
            public required HypeAddress? FirstChild { get; init; }
            public required uint ChildCount { get; init; }
            public required HypeAddress? NextBrother { get; init; }
            public required Transform3D LocalTransform { get; init; }
            public required HypeResolvedMesh? Mesh { get; init; }
        }

        private sealed class ParsedVisualMaterial
        {
            public required uint Flags { get; init; }
            public required string? TextureTgaName { get; init; }
            public required uint TextureFlags { get; init; }
            public required byte TextureFlagsByte { get; init; }
            public required uint TextureAlphaMask { get; init; }
        }

        private sealed class ParsedTextureInfo
        {
            public required uint Flags { get; init; }
            public required byte FlagsByte { get; init; }
            public required uint AlphaMask { get; init; }
            public required string? Name { get; init; }
        }

        private sealed class ParsedGameMaterial
        {
            public required uint VisualFlags { get; init; }
            public required string? TextureTgaName { get; init; }
            public required uint TextureFlags { get; init; }
            public required byte TextureFlagsByte { get; init; }
            public required uint TextureAlphaMask { get; init; }
        }
    }

    private readonly struct HypeAddress : IEquatable<HypeAddress>
    {
        public HypeAddress(string segmentId, int offset)
        {
            SegmentId = segmentId;
            Offset = offset;
        }

        public string SegmentId { get; }
        public int Offset { get; }

        public HypeAddress Add(int delta)
        {
            return new HypeAddress(SegmentId, Offset + delta);
        }

        public bool Equals(HypeAddress other)
        {
            return Offset == other.Offset &&
                   SegmentId.Equals(other.SegmentId, StringComparison.Ordinal);
        }

        public override bool Equals(object? obj)
        {
            return obj is HypeAddress other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(SegmentId, Offset);
        }

        public override string ToString()
        {
            return $"{SegmentId}@0x{Offset:X8}";
        }
    }

    private sealed class HypeRelocatedAddressSpace
    {
        private readonly Dictionary<string, HypeSegment> _segments = new(StringComparer.Ordinal);
        private readonly Dictionary<ushort, List<HypeSegment>> _blockSegments = new();
        private readonly List<HypeSegment> _blockSegmentList = new();

        public void AddSnaBlocks(HypeSnaImage? sna, HypeRelocationTable? rtb, string sourceTag)
        {
            if (sna == null)
            {
                return;
            }

            var blockIndex = 0;
            foreach (var block in sna.Blocks)
            {
                var pointerMap = BuildBlockPointerMap(rtb, block.Module, block.BlockId);
                var segmentId = BuildBlockSegmentId(sourceTag, block.Module, block.BlockId, blockIndex++);
                var segment = new HypeSegment(
                    segmentId,
                    block.Data,
                    block.BaseInMemory,
                    pointerMap,
                    pointerFilePointerMap: null);

                _segments[segmentId] = segment;
                _blockSegmentList.Add(segment);

                var key = HypeRelocationTable.RelocationKey(block.Module, block.BlockId);
                if (!_blockSegments.TryGetValue(key, out var bucket))
                {
                    bucket = new List<HypeSegment>();
                    _blockSegments[key] = bucket;
                }

                bucket.Add(segment);
            }
        }

        public void AddPointerFile(string segmentId, byte[]? bytes, HypeRelocationTable? table)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return;
            }

            var pointerFileMap = BuildPointerFileMap(bytes, table);
            _segments[segmentId] = new HypeSegment(
                segmentId,
                bytes,
                baseInMemory: null,
                blockPointerMap: null,
                pointerFilePointerMap: pointerFileMap);
        }

        public bool TryGetSegmentStart(string segmentId, out HypeAddress address)
        {
            if (_segments.ContainsKey(segmentId))
            {
                address = new HypeAddress(segmentId, 0);
                return true;
            }

            address = default;
            return false;
        }

        public HypeMemoryReader CreateReader(HypeAddress address)
        {
            return new HypeMemoryReader(this, address);
        }

        public bool TryResolvePointer(HypeAddress pointerFieldAddress, uint rawValue, out HypeAddress resolved)
        {
            resolved = default;
            if (!_segments.TryGetValue(pointerFieldAddress.SegmentId, out var sourceSegment))
            {
                return false;
            }

            if (sourceSegment.BlockPointerMap != null && sourceSegment.BaseInMemory.HasValue)
            {
                var memoryAddress = (uint)(sourceSegment.BaseInMemory.Value + pointerFieldAddress.Offset);
                if (sourceSegment.BlockPointerMap.TryGetValue(memoryAddress, out var pointerInfo))
                {
                    return TryResolvePointerTarget(pointerInfo, rawValue, out resolved);
                }
            }

            if (sourceSegment.PointerFilePointerMap != null &&
                sourceSegment.PointerFilePointerMap.TryGetValue(pointerFieldAddress.Offset, out var pointerFromFile))
            {
                return TryResolvePointerTarget(pointerFromFile, rawValue, out resolved);
            }

            return false;
        }

        public bool TryResolveRawAddress(uint rawValue, out HypeAddress resolved)
        {
            foreach (var segment in _blockSegmentList)
            {
                if (!segment.BaseInMemory.HasValue)
                {
                    continue;
                }

                var baseAddress = segment.BaseInMemory.Value;
                var relative = (long)rawValue - baseAddress;
                if (relative < 0 || relative >= segment.Data.Length)
                {
                    continue;
                }

                resolved = new HypeAddress(segment.Id, (int)relative);
                return true;
            }

            resolved = default;
            return false;
        }

        public bool TryGetDataSlice(HypeAddress address, int length, out ReadOnlySpan<byte> bytes)
        {
            bytes = default;
            if (!_segments.TryGetValue(address.SegmentId, out var segment))
            {
                return false;
            }

            if (address.Offset < 0 || address.Offset + length > segment.Data.Length)
            {
                return false;
            }

            bytes = new ReadOnlySpan<byte>(segment.Data, address.Offset, length);
            return true;
        }

        private bool TryResolvePointerTarget(
            HypeRelocationPointerInfo pointerInfo,
            uint rawValue,
            out HypeAddress resolved)
        {
            resolved = default;
            var key = HypeRelocationTable.RelocationKey(pointerInfo.Module, pointerInfo.BlockId);
            if (!_blockSegments.TryGetValue(key, out var candidates) || candidates.Count == 0)
            {
                return false;
            }

            HypeSegment? bestCandidate = null;
            long bestRelative = -1;
            var bestLength = -1;

            foreach (var candidate in candidates)
            {
                if (!candidate.BaseInMemory.HasValue)
                {
                    continue;
                }

                var relative = (long)rawValue - candidate.BaseInMemory.Value;
                if (relative < 0 || relative >= candidate.Data.Length)
                {
                    continue;
                }

                if (candidate.Data.Length > bestLength)
                {
                    bestCandidate = candidate;
                    bestRelative = relative;
                    bestLength = candidate.Data.Length;
                }
            }

            if (bestCandidate == null || bestRelative < 0)
            {
                return false;
            }

            resolved = new HypeAddress(bestCandidate.Id, (int)bestRelative);
            return true;
        }

        private static Dictionary<uint, HypeRelocationPointerInfo>? BuildBlockPointerMap(
            HypeRelocationTable? table,
            byte module,
            byte blockId)
        {
            if (table == null || !table.TryGetBlock(module, blockId, out var block) || block == null || block.Pointers.Count == 0)
            {
                return null;
            }

            var map = new Dictionary<uint, HypeRelocationPointerInfo>();
            foreach (var pointer in block.Pointers)
            {
                if (!map.ContainsKey(pointer.OffsetInMemory))
                {
                    map[pointer.OffsetInMemory] = pointer;
                }
            }

            return map;
        }

        private static Dictionary<int, HypeRelocationPointerInfo>? BuildPointerFileMap(
            byte[] data,
            HypeRelocationTable? table)
        {
            if (table == null || data.Length < 4)
            {
                return null;
            }

            var lookupByOffsetInMemory = new Dictionary<uint, HypeRelocationPointerInfo>();
            foreach (var block in table.PointerBlocks)
            {
                foreach (var pointer in block.Pointers)
                {
                    if (!lookupByOffsetInMemory.ContainsKey(pointer.OffsetInMemory))
                    {
                        lookupByOffsetInMemory[pointer.OffsetInMemory] = pointer;
                    }
                }
            }

            if (lookupByOffsetInMemory.Count == 0)
            {
                return null;
            }

            var map = new Dictionary<int, HypeRelocationPointerInfo>();
            for (var offset = 0; offset <= data.Length - 4; offset += 4)
            {
                var value = BitConverter.ToUInt32(data, offset);
                if (lookupByOffsetInMemory.TryGetValue(value, out var pointerInfo))
                {
                    map[offset] = pointerInfo;
                }
            }

            return map.Count > 0 ? map : null;
        }

        private static string BuildBlockSegmentId(string sourceTag, byte module, byte blockId, int index)
        {
            return $"{sourceTag}:block:{module:X2}:{blockId:X2}:{index:X4}";
        }

        private sealed class HypeSegment
        {
            public HypeSegment(
                string id,
                byte[] data,
                int? baseInMemory,
                Dictionary<uint, HypeRelocationPointerInfo>? blockPointerMap,
                Dictionary<int, HypeRelocationPointerInfo>? pointerFilePointerMap)
            {
                Id = id;
                Data = data;
                BaseInMemory = baseInMemory;
                BlockPointerMap = blockPointerMap;
                PointerFilePointerMap = pointerFilePointerMap;
            }

            public string Id { get; }
            public byte[] Data { get; }
            public int? BaseInMemory { get; }
            public Dictionary<uint, HypeRelocationPointerInfo>? BlockPointerMap { get; }
            public Dictionary<int, HypeRelocationPointerInfo>? PointerFilePointerMap { get; }
        }
    }

    private sealed class HypeMemoryReader
    {
        private readonly HypeRelocatedAddressSpace _space;
        private HypeAddress _position;

        public HypeMemoryReader(HypeRelocatedAddressSpace space, HypeAddress start)
        {
            _space = space;
            _position = start;
        }

        public HypeAddress Position => _position;

        public byte ReadByte()
        {
            var bytes = ReadBytesInternal(1);
            return bytes[0];
        }

        public short ReadInt16()
        {
            var bytes = ReadBytesInternal(2);
            return BitConverter.ToInt16(bytes, 0);
        }

        public ushort ReadUInt16()
        {
            var bytes = ReadBytesInternal(2);
            return BitConverter.ToUInt16(bytes, 0);
        }

        public int ReadInt32()
        {
            var bytes = ReadBytesInternal(4);
            return BitConverter.ToInt32(bytes, 0);
        }

        public uint ReadUInt32()
        {
            var bytes = ReadBytesInternal(4);
            return BitConverter.ToUInt32(bytes, 0);
        }

        public float ReadSingle()
        {
            var bytes = ReadBytesInternal(4);
            return BitConverter.ToSingle(bytes, 0);
        }

        public string ReadFixedString(int length)
        {
            var bytes = ReadBytesInternal(length);
            var zeroIndex = Array.IndexOf(bytes, (byte)0);
            var count = zeroIndex >= 0 ? zeroIndex : bytes.Length;
            return System.Text.Encoding.ASCII.GetString(bytes, 0, count);
        }

        public HypeAddress? ReadPointer()
        {
            var fieldAddress = _position;
            var raw = ReadUInt32();
            if (raw == 0 || raw == 0xFFFFFFFF)
            {
                return null;
            }

            if (_space.TryResolvePointer(fieldAddress, raw, out var resolved))
            {
                return resolved;
            }

            if (_space.TryResolveRawAddress(raw, out resolved))
            {
                return resolved;
            }

            return null;
        }

        private byte[] ReadBytesInternal(int length)
        {
            if (!_space.TryGetDataSlice(_position, length, out var span))
            {
                throw new InvalidDataException($"Read out of bounds at {_position} (+{length}).");
            }

            var bytes = span.ToArray();
            _position = _position.Add(length);
            return bytes;
        }
    }
}
