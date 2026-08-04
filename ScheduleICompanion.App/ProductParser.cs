using System.Text.Json;
using System.Text.RegularExpressions;

namespace ScheduleICompanion.App;

public sealed class ProductParser
{
    private readonly List<Regex> _rules = new();

    public ProductParser(string configDirectory)
    {
        Directory.CreateDirectory(configDirectory);
        var path = Path.Combine(configDirectory, "product-rules.json");

        if (!File.Exists(path))
        {
            var defaults = new[]
            {
                @"(?<customer>[\w '\-]+?)\s+wants?\s+(?<qty>\d+)\s*[x×]?\s*(?<product>[^.,;:\r\n]+)",
                @"order\s*:\s*(?<qty>\d+)\s*[x×]?\s*(?<product>[^.,;:\r\n]+)",
                @"(?<qty>\d+)\s*[x×]\s*(?<product>[^.,;:\r\n]+)",
                @"(?<product>[A-Za-z][A-Za-z0-9 '\-]+?)\s*\((?<qty>\d+)\)"
            };
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, new JsonSerializerOptions { WriteIndented = true }));
        }

        try
        {
            var patterns = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? Array.Empty<string>();
            _rules.AddRange(patterns.Select(p => new Regex(
                p, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant)));
        }
        catch
        {
            // A broken user rule must never stop the companion.
        }
    }

    public IReadOnlyList<ProductTotalRow> Parse(string text)
    {
        var found = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in _rules)
        {
            foreach (Match match in rule.Matches(text))
            {
                if (!match.Success || !int.TryParse(match.Groups["qty"].Value, out var quantity) || quantity <= 0)
                    continue;

                var product = Clean(match.Groups["product"].Value);
                if (string.IsNullOrWhiteSpace(product))
                    continue;

                found[product] = found.GetValueOrDefault(product) + quantity;
            }

            if (found.Count > 0)
                break;
        }

        return found.Select(x => new ProductTotalRow(x.Key, x.Value)).ToArray();
    }

    private static string Clean(string value) =>
        Regex.Replace(value.Trim(' ', '.', ',', ':', ';', '-', '\t'), @"\s+", " ");
}
