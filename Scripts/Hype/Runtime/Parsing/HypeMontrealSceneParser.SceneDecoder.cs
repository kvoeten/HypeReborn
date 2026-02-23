using System;
using System.Collections.Generic;
using Godot;

namespace HypeReborn.Hype.Runtime.Parsing;

public static partial class HypeMontrealSceneParser
{
    private sealed class HypeSceneDecoder
    {
        private readonly HypeRelocatedAddressSpace _space;
        private readonly List<string> _diagnostics;
        private readonly Dictionary<HypeAddress, ParsedSuperObject> _superObjects = new();
        private readonly Dictionary<HypeAddress, HypeResolvedMesh?> _ipoMeshByAddress = new();
        private readonly Dictionary<HypeAddress, HypeResolvedMesh?> _physicalMeshByAddress = new();
        private readonly Dictionary<HypeAddress, HypeResolvedMesh?> _geoMeshByAddress = new();
        private readonly Dictionary<HypeAddress, HypeParsedVisualMaterial> _visualMaterials = new();
        private readonly Dictionary<HypeAddress, HypeParsedTextureInfo> _textureInfos = new();
        private readonly Dictionary<HypeAddress, HypeParsedGameMaterial> _gameMaterials = new();
        private readonly HashSet<HypeAddress> _sectorCharacterDataAddresses = new();

        public HypeSceneDecoder(HypeRelocatedAddressSpace space, List<string> diagnostics)
        {
            _space = space;
            _diagnostics = diagnostics;
        }

