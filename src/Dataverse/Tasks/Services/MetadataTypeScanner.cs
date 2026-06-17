using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

/// <summary>
/// Reads type information from a .NET assembly using IL metadata only. 
/// No types are actually loaded into the CLR, so there are no issues with assembly binding or locking files on disk. 
/// </summary>
internal sealed class MetadataTypeScanner : IDisposable
{
    private readonly string _mainDllPath;
    private readonly List<string> _probeDirs;
    private readonly List<PEReader> _disposables = new List<PEReader>();
    private readonly Dictionary<string, MetadataReader> _readerByFile = new Dictionary<string, MetadataReader>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MetadataReader> _readerByAssembly = new Dictionary<string, MetadataReader>(StringComparer.OrdinalIgnoreCase);

    public MetadataTypeScanner(string mainDllPath, IEnumerable<string> probeDirs)
    {
        _mainDllPath = mainDllPath;
        _probeDirs = probeDirs
            .Where(d => !string.IsNullOrWhiteSpace(d) && Directory.Exists(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IEnumerable<ScannedType> GetPublicTypes()
    {
        MetadataReader reader = OpenReaderForFile(_mainDllPath);
        if (reader == null)
            yield break;

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if ((typeDef.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
                continue;

            yield return new ScannedType(this, reader, typeDef);
        }
    }


    internal bool DerivesFromBaseType(MetadataReader reader, TypeDefinition typeDef, string baseSimpleName)
    {
        EntityHandle baseHandle = typeDef.BaseType;
        int guard = 0;

        while (!baseHandle.IsNil && guard++ < 200)
        {
            if (baseHandle.Kind == HandleKind.TypeDefinition)
            {
                var td = reader.GetTypeDefinition((TypeDefinitionHandle)baseHandle);
                if (reader.GetString(td.Name) == baseSimpleName)
                    return true;

                baseHandle = td.BaseType;
            }
            else if (baseHandle.Kind == HandleKind.TypeReference)
            {
                var tr = reader.GetTypeReference((TypeReferenceHandle)baseHandle);

                if (reader.GetString(tr.Name) == baseSimpleName)
                    return true;

                var resolved = ResolveTypeReference(reader, tr);
                if (resolved == null)
                    return false;

                reader = resolved.Value.Reader;
                baseHandle = resolved.Value.Definition.BaseType;
            }
            else
            {
                return false;
            }
        }

        return false;
    }

    internal bool ImplementsInterface(MetadataReader reader, TypeDefinition typeDef, string interfaceNamespace, string interfaceName)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);

        MetadataReader currentReader = reader;
        TypeDefinition current = typeDef;
        int guard = 0;

        while (guard++ < 200)
        {
            foreach (var implHandle in current.GetInterfaceImplementations())
            {
                EntityHandle iface = currentReader.GetInterfaceImplementation(implHandle).Interface;
                if (InterfaceMatchesOrInherits(currentReader, iface, interfaceNamespace, interfaceName, visited))
                    return true;
            }

            EntityHandle baseHandle = current.BaseType;
            if (baseHandle.IsNil)
                return false;

            var next = ResolveToTypeDef(currentReader, baseHandle);
            if (next == null)
                return false;

            currentReader = next.Value.Reader;
            current = next.Value.Definition;
        }

        return false;
    }

    private bool InterfaceMatchesOrInherits(MetadataReader reader, EntityHandle ifaceHandle, string ns, string name, HashSet<string> visited)
    {
        string ifaceNs, ifaceName;
        if (TryGetTypeName(reader, ifaceHandle, out ifaceNs, out ifaceName))
        {
            if (ifaceName == name && (ns == null || ifaceNs == ns))
                return true;

            if (!visited.Add(ifaceNs + "." + ifaceName))
                return false;
        }

        // Follow interface inheritance (e.g. IMyPlugin : IPlugin).
        var resolved = ResolveToTypeDef(reader, ifaceHandle);
        if (resolved == null)
            return false;

        foreach (var implHandle in resolved.Value.Definition.GetInterfaceImplementations())
        {
            EntityHandle parent = resolved.Value.Reader.GetInterfaceImplementation(implHandle).Interface;
            if (InterfaceMatchesOrInherits(resolved.Value.Reader, parent, ns, name, visited))
                return true;
        }

        return false;
    }

    internal CrmRegistrationInfo ReadCrmRegistration(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var caHandle in typeDef.GetCustomAttributes())
        {
            var ca = reader.GetCustomAttribute(caHandle);
            if (GetAttributeTypeName(reader, ca) != "CrmPluginRegistrationAttribute")
                continue;

            try
            {
                CustomAttributeValue<string> value = ca.DecodeValue(StringTypeProvider.Instance);

                string name = value.FixedArguments.Length > 0 ? value.FixedArguments[0].Value as string : null;
                string group = null;
                foreach (var named in value.NamedArguments)
                {
                    if (named.Name == "Group" && named.Value is string groupArg)
                        group = groupArg;
                }

                return new CrmRegistrationInfo { Name = name, Group = group };
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private (MetadataReader Reader, TypeDefinition Definition)? ResolveToTypeDef(MetadataReader reader, EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
            return (reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle));

        if (handle.Kind == HandleKind.TypeReference)
            return ResolveTypeReference(reader, reader.GetTypeReference((TypeReferenceHandle)handle));

        return null;
    }

    private (MetadataReader Reader, TypeDefinition Definition)? ResolveTypeReference(MetadataReader reader, TypeReference tr)
    {
        string asmName = GetResolutionAssemblyName(reader, tr.ResolutionScope);
        if (asmName == null)
            return null;

        string ns = reader.GetString(tr.Namespace);
        string name = reader.GetString(tr.Name);

        MetadataReader other = OpenReaderForAssembly(asmName);
        if (other == null)
            return null;

        foreach (var handle in other.TypeDefinitions)
        {
            var td = other.GetTypeDefinition(handle);
            if (other.GetString(td.Name) == name && other.GetString(td.Namespace) == ns)
                return (other, td);
        }

        return null;
    }

    private static string GetResolutionAssemblyName(MetadataReader reader, EntityHandle scope)
    {
        if (scope.Kind == HandleKind.AssemblyReference)
            return reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);

        if (scope.Kind == HandleKind.TypeReference)
        {
            var parent = reader.GetTypeReference((TypeReferenceHandle)scope);
            return GetResolutionAssemblyName(reader, parent.ResolutionScope);
        }

        return null;
    }

    private static bool TryGetTypeName(MetadataReader reader, EntityHandle handle, out string ns, out string name)
    {
        if (handle.Kind == HandleKind.TypeDefinition)
        {
            var td = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
            ns = reader.GetString(td.Namespace);
            name = reader.GetString(td.Name);
            return true;
        }

        if (handle.Kind == HandleKind.TypeReference)
        {
            var tr = reader.GetTypeReference((TypeReferenceHandle)handle);
            ns = reader.GetString(tr.Namespace);
            name = reader.GetString(tr.Name);
            return true;
        }

        ns = null;
        name = null;
        return false;
    }

    private static string GetAttributeTypeName(MetadataReader reader, CustomAttribute ca)
    {
        switch (ca.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var memberRef = reader.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                if (memberRef.Parent.Kind == HandleKind.TypeReference)
                    return reader.GetString(reader.GetTypeReference((TypeReferenceHandle)memberRef.Parent).Name);
                if (memberRef.Parent.Kind == HandleKind.TypeDefinition)
                    return reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)memberRef.Parent).Name);
                return "";

            case HandleKind.MethodDefinition:
                var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
                return reader.GetString(reader.GetTypeDefinition(methodDef.GetDeclaringType()).Name);

            default:
                return "";
        }
    }

    private MetadataReader OpenReaderForAssembly(string assemblyName)
    {
        MetadataReader cached;
        if (_readerByAssembly.TryGetValue(assemblyName, out cached))
            return cached;

        MetadataReader reader = null;
        foreach (var dir in _probeDirs)
        {
            var candidate = Path.Combine(dir, assemblyName + ".dll");
            if (File.Exists(candidate))
            {
                reader = OpenReaderForFile(candidate);
                if (reader != null)
                    break;
            }
        }

        _readerByAssembly[assemblyName] = reader;
        return reader;
    }

    private MetadataReader OpenReaderForFile(string path)
    {
        string fullPath = Path.GetFullPath(path);

        MetadataReader cached;
        if (_readerByFile.TryGetValue(fullPath, out cached))
            return cached;

        MetadataReader reader = null;
        try
        {
            // Read the whole file into memory, so the dll is never locked on disk.
            var pe = new PEReader(ImmutableArray.Create(File.ReadAllBytes(fullPath)));
            _disposables.Add(pe);
            if (pe.HasMetadata)
                reader = pe.GetMetadataReader();
        }
        catch
        {
            reader = null;
        }

        _readerByFile[fullPath] = reader;
        return reader;
    }

    public void Dispose()
    {
        foreach (var pe in _disposables)
        {
            try { pe.Dispose(); }
            catch { }
        }

        _disposables.Clear();
    }

    private sealed class StringTypeProvider : ICustomAttributeTypeProvider<string>
    {
        public static readonly StringTypeProvider Instance = new StringTypeProvider();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
            => reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type == "System.Type";
    }
}

