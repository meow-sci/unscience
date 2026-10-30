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

/// <summary>
/// Metadata-only view of the real KSA.dll the tests were built against; it is never loaded or run.
/// Fixtures mirror private game methods by hand, so a game signature change used to pass silently
/// (KSA 5638 added <c>SimStep</c> to <c>DetectStructuralFailure</c>). Comparing the method the
/// production lookup resolved on the fixture with the shipped method closes that gap.
/// Linked into kitchen-sink.tests, the-tick.tests and (with KsaAssemblyProbe.Members.cs) ksa-upgrade.tests.
/// </summary>
internal sealed partial class KsaAssemblyProbe : ISignatureTypeProvider<string, object?>, IDisposable
{
    private sealed record Shape(bool IsStatic, string ReturnType, string[] ParameterTypes, string[] ParameterNames)
    {
        public override string ToString() =>
            $"{(IsStatic ? "static " : "")}{ReturnType} ({string.Join(", ", ParameterTypes.Zip(ParameterNames, (t, n) => $"{t} {n}"))})";
    }

    private static readonly Dictionary<short, OpCode> OpCodeTable = BuildOpCodeTable();
    private readonly PEReader _pe;
    private readonly MetadataReader _md;

    private KsaAssemblyProbe(string path)
    {
        _pe = new PEReader(File.OpenRead(path));
        _md = _pe.GetMetadataReader();
    }

    public void Dispose() => _pe.Dispose();

