using System.Collections.ObjectModel;
using verba_windows.Models;

namespace verba_windows.Services;

public sealed class CustomToneStore
{
    private const int MaximumCount = 12;
    private readonly SettingsStore _settings;

    public CustomToneStore(SettingsStore settings)
    {
        _settings = settings;
        Tones = new ObservableCollection<CustomTone>(settings.CustomTones.Take(MaximumCount));
    }

    public ObservableCollection<CustomTone> Tones { get; }

    public CustomTone Add(string instruction)
    {
        var text = instruction.Trim();
        var existing = Tones.FirstOrDefault(x => x.Instruction.Equals(text, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { MoveToFront(existing); return existing; }
        var tone = new CustomTone(Guid.NewGuid(), text, DateTimeOffset.UtcNow);
        Tones.Insert(0, tone);
        while (Tones.Count > MaximumCount) Tones.RemoveAt(Tones.Count - 1);
        Save();
        return tone;
    }

    public CustomTone Update(CustomTone existing, string instruction)
    {
        var index = Tones.ToList().FindIndex(x => x.Id == existing.Id);
        var updated = existing with { Instruction = instruction.Trim() };
        if (index >= 0) Tones[index] = updated;
        Save();
        return updated;
    }

    public void Delete(CustomTone tone)
    {
        var existing = Tones.FirstOrDefault(x => x.Id == tone.Id);
        if (existing is not null) { Tones.Remove(existing); Save(); }
    }

    public void MarkUsed(CustomTone tone)
    {
        var existing = Tones.FirstOrDefault(x => x.Id == tone.Id);
        if (existing is not null) MoveToFront(existing);
    }

    private void MoveToFront(CustomTone tone)
    {
        var index = Tones.IndexOf(tone);
        if (index <= 0) return;
        Tones.Move(index, 0);
        Save();
    }

    private void Save() => _settings.SetCustomTones(Tones.ToList());
}
