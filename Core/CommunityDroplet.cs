using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DynamicIsland.Core;

public enum CommunityActionType
{
    OpenUrl,
    ShellExecute,
    HttpRequest
}

public sealed record DropletActionDefinition(
    [property: JsonPropertyName("label")] string Label = "",
    [property: JsonPropertyName("glyph")] string Glyph = "\uE71D",
    [property: JsonPropertyName("command")] string Command = "",
    [property: JsonPropertyName("type")] string Type = "OpenUrl",
    [property: JsonPropertyName("method")] string Method = "GET");

public sealed record DropletMetricDefinition(
    [property: JsonPropertyName("label")] string Label = "",
    [property: JsonPropertyName("value")] string Value = "",
    [property: JsonPropertyName("glyph")] string Glyph = "");

public sealed record DropletCustomUiDefinition(
    [property: JsonPropertyName("badgeText")] string? BadgeText = null,
    [property: JsonPropertyName("subtext")] string? Subtext = null,
    [property: JsonPropertyName("metrics")] IReadOnlyList<DropletMetricDefinition>? Metrics = null);

/// <summary>
/// Schema specification for community-authored Droplet plugins.
/// Defined in JSON and hot-loaded from %LocalAppData%\DynamicIsland\Droplets\&lt;id&gt;.json
/// </summary>
public sealed record CommunityDropletDefinition(
    [property: JsonPropertyName("id")] string Id = "",
    [property: JsonPropertyName("name")] string Name = "",
    [property: JsonPropertyName("description")] string Description = "",
    [property: JsonPropertyName("version")] string Version = "1.0.0",
    [property: JsonPropertyName("author")] string Author = "Community",
    [property: JsonPropertyName("glyph")] string Glyph = "\uE7B5",
    [property: JsonPropertyName("category")] string Category = "Productivity",
    [property: JsonPropertyName("accentColor")] string? AccentColor = null,
    [property: JsonPropertyName("refreshIntervalSec")] int RefreshIntervalSec = 60,
    [property: JsonPropertyName("endpoint")] string? Endpoint = null,
    [property: JsonPropertyName("headers")] IReadOnlyDictionary<string, string>? Headers = null,
    [property: JsonPropertyName("responsePath")] string? ResponsePath = null,
    [property: JsonPropertyName("formatTemplate")] string? FormatTemplate = null,
    [property: JsonPropertyName("actions")] IReadOnlyList<DropletActionDefinition>? Actions = null,
    [property: JsonPropertyName("customUi")] DropletCustomUiDefinition? CustomUi = null,
    [property: JsonPropertyName("estimatedRamKb")] int EstimatedRamKb = 16);

/// <summary>
/// Validates community droplet JSON definitions against the official Droplet v1 specification.
/// </summary>
public static class CommunityDropletSchemaValidator
{
    private static readonly Regex IdRegex = new("^[a-zA-Z0-9_-]{2,64}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static bool Validate(CommunityDropletDefinition? def, out List<string> errors)
    {
        errors = new List<string>();
        if (def == null)
        {
            errors.Add("Droplet definition cannot be null.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(def.Id) || !IdRegex.IsMatch(def.Id))
        {
            errors.Add($"Invalid droplet 'id': '{def.Id}'. Must be 2-64 alphanumeric characters, underscores, or dashes.");
        }

        if (string.IsNullOrWhiteSpace(def.Name))
        {
            errors.Add("Droplet 'name' is required.");
        }
        else if (def.Name.Length > 100)
        {
            errors.Add("Droplet 'name' cannot exceed 100 characters.");
        }

        if (!Enum.TryParse<DropletCategory>(def.Category, ignoreCase: true, out _))
        {
            errors.Add($"Invalid category: '{def.Category}'. Must be Productivity, Media, System, or Capture.");
        }

        if (def.RefreshIntervalSec > 0 && def.RefreshIntervalSec < 5)
        {
            errors.Add($"'refreshIntervalSec' cannot be less than 5 seconds (got {def.RefreshIntervalSec}).");
        }
        else if (def.RefreshIntervalSec > 86400)
        {
            errors.Add($"'refreshIntervalSec' cannot exceed 86400 seconds (1 day).");
        }

        if (!string.IsNullOrWhiteSpace(def.Endpoint))
        {
            if (!Uri.TryCreate(def.Endpoint, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add($"Invalid endpoint URL: '{def.Endpoint}'. Must be an absolute http:// or https:// URL.");
            }
        }

        if (def.Actions != null)
        {
            for (int i = 0; i < def.Actions.Count; i++)
            {
                var action = def.Actions[i];
                if (string.IsNullOrWhiteSpace(action.Label))
                {
                    errors.Add($"Action[{i}] missing required 'label'.");
                }
                if (string.IsNullOrWhiteSpace(action.Command))
                {
                    errors.Add($"Action[{i}] missing required 'command'.");
                }
            }
        }

        return errors.Count == 0;
    }

    public static string Serialize(CommunityDropletDefinition def) =>
        JsonSerializer.Serialize(def, JsonOptions);

    public static CommunityDropletDefinition? Deserialize(string json) =>
        JsonSerializer.Deserialize<CommunityDropletDefinition>(json, JsonOptions);
}
