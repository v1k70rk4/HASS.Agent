using System.Text.Json;
using HASS.Agent.Companion.Logging;

namespace HASS.Agent.Companion.Configuration;

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static CompanionSettings LoadOrCreate(AppPaths paths, FileLog log)
    {
        CompanionSettings settings;
        string? originalJson = null;
        if (File.Exists(paths.SettingsFile))
        {
            originalJson = File.ReadAllText(paths.SettingsFile);
            settings = JsonSerializer.Deserialize<CompanionSettings>(originalJson, JsonOptions) ?? new CompanionSettings();
        }
        else
        {
            settings = new CompanionSettings();
        }

        settings.Normalize();
        var migratedPassword = settings.MigratePlainTextPassword();
        var migratedPasswordScope = settings.MigrateProtectedPasswordToMachineScope();
        var normalizedJson = Serialize(settings);
        if (originalJson is null || migratedPassword || migratedPasswordScope || !JsonEquals(originalJson, normalizedJson))
        {
            Write(paths, normalizedJson);
        }

        log.Info($"Loaded settings from {paths.SettingsFile}.");
        if (migratedPassword)
        {
            log.Info("Migrated MQTT password to Windows protected storage.");
        }
        if (migratedPasswordScope)
        {
            log.Info("Migrated MQTT password to machine protected storage for service access.");
        }

        return settings;
    }

    public static void Save(AppPaths paths, CompanionSettings settings)
    {
        settings.Normalize();
        Write(paths, Serialize(settings));
    }

    private static void Write(AppPaths paths, string json)
    {
        Directory.CreateDirectory(paths.ConfigDirectory);
        File.WriteAllText(paths.SettingsFile, json);
    }

    private static string Serialize(CompanionSettings settings)
    {
        return JsonSerializer.Serialize(settings, JsonOptions);
    }

    private static bool JsonEquals(string left, string right)
    {
        try
        {
            using var leftDocument = JsonDocument.Parse(left);
            using var rightDocument = JsonDocument.Parse(right);
            return JsonElementEquals(leftDocument.RootElement, rightDocument.RootElement);
        }
        catch
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    private static bool JsonElementEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object => ObjectEquals(left, right),
            JsonValueKind.Array => ArrayEquals(left, right),
            JsonValueKind.String => left.GetString() == right.GetString(),
            JsonValueKind.Number => left.GetRawText() == right.GetRawText(),
            JsonValueKind.True or JsonValueKind.False => left.GetBoolean() == right.GetBoolean(),
            JsonValueKind.Null or JsonValueKind.Undefined => true,
            _ => left.GetRawText() == right.GetRawText()
        };
    }

    private static bool ObjectEquals(JsonElement left, JsonElement right)
    {
        var leftProperties = left.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
        var rightProperties = right.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToList();
        if (leftProperties.Count != rightProperties.Count)
        {
            return false;
        }

        for (var index = 0; index < leftProperties.Count; index++)
        {
            if (leftProperties[index].Name != rightProperties[index].Name ||
                !JsonElementEquals(leftProperties[index].Value, rightProperties[index].Value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArrayEquals(JsonElement left, JsonElement right)
    {
        var leftItems = left.EnumerateArray().ToList();
        var rightItems = right.EnumerateArray().ToList();
        if (leftItems.Count != rightItems.Count)
        {
            return false;
        }

        for (var index = 0; index < leftItems.Count; index++)
        {
            if (!JsonElementEquals(leftItems[index], rightItems[index]))
            {
                return false;
            }
        }

        return true;
    }
}
