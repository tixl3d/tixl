#nullable enable
using T3.Core.Rendering;

namespace Lib.Utils.AssimpIo;

/// <summary>
/// Binary transfer format between the isolated import process and the editor. Both sides run the
/// same build, so unmanaged structs are written as raw memory. Reading treats the file as untrusted:
/// every length is checked against the remaining bytes before anything is allocated.
/// </summary>
internal static class AssimpModelDataSerializer
{
    public static void Write(Stream stream, AssimpModelData data)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);

        WriteArray<PbrVertex>(writer, data.Vertices);
        WriteArray<Int3>(writer, data.Triangles);
        WriteArray<MeshChunkDef>(writer, data.Chunks);
        WriteArray<int>(writer, data.ChunkMaterialIndices);
        WriteArray<Point>(writer, data.Points);

        writer.Write(data.ReusesPreviousMaterials);
        writer.Write(data.Materials.Count);
        foreach (var material in data.Materials)
        {
            WriteMaterial(writer, material);
        }

        WriteGeometry(writer, data.Geometry);

        writer.Write(data.SkinWeights != null);
        if (data.SkinWeights != null)
            WriteArray<AssimpSkinWeight>(writer, data.SkinWeights);

        WriteSkeleton(writer, data.Skeleton);
        WriteArray<Point>(writer, data.SkeletonRestPoints);

        writer.Write(data.AnimationClips.Count);
        foreach (var clip in data.AnimationClips)
        {
            WriteClip(writer, clip);
        }

        WriteValue(writer, data.BoundsMin);
        WriteValue(writer, data.BoundsMax);
        writer.Write(data.FileMeshCount);
        writer.Write(data.UsedMeshInstanceCount);
        writer.Write(data.BoneCount);
        writer.Write(data.MeshNames);
        writer.Write(data.AnimationNames);

        writer.Write(data.Warnings.Count);
        foreach (var warning in data.Warnings)
        {
            writer.Write(warning);
        }

        writer.Write(Magic);
    }

    /// <summary>Throws <see cref="InvalidDataException"/> for anything that isn't a complete result of this build.</summary>
    public static AssimpModelData Read(Stream stream)
    {
        if (stream.CanSeek && stream.Length > MaxResultBytes)
            throw new InvalidDataException("Import result is too large");

        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadInt32() != Magic || reader.ReadInt32() != FormatVersion)
            throw new InvalidDataException("Import result has an unknown format");

        var data = new AssimpModelData
                       {
                           Vertices = ReadArray<PbrVertex>(reader),
                           Triangles = ReadArray<Int3>(reader),
                           Chunks = ReadArray<MeshChunkDef>(reader),
                           ChunkMaterialIndices = ReadArray<int>(reader),
                           Points = ReadArray<Point>(reader),
                           ReusesPreviousMaterials = reader.ReadBoolean(),
                       };

        data.VertexCount = data.Vertices.Length;
        data.FaceCount = data.Triangles.Length;

        var materialCount = ReadCount(reader, MaxListCount);
        for (var materialIndex = 0; materialIndex < materialCount; materialIndex++)
        {
            data.Materials.Add(ReadMaterial(reader));
        }

        data.Geometry = ReadGeometry(reader);
        data.SkinWeights = reader.ReadBoolean() ? ReadArray<AssimpSkinWeight>(reader) : null;
        data.Skeleton = ReadSkeleton(reader);
        data.SkeletonRestPoints = ReadArray<Point>(reader);

        var clipCount = ReadCount(reader, MaxListCount);
        for (var clipIndex = 0; clipIndex < clipCount; clipIndex++)
        {
            data.AnimationClips.Add(ReadClip(reader));
        }

        data.BoundsMin = ReadValue<Vector3>(reader);
        data.BoundsMax = ReadValue<Vector3>(reader);
        data.FileMeshCount = reader.ReadInt32();
        data.UsedMeshInstanceCount = reader.ReadInt32();
        data.BoneCount = reader.ReadInt32();
        data.MeshNames = reader.ReadString();
        data.AnimationNames = reader.ReadString();

        var warningCount = ReadCount(reader, MaxListCount);
        for (var warningIndex = 0; warningIndex < warningCount; warningIndex++)
        {
            data.Warnings.Add(reader.ReadString());
        }

        if (reader.ReadInt32() != Magic)
            throw new InvalidDataException("Import result is incomplete");

        Validate(data);
        return data;
    }

    /// <summary>Indices from another process must not be trusted to stay inside their arrays.</summary>
    private static void Validate(AssimpModelData data)
    {
        var vertexCount = (uint)data.Vertices.Length;
        foreach (var triangle in data.Triangles)
        {
            if ((uint)triangle.X >= vertexCount || (uint)triangle.Y >= vertexCount || (uint)triangle.Z >= vertexCount)
                throw new InvalidDataException("Import result references vertices out of range");
        }

        if (data.ChunkMaterialIndices.Length != data.Chunks.Length)
            throw new InvalidDataException("Import result has inconsistent chunks");

        foreach (var materialIndex in data.ChunkMaterialIndices)
        {
            if (materialIndex < -1 || (!data.ReusesPreviousMaterials && materialIndex >= data.Materials.Count))
                throw new InvalidDataException("Import result references materials out of range");
        }

        foreach (var chunk in data.Chunks)
        {
            if (chunk.StartFaceIndex < 0 || chunk.FaceCount < 0 || (long)chunk.StartFaceIndex + chunk.FaceCount > data.Triangles.Length)
                throw new InvalidDataException("Import result has chunks out of range");
        }

        if (data.SkinWeights != null && data.SkinWeights.Length != data.Vertices.Length)
            throw new InvalidDataException("Import result has mismatching skin weights");

        var geometry = data.Geometry;
        if (geometry != null)
        {
            var offsets = geometry.FaceCornerOffsets;
            if (offsets.Length == 0 || offsets[0] != 0 || offsets[^1] != geometry.CornerCount)
                throw new InvalidDataException("Import result has broken geometry faces");

            for (var faceIndex = 1; faceIndex < offsets.Length; faceIndex++)
            {
                if (offsets[faceIndex] < offsets[faceIndex - 1])
                    throw new InvalidDataException("Import result has broken geometry faces");
            }

            var pointCount = (uint)geometry.PointCount;
            foreach (var pointIndex in geometry.CornerPointIndices)
            {
                if ((uint)pointIndex >= pointCount)
                    throw new InvalidDataException("Import result has geometry corners out of range");
            }

            foreach (var attribute in geometry.Attributes)
            {
                if (attribute.Domain == AttributeDomain.Corner && attribute.Count != geometry.CornerCount)
                    throw new InvalidDataException("Import result has mismatching geometry attributes");
            }
        }

        var skeleton = data.Skeleton;
        if (skeleton != null)
        {
            var jointCount = skeleton.ParentIndices.Length;
            if (skeleton.JointNames.Length != jointCount
                || skeleton.RestLocalTransforms.Length != jointCount
                || skeleton.InverseBindMatrices.Length != jointCount)
            {
                throw new InvalidDataException("Import result has an inconsistent skeleton");
            }

            foreach (var parentIndex in skeleton.ParentIndices)
            {
                if (parentIndex < -1 || parentIndex >= jointCount)
                    throw new InvalidDataException("Import result has joint parents out of range");
            }

            if (data.SkinWeights != null)
            {
                foreach (var weight in data.SkinWeights)
                {
                    var joints = weight.JointIndices;
                    if ((uint)joints.X >= jointCount || (uint)joints.Y >= jointCount || (uint)joints.Z >= jointCount || (uint)joints.W >= jointCount)
                        throw new InvalidDataException("Import result has skin weights out of range");
                }
            }
        }
    }

    #region parts
    private static void WriteMaterial(BinaryWriter writer, AssimpMaterialData material)
    {
        writer.Write(material.Name);
        WriteValue(writer, material.BaseColor);
        WriteValue(writer, material.EmissiveColor);
        writer.Write(material.Roughness);
        writer.Write(material.Metallic);
        WriteImage(writer, material.BaseColorMap);
        WriteImage(writer, material.EmissiveMap);
        WriteImage(writer, material.NormalMap);
        WriteImage(writer, material.RoughnessMetallicOcclusionMap);
    }

    private static AssimpMaterialData ReadMaterial(BinaryReader reader)
    {
        return new AssimpMaterialData
                   {
                       Name = reader.ReadString(),
                       BaseColor = ReadValue<Vector4>(reader),
                       EmissiveColor = ReadValue<Vector4>(reader),
                       Roughness = reader.ReadSingle(),
                       Metallic = reader.ReadSingle(),
                       BaseColorMap = ReadImage(reader),
                       EmissiveMap = ReadImage(reader),
                       NormalMap = ReadImage(reader),
                       RoughnessMetallicOcclusionMap = ReadImage(reader),
                   };
    }

    private static void WriteImage(BinaryWriter writer, AssimpTextureImage? image)
    {
        writer.Write(image != null);
        if (image == null)
            return;

        writer.Write(image.Width);
        writer.Write(image.Height);
        WriteArray<byte>(writer, image.Rgba);
    }

    private static AssimpTextureImage? ReadImage(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
            return null;

        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var rgba = ReadArray<byte>(reader);
        if (width <= 0 || height <= 0 || (long)width * height * 4 != rgba.Length)
            throw new InvalidDataException("Import result has a broken texture");

        return new AssimpTextureImage(width, height, rgba);
    }

    private static void WriteGeometry(BinaryWriter writer, MeshGeometry? geometry)
    {
        writer.Write(geometry != null);
        if (geometry == null)
            return;

        WriteArray<Vector3>(writer, geometry.Positions);
        WriteArray<int>(writer, geometry.FaceCornerOffsets);
        WriteArray<int>(writer, geometry.CornerPointIndices);
        WriteArray<GeometryPart>(writer, geometry.Parts);

        var attributes = new List<GeometryAttribute>();
        foreach (var attribute in geometry.Attributes)
        {
            if (GetAttributeTypeTag(attribute) >= 0)
                attributes.Add(attribute);
        }

        writer.Write(attributes.Count);
        foreach (var attribute in attributes)
        {
            writer.Write(attribute.Name);
            writer.Write((int)attribute.Domain);
            writer.Write(GetAttributeTypeTag(attribute));
            switch (attribute)
            {
                case GeometryAttribute<float> floats: WriteArray<float>(writer, floats.Values); break;
                case GeometryAttribute<Vector2> vectors2: WriteArray<Vector2>(writer, vectors2.Values); break;
                case GeometryAttribute<Vector3> vectors3: WriteArray<Vector3>(writer, vectors3.Values); break;
                case GeometryAttribute<Vector4> vectors4: WriteArray<Vector4>(writer, vectors4.Values); break;
                case GeometryAttribute<int> ints: WriteArray<int>(writer, ints.Values); break;
            }
        }
    }

    private static MeshGeometry? ReadGeometry(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
            return null;

        var geometry = new MeshGeometry
                           {
                               Positions = ReadArray<Vector3>(reader),
                               FaceCornerOffsets = ReadArray<int>(reader),
                               CornerPointIndices = ReadArray<int>(reader),
                               Parts = ReadArray<GeometryPart>(reader),
                           };

        var attributeCount = ReadCount(reader, MaxListCount);
        for (var attributeIndex = 0; attributeIndex < attributeCount; attributeIndex++)
        {
            var name = reader.ReadString();
            var domain = (AttributeDomain)reader.ReadInt32();
            if (!Enum.IsDefined(domain))
                throw new InvalidDataException("Import result has an unknown attribute domain");

            switch (reader.ReadInt32())
            {
                case 0: AddAttribute(geometry, name, domain, ReadArray<float>(reader)); break;
                case 1: AddAttribute(geometry, name, domain, ReadArray<Vector2>(reader)); break;
                case 2: AddAttribute(geometry, name, domain, ReadArray<Vector3>(reader)); break;
                case 3: AddAttribute(geometry, name, domain, ReadArray<Vector4>(reader)); break;
                case 4: AddAttribute(geometry, name, domain, ReadArray<int>(reader)); break;
                default: throw new InvalidDataException("Import result has an unknown attribute type");
            }
        }

        geometry.InvalidateTopologyCaches();
        return geometry;
    }

    private static void AddAttribute<T>(MeshGeometry geometry, string name, AttributeDomain domain, T[] values) where T : unmanaged
    {
        var attribute = new GeometryAttribute<T>(name, domain, 0) { Values = values };
        geometry.Attributes.Add(attribute);
    }

    private static int GetAttributeTypeTag(GeometryAttribute attribute)
    {
        return attribute switch
                   {
                       GeometryAttribute<float>   => 0,
                       GeometryAttribute<Vector2> => 1,
                       GeometryAttribute<Vector3> => 2,
                       GeometryAttribute<Vector4> => 3,
                       GeometryAttribute<int>     => 4,
                       _                          => -1,
                   };
    }

    private static void WriteSkeleton(BinaryWriter writer, SceneSetup.SceneSkeleton? skeleton)
    {
        writer.Write(skeleton != null);
        if (skeleton == null)
            return;

        writer.Write(skeleton.JointNames.Length);
        foreach (var name in skeleton.JointNames)
        {
            writer.Write(name ?? string.Empty);
        }

        WriteArray<int>(writer, skeleton.ParentIndices);
        WriteArray<SceneSetup.Transform>(writer, skeleton.RestLocalTransforms);
        WriteArray<Matrix4x4>(writer, skeleton.InverseBindMatrices);
    }

    private static SceneSetup.SceneSkeleton? ReadSkeleton(BinaryReader reader)
    {
        if (!reader.ReadBoolean())
            return null;

        var nameCount = ReadCount(reader, MaxListCount);
        var names = new string[nameCount];
        for (var nameIndex = 0; nameIndex < nameCount; nameIndex++)
        {
            names[nameIndex] = reader.ReadString();
        }

        return new SceneSetup.SceneSkeleton
                   {
                       JointNames = names,
                       ParentIndices = ReadArray<int>(reader),
                       RestLocalTransforms = ReadArray<SceneSetup.Transform>(reader),
                       InverseBindMatrices = ReadArray<Matrix4x4>(reader),
                   };
    }

    private static void WriteClip(BinaryWriter writer, SceneSetup.SceneAnimClip clip)
    {
        writer.Write(clip.Name ?? string.Empty);
        writer.Write(clip.Duration);
        writer.Write(clip.Channels.Count);
        foreach (var channel in clip.Channels)
        {
            writer.Write(channel.SkeletonIndex);
            writer.Write(channel.JointIndex);
            WriteSampler(writer, channel.TranslationSampler as AssimpVector3Sampler);
            WriteSampler(writer, channel.RotationSampler as AssimpQuaternionSampler);
            WriteSampler(writer, channel.ScaleSampler as AssimpVector3Sampler);
        }
    }

    private static SceneSetup.SceneAnimClip ReadClip(BinaryReader reader)
    {
        var clip = new SceneSetup.SceneAnimClip { Name = reader.ReadString(), Duration = reader.ReadSingle() };
        var channelCount = ReadCount(reader, MaxListCount);
        for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
        {
            var channel = new SceneSetup.JointAnimChannel
                              {
                                  SkeletonIndex = reader.ReadInt32(),
                                  JointIndex = reader.ReadInt32(),
                              };

            if (ReadSampler(reader, out float[] times, out Vector3[] translations, out bool[] isStep))
                channel.TranslationSampler = new AssimpVector3Sampler(times, translations, isStep);

            if (ReadSampler(reader, out times, out Quaternion[] rotations, out isStep))
                channel.RotationSampler = new AssimpQuaternionSampler(times, rotations, isStep);

            if (ReadSampler(reader, out times, out Vector3[] scales, out isStep))
                channel.ScaleSampler = new AssimpVector3Sampler(times, scales, isStep);

            clip.Channels.Add(channel);
        }

        return clip;
    }

    private static void WriteSampler<T>(BinaryWriter writer, AssimpKeyframeSampler<T>? sampler) where T : unmanaged
    {
        writer.Write(sampler != null);
        if (sampler == null)
            return;

        WriteArray(writer, sampler.Times);
        WriteArray(writer, sampler.Values);
        WriteArray(writer, sampler.IsStep);
    }

    private static bool ReadSampler<T>(BinaryReader reader, out float[] times, out T[] values, out bool[] isStep) where T : unmanaged
    {
        times = [];
        values = [];
        isStep = [];
        if (!reader.ReadBoolean())
            return false;

        times = ReadArray<float>(reader);
        values = ReadArray<T>(reader);
        isStep = ReadArray<bool>(reader);
        if (times.Length == 0 || times.Length != values.Length || times.Length != isStep.Length)
            throw new InvalidDataException("Import result has a broken animation channel");

        for (var keyIndex = 1; keyIndex < times.Length; keyIndex++)
        {
            if (!(times[keyIndex - 1] <= times[keyIndex]))
                throw new InvalidDataException("Import result has unsorted animation keys");
        }

        return true;
    }
    #endregion

    #region primitives
    private static void WriteArray<T>(BinaryWriter writer, ReadOnlySpan<T> values) where T : unmanaged
    {
        writer.Write(values.Length);
        writer.Write(MemoryMarshal.AsBytes(values));
    }

    private static T[] ReadArray<T>(BinaryReader reader) where T : unmanaged
    {
        var elementSize = Unsafe.SizeOf<T>();
        var count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException("Import result has an invalid element count");

        // Byte count, not element count: a 16k RGBA texture is only a few million bytes but a single
        // 50M-vertex array is several gigabytes. Both are bounded and the multiplication can't overflow.
        var byteCount = (long)count * elementSize;
        if (byteCount > MaxArrayBytes)
            throw new InvalidDataException("Import result element count exceeds the allowed size");

        var remaining = reader.BaseStream.CanSeek ? reader.BaseStream.Length - reader.BaseStream.Position : long.MaxValue;
        if (byteCount > remaining)
            throw new InvalidDataException("Import result is truncated");

        var values = count == 0 ? [] : new T[count];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }

    private static void WriteValue<T>(BinaryWriter writer, T value) where T : unmanaged
    {
        writer.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));
    }

    private static T ReadValue<T>(BinaryReader reader) where T : unmanaged
    {
        var value = default(T);
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(new Span<T>(ref value)));
        return value;
    }

    private static int ReadCount(BinaryReader reader, int maximum)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > maximum)
            throw new InvalidDataException("Import result has an invalid element count");

        return count;
    }
    #endregion

    /** "TXAS" */
    private const int Magic = 0x53415854;

    /** Bump when the layout of the result or of any written struct changes. */
    private const int FormatVersion = 1;

    private const int MaxListCount = 10_000_000;

    /** Guards against mapping a corrupted or unrelated file into huge arrays; real results stay well below. */
    private const long MaxResultBytes = 8L * 1024 * 1024 * 1024;
    private const long MaxArrayBytes = 4L * 1024 * 1024 * 1024;
}
