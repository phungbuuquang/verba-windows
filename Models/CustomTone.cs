namespace verba_windows.Models;

public sealed record CustomTone(Guid Id, string Instruction, DateTimeOffset CreatedAt)
{
    public string Title => Instruction.Length <= 22 ? Instruction : Instruction[..22].TrimEnd() + "…";
}

public abstract record ToneSelection
{
    public sealed record Preset(Tone Tone) : ToneSelection;
    public sealed record Custom(CustomTone Tone) : ToneSelection;

    public string? ApiValue => this is Preset p ? p.Tone.ToApiValue() : null;
    public string? Instruction => this is Custom c ? $"use this tone: {c.Tone.Instruction}" : null;
    public CustomTone? CustomTone => (this as Custom)?.Tone;
}
