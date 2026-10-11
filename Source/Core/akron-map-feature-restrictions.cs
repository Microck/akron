using System;
using System.Collections.Generic;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

// The entity is only an authoring marker. Policy reads every room's map data before
// entities are constructed, so its position, spawn flags and current room cannot bypass it.
[CustomEntity(AkronMapFeatureRestrictions.EntityName)]
public sealed class AkronFeatureRestrictionsEntity : Entity
{
    public AkronFeatureRestrictionsEntity(EntityData data, Vector2 offset) : base(data.Position + offset)
    {
        Active = false;
        Visible = false;
    }
}

internal sealed class AkronMapFeatureRestrictions
{
    internal const string EntityName = "Akron/featureRestrictions";
    internal static readonly AkronMapFeatureRestrictions Empty = new(Array.Empty<LevelData>());
    private readonly bool[] restricted = new bool[AkronFeatureRegistry.FeatureCapacity];

    internal IReadOnlyList<AkronFeatureKind> Features { get; }
    internal IReadOnlyList<string> UnknownIdentities { get; }

    internal AkronMapFeatureRestrictions(IEnumerable<LevelData> rooms)
    {
        List<AkronFeatureKind> features = new();
        List<string> unknown = new();
        foreach (LevelData room in rooms)
        {
            if (room.Entities == null)
            {
                continue;
            }
            foreach (EntityData entity in room.Entities)
            {
                if (entity.Name != EntityName)
                {
                    continue;
                }
                string declaration = entity.Values != null && entity.Values.TryGetValue("features", out object value)
                    ? value as string ?? string.Empty
                    : string.Empty;
                foreach (string entry in declaration.Split(','))
                {
                    string identity = entry.Trim();
                    if (identity.Length == 0)
                    {
                        continue;
                    }
                    if (!AkronFeatureRegistry.TryResolveIdentity(identity, out AkronFeatureKind feature))
                    {
                        unknown.Add(identity);
                        continue;
                    }
                    if (!restricted[(int)feature])
                    {
                        restricted[(int)feature] = true;
                        features.Add(feature);
                    }
                }
            }
        }
        Features = features.AsReadOnly();
        UnknownIdentities = unknown.AsReadOnly();
    }

    internal bool Contains(AkronFeatureKind feature)
    {
        int index = (int)feature;
        return index >= 0 && index < restricted.Length && restricted[index];
    }
}
