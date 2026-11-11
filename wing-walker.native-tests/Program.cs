using System;
using System.Collections;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            Run(args);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void Run(string[] args)
    {
        string repoRoot = FindRepoRoot();
        bool allowIlHeaderRetarget = args.Contains("--allow-il-header-retarget", StringComparer.Ordinal);
        Require(args.All(argument => argument == "--allow-il-header-retarget"), "unknown option; supported option: --allow-il-header-retarget");
        string? dllDirectory = Environment.GetEnvironmentVariable("KSA_DLL_DIR");
        if (string.IsNullOrWhiteSpace(dllDirectory))
            dllDirectory = Path.GetFullPath(Path.Combine(repoRoot, "..", "ksa-game-assemblies", "current", "dll"));
        else
            dllDirectory = Path.GetFullPath(dllDirectory);

        string ksaDll = Path.Combine(dllDirectory, "KSA.dll");
        Require(File.Exists(ksaDll), $"KSA reference assembly is missing: {ksaDll}");
        string buildId = ReadBuildId(Path.Combine(repoRoot, "..", "ksa-game-assemblies", "current", "version.json"));
        string loadPath = allowIlHeaderRetarget ? MakeArm64ScratchCopy(ksaDll) : ksaDll;
        var loadContext = new KsaLoadContext(dllDirectory);
        Assembly ksa = loadContext.LoadFromAssemblyPath(loadPath);
        string assemblyVersion = ksa.GetName().Version?.ToString() ?? "<none>";
        string fileVersion = FileVersionInfo.GetVersionInfo(ksaDll).FileVersion ?? "<none>";
        Require(assemblyVersion == buildId && fileVersion == buildId,
            $"selected KSA DLL build mismatch: current/version.json={buildId}, assembly version={assemblyVersion}, file version={fileVersion}");
        Console.WriteLine($"KSA source build: {buildId}; reflected assembly identity: {ksa.GetName().FullName}; file version: {fileVersion}");
        Console.WriteLine($"KSA assembly loaded from: {ksa.Location}");

        Assembly defaultKsa = LoadKsaIntoDefaultContext(loadPath, dllDirectory);
        Require(defaultKsa.GetName().Version?.ToString() == buildId,
            "the native BoneAnimRuntime fixture resolved a different default-context KSA build");

        string xmlPath = Path.Combine(repoRoot, "wing-walker", "WingWalker.xml");
        Require(File.Exists(xmlPath), $"Wing Walker asset is missing: {xmlPath}");
        List<XElement> rows = XDocument.Load(xmlPath).Root!.Elements().ToList();

        Type errorsType = RequiredType(ksa, "KSA.ContentErrors");
        object errors = Activator.CreateInstance(errorsType)!;
        Type stampsType = RequiredType(ksa, "KSA.ContentStamps");
        MethodInfo stamp = stampsType.GetMethod("Stamp", BindingFlags.Public | BindingFlags.Static)!;
        for (int i = 0; i < rows.Count; i++)
            stamp.Invoke(null, [rows[i], "unscience", 0, "WingWalker.xml", 0, i]);

        Type gameTagsType = RequiredType(ksa, "KSA.GameContentTags");
        MethodInfo buildTags = gameTagsType.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)!;
        object tagTable = buildTags.Invoke(null, null)!;
        int partCollectionCountBefore = NativeXmlAssertions.PartCollectionCount(ksa);

        Type prePassType = RequiredType(ksa, "KSA.ContentPrePass");
        MethodInfo resolve = prePassType.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static,
            binder: null, [typeof(List<XElement>), errorsType], modifiers: null)!;
        var flatRows = (IEnumerable)resolve.Invoke(null, [rows, errors])!;

        Type deserializerType = RequiredType(ksa, "KSA.ContentDeserializer");
        MethodInfo deserialize = deserializerType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "Deserialize" && method.GetParameters().Length == 5);
        Type tagBindingType = RequiredType(ksa, "KSA.ContentTagBinding");
        PropertyInfo bindingsProperty = tagTable.GetType().GetProperty("Bindings")!;
        IEnumerable bindings = (IEnumerable)bindingsProperty.GetValue(tagTable)!;

        var bindingByTag = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (object binding in bindings)
            bindingByTag[(string)tagBindingType.GetProperty("Tag")!.GetValue(binding)!] = binding;

        var decoded = new List<(string Tag, string Id, object Value)>();
        foreach (object flat in flatRows)
        {
            string tag = (string)flat.GetType().GetProperty("Tag")!.GetValue(flat)!;
            string id = (string)flat.GetType().GetProperty("Id")!.GetValue(flat)!;
            Require(bindingByTag.TryGetValue(tag, out object? binding), $"native KSA tag table has no binding for <{tag}>");
            object? value = deserialize.Invoke(null, [flat, binding, tagTable, null, errors]);
            Require(value != null, $"native deserializer rejected <{tag} Id=\"{id}\">");
            decoded.Add((tag, id, value!));
        }

        int errorCount = (int)errorsType.GetProperty("Count")!.GetValue(errors)!;
        if (errorCount != 0)
            throw new InvalidOperationException("Native content parser reported errors:\n" + FormatErrors(errorsType, errors));

        int partCount = decoded.Count(item => item.Tag == "Part");
        Require(partCount == 2, $"expected two deserialized final parts; found {partCount}");
        NativeXmlAssertions.Verify(ksa, loadContext, tagTable, decoded, partCollectionCountBefore);
        Console.WriteLine($"PASS: native XML pre-pass flattened {decoded.Count} rows and deserialized {partCount} Part rows with zero content errors");
    }

    private static string ReadBuildId(string versionFile)
    {
        Require(File.Exists(versionFile), $"KSA current/version.json is missing: {versionFile}");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(versionFile));
        return document.RootElement.GetProperty("build").GetString() ?? "<missing>";
    }

    private static string MakeArm64ScratchCopy(string sourcePath)
    {
        byte[] original = File.ReadAllBytes(sourcePath);
        Require(original.Length >= 0x100, "KSA.dll is too short to be a valid PE image");
        int peOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(0x3c, 4)));
        Require(peOffset >= 0 && peOffset + 24 <= original.Length && original.AsSpan(peOffset, 4).SequenceEqual("PE\0\0"u8),
            "KSA.dll has no valid PE header");
        Require(BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(peOffset + 4, 2)) == 0x8664,
            "--allow-il-header-retarget only accepts the x64 KSA.dll reference assembly");

        int optionalHeader = peOffset + 24;
        Require(BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(optionalHeader, 2)) == 0x20b,
            "--allow-il-header-retarget expects a PE32+ image");
        int sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(peOffset + 6, 2));
        int sectionTable = optionalHeader + BinaryPrimitives.ReadUInt16LittleEndian(original.AsSpan(peOffset + 20, 2));
        uint clrRva = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(optionalHeader + 112 + 14 * 8, 4));
        uint clrSize = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(optionalHeader + 112 + 14 * 8 + 4, 4));
        int clrOffset = RvaToOffset(original, sectionTable, sectionCount, clrRva);
        Require(clrSize >= 72 && clrOffset + 72 <= original.Length, "KSA.dll has no complete CLR header");
        uint clrFlags = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(clrOffset + 16, 4));
        uint managedNativeHeaderRva = BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(clrOffset + 64, 4));
        Require((clrFlags & 1) != 0 && managedNativeHeaderRva == 0,
            "refusing retarget: KSA.dll is not ILONLY or has a native/ReadyToRun header");

        byte[] retargeted = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(retargeted.AsSpan(peOffset + 4, 2), 0xaa64);
        for (int i = 0; i < original.Length; i++)
            if (i < peOffset + 4 || i >= peOffset + 6)
                Require(original[i] == retargeted[i], "scratch retarget changed bytes outside the 2-byte PE machine field");

        string scratchDirectory = Path.Combine(Path.GetTempPath(), "wing-walker.native-tests-arm64");
        Directory.CreateDirectory(scratchDirectory);
        string scratchPath = Path.Combine(scratchDirectory, "KSA.dll");
        File.WriteAllBytes(scratchPath, retargeted);
        Console.WriteLine($"ARM64 local-run shim: ILONLY=True, ManagedNativeHeader RVA=0x0; only PE Machine changed in scratch copy; source SHA256={Convert.ToHexString(SHA256.HashData(original))}");
        return scratchPath;
    }

    private static int RvaToOffset(byte[] image, int sectionTable, int sectionCount, uint rva)
    {
        for (int i = 0; i < sectionCount; i++)
        {
            int section = sectionTable + i * 40;
            uint virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(section + 8, 4));
            uint virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(section + 12, 4));
            uint rawSize = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(section + 16, 4));
            uint rawOffset = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(section + 20, 4));
            if (rva >= virtualAddress && rva - virtualAddress < Math.Max(virtualSize, rawSize))
                return checked((int)(rawOffset + rva - virtualAddress));
        }
        throw new InvalidOperationException("KSA.dll CLR header RVA is outside every PE section.");
    }

    private static Assembly LoadKsaIntoDefaultContext(string loadPath, string dllDirectory)
    {
        string[] searchDirectories = new[] { dllDirectory }
            .Concat((Environment.GetEnvironmentVariable("KSA_EXTRA_DLL_DIRS") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AssemblyLoadContext.Default.Resolving += (context, name) => ResolveFromDirectories(context, name, searchDirectories);
        return AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly => assembly.GetName().Name == "KSA")
            ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(loadPath);
    }

    private static Assembly? ResolveFromDirectories(AssemblyLoadContext context, AssemblyName requested, string[] directories)
    {
        if (requested.Name == null || requested.Name.StartsWith("System.", StringComparison.Ordinal)
            || requested.Name is "System" or "System.Private.CoreLib" or "netstandard")
            return null;

        foreach (string directory in directories)
        {
            string candidate = Path.Combine(directory, requested.Name + ".dll");
            if (!File.Exists(candidate))
                continue;
            AssemblyName found = AssemblyName.GetAssemblyName(candidate);
            if (requested.Version != null && found.Version != requested.Version)
                continue;
            return context.LoadFromAssemblyPath(candidate);
        }
        return null;
    }

    private static string FormatErrors(Type errorsType, object errors)
    {
        var lines = new List<string>();
        int count = (int)errorsType.GetProperty("Count")!.GetValue(errors)!;
        PropertyInfo indexer = errorsType.GetProperty("Item")!;
        for (int i = 0; i < count; i++)
        {
            object error = indexer.GetValue(errors, [i])!;
            lines.Add(error.ToString() ?? error.GetType().FullName ?? "<unknown content error>");
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current != null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "wing-walker", "WingWalker.xml")) &&
                File.Exists(Path.Combine(current.FullName, "Directory.Build.props")))
                return current.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the unscience repository root.");
    }

    private static Type RequiredType(Assembly assembly, string name) =>
        assembly.GetType(name, throwOnError: true)!;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class KsaLoadContext : AssemblyLoadContext
    {
        private readonly string[] _directories;

        public KsaLoadContext(string dllDirectory) : base("KSA native XML validation", isCollectible: false)
        {
            _directories = new[] { dllDirectory }
                .Concat((Environment.GetEnvironmentVariable("KSA_EXTRA_DLL_DIRS") ?? string.Empty)
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null || assemblyName.Name.StartsWith("System.", StringComparison.Ordinal)
                || assemblyName.Name is "System" or "System.Private.CoreLib" or "netstandard")
                return null;

            foreach (string directory in _directories)
            {
                string candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                if (!File.Exists(candidate))
                    continue;
                AssemblyName found = AssemblyName.GetAssemblyName(candidate);
                if (assemblyName.Version != null && found.Version != assemblyName.Version)
                    continue;
                return LoadFromAssemblyPath(candidate);
            }
            return null;
        }
    }
}