/// <summary>A top-level public type discovered by <see cref="MetadataTypeScanner"/>.</summary>
internal sealed class ScannedType
{
    private readonly MetadataTypeScanner _owner;
    private readonly MetadataReader _reader;
    private readonly TypeDefinition _definition;

    internal ScannedType(MetadataTypeScanner owner, MetadataReader reader, TypeDefinition definition)
    {
        _owner = owner;
        _reader = reader;
        _definition = definition;

        Namespace = reader.GetString(definition.Namespace);
        Name = reader.GetString(definition.Name);
        FullName = string.IsNullOrEmpty(Namespace) ? Name : Namespace + "." + Name;
        IsAbstract = (definition.Attributes & TypeAttributes.Abstract) != 0;
        IsInterface = (definition.Attributes & TypeAttributes.Interface) != 0;
    }

    public string Namespace { get; }
    public string Name { get; }
    public string FullName { get; }
    public bool IsAbstract { get; }
    public bool IsInterface { get; }

    public bool DerivesFromBaseType(string baseSimpleName)
        => _owner.DerivesFromBaseType(_reader, _definition, baseSimpleName);

    public bool ImplementsInterface(string interfaceNamespace, string interfaceName)
        => _owner.ImplementsInterface(_reader, _definition, interfaceNamespace, interfaceName);

    public CrmRegistrationInfo TryGetCrmRegistration()
        => _owner.ReadCrmRegistration(_reader, _definition);
}

internal sealed class CrmRegistrationInfo
{
    public string Name { get; set; }
    public string Group { get; set; }
}
