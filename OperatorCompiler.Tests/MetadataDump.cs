using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;

namespace OperatorCompiler.Tests;

/// <summary>
/// Writes an assembly as order-independent text: everything that makes up its behaviour (types, members,
/// signatures, attributes, IL with tokens resolved to names) and nothing that depends on emit order.
/// Two builds of the same sources with the same options produce identical dumps.
/// </summary>
internal static class MetadataDump
{
    public static string Dump(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var provider = new NameProvider(reader);
        var lines = new List<string>();

        var assembly = reader.GetAssemblyDefinition();
        lines.Add($"assembly {reader.GetString(assembly.Name)} {assembly.Version} flags={assembly.Flags} hashAlg={assembly.HashAlgorithm}");
        lines.Add($"pe machine={pe.PEHeaders.CoffHeader.Machine} corflags={pe.PEHeaders.CorHeader!.Flags} subsystem={pe.PEHeaders.PEHeader!.Subsystem}");
        foreach (var a in Attributes(reader, provider, assembly.GetCustomAttributes()))
            lines.Add("assembly-attr " + a);
        foreach (var a in Attributes(reader, provider, reader.GetModuleDefinition().GetCustomAttributes()))
            lines.Add("module-attr " + a);
        foreach (var handle in reader.AssemblyReferences)
        {
            var r = reader.GetAssemblyReference(handle);
            lines.Add($"assembly-ref {reader.GetString(r.Name)} {r.Version}");
        }

        foreach (var handle in reader.ManifestResources)
        {
            var r = reader.GetManifestResource(handle);
            lines.Add($"resource {reader.GetString(r.Name)} {r.Attributes}");
        }

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = provider.TypeDefName(typeHandle);
            if (typeName == "<Module>")
                continue;

            var baseType = type.BaseType.IsNil ? "" : provider.TypeName(type.BaseType);
            lines.Add($"type {typeName} : {baseType} flags={type.Attributes} layout={type.GetLayout().PackingSize},{type.GetLayout().Size}");
            foreach (var i in type.GetInterfaceImplementations())
                lines.Add($"type {typeName} implements {provider.TypeName(reader.GetInterfaceImplementation(i).Interface)}");
            foreach (var g in type.GetGenericParameters())
                lines.Add($"type {typeName} generic {GenericParameter(reader, provider, g)}");
            foreach (var a in Attributes(reader, provider, type.GetCustomAttributes()))
                lines.Add($"type {typeName} attr {a}");

            foreach (var fieldHandle in type.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                var name = $"{typeName}::{reader.GetString(field.Name)}";
                var constant = field.GetDefaultValue().IsNil ? "" : " const=" + Hex(reader.GetBlobBytes(reader.GetConstant(field.GetDefaultValue()).Value));
                var rva = field.GetRelativeVirtualAddress() != 0 ? " has-rva" : "";
                lines.Add($"field {name} {field.DecodeSignature(provider, null)} flags={field.Attributes}{constant}{rva}");
                foreach (var a in Attributes(reader, provider, field.GetCustomAttributes()))
                    lines.Add($"field {name} attr {a}");
            }

            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                var signature = method.DecodeSignature(provider, null);
                var name = $"{typeName}::{reader.GetString(method.Name)}({string.Join(",", signature.ParameterTypes)}):{signature.ReturnType}";
                lines.Add($"method {name} flags={method.Attributes} impl={method.ImplAttributes} generic={signature.GenericParameterCount}");
                foreach (var g in method.GetGenericParameters())
                    lines.Add($"method {name} generic {GenericParameter(reader, provider, g)}");
                foreach (var a in Attributes(reader, provider, method.GetCustomAttributes()))
                    lines.Add($"method {name} attr {a}");
                foreach (var p in method.GetParameters())
                {
                    var parameter = reader.GetParameter(p);
                    var pName = $"{name} param{parameter.SequenceNumber} {reader.GetString(parameter.Name)} {parameter.Attributes}";
                    lines.Add($"method {pName}");
                    foreach (var a in Attributes(reader, provider, parameter.GetCustomAttributes()))
                        lines.Add($"method {pName} attr {a}");
                }

                if (method.RelativeVirtualAddress != 0)
                {
                    var body = pe.GetMethodBody(method.RelativeVirtualAddress);
                    var locals = body.LocalSignature.IsNil
                                     ? ""
                                     : string.Join(",", reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(provider, null));
                    lines.Add($"method {name} body maxstack={body.MaxStack} init={body.LocalVariablesInitialized} locals=[{locals}]");
                    foreach (var region in body.ExceptionRegions)
                        lines.Add($"method {name} region {region.Kind} try={region.TryOffset}+{region.TryLength} handler={region.HandlerOffset}+{region.HandlerLength}"
                                  + (region.CatchType.IsNil ? "" : " catch=" + provider.TypeName(region.CatchType)));
                    lines.Add($"method {name} il {Il(reader, provider, body.GetILReader())}");
                }
            }

