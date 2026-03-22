namespace DanceLibraryFFXIV;

public sealed class EmoteModEntry
{
    public string ModDirectory { get; init; } = string.Empty;

    public string ModDisplayName { get; init; } = string.Empty;

    public string EmoteCommand { get; init; } = string.Empty;

    public string EmoteDisplayName { get; init; } = string.Empty;

    public bool IsDance { get; init; }

    public bool HasOptions { get; init; }

    public bool IsActive { get; set; }
}