    /// <summary>Run-time KSA_DLL_DIR/KSAFolder, else the KSAFolder baked in at build time.</summary>
    public static string KsaDllPath()
    {
        string? folder = Environment.GetEnvironmentVariable("KSA_DLL_DIR")
            ?? Environment.GetEnvironmentVariable("KSAFolder")
            ?? typeof(KsaAssemblyProbe).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "KSAFolder")?.Value;
        return Path.Combine(folder ?? "", "KSA.dll");
    }

    /// <summary>
    /// Requires the single real KSA method with the fixture method's declaring type and name to have the
    /// same static/return/parameter types and names, and to call each anchor exactly once.
    /// Returns false, loudly, when KSA.dll is unavailable.
    /// </summary>
    public static bool VerifyMirror(MethodInfo fixture, Action<bool, string> require, params string[] singleCallAnchors)
    {
        string path = KsaDllPath();
        if (!File.Exists(path))
        {
            Console.WriteLine($"WARN: real KSA.dll not found at '{path}'; fixture-vs-game checks for {fixture.Name} skipped.");
            return false;
        }
        using var probe = new KsaAssemblyProbe(path);
        string typeName = fixture.DeclaringType!.FullName!;
        var real = probe.FindMethods(typeName, fixture.Name);
        require(real.Count == 1, $"real KSA has exactly one {typeName}.{fixture.Name}");
        var (shape, calls) = real[0];
        var mirror = new Shape(fixture.IsStatic, fixture.ReturnType.FullName!,
            fixture.GetParameters().Select(p => p.ParameterType.FullName!).ToArray(),
            fixture.GetParameters().Select(p => p.Name!).ToArray());
        require(shape.IsStatic == mirror.IsStatic && shape.ReturnType == mirror.ReturnType
            && shape.ParameterTypes.SequenceEqual(mirror.ParameterTypes)
            && shape.ParameterNames.SequenceEqual(mirror.ParameterNames),
            $"production-resolved fixture {fixture.Name} {mirror} mirrors real KSA {shape}");
        foreach (string anchor in singleCallAnchors)
            require(calls.Count(call => call == anchor) == 1, $"real KSA {fixture.Name} calls {anchor} exactly once");
        return true;
    }

    private List<(Shape Shape, List<string> Calls)> FindMethods(string typeFullName, string methodName)
    {
        var found = new List<(Shape, List<string>)>();
        foreach (var typeHandle in _md.TypeDefinitions)
        {
            if (TypeName(typeHandle) != typeFullName) continue;
            foreach (var methodHandle in _md.GetTypeDefinition(typeHandle).GetMethods())
            {
                var method = _md.GetMethodDefinition(methodHandle);
                if (_md.GetString(method.Name) != methodName) continue;
                var signature = method.DecodeSignature(this, null);
                var names = new string[signature.ParameterTypes.Length];
                foreach (var parameterHandle in method.GetParameters())
                {
                    var parameter = _md.GetParameter(parameterHandle);
                    if (parameter.SequenceNumber > 0) names[parameter.SequenceNumber - 1] = _md.GetString(parameter.Name);
                }
                bool isStatic = (method.Attributes & MethodAttributes.Static) != 0;
                var calls = method.RelativeVirtualAddress == 0
                    ? new List<string>()
                    : ScanCalls(_pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader());
                found.Add((new Shape(isStatic, signature.ReturnType, signature.ParameterTypes.ToArray(), names), calls));
            }
        }
        return found;
    }

    /// <summary>Lists "Type::Member" for every call/callvirt in an IL body.</summary>
    private List<string> ScanCalls(BlobReader il) => ScanCallTargets(il).Select(MemberName).ToList();

    /// <summary>Lists the method handle of every call/callvirt in an IL body.</summary>
    private static List<EntityHandle> ScanCallTargets(BlobReader il)
    {
        var calls = new List<EntityHandle>();
        while (il.RemainingBytes > 0)
        {
            short value = il.ReadByte();
            if (value == 0xFE) value = unchecked((short)(0xFE00 | il.ReadByte()));
            var op = OpCodeTable[value];
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
                    if (op == OpCodes.Call || op == OpCodes.Callvirt) calls.Add(target);
                    break;
                default: il.ReadInt32(); break;
            }
        }
        return calls;
    }

    private static Dictionary<short, OpCode> BuildOpCodeTable()
    {
        var table = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (field.GetValue(null) is OpCode op) table.TryAdd(op.Value, op);
        return table;
    }

    private string MemberName(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.MethodDefinition => MethodDefinitionName((MethodDefinitionHandle)handle),
        HandleKind.MemberReference => MemberReferenceName((MemberReferenceHandle)handle),
        HandleKind.MethodSpecification => MemberName(_md.GetMethodSpecification((MethodSpecificationHandle)handle).Method),
        _ => handle.Kind.ToString()
    };

    private string MethodDefinitionName(MethodDefinitionHandle handle)
    {
        var method = _md.GetMethodDefinition(handle);
        return $"{TypeName(method.GetDeclaringType())}::{_md.GetString(method.Name)}";
    }

    private string MemberReferenceName(MemberReferenceHandle handle)
    {
        var member = _md.GetMemberReference(handle);
        return $"{EntityTypeName(member.Parent)}::{_md.GetString(member.Name)}";
    }

    private string EntityTypeName(EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeDefinition => TypeName((TypeDefinitionHandle)handle),
        HandleKind.TypeReference => TypeName((TypeReferenceHandle)handle),
        HandleKind.TypeSpecification => _md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null),
        _ => handle.Kind.ToString()
    };

    private string TypeName(TypeDefinitionHandle handle)
    {
        var type = _md.GetTypeDefinition(handle);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil) return $"{TypeName(declaring)}+{_md.GetString(type.Name)}";
        string ns = _md.GetString(type.Namespace);
        return ns.Length == 0 ? _md.GetString(type.Name) : $"{ns}.{_md.GetString(type.Name)}";
    }

    private string TypeName(TypeReferenceHandle handle)
    {
        var type = _md.GetTypeReference(handle);
        if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            return $"{TypeName((TypeReferenceHandle)type.ResolutionScope)}+{_md.GetString(type.Name)}";
        string ns = _md.GetString(type.Namespace);
        return ns.Length == 0 ? _md.GetString(type.Name) : $"{ns}.{_md.GetString(type.Name)}";
    }

    // ISignatureTypeProvider: render signature types as reflection-style full names.
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => $"System.{typeCode}";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => TypeName(handle);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => TypeName(handle);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
        reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', shape.Rank - 1)}]";
    public string GetByReferenceType(string elementType) => $"{elementType}&";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => elementType;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
        $"{genericType}[{string.Join(",", typeArguments)}]";
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
}
