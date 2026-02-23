using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace HypeReborn.Hype.Runtime.Parsing;

public static partial class HypeMontrealCharacterParser
{
    private sealed class HypeCharacterDecoder
    {
        private readonly HypeRelocatedAddressSpace _space;
        private readonly List<string> _diagnostics;
        private readonly Dictionary<HypeAddress, ParsedSuperObject> _superObjects = new();
        private readonly Dictionary<HypeAddress, HypeResolvedMesh?> _physicalMeshByAddress = new();
        private readonly Dictionary<HypeAddress, HypeResolvedMesh?> _geoMeshByAddress = new();
        private readonly Dictionary<HypeAddress, HypeParsedVisualMaterial> _visualMaterials = new();
        private readonly Dictionary<HypeAddress, HypeParsedTextureInfo> _textureInfos = new();
        private readonly Dictionary<HypeAddress, HypeParsedGameMaterial> _gameMaterials = new();
        private readonly HashSet<ushort> _reportedUnsupportedVisualElementTypes = new();

        public HypeCharacterDecoder(HypeRelocatedAddressSpace space, List<string> diagnostics)
        {
            _space = space;
            _diagnostics = diagnostics;
        }

        public HypeCharacterActorAsset? ParseBestActor(IReadOnlyList<HypeAddress> roots, string levelName)
        {
            var actors = ParseActors(roots, levelName);
            if (actors.Count == 0)
            {
                return null;
            }

            return actors[0];
        }

        public IReadOnlyList<HypeCharacterActorAsset> ParseActors(IReadOnlyList<HypeAddress> roots, string levelName)
        {
            var candidates = new List<PersoCandidate>();
            var sectorCharacterDataAddresses = CollectSectorCharacterDataAddresses(roots);
            var traversalStack = new HashSet<HypeAddress>();
            foreach (var root in roots)
            {
                ParseSuperObjectRecursive(root, candidates, traversalStack, sectorCharacterDataAddresses);
            }

            var actors = candidates
                .Where(x => x.Actor != null)
                .GroupBy(x => x.Actor!.ActorId, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var preferred = group
                        .OrderByDescending(x => x.IsMainActor)
                        .ThenByDescending(x => x.IsSectorCharacterListMember)
                        .ThenByDescending(x => x.ChannelCount)
                        .ThenByDescending(x => x.FrameCount)
                        .ThenByDescending(x => x.ObjectVisualCount)
                        .First();
                    return preferred.Actor!;
                })
                .OrderByDescending(x => x.IsMainActor)
                .ThenByDescending(x => x.IsSectorCharacterListMember)
                .ThenByDescending(x => x.ChannelCount)
                .ThenByDescending(x => x.Frames.Count)
                .ThenByDescending(x => x.Objects.Count)
                .ToArray();

            if (actors.Length == 0)
            {
                _diagnostics.Add($"No Montreal actor with animation was discovered in '{levelName}'.");
            }

            return actors;
        }

        private void ParseSuperObjectRecursive(
            HypeAddress superObjectAddress,
            List<PersoCandidate> candidates,
            HashSet<HypeAddress> traversalStack,
            HashSet<HypeAddress> sectorCharacterDataAddresses)
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

                if (superObject.Type == MontrealSuperObjectType.Perso && superObject.DataAddress.HasValue)
                {
                    var isSectorCharacterMember = sectorCharacterDataAddresses.Contains(superObject.DataAddress.Value);
                    var candidate = ParsePerso(superObject, superObjectAddress, isSectorCharacterMember);
                    if (candidate?.Actor != null)
                    {
                        candidates.Add(candidate);
                    }
                }

