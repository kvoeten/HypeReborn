using Godot;

namespace HypeReborn.Hype.Maps;

public static class HypeMapDefinitionSerializer
{
    public static HypeMapDefinition? Resolve(Resource? resource)
    {
        if (resource == null)
        {
            return null;
        }

        if (resource is HypeMapDefinition typed)
        {
            return typed;
        }

        return new HypeMapDefinition
        {
            LevelName = ReadString(resource, nameof(HypeMapDefinition.LevelName)),
            ExternalGameRootOverride = ReadString(resource, nameof(HypeMapDefinition.ExternalGameRootOverride)),
            LoadLanguageLayer = ReadBool(resource, nameof(HypeMapDefinition.LoadLanguageLayer), true),
            LoadScripts = ReadBool(resource, nameof(HypeMapDefinition.LoadScripts), true),
            LoadAnimationCatalog = ReadBool(resource, nameof(HypeMapDefinition.LoadAnimationCatalog), true),
            DesignerNotes = ReadString(resource, nameof(HypeMapDefinition.DesignerNotes))
        };
    }

    private static string ReadString(Resource resource, string propertyName, string fallback = "")
    {
        try
        {
            var value = resource.Get(propertyName);
            return value.VariantType == Variant.Type.Nil ? fallback : value.AsString();
        }
        catch
        {
            return fallback;
        }
    }

    private static bool ReadBool(Resource resource, string propertyName, bool fallback)
    {
        try
        {
            var value = resource.Get(propertyName);
            return value.VariantType == Variant.Type.Nil ? fallback : value.AsBool();
        }
        catch
        {
            return fallback;
        }
    }
}
