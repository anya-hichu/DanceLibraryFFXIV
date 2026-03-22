using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace DanceLibraryFFXIV;

public sealed class ModScanner(PenumbraIpc penumbra, IPluginLog log)
{
    private readonly PenumbraIpc penumbra = penumbra;
    private readonly IPluginLog log = log;

    public List<EmoteModEntry> ScanMods()
    {
        var emoteModEntryList = new List<EmoteModEntry>();
        if (!penumbra.IsAvailable)
        {
            log.Warning("[DanceLibrary] ModScanner: Penumbra not available — skipping scan");
            return emoteModEntryList;
        }
        var modList = penumbra.GetModList();
        if (modList == null || modList.Count == 0)
        {
            log.Info("[DanceLibrary] ModScanner: no mods found");
            return emoteModEntryList;
        }
        log.Info($"[DanceLibrary] ModScanner: scanning {modList.Count} mods...");
        var num = 0;
        foreach ((var str, var displayName1) in modList)
        {
            var changedItems = penumbra.GetChangedItems(str);
            if (changedItems != null && changedItems.Count != 0)
            {
                var emoteEntries = ExtractEmoteEntries(str, displayName1, changedItems);
                if (emoteEntries.Count != 0)
                {
                    var availableModSettings = penumbra.GetAvailableModSettings(str);
                    var flag = availableModSettings != null && availableModSettings.Count > 0;
                    foreach ((var command, var displayName2, var isDance) in emoteEntries)
                    {
                        emoteModEntryList.Add(new EmoteModEntry()
                        {
                            ModDirectory = str,
                            ModDisplayName = displayName1,
                            EmoteCommand = command,
                            EmoteDisplayName = displayName2,
                            IsDance = isDance,
                            HasOptions = flag,
                            IsActive = false
                        });
                        ++num;
                    }
                }
            }
        }
        log.Info($"[DanceLibrary] ModScanner: found {num} emote mod entries ({emoteModEntryList.FindAll((Predicate<EmoteModEntry>)(e => e.IsDance)).Count} dances, {emoteModEntryList.FindAll((Predicate<EmoteModEntry>)(e => !e.IsDance)).Count} other)");
        return emoteModEntryList;
    }

    private List<(string command, string displayName, bool isDance)> ExtractEmoteEntries(
      string modDirectory,
      string displayName,
      Dictionary<string, object?> changedItems)
    {
        var emoteEntries = new List<(string, string, bool)>();
        var stringSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((var key, var _) in changedItems)
        {
            if (key.StartsWith("Emote: ", StringComparison.OrdinalIgnoreCase))
            {
                var command1 = key.Substring("Emote: ".Length).Trim();
                if (string.IsNullOrEmpty(command1))
                {
                    log.Debug("[DanceLibrary] Skipping empty emote key in mod: " + displayName);
                }
                else
                {
                    var command2 = EmoteData.NormalizeCommand(command1);
                    var executeCommand = EmoteData.GetExecuteCommand(command2);
                    var displayName1 = EmoteData.GetDisplayName(command2);
                    if (!stringSet.Add(displayName1))
                    {
                        log.Debug($"[DanceLibrary] Skipping duplicate emote '{displayName1}' (execute='{executeCommand}') in mod: {displayName} (Penumbra key: {key})");
                    }
                    else
                    {
                        var flag = EmoteData.IsDance(executeCommand);
                        log.Debug($"[DanceLibrary] Found emote mod: [{displayName}] → penumbra={command2}, execute={executeCommand}, display={displayName1}, isDance={flag}");
                        emoteEntries.Add((executeCommand, displayName1, flag));
                    }
                }
            }
        }
        return emoteEntries;
    }
}
