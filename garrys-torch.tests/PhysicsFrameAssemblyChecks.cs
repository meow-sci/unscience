using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

/// <summary>Read actual KSA metadata/IL without loading or executing its Windows/native code.</summary>
internal sealed class PhysicsFrameAssemblyChecks : ISignatureTypeProvider<string, object?>, IDisposable
{
    private static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.GetValue(null))
        .OfType<OpCode>().DistinctBy(op => op.Value).ToDictionary(op => op.Value);
    private static readonly string[] Seams =
    {
        "ApplyOrbitSolvers", "ApplyVehicleSolvers", "ApplyClothSolvers", "GetJobSimStep",
        "ExecuteNextClothSolvers", "ExecuteNextVehicleSolvers", "ExecuteNextOrbitSolvers"
    };
    private readonly PEReader _pe;
    private readonly MetadataReader _md;

    private PhysicsFrameAssemblyChecks(string path)
    {
        _pe = new PEReader(File.OpenRead(path));
        _md = _pe.GetMetadataReader();
    }

    public void Dispose() => _pe.Dispose();

    public static void Run()
    {
        string? folder = Environment.GetEnvironmentVariable("KSA_DLL_DIR")
            ?? Environment.GetEnvironmentVariable("KSAFolder")
            ?? typeof(PhysicsFrameAssemblyChecks).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "KSAFolder")?.Value;
        string path = Path.Combine(folder ?? "", "KSA.dll");
        Require(File.Exists(path), $"real KSA.dll is required for frame seam verification: {path}");
        using var probe = new PhysicsFrameAssemblyChecks(path);
        probe.Verify();
        Console.WriteLine($"PASS: real KSA PrepareFrame signature and seven ordered IL seams ({path})");
    }

    private void Verify()
    {
        MethodDefinition frame = FindMethod("KSA.Program", "PrepareFrame");
        var signature = frame.DecodeSignature(this, null);
        Require((frame.Attributes & MethodAttributes.Static) == 0
            && signature.ReturnType == "KSA.Program+OnFrameResult"
            && signature.ParameterTypes.SequenceEqual(new[] { "System.Double", "System.Double" }),
            $"real PrepareFrame is instance OnFrameResult(double,double): {signature.ReturnType}({string.Join(",", signature.ParameterTypes)})");
        var body = _pe.GetMethodBody(frame.RelativeVirtualAddress);
        List<string> calls = ReadCalls(body.GetILReader());
        int previous = -1;
        for (int i = 0; i < Seams.Length; i++)
        {
            string name = Seams[i];
            MethodDefinition seam = FindMethod("KSA.Universe", name);
            var shape = seam.DecodeSignature(this, null);
            string[] args = i < 3 ? Array.Empty<string>()
                : i == 3 ? new[] { "System.Double" } : new[] { "System.Double", "KSA.SimStep" };
            Require((seam.Attributes & MethodAttributes.Static) != 0
                && shape.ReturnType == (i == 3 ? "KSA.SimStep" : "System.Void")
                && shape.ParameterTypes.SequenceEqual(args), $"real Universe.{name} signature matches production lookup");
            string anchor = "KSA.Universe::" + name;
            int index = calls.IndexOf(anchor);
            Require(index > previous && calls.Count(call => call == anchor) == 1,
                $"real PrepareFrame has exactly one ordered {anchor} call");
            previous = index;
        }
        // Track the important optimized 5638 shape without requiring it in older supported builds.
        if (!body.LocalSignature.IsNil)
        {
            var locals = _md.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(this, null);
            Console.WriteLine($"real KSA frame IL: {body.GetILBytes()!.Length} bytes; "
                + $"ReadOnlySpan<Vehicle> local={locals.Contains("System.ReadOnlySpan`1[KSA.Vehicle]")}");
        }
    }

    private MethodDefinition FindMethod(string typeName, string name)
    {
        var found = new List<MethodDefinition>();
        foreach (var handle in _md.TypeDefinitions)
        {
            if (TypeName(handle) != typeName) continue;
            foreach (var method in _md.GetTypeDefinition(handle).GetMethods())
            {
                var definition = _md.GetMethodDefinition(method);
                if (_md.GetString(definition.Name) == name) found.Add(definition);
            }
        }
        Require(found.Count == 1, $"real KSA has exactly one {typeName}.{name}");
        return found[0];
    }

    private List<string> ReadCalls(BlobReader il)
    {
        var calls = new List<string>();
        while (il.RemainingBytes > 0)
        {
            short value = il.ReadByte();
            if (value == 0xFE) value = unchecked((short)(0xFE00 | il.ReadByte()));
            OpCode op = Opcodes[value];
            switch (op.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar: il.ReadByte(); break;
                case OperandType.InlineVar: il.ReadInt16(); break;
                case OperandType.InlineI8 or OperandType.InlineR: il.ReadInt64(); break;
                case OperandType.InlineSwitch:
                    for (int targets = il.ReadInt32(); targets > 0; targets--) il.ReadInt32();
                    break;
                case OperandType.InlineMethod:
                    var target = MetadataTokens.EntityHandle(il.ReadInt32());
                    if (op == OpCodes.Call || op == OpCodes.Callvirt) calls.Add(MemberName(target));
                    break;
                default: il.ReadInt32(); break;
            }
        }
        return calls;
    }

    private string MemberName(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.MethodSpecification)
            return MemberName(_md.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
        if (handle.Kind == HandleKind.MethodDefinition)
        {
            var method = _md.GetMethodDefinition((MethodDefinitionHandle)handle);
            return $"{TypeName(method.GetDeclaringType())}::{_md.GetString(method.Name)}";
        }
        if (handle.Kind == HandleKind.MemberReference)
        {
            var member = _md.GetMemberReference((MemberReferenceHandle)handle);
            return $"{EntityName(member.Parent)}::{_md.GetString(member.Name)}";
        }
        return handle.Kind.ToString();
    }

    private string EntityName(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeName((TypeDefinitionHandle)handle),
        HandleKind.TypeReference => TypeName((TypeReferenceHandle)handle),
        HandleKind.TypeSpecification => _md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null),
        _ => handle.Kind.ToString()
    };

    private string TypeName(TypeDefinitionHandle handle)
    {
        var type = _md.GetTypeDefinition(handle);
        if (!type.GetDeclaringType().IsNil) return $"{TypeName(type.GetDeclaringType())}+{_md.GetString(type.Name)}";
        return Qualify(type.Namespace, type.Name);
    }

    private string TypeName(TypeReferenceHandle handle)
    {
        var type = _md.GetTypeReference(handle);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return $"{TypeName((TypeReferenceHandle)type.ResolutionScope)}+{_md.GetString(type.Name)}";
        return Qualify(type.Namespace, type.Name);
    }

    private string Qualify(StringHandle ns, StringHandle name) =>
        _md.GetString(ns) is { Length: > 0 } prefix ? $"{prefix}.{_md.GetString(name)}" : _md.GetString(name);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => $"System.{typeCode}";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => TypeName(handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => TypeName(handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    public string GetByReferenceType(string elementType) => $"{elementType}&";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> arguments) => $"{genericType}[{string.Join(",", arguments)}]";
    public string GetGenericMethodParameter(object? context, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? context, int index) => $"!{index}";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
}