                var visitedChildren = new HashSet<HypeAddress>();
                var nextChild = superObject.FirstChild;
                var remaining = (int)Math.Min(superObject.ChildCount, MaxChildChain);
                while (nextChild.HasValue && remaining-- > 0 && visitedChildren.Add(nextChild.Value))
                {
                    ParseSuperObjectRecursive(nextChild.Value, candidates, traversalStack, sectorCharacterDataAddresses);
                    nextChild = ParseSuperObject(nextChild.Value)?.NextBrother;
                }
            }
            finally
            {
                traversalStack.Remove(superObjectAddress);
            }
        }

        private PersoCandidate? ParsePerso(
            ParsedSuperObject superObject,
            HypeAddress superObjectAddress,
            bool isSectorCharacterMember)
        {
            try
            {
                var reader = _space.CreateReader(superObject.DataAddress!.Value);
                var off3dData = reader.ReadPointer();
                var offStdGame = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadUInt32();
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadUInt32();
                _ = reader.ReadPointer();

                if (!off3dData.HasValue)
                {
                    return null;
                }

                var customBits = ReadCustomBits(offStdGame);
                var isMainActor = (customBits & MainActorBit) != 0;
                var isTargettable = (customBits & TargettableCustomBit) != 0;

                var p3dReader = _space.CreateReader(off3dData.Value);
                _ = p3dReader.ReadPointer();
                var offStateCurrent = p3dReader.ReadPointer();
                _ = p3dReader.ReadPointer();
                var offObjectList = p3dReader.ReadPointer();
                var offObjectListInitial = p3dReader.ReadPointer();
                _ = p3dReader.ReadPointer();

                if (!offStateCurrent.HasValue)
                {
                    return null;
                }

                Dictionary<int, HypeCharacterObjectVisual>? objectVisuals = null;
                if (offObjectList.HasValue)
                {
                    objectVisuals = ParseObjectList(offObjectList.Value);
                }

                if (offObjectListInitial.HasValue &&
                    (!offObjectList.HasValue || !offObjectListInitial.Value.Equals(offObjectList.Value)))
                {
                    var initialObjectVisuals = ParseObjectList(offObjectListInitial.Value);
                    objectVisuals = SelectBetterObjectVisualSet(objectVisuals, initialObjectVisuals);
                }

                if (objectVisuals == null || objectVisuals.Count == 0)
                {
                    return null;
                }

                var stateReader = _space.CreateReader(offStateCurrent.Value);
                _ = stateReader.ReadPointer();
                _ = stateReader.ReadPointer();
                _ = stateReader.ReadPointer();
                var offAnimation = stateReader.ReadPointer();
                SkipLinkedListHeader(stateReader);
                SkipLinkedListHeader(stateReader);
                _ = stateReader.ReadPointer();
                _ = stateReader.ReadPointer();
                _ = stateReader.ReadUInt32();
                _ = stateReader.ReadUInt32();
                _ = stateReader.ReadByte();
                _ = stateReader.ReadByte();
                _ = stateReader.ReadByte();
                var stateSpeed = stateReader.ReadByte();

                if (!offAnimation.HasValue)
                {
                    return null;
                }

                if (!TryParseMontrealAnimation(offAnimation.Value, out var frames, out var channelCount))
                {
                    return null;
                }

                var actor = new HypeCharacterActorAsset
                {
                    LevelName = string.Empty,
                    ActorId = $"perso:{superObjectAddress}",
                    IsMainActor = isMainActor,
                    IsSectorCharacterListMember = isSectorCharacterMember,
                    IsTargettable = isTargettable,
                    CustomBits = customBits,
                    FramesPerSecond = Math.Max(1f, stateSpeed),
                    ChannelCount = channelCount,
                    Objects = objectVisuals.Values.OrderBy(x => x.ObjectIndex).ToArray(),
                    Frames = frames
                };

                return new PersoCandidate
                {
                    IsMainActor = isMainActor,
                    IsSectorCharacterListMember = isSectorCharacterMember,
                    ChannelCount = actor.ChannelCount,
                    FrameCount = actor.Frames.Count,
                    ObjectVisualCount = actor.Objects.Count,
                    Actor = actor
                };
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse Perso '{superObjectAddress}': {ex.Message}");
                return null;
            }
        }

        private bool TryParseMontrealAnimation(
            HypeAddress animationAddress,
            out IReadOnlyList<HypeCharacterFrameAsset> frames,
            out int channelCount)
        {
            frames = Array.Empty<HypeCharacterFrameAsset>();
            channelCount = 0;

            try
            {
                var reader = _space.CreateReader(animationAddress);
                var offFrames = reader.ReadPointer();
                var numFrames = reader.ReadByte();
                _ = reader.ReadByte();
                var numChannels = reader.ReadByte();
                _ = reader.ReadByte();
                _ = reader.ReadPointer();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                for (var i = 0; i < 21; i++)
                {
                    _ = reader.ReadSingle();
                }
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();

                if (!offFrames.HasValue || numFrames == 0 || numChannels == 0)
                {
                    return false;
                }

                channelCount = numChannels;
                var frameList = new List<HypeCharacterFrameAsset>(numFrames);
                for (var frameIndex = 0; frameIndex < numFrames; frameIndex++)
                {
                    var frameReader = _space.CreateReader(offFrames.Value.Add(frameIndex * 16));
                    var offChannels = frameReader.ReadPointer();
                    _ = frameReader.ReadPointer();
                    _ = frameReader.ReadPointer();
                    var offHierarchies = frameReader.ReadPointer();

                    var samples = new HypeCharacterChannelSample[numChannels];
                    for (var i = 0; i < numChannels; i++)
                    {
                        samples[i] = new HypeCharacterChannelSample(-1, Transform3D.Identity);
                    }

                    if (offChannels.HasValue)
                    {
                        var pointerReader = _space.CreateReader(offChannels.Value);
                        for (var channelIndex = 0; channelIndex < numChannels; channelIndex++)
                        {
                            var offChannel = pointerReader.ReadPointer();
                            if (!offChannel.HasValue)
                            {
                                continue;
                            }

                            samples[channelIndex] = ParseMontrealChannelSample(offChannel.Value);
                        }
                    }

                    var parentByChannel = Enumerable.Repeat(-1, numChannels).ToArray();
                    if (offHierarchies.HasValue)
                    {
                        var hierarchyHeaderReader = _space.CreateReader(offHierarchies.Value);
                        var hierarchyCount = (int)hierarchyHeaderReader.ReadUInt32();
                        var offHierarchyArray = hierarchyHeaderReader.ReadPointer();
                        if (hierarchyCount > 0 && offHierarchyArray.HasValue)
                        {
                            var hierarchyReader = _space.CreateReader(offHierarchyArray.Value);
                            for (var i = 0; i < hierarchyCount; i++)
                            {
                                var child = hierarchyReader.ReadInt16();
                                var parent = hierarchyReader.ReadInt16();
                                if (child >= 0 && child < numChannels && parent >= 0 && parent < numChannels && child != parent)
                                {
                                    parentByChannel[child] = parent;
                                }
                            }
                        }
                    }

                    frameList.Add(new HypeCharacterFrameAsset
                    {
                        ChannelSamples = samples,
                        ParentChannelIndices = parentByChannel
                    });
                }

                frames = frameList;
                return frameList.Count > 0;
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse Montreal animation {animationAddress}: {ex.Message}");
                return false;
            }
        }

        private HypeCharacterChannelSample ParseMontrealChannelSample(HypeAddress channelAddress)
        {
            try
            {
                var reader = _space.CreateReader(channelAddress);
                var pointerFieldAddress = reader.Position;
                var matrixPointerOrFlag = reader.ReadUInt32();
                var objectIndexRaw = reader.ReadByte();
                _ = reader.ReadByte();
                _ = reader.ReadInt16();
                _ = reader.ReadInt16();
                _ = reader.ReadByte();
                _ = reader.ReadByte();
                _ = reader.ReadUInt32();

                var objectIndex = objectIndexRaw == byte.MaxValue ? -1 : objectIndexRaw;
                var transform = Transform3D.Identity;
                if (matrixPointerOrFlag > 1 && TryResolvePointer(pointerFieldAddress, matrixPointerOrFlag, out var matrixAddress))
                {
                    transform = ReadCompressedTransform(matrixAddress);
                }

                return new HypeCharacterChannelSample(objectIndex, transform);
            }
            catch
            {
                return new HypeCharacterChannelSample(-1, Transform3D.Identity);
            }
        }

        private static void SkipLinkedListHeader(HypeMemoryReader reader)
        {
            _ = reader.ReadPointer();
            _ = reader.ReadPointer();
            _ = reader.ReadUInt32();
        }

        private HashSet<HypeAddress> CollectSectorCharacterDataAddresses(IReadOnlyList<HypeAddress> roots)
        {
            var output = new HashSet<HypeAddress>();
            var traversalStack = new HashSet<HypeAddress>();
            foreach (var root in roots)
            {
                CollectSectorCharacterDataAddressesRecursive(root, traversalStack, output);
            }

            return output;
        }

        private void CollectSectorCharacterDataAddressesRecursive(
            HypeAddress superObjectAddress,
            HashSet<HypeAddress> traversalStack,
            HashSet<HypeAddress> output)
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
                    ReadSectorCharacterDataAddresses(superObject.DataAddress.Value, output);
                }

                var visitedChildren = new HashSet<HypeAddress>();
                var nextChild = superObject.FirstChild;
                var remaining = (int)Math.Min(superObject.ChildCount, MaxChildChain);
                while (nextChild.HasValue && remaining-- > 0 && visitedChildren.Add(nextChild.Value))
                {
                    CollectSectorCharacterDataAddressesRecursive(nextChild.Value, traversalStack, output);
                    nextChild = ParseSuperObject(nextChild.Value)?.NextBrother;
                }
            }
            finally
            {
                traversalStack.Remove(superObjectAddress);
            }
        }

        private void ReadSectorCharacterDataAddresses(HypeAddress sectorDataAddress, HashSet<HypeAddress> output)
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
                        output.Add(connection.Value);
                    }

                    nextNode = next;
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse sector character list at {sectorDataAddress}: {ex.Message}");
            }
        }

        private uint ReadCustomBits(HypeAddress? stdGameAddress)
        {
            if (!stdGameAddress.HasValue)
            {
                return 0;
            }

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

        private Dictionary<int, HypeCharacterObjectVisual> ParseObjectList(HypeAddress objectListAddress)
        {
            var output = new Dictionary<int, HypeCharacterObjectVisual>();
            try
            {
                var reader = _space.CreateReader(objectListAddress);
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                var offStart = reader.ReadPointer();
                _ = reader.ReadPointer();
                var entryCount = reader.ReadUInt16();
                _ = reader.ReadUInt16();

                if (!offStart.HasValue || entryCount == 0)
                {
                    return output;
                }

                var entryReader = _space.CreateReader(offStart.Value);
                for (var objectIndex = 0; objectIndex < entryCount; objectIndex++)
                {
                    var offScale = entryReader.ReadPointer();
                    var offPhysicalObject = entryReader.ReadPointer();
                    _ = entryReader.ReadUInt32();
                    _ = entryReader.ReadUInt16();
                    _ = entryReader.ReadUInt16();
                    _ = entryReader.ReadUInt32();

                    if (!offPhysicalObject.HasValue)
                    {
                        continue;
                    }

                    var scale = Vector3.One;
                    if (offScale.HasValue)
                    {
                        var scaleReader = _space.CreateReader(offScale.Value);
                        var x = scaleReader.ReadSingle();
                        var z = scaleReader.ReadSingle();
                        var y = scaleReader.ReadSingle();
                        scale = new Vector3(x, y, z);
                    }

                    output[objectIndex] = new HypeCharacterObjectVisual
                    {
                        ObjectIndex = objectIndex,
                        ScaleMultiplier = scale,
                        Mesh = ParsePhysicalObjectMesh(offPhysicalObject.Value)
                    };
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse ObjectList {objectListAddress}: {ex.Message}");
            }

            return output;
        }

        private Transform3D ReadCompressedTransform(HypeAddress address)
        {
            var reader = _space.CreateReader(address);
            var packedType = reader.ReadUInt16();
            var actualType = packedType < 128 ? packedType & 0xF : 128;

            var rawPosition = Vector3.Zero;
            var rawScale = Vector3.One;
            var rotation = Quaternion.Identity;
            var hasRotation = false;

            if (actualType is 1 or 3 or 7 or 11 or 15)
            {
                var x = reader.ReadInt16() / 512f;
                var y = reader.ReadInt16() / 512f;
                var z = reader.ReadInt16() / 512f;
                rawPosition = new Vector3(x, y, z);
            }

            if (actualType is 2 or 3 or 7 or 11 or 15)
            {
                var w = reader.ReadInt16() / (float)short.MaxValue;
                var x = reader.ReadInt16() / (float)short.MaxValue;
                var y = reader.ReadInt16() / (float)short.MaxValue;
                var z = reader.ReadInt16() / (float)short.MaxValue;
                var rawQuaternion = new Quaternion(x, y, z, w);
                var internalQuaternion = ConvertMatrixColumnsToQuaternion(BuildRotationColumns(rawQuaternion), convertAxes: false);
                rotation = ConvertMatrixColumnsToQuaternion(BuildRotationColumns(internalQuaternion), convertAxes: true);
                hasRotation = true;
            }

            if (actualType == 7)
            {
                var uniform = reader.ReadInt16() / 256f;
                rawScale = new Vector3(uniform, uniform, uniform);
            }
            else if (actualType == 11)
            {
                var x = reader.ReadInt16() / 256f;
                var y = reader.ReadInt16() / 256f;
                var z = reader.ReadInt16() / 256f;
                rawScale = new Vector3(x, y, z);
            }
            else if (actualType == 15)
            {
                var m0 = reader.ReadInt16() / 256f;
                _ = reader.ReadInt16();
                _ = reader.ReadInt16();
                var m3 = reader.ReadInt16() / 256f;
                _ = reader.ReadInt16();
                var m5 = reader.ReadInt16() / 256f;
                rawScale = new Vector3(m0, m3, m5);
            }

            var position = new Vector3(rawPosition.X, rawPosition.Z, rawPosition.Y);
            var scale = new Vector3(rawScale.X, rawScale.Z, rawScale.Y);
            var basis = new Basis(hasRotation ? rotation : Quaternion.Identity).Scaled(scale);
            return new Transform3D(basis, position);
        }

        private bool TryResolvePointer(HypeAddress pointerFieldAddress, uint rawValue, out HypeAddress address)
        {
            if (_space.TryResolvePointer(pointerFieldAddress, rawValue, out address))
            {
                return true;
            }

            return _space.TryResolveRawAddress(rawValue, out address);
        }

        private static (Vector3 Col0, Vector3 Col1, Vector3 Col2) BuildRotationColumns(Quaternion quaternion)
        {
            var x = quaternion.X;
            var y = quaternion.Y;
            var z = quaternion.Z;
            var w = quaternion.W;
            var magnitude = MathF.Sqrt((x * x) + (y * y) + (z * z) + (w * w));
            if (magnitude <= float.Epsilon)
            {
                return (Vector3.Right, Vector3.Up, Vector3.Back);
            }

            var inv = 1f / magnitude;
            x *= inv;
            y *= inv;
            z *= inv;
            w *= inv;

            var twoX = 2f * x;
            var twoY = 2f * y;
            var twoZ = 2f * z;
            var xw = twoX * w;
            var yw = twoY * w;
            var zw = twoZ * w;
            var xx = twoX * x;
            var yx = twoY * x;
            var zx = twoZ * x;
            var yy = twoY * y;
            var zy = twoZ * y;
            var zz = twoZ * z;

            var m00 = 1f - (zz + yy);
            var m01 = yx + zw;
            var m02 = zx - yw;
            var m10 = yx - zw;
            var m11 = 1f - (zz + xx);
            var m12 = zy + xw;
            var m20 = zx + yw;
            var m21 = zy - xw;
            var m22 = 1f - (yy + xx);

            return (
                new Vector3(m00, m10, m20),
                new Vector3(m01, m11, m21),
                new Vector3(m02, m12, m22));
        }

        private static Quaternion ConvertMatrixColumnsToQuaternion((Vector3 Col0, Vector3 Col1, Vector3 Col2) columns, bool convertAxes)
        {
            var m00 = columns.Col0.X;
            var m01 = columns.Col1.X;
            var m02 = columns.Col2.X;
            var m10 = columns.Col0.Y;
            var m11 = columns.Col1.Y;
            var m12 = columns.Col2.Y;
            var m20 = columns.Col0.Z;
            var m21 = columns.Col1.Z;
            var m22 = columns.Col2.Z;

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
            if (!convertAxes)
            {
                return q.Normalized();
            }

            return new Quaternion(q.X, q.Z, q.Y, -q.W).Normalized();
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
                _ = reader.ReadPointer();
                _ = reader.ReadPointer();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadUInt32();
                _ = reader.ReadPointer();

                var parsed = new ParsedSuperObject
                {
                    TypeCode = typeCode,
                    Type = GetMontrealSuperObjectType(typeCode),
                    DataAddress = offData,
                    FirstChild = offChildHead,
                    ChildCount = childCount,
                    NextBrother = offBrotherNext
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
                var offLodDistances = visualSetReader.ReadPointer();
                var offLodDataOffsets = visualSetReader.ReadPointer();
                _ = visualSetReader.ReadUInt32();

                if (lodCount == 0 || !offLodDataOffsets.HasValue || (visualSetType != 0 && visualSetType != 1))
                {
                    _physicalMeshByAddress[physicalObjectAddress] = null;
                    return null;
                }

                var lodCandidates = ReadLodCandidates(lodCount, offLodDataOffsets.Value, offLodDistances);
                foreach (var lod in lodCandidates.OrderBy(x => x.Distance))
                {
                    mesh = visualSetType switch
                    {
                        0 => ParseGeometricObjectMesh(lod.Address),
                        1 => ParsePatchGeometricObjectMesh(lod.Address),
                        _ => null
                    };

                    if (mesh != null)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse PhysicalObject {physicalObjectAddress}: {ex.Message}");
            }

            _physicalMeshByAddress[physicalObjectAddress] = mesh;
            return mesh;
        }

        private HypeResolvedMesh? ParsePatchGeometricObjectMesh(HypeAddress patchObjectAddress)
        {
            try
            {
                var reader = _space.CreateReader(patchObjectAddress);
                var offGeometricObject = reader.ReadPointer();
                var propertyCount = (int)reader.ReadUInt32();
                var offProperties = reader.ReadPointer();
                if (!offGeometricObject.HasValue)
                {
                    return null;
                }

                Dictionary<int, Vector3>? vertexOverrides = null;
                if (propertyCount > 0 && offProperties.HasValue)
                {
                    vertexOverrides = new Dictionary<int, Vector3>(propertyCount);
                    var propertyReader = _space.CreateReader(offProperties.Value);
                    for (var i = 0; i < propertyCount; i++)
                    {
                        var vertexIndex = propertyReader.ReadUInt16();
                        _ = propertyReader.ReadUInt16();
                        var x = propertyReader.ReadSingle();
                        var z = propertyReader.ReadSingle();
                        var y = propertyReader.ReadSingle();
                        var delta = new Vector3(x, y, z);
                        if (vertexOverrides.TryGetValue(vertexIndex, out var existing))
                        {
                            vertexOverrides[vertexIndex] = existing + delta;
                        }
                        else
                        {
                            vertexOverrides[vertexIndex] = delta;
                        }
                    }
                }

                return ParseGeometricObjectMesh(offGeometricObject.Value, vertexOverrides);
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse PatchGeometricObject {patchObjectAddress}: {ex.Message}");
                return null;
            }
        }

        private IReadOnlyList<LodCandidate> ReadLodCandidates(
            int lodCount,
            HypeAddress offLodDataOffsets,
            HypeAddress? offLodDistances)
        {
            var output = new List<LodCandidate>(lodCount);
            var lodAddressReader = _space.CreateReader(offLodDataOffsets);
            HypeMemoryReader? lodDistanceReader = null;
            if (offLodDistances.HasValue)
            {
                try
                {
                    lodDistanceReader = _space.CreateReader(offLodDistances.Value);
                }
                catch
                {
                    lodDistanceReader = null;
                }
            }

            for (var i = 0; i < lodCount; i++)
            {
                var lodAddr = lodAddressReader.ReadPointer();
                float lodDistance = i;
                if (lodDistanceReader != null)
                {
                    lodDistance = lodDistanceReader.ReadSingle();
                }

                if (lodAddr.HasValue)
                {
                    output.Add(new LodCandidate(lodAddr.Value, lodDistance));
                }
            }

            return output;
        }

        private HypeResolvedMesh? ParseGeometricObjectMesh(HypeAddress geometricObjectAddress)
        {
            if (_geoMeshByAddress.TryGetValue(geometricObjectAddress, out var cached))
            {
                return cached;
            }

            var mesh = ParseGeometricObjectMeshInternal(geometricObjectAddress, vertexOverrides: null);
            _geoMeshByAddress[geometricObjectAddress] = mesh;
            return mesh;
        }

        private HypeResolvedMesh? ParseGeometricObjectMesh(
            HypeAddress geometricObjectAddress,
            IReadOnlyDictionary<int, Vector3>? vertexOverrides)
        {
            if (vertexOverrides == null || vertexOverrides.Count == 0)
            {
                return ParseGeometricObjectMesh(geometricObjectAddress);
            }

            return ParseGeometricObjectMeshInternal(geometricObjectAddress, vertexOverrides);
        }

        private HypeResolvedMesh? ParseGeometricObjectMeshInternal(
            HypeAddress geometricObjectAddress,
            IReadOnlyDictionary<int, Vector3>? vertexOverrides)
        {
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
                    return null;
                }

                var vertices = ReadXzyVector3Array(offVertices.Value, vertexCount);
                ApplyVertexOverrides(vertices, vertexOverrides);

                var vertexNormals = offNormals.HasValue
                    ? ReadXzyVector3Array(offNormals.Value, vertexCount)
                    : null;
                var elementTypes = ReadUInt16Array(offElementTypes.Value, elementCount);

                var surfaces = new List<HypeResolvedMeshSurface>();
                for (var elementIndex = 0; elementIndex < elementCount; elementIndex++)
                {
                    var elementPointerReader = _space.CreateReader(offElements.Value.Add(elementIndex * 4));
                    var offElement = elementPointerReader.ReadPointer();
                    if (!offElement.HasValue)
                    {
                        continue;
                    }

                    var elementType = elementTypes[elementIndex];
                    var surface = elementType switch
                    {
                        1 => ParseTriangleElement(offElement.Value, vertices, vertexNormals),
                        3 => ParseSpriteElement(offElement.Value, vertices),
                        _ => null
                    };

                    if (surface != null)
                    {
                        surfaces.Add(surface);
                        continue;
                    }

                    if (elementType != 1 && elementType != 3 && _reportedUnsupportedVisualElementTypes.Add(elementType))
                    {
                        _diagnostics.Add(
                            $"Unsupported geometric visual element type {elementType} in GeometricObject {geometricObjectAddress}.");
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
                var sourceFaceNormals = offNormals.HasValue ? ReadXzyVector3Array(offNormals.Value, triangleCount) : null;

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

                    if (TryGetFaceNormal(sourceFaceNormals, triangleIndex, out var authoredFaceNormal))
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
                    var fallbackFaceNormal = sourceFaceNormals != null && triangleIndex < sourceFaceNormals.Length
                        ? sourceFaceNormals[triangleIndex]
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

        private HypeResolvedMeshSurface? ParseSpriteElement(HypeAddress elementAddress, Vector3[] sourceVertices)
        {
            try
            {
                var reader = _space.CreateReader(elementAddress);
                var scaleX = reader.ReadSingle();
                var scaleY = reader.ReadSingle();
                var offVisualMaterial = reader.ReadPointer();
                var vertexIndex = (int)reader.ReadUInt16();
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt16();

                if (!IsInRange(vertexIndex, sourceVertices.Length))
                {
                    return null;
                }

                var halfX = MathF.Abs(scaleX) * 0.5f;
                var halfY = MathF.Abs(scaleY) * 0.5f;
                if (halfX <= float.Epsilon || halfY <= float.Epsilon)
                {
                    return null;
                }

                var visualMaterial = offVisualMaterial.HasValue ? ParseVisualMaterial(offVisualMaterial.Value) : null;
                var center = sourceVertices[vertexIndex];

                var vertices = new[]
                {
                    center + new Vector3(0f, -halfY, -halfX),
                    center + new Vector3(0f, -halfY, halfX),
                    center + new Vector3(0f, halfY, -halfX),
                    center + new Vector3(0f, halfY, halfX)
                };

                var mirrorU = visualMaterial != null && (visualMaterial.TextureFlagsByte & 0x4) != 0;
                var mirrorV = visualMaterial != null && (visualMaterial.TextureFlagsByte & 0x8) != 0;
                var uMax = 1f + (mirrorU ? 1f : 0f);
                var vMin = mirrorV ? -1f : 0f;
                var uvs = new[]
                {
                    new Vector2(0f, vMin),
                    new Vector2(uMax, vMin),
                    new Vector2(0f, 1f),
                    new Vector2(uMax, 1f)
                };

                return new HypeResolvedMeshSurface
                {
                    Vertices = vertices,
                    Uvs = uvs,
                    Normals = new[] { Vector3.Right, Vector3.Right, Vector3.Right, Vector3.Right },
                    Indices = new[] { 0, 2, 1, 1, 2, 3 },
                    DoubleSided = true,
                    TextureTgaName = visualMaterial?.TextureTgaName,
                    VisualMaterialFlags = visualMaterial?.Flags ?? 0,
                    TextureFlags = visualMaterial?.TextureFlags ?? 0,
                    TextureFlagsByte = visualMaterial?.TextureFlagsByte ?? 0,
                    TextureAlphaMask = visualMaterial?.TextureAlphaMask ?? 0
                };
            }
            catch (Exception ex)
            {
                _diagnostics.Add($"Failed to parse sprite element {elementAddress}: {ex.Message}");
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

        private HypeParsedVisualMaterial? ParseVisualMaterial(HypeAddress address)
        {
            return HypeMaterialDecoder.ParseVisualMaterial(
                _space,
                address,
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

        private static Dictionary<int, HypeCharacterObjectVisual> SelectBetterObjectVisualSet(
            Dictionary<int, HypeCharacterObjectVisual>? primary,
            Dictionary<int, HypeCharacterObjectVisual> candidate)
        {
            if (primary == null || primary.Count == 0)
            {
                return candidate;
            }

            if (candidate.Count == 0)
            {
                return primary;
            }

            var primaryScore = ComputeObjectVisualScore(primary);
            var candidateScore = ComputeObjectVisualScore(candidate);
            return candidateScore > primaryScore ? candidate : primary;
        }

        private static long ComputeObjectVisualScore(Dictionary<int, HypeCharacterObjectVisual> visuals)
        {
            long meshCount = 0;
            long surfaceCount = 0;
            long vertexCount = 0;
            foreach (var visual in visuals.Values)
            {
                if (visual.Mesh == null)
                {
                    continue;
                }

                meshCount++;
                surfaceCount += visual.Mesh.Surfaces.Count;
                foreach (var surface in visual.Mesh.Surfaces)
                {
                    vertexCount += surface.Vertices.Length;
                }
            }

            return (meshCount << 40) + (surfaceCount << 20) + vertexCount;
        }

        private static void ApplyVertexOverrides(
            Vector3[] vertices,
            IReadOnlyDictionary<int, Vector3>? vertexOverrides)
        {
            if (vertexOverrides == null || vertexOverrides.Count == 0)
            {
                return;
            }

            foreach (var pair in vertexOverrides)
            {
                if (pair.Key >= 0 && pair.Key < vertices.Length)
                {
                    vertices[pair.Key] += pair.Value;
                }
            }
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

        private readonly struct LodCandidate
        {
            public LodCandidate(HypeAddress address, float distance)
            {
                Address = address;
                Distance = distance;
            }

            public HypeAddress Address { get; }
            public float Distance { get; }
        }

        private sealed class PersoCandidate
        {
            public required bool IsMainActor { get; init; }
            public required bool IsSectorCharacterListMember { get; init; }
            public required int ChannelCount { get; init; }
            public required int FrameCount { get; init; }
            public required int ObjectVisualCount { get; init; }
            public required HypeCharacterActorAsset? Actor { get; init; }
        }

        private sealed class ParsedSuperObject
        {
            public required uint TypeCode { get; init; }
            public required MontrealSuperObjectType Type { get; init; }
            public required HypeAddress? DataAddress { get; init; }
            public required HypeAddress? FirstChild { get; init; }
            public required uint ChildCount { get; init; }
            public required HypeAddress? NextBrother { get; init; }
        }
    }
}
