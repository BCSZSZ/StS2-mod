namespace CardValueOverlay.Core.Configuration;

public sealed record CardValueGenerationMetadata
{
    public string? Method { get; init; }

    public TrainingHorizonTimestamps? UpdatedAt { get; init; } = new();
}
