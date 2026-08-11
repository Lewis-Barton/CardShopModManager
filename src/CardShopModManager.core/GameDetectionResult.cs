namespace CardShopModManager.Core;

public sealed record GameDetectionResult (
    bool IsValid,
    string? GameExecutablePath,
    string? Error
);