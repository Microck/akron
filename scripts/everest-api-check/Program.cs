// Checks that every Everest API Akron.dll references still exists in a given
// Celeste.Mod.mm.dll (Everest's patch assembly, shipped in each Everest build's
// main.zip).
//
// CI compiles Akron against a pinned, Everest-patched Celeste reference set that
// cannot be regenerated without the game. Everest's own code (the Celeste.Mod
// namespace) ships in Celeste.Mod.mm.dll, so comparing Akron's references against
// a newer mm.dll catches removed or renamed Everest APIs without the game files.
//
// Scope and limits: only types in the Celeste.Mod namespace (excluding Akron's own
// Celeste.Mod.Akron) are checked. Methods match by name and parameter count, fields
// by name. Changes Everest makes to vanilla Celeste types, and IL hook targets, are
// not covered.
//
// Usage: everest-api-check <Akron.dll> <Celeste.Mod.mm.dll>
// Exit code 1 lists every missing type or member.
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: everest-api-check <Akron.dll> <Celeste.Mod.mm.dll>");
    return 2;
}

using var akronFile = File.OpenRead(args[0]);
using var akronPe = new PEReader(akronFile);
MetadataReader akron = akronPe.GetMetadataReader();

using var everestFile = File.OpenRead(args[1]);
using var everestPe = new PEReader(everestFile);
MetadataReader everest = everestPe.GetMetadataReader();

// Everest type full name ("Ns.Outer+Inner") -> member names, with methods keyed
// as "name/paramCount" so overloads with a different arity still count as missing.
var everestTypes = new Dictionary<string, HashSet<string>>();
foreach (TypeDefinitionHandle handle in everest.TypeDefinitions)
{
    TypeDefinition type = everest.GetTypeDefinition(handle);
    var members = new HashSet<string>();
    foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
    {
        MethodDefinition method = everest.GetMethodDefinition(methodHandle);
        int parameters = method.DecodeSignature(new ArityProvider(), null).RequiredParameterCount;
        members.Add($"{everest.GetString(method.Name)}/{parameters}");
    }
    foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
    {
        members.Add(everest.GetString(everest.GetFieldDefinition(fieldHandle).Name));
    }
    everestTypes[DefinitionName(everest, type)] = members;
}

var missing = new SortedSet<string>();
int checkedMembers = 0;
var referencedTypes = new HashSet<string>();
foreach (MemberReferenceHandle handle in akron.MemberReferences)
{
    MemberReference member = akron.GetMemberReference(handle);
    if (member.Parent.Kind != HandleKind.TypeReference)
    {
        continue;
    }
    string? typeName = EverestReferenceName(akron, (TypeReferenceHandle)member.Parent);
    if (typeName is null)
    {
        continue;
    }
    referencedTypes.Add(typeName);
    string name = akron.GetString(member.Name);
    string key = member.GetKind() == MemberReferenceKind.Method
        ? $"{name}/{member.DecodeMethodSignature(new ArityProvider(), null).RequiredParameterCount}"
        : name;
    checkedMembers++;
    if (!everestTypes.TryGetValue(typeName, out HashSet<string>? members))
    {
        missing.Add($"type   {typeName}");
    }
    else if (!members.Contains(key))
    {
        missing.Add($"member {typeName}::{key}");
    }
}

// Types referenced only by signature (no member access) must still exist.
foreach (TypeReferenceHandle handle in akron.TypeReferences)
{
    string? typeName = EverestReferenceName(akron, handle);
    if (typeName is null)
    {
        continue;
    }
    referencedTypes.Add(typeName);
    if (!everestTypes.ContainsKey(typeName))
    {
        missing.Add($"type   {typeName}");
    }
}

Console.WriteLine($"Checked {referencedTypes.Count} Everest types and {checkedMembers} member references.");
foreach (string entry in missing)
{
    Console.WriteLine($"MISSING {entry}");
}
return missing.Count == 0 ? 0 : 1;

static string DefinitionName(MetadataReader reader, TypeDefinition type)
{
    string name = reader.GetString(type.Name);
    TypeDefinitionHandle declaring = type.GetDeclaringType();
    if (!declaring.IsNil)
    {
        return $"{DefinitionName(reader, reader.GetTypeDefinition(declaring))}+{name}";
    }
    string ns = reader.GetString(type.Namespace);
    return ns.Length == 0 ? name : $"{ns}.{name}";
}

// Full name of a type reference that resolves into the Celeste assembly and the
// Celeste.Mod namespace; null for anything else.
static string? EverestReferenceName(MetadataReader reader, TypeReferenceHandle handle)
{
    TypeReference type = reader.GetTypeReference(handle);
    string name = reader.GetString(type.Name);
    if (type.ResolutionScope.Kind == HandleKind.TypeReference)
    {
        string? outer = EverestReferenceName(reader, (TypeReferenceHandle)type.ResolutionScope);
        return outer is null ? null : $"{outer}+{name}";
    }
    if (type.ResolutionScope.Kind != HandleKind.AssemblyReference)
    {
        return null;
    }
    AssemblyReference assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope);
    string ns = reader.GetString(type.Namespace);
    bool isEverest = reader.GetString(assembly.Name) == "Celeste"
        && (ns == "Celeste.Mod" || ns.StartsWith("Celeste.Mod.", StringComparison.Ordinal))
        && !ns.StartsWith("Celeste.Mod.Akron", StringComparison.Ordinal);
    return isEverest ? $"{ns}.{name}" : null;
}

// Signature decoding only needs parameter counts, so every type decodes to 0.
sealed class ArityProvider : ISignatureTypeProvider<int, object?>
{
    public int GetArrayType(int elementType, ArrayShape shape) => 0;
    public int GetByReferenceType(int elementType) => 0;
    public int GetFunctionPointerType(MethodSignature<int> signature) => 0;
    public int GetGenericInstantiation(int genericType, System.Collections.Immutable.ImmutableArray<int> typeArguments) => 0;
    public int GetGenericMethodParameter(object? genericContext, int index) => 0;
    public int GetGenericTypeParameter(object? genericContext, int index) => 0;
    public int GetModifiedType(int modifier, int unmodifiedType, bool isRequired) => 0;
    public int GetPinnedType(int elementType) => 0;
    public int GetPointerType(int elementType) => 0;
    public int GetPrimitiveType(PrimitiveTypeCode typeCode) => 0;
    public int GetSZArrayType(int elementType) => 0;
    public int GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => 0;
    public int GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => 0;
    public int GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => 0;
}
