using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DanceLibraryFFXIV.Windows;

public sealed class ModSettingsWindow : IDisposable
{
    private readonly Configuration config;
    private readonly PenumbraIpc penumbra;
    private readonly IPluginLog log;
    private EmoteModEntry? entry;
    private IReadOnlyDictionary<string, (string[] Options, int GroupType)>? availableOptions;
    private Dictionary<string, List<string>> pendingSelections = [];
    private Guid? collectionId;
    private string statusMessage = string.Empty;
    private Vector4 statusColor = Vector4.One;
    private static readonly Vector4 ColorSuccess = new(0.4f, 1f, 0.4f, 1f);
    private static readonly Vector4 ColorError = new(1f, 0.4f, 0.3f, 1f);
    private static readonly Vector4 ColorStarFilled = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 ColorStarEmpty = new(0.45f, 0.45f, 0.45f, 1f);

    public bool IsVisible { get; private set; }

    public ModSettingsWindow(Configuration config, PenumbraIpc penumbra, IPluginLog log)
    {
        this.config = config;
        this.penumbra = penumbra;
        this.log = log;
    }

    public void Open(EmoteModEntry entry)
    {
        this.entry = entry;
        statusMessage = string.Empty;
        pendingSelections.Clear();
        var availableModSettings = penumbra.GetAvailableModSettings(entry.ModDirectory);
        availableOptions = availableModSettings != null ? new Dictionary<string, (string[], int)>(availableModSettings) : null;
        collectionId = penumbra.GetPlayerCollectionId();

        if (config.ModOptionOverrides.TryGetValue(entry.ModDirectory, out var source))
        {
            pendingSelections = source.ToDictionary(kvp => kvp.Key, kvp => new List<string>(kvp.Value));
            log.Debug("[DanceLibrary] Settings window: loaded plugin-stored options for " + entry.ModDisplayName);
        }
        else if (collectionId.HasValue)
        {
            (bool enabled, int priority, Dictionary<string, List<string>> options)? currentModSettings = penumbra.GetCurrentModSettings(collectionId.Value, entry.ModDirectory);
            if (currentModSettings.HasValue)
                pendingSelections = currentModSettings.Value.options.ToDictionary(kvp => kvp.Key, kvp => new List<string>(kvp.Value));
            log.Debug("[DanceLibrary] Settings window: loaded Penumbra current options for " + entry.ModDisplayName);
        }
        IsVisible = true;
        log.Debug("[DanceLibrary] Settings window opened for: " + entry.ModDisplayName);
    }

    public void Close()
    {
        IsVisible = false;
        entry = null;
        statusMessage = string.Empty;
    }

    public void Toggle(EmoteModEntry entry)
    {
        if (IsVisible && this.entry?.ModDirectory == entry.ModDirectory)
            Close();
        else
            Open(entry);
    }

    public void Draw()
    {
        if (!IsVisible || entry == null)
            return;
        ImGui.SetNextWindowSizeConstraints(new Vector2(350f, 200f), new Vector2(600f, 600f));
        ImGui.SetNextWindowSize(new Vector2(420f, 320f), ImGuiCond.FirstUseEver);
        string name = entry.ModDisplayName + " — Settings###DLSettings";
        bool open = true;
        if (!ImGui.Begin(name, ref open))
        {
            ImGui.End();
            if (open)
                return;
            Close();
        }
        else
        {
            try
            {
                DrawWindowContents();
            }
            finally
            {
                ImGui.End();
            }
            if (open)
                return;
            Close();
        }
    }