        public IReadOnlyList<HypeResolvedEntity> Decode(IReadOnlyList<HypeAddress> roots)
        {
            var entities = new List<HypeResolvedEntity>();
            var traversalStack = new HashSet<HypeAddress>();
            _sectorCharacterDataAddresses.Clear();
            CollectSectorCharacterDataAddresses(roots);

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

                if (superObject.Type == MontrealSuperObjectType.Perso && superObject.DataAddress.HasValue)
                {
                    var persoState = ReadPersoRuntimeState(superObject.DataAddress.Value);
                    var actorId = $"perso:{superObjectAddress}";
                    var isSectorCharacterMember = _sectorCharacterDataAddresses.Contains(superObject.DataAddress.Value);
                    entities.Add(new HypeResolvedEntity
                    {
                        Id = $"actor:{superObjectAddress}",
                        Name = BuildActorEntityName(superObjectAddress, persoState.CustomBits, isSectorCharacterMember),
                        Kind = HypeResolvedEntityKind.Actor,
                        Transform = worldTransform,
                        ActorId = actorId,
                        IsMainActor = (persoState.CustomBits & MainActorBit) != 0,
                        IsSectorCharacterListMember = isSectorCharacterMember,
                        IsTargettable = (persoState.CustomBits & TargettableCustomBit) != 0,
                        CustomBits = persoState.CustomBits
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
                    DataAddress = offData,
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

        private void CollectSectorCharacterDataAddresses(IReadOnlyList<HypeAddress> roots)
        {
            var traversalStack = new HashSet<HypeAddress>();
            foreach (var root in roots)
            {
                CollectSectorCharacterDataAddressesRecursive(root, traversalStack);
            }
        }

        private void CollectSectorCharacterDataAddressesRecursive(
            HypeAddress superObjectAddress,
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

                if (superObject.Type == MontrealSuperObjectType.Sector && superObject.DataAddress.HasValue)
                {
                    ReadSectorCharacterDataAddresses(superObject.DataAddress.Value);
                }

                var visitedChildren = new HashSet<HypeAddress>();
                var nextChild = superObject.FirstChild;
                var remaining = (int)Math.Min(superObject.ChildCount, MaxChildChain);
                while (nextChild.HasValue && remaining-- > 0 && visitedChildren.Add(nextChild.Value))
                {
                    CollectSectorCharacterDataAddressesRecursive(nextChild.Value, traversalStack);
                    nextChild = ParseSuperObject(nextChild.Value)?.NextBrother;
                }
            }
            finally
            {
                traversalStack.Remove(superObjectAddress);
            }
        }

        private void ReadSectorCharacterDataAddresses(HypeAddress sectorDataAddress)
        {
            try
            {
                var listHeaderReader = _space.CreateReader(sectorDataAddress.Add(28));
                var nextNode = listHeaderReader.ReadPointer();
                var visitedNodes = new HashSet<HypeAddress>();
                var remaining = MaxLinkedListTraversal;
                while (nextNode.HasValue && remaining-- > 0 && visitedNodes.Add(nextNode.Value))
                {
                    var nodeReader = _space.CreateReader(nextNode.Value);
                    var connection = nodeReader.ReadPointer();
                    var next = nodeReader.ReadPointer();
                    if (connection.HasValue)
                    {
                        _sectorCharacterDataAddresses.Add(connection.Value);
                    }

                    nextNode = next;
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse sector character list at {sectorDataAddress}: {ex.Message}");
            }
        }

        private PersoRuntimeState ReadPersoRuntimeState(HypeAddress persoDataAddress)
        {
            try
            {
                var reader = _space.CreateReader(persoDataAddress);
                _ = reader.ReadPointer(); // off3dData
                var offStdGame = reader.ReadPointer();
                var customBits = ReadCustomBits(offStdGame);
                return new PersoRuntimeState(customBits);
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse Perso runtime state at {persoDataAddress}: {ex.Message}");
                return new PersoRuntimeState(0);
            }
        }

        private uint ReadCustomBits(HypeAddress? stdGameAddress)
        {
            if (!stdGameAddress.HasValue)
            {
                return 0;
            }

            // Rayman/Hype runtime typically reads custom bits from *(offStdGame+4)+44.
            if (TryReadPointerAt(stdGameAddress.Value, 4, out var nestedStdGame) &&
                TryReadUInt32At(nestedStdGame, 44, out var nestedBits))
            {
                return nestedBits;
            }

            if (TryReadUInt32At(stdGameAddress.Value, 44, out var directBits))
            {
                return directBits;
            }

            if (TryReadUInt32At(stdGameAddress.Value, 36, out var legacyBits))
            {
                return legacyBits;
            }

            return 0;
        }

        private bool TryReadPointerAt(HypeAddress baseAddress, int offset, out HypeAddress pointer)
        {
            pointer = default;
            try
            {
                var reader = _space.CreateReader(baseAddress.Add(offset));
                var value = reader.ReadPointer();
                if (!value.HasValue)
                {
                    return false;
                }

                pointer = value.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryReadUInt32At(HypeAddress baseAddress, int offset, out uint value)
        {
            value = 0;
            try
            {
                var reader = _space.CreateReader(baseAddress.Add(offset));
                value = reader.ReadUInt32();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildActorEntityName(HypeAddress superObjectAddress, uint customBits, bool isSectorCharacterMember)
        {
            string role;
            if ((customBits & MainActorBit) != 0)
            {
                role = "hero";
            }
            else if (!isSectorCharacterMember)
            {
                role = "level_actor";
            }
            else if ((customBits & TargettableCustomBit) != 0)
            {
                role = "enemy";
            }
            else
            {
                role = "npc";
            }

            return $"{role}_{superObjectAddress.Offset:X8}";
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

        private HypeParsedGameMaterial? ParseGameMaterial(HypeAddress address)
        {
            return HypeMaterialDecoder.ParseGameMaterial(
                _space,
                address,
                _gameMaterials,
                _visualMaterials,
                _textureInfos,
                _diagnostics);
        }

        private static bool IsInRange(int value, int length)
        {
            return HypeGeometryReader.IsInRange(value, length);
        }

        private static Vector3 ComputeFaceNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            return HypeGeometryReader.ComputeFaceNormal(a, b, c);
        }

        private static Vector3? TryGetVertexNormal(Vector3[]? sourceVertexNormals, int index)
        {
            return HypeGeometryReader.TryGetVertexNormal(sourceVertexNormals, index);
        }

        private static bool TryGetFaceNormal(Vector3[]? sourceFaceNormals, int triangleIndex, out Vector3 normal)
        {
            return HypeGeometryReader.TryGetFaceNormal(sourceFaceNormals, triangleIndex, out normal);
        }

        private static int WrapIndex(int index, int length)
        {
            return HypeGeometryReader.WrapIndex(index, length);
        }

        private Vector3[] ReadXzyVector3Array(HypeAddress address, int count)
        {
            return HypeGeometryReader.ReadXzyVector3Array(_space, address, count);
        }

        private int[] ReadInt16Array(HypeAddress address, int count)
        {
            return HypeGeometryReader.ReadInt16Array(_space, address, count);
        }

        private ushort[] ReadUInt16Array(HypeAddress address, int count)
        {
            return HypeGeometryReader.ReadUInt16Array(_space, address, count);
        }

        private Vector2[] ReadVector2Array(HypeAddress address, int count)
        {
            return HypeGeometryReader.ReadVector2Array(_space, address, count);
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
            public required HypeAddress? DataAddress { get; init; }
            public required HypeAddress? FirstChild { get; init; }
            public required uint ChildCount { get; init; }
            public required HypeAddress? NextBrother { get; init; }
            public required Transform3D LocalTransform { get; init; }
            public required HypeResolvedMesh? Mesh { get; init; }
        }

        private readonly struct PersoRuntimeState
        {
            public PersoRuntimeState(uint customBits)
            {
                CustomBits = customBits;
            }

            public uint CustomBits { get; }
        }
    }
}