            foreach (var propertyHandle in type.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var name = $"{typeName}::{reader.GetString(property.Name)}";
                lines.Add($"property {name} {property.DecodeSignature(provider, null).ReturnType} flags={property.Attributes}");
                foreach (var a in Attributes(reader, provider, property.GetCustomAttributes()))
                    lines.Add($"property {name} attr {a}");
            }

            foreach (var eventHandle in type.GetEvents())
            {
                var e = reader.GetEventDefinition(eventHandle);
                lines.Add($"event {typeName}::{reader.GetString(e.Name)} {provider.TypeName(e.Type)}");
            }

            foreach (var impl in type.GetMethodImplementations())
            {
                var mi = reader.GetMethodImplementation(impl);
                lines.Add($"type {typeName} methodimpl {provider.MemberName(mi.MethodBody)} => {provider.MemberName(mi.MethodDeclaration)}");
            }
        }

        lines.Sort(StringComparer.Ordinal);
        return string.Join('\n', lines) + "\n";
    }

    private static string GenericParameter(MetadataReader reader, NameProvider provider, GenericParameterHandle handle)
    {
        var g = reader.GetGenericParameter(handle);
        var constraints = g.GetConstraints().Select(c => provider.TypeName(reader.GetGenericParameterConstraint(c).Type)).Order(StringComparer.Ordinal);
        return $"{g.Index} {reader.GetString(g.Name)} {g.Attributes} [{string.Join(",", constraints)}]";
    }

    private static IEnumerable<string> Attributes(MetadataReader reader, NameProvider provider, CustomAttributeHandleCollection handles)
    {
        var result = new List<string>();
        foreach (var handle in handles)
        {
            var attribute = reader.GetCustomAttribute(handle);
            result.Add($"{provider.MemberName(attribute.Constructor)} {Hex(reader.GetBlobBytes(attribute.Value))}");
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    private static string Hex(byte[] bytes) => bytes.Length <= 64 ? Convert.ToHexString(bytes) : "sha:" + Convert.ToHexString(SHA256.HashData(bytes))[..16];

    // ---------------------------------------------------------------- IL with resolved tokens

    private static readonly Dictionary<short, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                       .Select(f => (OpCode)f.GetValue(null)!)
                       .ToDictionary(o => o.Value);

    private static string Il(MetadataReader reader, NameProvider provider, BlobReader il)
    {
        var builder = new StringBuilder();
        while (il.RemainingBytes > 0)
        {
            var offset = il.Offset;
            short value = il.ReadByte();
            if (value == 0xFE)
                value = unchecked((short)(0xFE00 | il.ReadByte()));
            var opCode = OpCodesByValue[value];
            builder.Append(offset).Append(':').Append(opCode.Name);
            switch (opCode.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                    builder.Append(' ').Append(il.ReadSByte());
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    builder.Append(' ').Append(il.ReadByte());
                    break;
                case OperandType.InlineVar:
                    builder.Append(' ').Append(il.ReadUInt16());
                    break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                    builder.Append(' ').Append(il.ReadInt32());
                    break;
                case OperandType.ShortInlineR:
                    builder.Append(' ').Append(il.ReadSingle().ToString("R"));
                    break;
                case OperandType.InlineI8:
                    builder.Append(' ').Append(il.ReadInt64());
                    break;
                case OperandType.InlineR:
                    builder.Append(' ').Append(il.ReadDouble().ToString("R"));
                    break;
                case OperandType.InlineSwitch:
                    var count = il.ReadInt32();
                    for (var i = 0; i < count; i++)
                        builder.Append(' ').Append(il.ReadInt32());
                    break;
                case OperandType.InlineString:
                    builder.Append(" \"").Append(reader.GetUserString(MetadataTokens.UserStringHandle(il.ReadInt32() & 0xFFFFFF))).Append('"');
                    break;
                case OperandType.InlineSig:
                    var sig = reader.GetStandaloneSignature((StandaloneSignatureHandle)MetadataTokens.EntityHandle(il.ReadInt32()));
                    builder.Append(" sig:").Append(Hex(reader.GetBlobBytes(sig.Signature)));
                    break;
                default: // InlineField, InlineMethod, InlineTok, InlineType
                    builder.Append(' ').Append(provider.EntityName(MetadataTokens.EntityHandle(il.ReadInt32())));
                    break;
            }

            builder.Append(';');
        }

        return builder.ToString();
    }

    // ---------------------------------------------------------------- names

    private sealed class NameProvider(MetadataReader reader) : ISignatureTypeProvider<string, object?>
    {
        public string EntityName(EntityHandle handle) => handle.Kind switch
                                                             {
                                                                 HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification => TypeName(handle),
                                                                 _ => MemberName(handle),
                                                             };

        public string TypeName(EntityHandle handle) => handle.Kind switch
                                                           {
                                                               HandleKind.TypeDefinition => TypeDefName((TypeDefinitionHandle)handle),
                                                               HandleKind.TypeReference => TypeRefName((TypeReferenceHandle)handle),
                                                               HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null),
                                                               _ => handle.Kind.ToString(),
                                                           };

        public string TypeDefName(TypeDefinitionHandle handle)
        {
            var type = reader.GetTypeDefinition(handle);
            var name = reader.GetString(type.Name);
            var declaring = type.GetDeclaringType();
            if (!declaring.IsNil)
                return TypeDefName(declaring) + "/" + name;
            var ns = reader.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        private string TypeRefName(TypeReferenceHandle handle)
        {
            var type = reader.GetTypeReference(handle);
            var name = reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
                return TypeRefName((TypeReferenceHandle)type.ResolutionScope) + "/" + name;
            var ns = reader.GetString(type.Namespace);
            var scope = type.ResolutionScope.Kind == HandleKind.AssemblyReference
                            ? "[" + reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name) + "]"
                            : "";
            return scope + (ns.Length == 0 ? name : ns + "." + name);
        }

        public string MemberName(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                {
                    var m = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var s = m.DecodeSignature(this, null);
                    return $"{TypeDefName(m.GetDeclaringType())}::{reader.GetString(m.Name)}({string.Join(",", s.ParameterTypes)}):{s.ReturnType}";
                }
                case HandleKind.FieldDefinition:
                {
                    var f = reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                    return $"{TypeDefName(f.GetDeclaringType())}::{reader.GetString(f.Name)}";
                }
                case HandleKind.MemberReference:
                {
                    var m = reader.GetMemberReference((MemberReferenceHandle)handle);
                    var parent = m.Parent.Kind == HandleKind.MethodDefinition ? MemberName(m.Parent) : TypeName(m.Parent);
                    if (m.GetKind() == MemberReferenceKind.Method)
                    {
                        var s = m.DecodeMethodSignature(this, null);
                        return $"{parent}::{reader.GetString(m.Name)}({string.Join(",", s.ParameterTypes)}):{s.ReturnType}";
                    }

                    return $"{parent}::{reader.GetString(m.Name)}:{m.DecodeFieldSignature(this, null)}";
                }
                case HandleKind.MethodSpecification:
                {
                    var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return $"{MemberName(spec.Method)}<{string.Join(",", spec.DecodeSignature(this, null))}>";
                }
                default:
                    return handle.Kind.ToString();
            }
        }

        public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => $"fnptr({string.Join(",", signature.ParameterTypes)}):{signature.ReturnType}";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => $"{genericType}<{string.Join(",", typeArguments)}>";
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => $"{unmodifiedType} {(isRequired ? "modreq" : "modopt")}({modifier})";
        public string GetPinnedType(string elementType) => elementType + " pinned";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle handle, byte rawTypeKind) => TypeDefName(handle);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle handle, byte rawTypeKind) => TypeRefName(handle);
        public string GetTypeFromSpecification(MetadataReader r, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
            => r.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
