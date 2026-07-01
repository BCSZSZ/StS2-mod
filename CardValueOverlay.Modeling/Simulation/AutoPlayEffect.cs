namespace CardValueOverlay.Modeling.Simulation;

/// <summary>
/// Resolved auto-play descriptor attached to a single <see cref="SimulationCard"/> form.
/// Describes how a played card auto-plays OTHER cards (the CardCmd.AutoPlay effect).
/// The value of those auto-plays accrues to the auto-played cards / deck EV, not to the
/// trigger card, so any card carrying this effect is valued via play-delta.
/// </summary>
public sealed record AutoPlayEffect(
    string SourcePile,
    string CardTypeFilter,
    bool ExcludeUnplayable,
    string Selection,
    bool RepeatSameCard,
    int Count);

/// <summary>
/// Curated per-card auto-play descriptor loaded from
/// data/manual-tags/card_autoplay_effects.json. Carries both the unupgraded and upgraded
/// auto-play counts; the builder resolves the count for a form's upgrade level.
/// </summary>
public sealed record AutoPlayEffectEntry(
    string ModelId,
    string? TypeName,
    string SourcePile,
    string CardTypeFilter,
    bool ExcludeUnplayable,
    string Selection,
    bool RepeatSameCard,
    int Count,
    int? CountUpgraded);

/// <summary>
/// Deserialization envelope for card_autoplay_effects.json.
/// </summary>
public sealed record AutoPlayEffectCatalog(
    int SchemaVersion,
    string? Description,
    IReadOnlyList<AutoPlayEffectEntry> Cards);