    private void DrawWindowContents()
    {
        if (entry == null)
            return;
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.5f, 1f), (ImU8String)entry.ModDisplayName);
        ImGui.SameLine();
        ImU8String text = new ImU8String(2, 1);
        text.AppendLiteral("(");
        text.AppendFormatted<string>(entry.EmoteDisplayName);
        text.AppendLiteral(")");
        ImGui.TextDisabled(text);
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextDisabled((ImU8String)"Rating:");
        DrawStarRating(entry.ModDirectory);
        ImGui.Spacing();
        ImGui.Separator();
        if (availableOptions == null || availableOptions.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextWrapped((ImU8String)"This mod has no configurable option groups in Penumbra.");
            ImGui.Spacing();
            DrawCloseButton();
        }
        else if (!collectionId.HasValue)
        {
            ImGui.Spacing();
            ImGui.TextColored(in ColorError, (ImU8String)"Could not determine your Penumbra collection.");
            ImGui.TextWrapped((ImU8String)"Make sure your character is loaded and Penumbra is active.");
            ImGui.Spacing();
            DrawCloseButton();
        }
        else
        {
            ImGui.Spacing();
            DrawOptionGroups();
            ImGui.Spacing();
            if (!string.IsNullOrEmpty(statusMessage))
            {
                ImGui.TextColored(in statusColor, (ImU8String)statusMessage);
                ImGui.Spacing();
            }
            ImGui.TextDisabled((ImU8String)"Saved to plugin. Click Perform to apply these options in-game.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            DrawApplyButton();
            ImGui.SameLine();
            DrawCloseButton();
        }
    }

    private void DrawOptionGroups()
    {
        if (availableOptions == null)
            return;
        var groupIndex = 0;
        foreach ((var str, (var strArray, var GroupType)) in (IEnumerable<KeyValuePair<string, (string[] Options, int GroupType)>>)availableOptions)
        {
            ImGui.TextColored(new Vector4(0.8f, 0.8f, 1f, 1f), str);

            if (!pendingSelections.TryGetValue(str, out var selected))
            {
                selected = [];
                pendingSelections[str] = selected;
            }
            if (GroupType == 0)
            {
                DrawSingleSelectGroup(str, strArray, selected, groupIndex);
            }
            else
            {
                DrawMultiSelectGroup(str, strArray, selected, groupIndex);
                ImGui.TextDisabled("  (multi-select)");
            }
            ImGui.Spacing();
            ++groupIndex;
        }
    }

    private void DrawSingleSelectGroup(
      string groupName,
      string[] options,
      List<string> selected,
      int groupIndex)
    {
        for (int index = 0; index < options.Length; ++index)
        {
            var option = options[index];
            var active = selected.Count > 0 && selected[0] == option;
            var label = new ImU8String(9, 3);
            label.AppendFormatted<string>(option);
            label.AppendLiteral("##radio_");
            label.AppendFormatted<int>(groupIndex);
            label.AppendLiteral("_");
            label.AppendFormatted<int>(index);
            if (ImGui.RadioButton(label, active))
            {
                selected.Clear();
                selected.Add(option);
            }
        }
    }

    private void DrawMultiSelectGroup(
      string groupName,
      string[] options,
      List<string> selected,
      int groupIndex)
    {
        for (int index = 0; index < options.Length; ++index)
        {
            var option = options[index];
            var v = selected.Contains(option);
            var label = new ImU8String(6, 3);
            label.AppendFormatted<string>(option);
            label.AppendLiteral("##cb_");
            label.AppendFormatted<int>(groupIndex);
            label.AppendLiteral("_");
            label.AppendFormatted<int>(index);
            if (ImGui.Checkbox(label, ref v))
            {
                if (v)
                {
                    if (!selected.Contains(option))
                        selected.Add(option);
                }
                else
                    selected.Remove(option);
            }
        }
    }

    private void DrawStarRating(string modDirectory)
    {
        var valueOrDefault = config.ModStarRatings.GetValueOrDefault(modDirectory, 0);
        for (var index = 1; index <= 5; ++index)
        {
            ImGui.SameLine();
            var flag = index <= valueOrDefault;
            ImGui.PushStyleColor(ImGuiCol.Text, flag ? ColorStarFilled : ColorStarEmpty);
            var label = new ImU8String(6, 2);
            label.AppendFormatted<string>(flag ? "★" : "☆");
            label.AppendLiteral("##star");
            label.AppendFormatted<int>(index);
            if (ImGui.SmallButton(label))
            {
                int num = index == valueOrDefault ? 0 : index;
                config.ModStarRatings[modDirectory] = num;
                config.Save();
                log.Debug($"[DanceLibrary] Star rating set: {modDirectory} → {num}★");
            }
            ImGui.PopStyleColor();
        }
    }

    private void DrawApplyButton()
    {
        if (!ImGui.Button((ImU8String)"Apply"))
            return;
        if (entry == null || availableOptions == null)
        {
            statusMessage = "Cannot save — mod data not available.";
            statusColor = ColorError;
        }
        else
        {
            var dictionary = pendingSelections.ToDictionary(kvp => kvp.Key, kvp => new List<string>(kvp.Value));
            config.ModOptionOverrides[entry.ModDirectory] = dictionary;
            config.Save();
            statusMessage = "Settings saved! Click Perform to apply them in-game.";
            statusColor = ColorSuccess;
            log.Info($"[DanceLibrary] Option overrides saved for: {entry.ModDisplayName} ({dictionary.Count} groups)");
        }
    }

    private void DrawCloseButton()
    {
        if (!ImGui.Button((ImU8String)"Close"))
            return;
        Close();
    }

    public void Dispose()
    {
        IsVisible = false;
        entry = null;
    }
}
