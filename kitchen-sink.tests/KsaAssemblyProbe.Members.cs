using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;

/// <summary>
/// Overload-aware queries for invariants no single fixture method can name: which constructors chain
/// to which (KSA 5707 routed Part construction through one private constructor), and every method of
/// a type that reaches a callee (so a newly added stock caller cannot be missed). Metadata only.
/// </summary>
internal sealed partial class KsaAssemblyProbe
{
    internal sealed record Constructor(string[] Parameters, bool IsPublic, List<string[]> ChainsTo);

    /// <summary>Opens the real KSA.dll, or prints a loud WARN and returns null when it is unavailable.</summary>
    public static KsaAssemblyProbe? TryOpen(string purpose)
    {
        string path = KsaDllPath();
        if (File.Exists(path)) return new KsaAssemblyProbe(path);
        Console.WriteLine($"WARN: real KSA.dll not found at '{path}'; {purpose} skipped.");
        return null;
    }

    /// <summary>Every instance constructor of the type, with the parameter types of each same-type constructor it calls.</summary>
    public List<Constructor> Constructors(string typeFullName)
    {
        var result = new List<Constructor>();
        foreach (var method in Methods(typeFullName, ".ctor"))
        {
            var chains = new List<string[]>();
            foreach (var target in Body(method).SelectMany(ScanCallTargets))
            {
                if (target.Kind != HandleKind.MethodDefinition) continue;
                var callee = _md.GetMethodDefinition((MethodDefinitionHandle)target);
                if (_md.GetString(callee.Name) == ".ctor" && callee.GetDeclaringType() == method.GetDeclaringType())
                    chains.Add(callee.DecodeSignature(this, null).ParameterTypes.ToArray());
            }
            bool isPublic = (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
            result.Add(new Constructor(method.DecodeSignature(this, null).ParameterTypes.ToArray(), isPublic, chains));
        }
        return result;
    }

    /// <summary>Distinct names of the type's methods whose bodies call <paramref name="callee"/> ("Type::Member").</summary>
    public List<string> MethodsCalling(string typeFullName, string callee) =>
        Methods(typeFullName, null)
            .Where(m => Body(m).SelectMany(ScanCalls).Contains(callee))
            .Select(m => _md.GetString(m.Name))
            .Distinct()
            .ToList();

    /// <summary>Calls ("Type::Member") made by each overload of the named method.</summary>
    public List<List<string>> CallsOf(string typeFullName, string methodName) =>
        Methods(typeFullName, methodName).Select(m => Body(m).SelectMany(ScanCalls).ToList()).ToList();

    private IEnumerable<MethodDefinition> Methods(string typeFullName, string? name)
    {
        foreach (var typeHandle in _md.TypeDefinitions)
        {
            if (TypeName(typeHandle) != typeFullName) continue;
            foreach (var handle in _md.GetTypeDefinition(typeHandle).GetMethods())
            {
                var method = _md.GetMethodDefinition(handle);
                if (name == null || _md.GetString(method.Name) == name) yield return method;
            }
        }
    }

    private IEnumerable<BlobReader> Body(MethodDefinition method)
    {
        if (method.RelativeVirtualAddress != 0) yield return _pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
    }
}
