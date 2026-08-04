using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: AssemblyInspector <assembly> <regex>");
    return 2;
}

using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var metadata = pe.GetMetadataReader();
var pattern = new Regex(args[1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
var typesOnly = args.Skip(2).Any(arg => arg.Equals("--types-only", StringComparison.OrdinalIgnoreCase));
var allMembers = args.Skip(2).Any(arg => arg.Equals("--all-members", StringComparison.OrdinalIgnoreCase));

foreach (var handle in metadata.TypeDefinitions)
{
    var type = metadata.GetTypeDefinition(handle);
    var ns = metadata.GetString(type.Namespace);
    var name = metadata.GetString(type.Name);
    var fullName = string.IsNullOrEmpty(ns) ? name : $"{ns}.{name}";
    var memberNames = type.GetMethods().Select(h => metadata.GetString(metadata.GetMethodDefinition(h).Name))
        .Concat(type.GetFields().Select(h => metadata.GetString(metadata.GetFieldDefinition(h).Name)))
        .Concat(type.GetProperties().Select(h => metadata.GetString(metadata.GetPropertyDefinition(h).Name)))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(x => x)
        .ToArray();

    if (!pattern.IsMatch(fullName) && (typesOnly || !memberNames.Any(member => pattern.IsMatch(member))))
        continue;

    Console.WriteLine(fullName);
    foreach (var member in memberNames.Where(member => !typesOnly && (allMembers || pattern.IsMatch(member))))
        Console.WriteLine($"  {member}");
}

return 0;
