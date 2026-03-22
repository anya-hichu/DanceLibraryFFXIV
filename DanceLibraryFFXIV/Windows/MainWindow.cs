using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace DanceLibraryFFXIV.Windows;

public sealed class MainWindow : IDisposable
{
    private readonly Configuration config;
    private readonly ModScanner scanner;
    private readonly PenumbraIpc penumbra;
    private readonly ChatSender chatSender;
    private readonly ModSettingsWindow settingsWindow;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private ScanState scanState;
    private bool howToPopupPending;
    private bool backupPopupPending;
    private string backupResultMessage = string.Empty;
    private string scanError = string.Empty;
    private Task? scanTask;
    private readonly Lock @lock = new();
    private List<EmoteModEntry> allEntries = [];
    private static readonly string[] BuiltInCategories = [
        "Dance",
        "Emote",
        "NSFW"
    ];
    private string[] categories = [
        "Dance",
        "Emote",
        "NSFW",
        "Other"
    ];
    private readonly HashSet<string> activeMods = [];
    private readonly bool moveModeEnabled = false;
    private int moveModeTargetIdx;
    private string? draggedModDir;
    private string? draggedFromCategory;
    private string? draggedFromGroupName;
    private static readonly byte[] DragPayloadData = [1];

    private int draggedGroupReorderIndex = -1;
    private string? draggedGroupReorderCategory;
    private readonly HashSet<string> selectedMods = [];
    private string? selectionAnchorDir;
    private readonly Dictionary<string, List<string>> cachedTabDrawOrder = [];
    private string? activeTabCategory;
    private string? renamingCategory;
    private int renamingGroupIndex = -1;
    private string renameBuffer = string.Empty;
    private bool renameGroupNeedsFocus;
    private bool renameGroupWasActive;
    private string newTabBuffer = string.Empty;
    private bool addingTabInMenu;
    private string? addingTabForCat;
    private bool addTabNeedsKeyboardFocus;
    private string? renamingTabName;
    private string renameTabBuffer = string.Empty;
    private int starFilter;
    private static readonly string[] StarFilterLabels = [
        "All",
        "1★+",
        "2★+",
        "3★+",
        "4★+",
        "5★ only"
    ];
    private readonly HashSet<string> expandedMods = [];
    private readonly Dictionary<string, List<RenderRow>> cachedRenderRows = [];
    private bool tabCacheDirty = true;
    private List<EmoteModEntry>? cachedEntriesRef;
    private readonly Dictionary<string, List<EmoteModEntry>> cachedTabEntries = [];
    private readonly Dictionary<string, List<EmoteModEntry>> cachedUngroupedOrdered = [];
    private bool rowWidthsCached;
    private float cachedResetW;
    private float cachedSettingsW;
    private float cachedPerformW;
    private float cachedCatBtnW;
    private float cachedPnbW;
    private static readonly Vector4 ColorActive = new(0.4f, 1f, 0.5f, 1f);
    private static readonly Vector4 ColorWarning = new(1f, 0.85f, 0.3f, 1f);
    private static readonly Vector4 ColorError = new(1f, 0.4f, 0.3f, 1f);
    private static readonly Vector4 ColorMuted = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ColorTitle = new(0.9f, 0.8f, 1f, 1f);
    private static readonly Vector4 ColorMoveModeOn = new(0.2f, 0.7f, 0.3f, 1f);

    public bool IsVisible
    {
        get => config.IsMainWindowVisible;
        set
        {
            config.IsMainWindowVisible = value;
            config.Save();
        }
    }

    public MainWindow(
      Configuration config,
      ModScanner scanner,
      PenumbraIpc penumbra,
      ChatSender chatSender,
      ModSettingsWindow settingsWindow,
      IFramework framework,
      IPluginLog log)
    {
        this.config = config;
        this.scanner = scanner;
        this.penumbra = penumbra;
        this.chatSender = chatSender;
        this.settingsWindow = settingsWindow;
        this.framework = framework;
        this.log = log;
        RebuildCategories();
    }

    public void Open()
    {
        IsVisible = true;
        if (scanState != ScanState.NotScanned)
            return;
        StartScan();
    }

    public void Toggle()
    {
        if (IsVisible)
            IsVisible = false;
        else
            Open();
    }

    private void StartScan()
    {
        if (scanState == ScanState.Scanning)
            return;
        scanState = ScanState.Scanning;
        scanError = string.Empty;
        if (!penumbra.IsAvailable)
            penumbra.CheckAvailability();
        scanTask = Task.Run((Action)(() =>
        {
            try
            {
                log.Debug("[DanceLibrary] Background scan started");
                var emoteModEntryList = scanner.ScanMods();
                var dedupSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var count = emoteModEntryList.Count;
                emoteModEntryList.RemoveAll(e => !dedupSeen.Add($"{e.ModDirectory}\0{e.EmoteDisplayName}"));
                if (emoteModEntryList.Count != count)
                    log.Warning($"[DanceLibrary] Final dedup removed {count - emoteModEntryList.Count} duplicate scan entries");
                lock (@lock)
                {
                    allEntries = emoteModEntryList;
                    scanState = ScanState.Done;
                }
                log.Info($"[DanceLibrary] Scan complete: {emoteModEntryList.Count} emote mod entries");
            }
            catch (Exception ex)
            {
                lock (@lock)
                {
                    scanError = ex.Message;
                    scanState = ScanState.Error;
                }
                log.Error(ex, "[DanceLibrary] Background scan failed");
            }
        }));
    }

    public void Draw()
    {
        settingsWindow.Draw();
        if (!IsVisible)
            return;
        ImGui.SetNextWindowSize(new Vector2(650f, 540f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(450f, 300f), new Vector2(1400f, 1200f));
        var isVisible = IsVisible;
        if (!ImGui.Begin("Dance Library###DLMain", ref isVisible))
        {
            ImGui.End();
            if (isVisible)
                return;
            IsVisible = false;
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
            if (isVisible)
                return;
            IsVisible = false;
        }
    }

    private void DrawWindowContents()
    {
        DrawHeader();
        DrawHowToPopup();
        DrawBackupPopup();
        DrawStatusBar();
        ImGui.Separator();
        ImGui.Spacing();
        if (!penumbra.IsAvailable)
        {
            ImGui.TextColored(in ColorError, "Penumbra is not available.");
            ImGui.TextWrapped("Make sure Penumbra is installed and loaded, then click Refresh.");
        }
        else if (scanState == ScanState.Scanning)
            ImGui.TextColored(in ColorWarning, "Scanning mods...");
        else if (scanState == ScanState.Error)
        {
            ImGui.TextColored(in ColorError, "Scan failed:");
            ImGui.TextWrapped(scanError);
            ImGui.Spacing();
            if (!ImGui.Button("Retry"))
                return;
            StartScan();
        }
        else if (scanState == ScanState.NotScanned)
            ImGui.TextDisabled("Press Refresh to scan your Penumbra mods.");
        else
            DrawTabBar();
    }

    private void DrawHeader()
    {
        ImGui.TextColored(in ColorTitle, "Dance Library");
        var x1 = ImGui.GetStyle().ItemSpacing.X;
        var x2 = ImGui.GetStyle().WindowPadding.X;
        var x3 = ImGui.GetStyle().FramePadding.X;
        var str = scanState == ScanState.Scanning ? "Scanning..." : "Refresh";
        var num1 = ImGui.CalcTextSize(str).X + x3 * 2f;
        var num2 = ImGui.CalcTextSize("Reset All").X + x3 * 2f;
        var num3 = ImGui.CalcTextSize("Backup").X + x3 * 2f;
        var num4 = ImGui.CalcTextSize("?").X + x3 * 2f + x1 + num2 + x1 + num3 + x1 + num1;
        ImGui.SameLine((float)((double)ImGui.GetWindowWidth() - (double)num4 - (double)x2 - 4.0));
        if (ImGui.SmallButton("?"))
            howToPopupPending = true;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How to use Dance Library");
        ImGui.SameLine();
        int num5 = scanState == ScanState.Done ? 1 : 0;
        if (num5 == 0)
            ImGui.BeginDisabled();
        if (ImGui.SmallButton("Reset All"))
            ResetAllMods();
        if (num5 == 0)
            ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Remove temporary Penumbra settings from all known mods");
        ImGui.SameLine();
        if (ImGui.SmallButton("Backup"))
            DoBackup();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Save a copy of your library layout to Downloads");
        ImGui.SameLine();
        if (scanState == ScanState.Scanning)
            ImGui.BeginDisabled();
        if (ImGui.SmallButton(str))
        {
            penumbra.CheckAvailability();
            StartScan();
        }
        if (scanState != ScanState.Scanning)
            return;
        ImGui.EndDisabled();
    }

    private void DrawHowToPopup()
    {
        if (howToPopupPending)
        {
            ImGui.OpenPopup("How To Use Dance Library##dlhowto");
            howToPopupPending = false;
        }
        ImGui.SetNextWindowSize(new Vector2(520f, 580f), ImGuiCond.Always);
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal("How To Use Dance Library##dlhowto", ImGuiWindowFlags.NoResize))
            return;
        ImGui.BeginChild("##howto_scroll", new Vector2(0.0f, -35f));
        ImGui.TextColored(in ColorTitle, "Performing Emotes");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Click a mod name or the Perform button to temporarily enable the mod via Penumbra and execute the emote in-game.");
        ImGui.Spacing();
        ImGui.TextWrapped("Reset  — removes the temporary Penumbra setting for that mod (returns it to its normal state).");
        ImGui.TextWrapped("Reset All  — removes temporary settings from every mod at once.");
        ImGui.Spacing();
        ImGui.TextColored(in ColorTitle, "Row Buttons");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("★ / ☆  — Favorite toggle. Favorited mods sort to the top of their section.");
        ImGui.TextWrapped("[Category]  — Click to reassign the mod to a different tab.");
        ImGui.TextWrapped("Perform  — Enable mod + execute emote.");
        ImGui.TextWrapped("Settings  — Open the option editor for mods with configurable Penumbra options (sound, outfit variants, etc.).");
        ImGui.TextWrapped("Pnb  — Jump directly to this mod in the Penumbra mod browser.");
        ImGui.TextWrapped("Reset  — Remove this mod's temporary Penumbra setting.");
        ImGui.Spacing();
        ImGui.TextColored(in ColorTitle, "Star Ratings & Filter");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Open Settings for any mod to assign it a 1–5 star personal quality rating.");
        ImGui.TextWrapped("Use the star filter dropdown (All / 1★+ / 2★+ … / 5★ only) at the top of each tab to show only mods at or above your chosen rating.");
        ImGui.Spacing();
        ImGui.TextColored(in ColorTitle, "Right-Click Menu (on any mod row)");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Open in Penumbra  — Open this mod's page in Penumbra (single-mod only).");
        ImGui.TextWrapped("Move to category  — Reassign to a different tab.");
        ImGui.TextWrapped("Move to group  — Move into an existing group in this tab.");
        ImGui.TextWrapped("Remove from group  — Return mod to ungrouped.");
        ImGui.TextWrapped("Block mod  — Hide the mod from all plugin operations. Find it again in the Unblock tab; right-click there to restore it.");
        ImGui.Spacing();
        ImGui.TextWrapped("Ctrl+click or Shift+click to select multiple mods, then right-click for bulk Move / Block operations.");
        ImGui.Spacing();
        ImGui.TextColored(in ColorTitle, "Organizing with Groups");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Click '+ New Group' at the bottom of any tab to create a collapsible named group.");
        ImGui.TextWrapped("≡  — Drag handle on each row. Drag a mod onto another mod or group header to reorder or move it.");
        ImGui.TextWrapped("▲/▼  — Arrow button on group headers. Drag to reorder groups.");
        ImGui.TextWrapped("✎  — Rename a group inline.");
        ImGui.TextWrapped("X  — Delete a group (its mods return to ungrouped).");
        ImGui.TextWrapped("Right-click a group header for Rename / Delete options.");
        ImGui.Spacing();
        ImGui.TextColored(in ColorTitle, "Tabs");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Dance, Emote, NSFW, and Other are built-in tabs that cannot be renamed or deleted.");
        ImGui.TextWrapped("Right-click any tab to create a custom tab with any name. Right-click a custom tab to rename or delete it.");
        ImGui.TextWrapped("Deleting a custom tab moves all its mods to Other automatically.");
        ImGui.TextWrapped("Unblock tab  — Lists mods you have blocked. Right-click any entry to unblock it.");
        ImGui.Spacing();
        ImGui.TextColored(in ColorTitle, "Header Buttons");
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Backup  — Copies your library layout (categories, groups, favorites, ratings) to a timestamped JSON file in your Downloads folder. Does not include Penumbra settings.");
        ImGui.TextWrapped("Refresh  — Re-scans all Penumbra mods for emote overrides. Run this after installing or removing mods.");
        ImGui.EndChild();
        ImGui.Spacing();
        var x = 120f;
        ImGui.SetCursorPosX((float)(((double)ImGui.GetWindowWidth() - (double)x) * 0.5));
        if (ImGui.Button("Close", new Vector2(x, 0.0f)))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DoBackup()
    {
        try
        {
            var sourceFileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XIVLauncher", "pluginConfigs", "DanceLibraryFFXIV.json");
            var path1 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var str1 = $"DanceLibraryFFXIV_{DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}.json";
            var path2 = str1;
            var str2 = Path.Combine(path1, path2);
            var destFileName = str2;
            File.Copy(sourceFileName, destFileName, false);
            backupResultMessage = "Backup saved!\n\nDownloads\\" + str1;
            log.Info("[DanceLibrary] Backup saved to " + str2);
        }
        catch (Exception ex)
        {
            backupResultMessage = "Backup failed:\n" + ex.Message;
            log.Error(ex, "[DanceLibrary] Backup failed");
        }
        backupPopupPending = true;
    }

    private void DrawBackupPopup()
    {
        if (backupPopupPending)
        {
            ImGui.OpenPopup("Backup Result##dlbk");
            backupPopupPending = false;
        }
        ImGui.SetNextWindowPos(ImGui.GetIO().DisplaySize * 0.5f, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal("Backup Result##dlbk", ImGuiWindowFlags.AlwaysAutoResize))
            return;
        ImGui.TextUnformatted(backupResultMessage);
        ImGui.Spacing();
        if (ImGui.Button("OK", new Vector2(120f, 0.0f)))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawStatusBar()
    {
        List<EmoteModEntry> allEntries;
        lock (@lock)
            allEntries = this.allEntries;
        switch (scanState)
        {
            case ScanState.Scanning:
                ImGui.TextColored(in ColorWarning, "Scanning...");
                break;
            case ScanState.Done:
                if (allEntries.Count == 0)
                {
                    ImGui.TextColored(in ColorMuted, "No emote mods found. Install Penumbra dance mods to see them here.");
                    break;
                }
                Dictionary<string, int> catCounts = [];
                foreach (var category in categories)
                    catCounts[category] = 0;
                foreach (var entry in allEntries)
                {
                    var modCategory = GetModCategory(entry);
                    if (catCounts.TryGetValue(modCategory, out var value))
                        catCounts[modCategory] = ++value;
                    else
                        ++catCounts["Other"];
                }

                var values = categories.Where(c => catCounts[c] > 0).Select(c => $"{catCounts[c]} {c}");
                ref readonly var local1 = ref ColorMuted;
                var imU8String1 = new ImU8String(27, 2);
                imU8String1.AppendLiteral("Found ");
                imU8String1.AppendFormatted(allEntries.Count);
                imU8String1.AppendLiteral(" emote mod entries (");
                imU8String1.AppendFormatted(string.Join(", ", values));
                imU8String1.AppendLiteral(")");
                var text1 = imU8String1;
                ImGui.TextColored(in local1, text1);
                break;
            case ScanState.Error:
                ref readonly var local2 = ref ColorError;
                var imU8String2 = new ImU8String(12, 1);
                imU8String2.AppendLiteral("Scan error: ");
                imU8String2.AppendFormatted(scanError);
                var text2 = imU8String2;
                ImGui.TextColored(in local2, text2);
                break;
            default:
                ImGui.TextColored(in ColorMuted, "Not yet scanned.");
                break;
        }
    }

    private void DrawUnblockTab()
    {
        var emoteModEntryList2 = cachedTabEntries.TryGetValue("Unblock", out var emoteModEntryList1) ? emoteModEntryList1 : [];
        var label = new ImU8String(24, 1);
        label.AppendLiteral("Unblock (");
        label.AppendFormatted(emoteModEntryList2.Count);
        label.AppendLiteral(")###tab_Unblock");
        if (!ImGui.BeginTabItem(label))
            return;
        if (activeTabCategory != "Unblock")
        {
            ClearSelection();
            activeTabCategory = "Unblock";
        }
        ImGui.Spacing();
        if (emoteModEntryList2.Count == 0)
        {
            ImGui.TextColored(in ColorMuted, "No blocked mods.");
        }
        else
        {
            ImGui.TextDisabled("Blocked mods are hidden from all plugin operations.");
            ImGui.TextDisabled("Right-click a mod to unblock it. It will return to its previous category (or Other).");
            ImGui.Separator();
            ImGui.Spacing();
            foreach (EmoteModEntry entry in emoteModEntryList2)
                DrawBlockedModRow(entry);
        }
        ImGui.EndTabItem();
    }

    private void DrawBlockedModRow(EmoteModEntry entry)
    {
        var strId = new ImU8String(8, 1);
        strId.AppendLiteral("blocked_");
        strId.AppendFormatted(entry.ModDirectory);
        ImGui.PushID(strId);
        ImGui.PushStyleColor(ImGuiCol.Text, ColorMuted);
        var label = new ImU8String(14, 1);
        label.AppendLiteral("  ");
        label.AppendFormatted(entry.ModDisplayName);
        label.AppendLiteral("##blockedrow");
        ImGui.Selectable(label);
        ImGui.PopStyleColor();
        if (ImGui.BeginPopupContextItem("##blockedrowctx"))
        {
            if (ImGui.MenuItem("Unblock"))
                UnblockMod(entry.ModDirectory);
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawTabBar()
    {
        List<EmoteModEntry> allEntries;
        lock (@lock)
            allEntries = this.allEntries;
        if (tabCacheDirty || allEntries != cachedEntriesRef)
            RebuildTabCache(allEntries);
        if (!ImGui.BeginTabBar("##DLTabs"))
            return;
        foreach (var category in categories)
        {
            var entries = cachedTabEntries.TryGetValue(category, out var emoteModEntryList) ? emoteModEntryList : [];
            var isCustom = config.CustomCategories.Contains(category);
            var label = new ImU8String(10, 3);
            label.AppendFormatted<string>(category);
            label.AppendLiteral(" (");
            label.AppendFormatted<int>(entries.Count);
            label.AppendLiteral(")###tab_");
            label.AppendFormatted<string>(category);
            var tabOpened = ImGui.BeginTabItem(label);
            if (!DrawTabContextMenu(category, isCustom, ref tabOpened) && tabOpened)
            {
                DrawEntryList(entries, category);
                ImGui.EndTabItem();
            }
        }
        DrawUnblockTab();
        ImGui.EndTabBar();
    }

    private bool DrawTabContextMenu(string cat, bool isCustom, ref bool tabOpened)
    {
        ImU8String strId = new ImU8String(9, 1);
        strId.AppendLiteral("##tabctx_");
        strId.AppendFormatted<string>(cat);
        if (!ImGui.BeginPopupContextItem(strId))
        {
            if (addingTabForCat == cat)
            {
                addingTabInMenu = false;
                addingTabForCat = null;
            }
            return false;
        }
        if (isCustom)
        {
            if (renamingTabName == cat)
            {
                ImGui.Text("Rename tab:");
                ImGui.SetNextItemWidth(160f);
                bool flag = ImGui.InputText("##tabren", ref renameTabBuffer, 64 /*0x40*/, ImGuiInputTextFlags.EnterReturnsTrue);
                ImGui.SameLine();
                if (ImGui.SmallButton("OK") | flag)
                {
                    string newName = renameTabBuffer.Trim();
                    if (!string.IsNullOrEmpty(newName) && !Array.Exists<string>(categories, (Predicate<string>)(c => c.Equals(newName, StringComparison.OrdinalIgnoreCase))))
                        RenameCustomTab(cat, newName);
                    renamingTabName = null;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Cancel"))
                {
                    renamingTabName = null;
                    ImGui.CloseCurrentPopup();
                }
            }
            else
            {
                if (ImGui.MenuItem("Rename"))
                {
                    renamingTabName = cat;
                    renameTabBuffer = cat;
                }
                ImU8String label = new ImU8String(9, 1);
                label.AppendLiteral("Delete \"");
                label.AppendFormatted<string>(cat);
                label.AppendLiteral("\"");
                if (ImGui.MenuItem(label))
                {
                    DeleteCustomTab(cat);
                    if (tabOpened)
                    {
                        ImGui.EndTabItem();
                        tabOpened = false;
                    }
                    ImGui.EndPopup();
                    return true;
                }
                ImGui.Separator();
            }
        }
        if (ImGui.MenuItem("Reset Category"))
            ResetCategory(cat);
        ImGui.Separator();
        if (!addingTabInMenu)
        {
            if (ImGui.Selectable("Add Tab...", flags: ImGuiSelectableFlags.DontClosePopups))
            {
                addingTabInMenu = true;
                addingTabForCat = cat;
                addTabNeedsKeyboardFocus = true;
                newTabBuffer = string.Empty;
            }
        }
        else
        {
            ImGui.Text("New tab name:");
            ImGui.SetNextItemWidth(160f);
            if (addTabNeedsKeyboardFocus)
            {
                ImGui.SetKeyboardFocusHere();
                addTabNeedsKeyboardFocus = false;
            }
            bool flag = ImGui.InputText("##newtabname", ref newTabBuffer, 64 /*0x40*/, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if (ImGui.SmallButton("Add") | flag)
            {
                var name = newTabBuffer.Trim();
                if (!string.IsNullOrEmpty(name) && !Array.Exists(categories, c => c.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    AddCustomTab(name);
                addingTabInMenu = false;
                addingTabForCat = null;
                newTabBuffer = string.Empty;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Cancel"))
            {
                addingTabInMenu = false;
                addingTabForCat = null;
                newTabBuffer = string.Empty;
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.EndPopup();
        return false;
    }

    private void DrawEntryList(List<EmoteModEntry> entries, string category)
    {
        if (activeTabCategory != category)
        {
            ClearSelection();
            activeTabCategory = category;
        }
        if (entries.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(in ColorMuted, "No mods in this category yet.");
            ImGui.TextDisabled("Use the category dropdown (or right-click) to assign mods here.");
        }
        else
        {
            EnsureRowWidthsCached();
            HashSet<string> presentDirs = [.. entries.Select(e => e.ModDirectory)];
            ImU8String label = new ImU8String(16 /*0x10*/, 1);
            label.AppendLiteral("+ New Group##ng_");
            label.AppendFormatted<string>(category);
            if (ImGui.SmallButton(label))
                AddGroup(category);
            ImGui.SameLine();
            ImGui.TextDisabled("  ★ Filter:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(72f);
            int starFilter = this.starFilter;
            if (ImGui.Combo("##starfilter", ref this.starFilter, (ReadOnlySpan<string>)StarFilterLabels, StarFilterLabels.Length) && starFilter != this.starFilter)
                log.Debug("[DanceLibrary] Star filter changed: " + StarFilterLabels[this.starFilter]);
            ImGui.Spacing();
            DrawColumnHeaders();
            Vector2 size = new Vector2(0.0f, ImGui.GetContentRegionAvail().Y);
            ImU8String strId = new ImU8String(11, 1);
            strId.AppendLiteral("##DLScroll_");
            strId.AppendFormatted<string>(category);
            ImGui.BeginChild(strId, size, flags: ImGuiWindowFlags.AlwaysVerticalScrollbar);
            DrawUngroupedSection(entries, category);
            List<ModGroup> categoryGroups = GetCategoryGroups(category);
            for (int index = 0; index < categoryGroups.Count; ++index)
                DrawGroup(entries, category, index, categoryGroups[index], presentDirs);
            ImGui.EndChild();
        }
    }

    private void DrawColumnHeaders()
    {
        float x = ImGui.GetStyle().ItemSpacing.X;
        double offsetFromStartX1 = (double)ImGui.GetContentRegionMax().X - (double)ImGui.GetStyle().ScrollbarSize - (double)cachedResetW - (double)x - (double)cachedPnbW - (double)x - (double)cachedSettingsW - (double)x - (double)cachedPerformW - (double)x - (double)cachedCatBtnW;
        double offsetFromStartX2 = offsetFromStartX1 - (double)x - 130.0;
        ImGui.TextColored(in ColorMuted, "Mod Name");
        ImGui.SameLine((float)offsetFromStartX2);
        ImGui.TextColored(in ColorMuted, "Emote");
        ImGui.SameLine((float)offsetFromStartX1);
        ImGui.TextColored(in ColorMuted, "Category");
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void DrawUngroupedSection(List<EmoteModEntry> tabEntries, string category)
    {
        if (!cachedRenderRows.TryGetValue(category, out var source) || source.Count == 0)
            return;
        var renderRowList = starFilter > 0 ? [.. source.Where(r => GetStarRating(r.Entry.ModDirectory) >= starFilter)] : source;
        if (renderRowList.Count == 0)
        {
            ref readonly Vector4 local = ref ColorMuted;
            ImU8String imU8String = new ImU8String(38, 1);
            imU8String.AppendLiteral("No mods with ");
            imU8String.AppendFormatted<int>(starFilter);
            imU8String.AppendLiteral("+ stars in this category.");
            ImU8String text = imU8String;
            ImGui.TextColored(in local, text);
            ImGui.Spacing();
        }
        else
        {
            for (int index = 0; index < renderRowList.Count; ++index)
            {
                RenderRow renderRow = renderRowList[index];
                if (renderRow.Kind == RowKind.MultiChild)
                    DrawChildRow(renderRow.Entry, category);
                else
                    DrawEntryRow(renderRow.Entry, index, category, null, renderRow.Kind == RowKind.MultiParent, renderRow.EmoteCount);
            }
            ImGui.Spacing();
        }
    }

    private void DrawGroup(
      List<EmoteModEntry> tabEntries,
      string category,
      int gi,
      ModGroup group,
      HashSet<string> presentDirs)
    {
        ImGui.Spacing();
        var strId1 = new ImU8String(5, 2);
        strId1.AppendLiteral("grp_");
        strId1.AppendFormatted<string>(category);
        strId1.AppendLiteral("_");
        strId1.AppendFormatted<int>(gi);
        ImGui.PushID(strId1);
        var dir = group.IsCollapsed ? ImGuiDir.Right : ImGuiDir.Down;
        var strId2 = new ImU8String(7, 0);
        strId2.AppendLiteral("##arrow");
        if (ImGui.ArrowButton(strId2, dir))
        {
            group.IsCollapsed = !group.IsCollapsed;
            config.Save();
        }
        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
        {
            draggedGroupReorderIndex = gi;
            draggedGroupReorderCategory = category;
            ImGui.SetDragDropPayload("DL_GROUP", (ReadOnlySpan<byte>)DragPayloadData);
            ImU8String text = new ImU8String(7, 1);
            text.AppendLiteral("Group: ");
            text.AppendFormatted<string>(group.Name);
            ImGui.Text(text);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginDragDropTarget())
        {
            if (!ImGui.AcceptDragDropPayload("DL_GROUP").IsNull && draggedGroupReorderCategory == category && draggedGroupReorderIndex >= 0 && draggedGroupReorderIndex != gi && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                var categoryGroups = GetCategoryGroups(category);
                var modGroup = categoryGroups[draggedGroupReorderIndex];
                categoryGroups.RemoveAt(draggedGroupReorderIndex);
                categoryGroups.Insert(gi, modGroup);
                config.Save();
                draggedGroupReorderIndex = -1;
            }
            ImGui.EndDragDropTarget();
        }
        ImGui.SameLine();
        var x = ImGui.GetStyle().ItemSpacing.X;
        var num1 = ImGui.CalcTextSize("✎").X + ImGui.GetStyle().FramePadding.X * 2f;
        var num2 = Math.Max(60f, (float)((double)ImGui.GetContentRegionAvail().X - (double)num1 * 2.0 - (double)x * 2.0));
        if ((renamingGroupIndex != gi ? 0 : (renamingCategory == category ? 1 : 0)) != 0)
        {
            ImGui.SetNextItemWidth(num2);
            if (renameGroupNeedsFocus)
            {
                ImGui.SetKeyboardFocusHere();
                renameGroupNeedsFocus = false;
            }
            ImGui.InputText("##rename", ref renameBuffer, 128 /*0x80*/);
            if (ImGui.IsItemActive())
                renameGroupWasActive = true;
            if (ImGui.IsItemDeactivatedAfterEdit() || (!ImGui.IsItemActive() && renameGroupWasActive && renamingGroupIndex == gi))
            {
                group.Name = string.IsNullOrWhiteSpace(renameBuffer) ? "New Group" : renameBuffer.Trim();
                config.Save();
                renamingGroupIndex = -1;
                renamingCategory = null;
                renameGroupWasActive = false;
            }
        }
        else
        {
            int num3 = group.ModDirectories.Count(d => presentDirs.Contains(d));
            if (ImGui.Selectable($"{group.Name} ({num3})###grplbl_{category}_{gi}", size: new Vector2(num2, 0.0f)))
            {
                group.IsCollapsed = !group.IsCollapsed;
                config.Save();
            }
            ImU8String strId3 = new ImU8String(10, 2);
            strId3.AppendLiteral("##grpctx_");
            strId3.AppendFormatted(category);
            strId3.AppendLiteral("_");
            strId3.AppendFormatted(gi);
            if (ImGui.BeginPopupContextItem(strId3))
            {
                if (ImGui.MenuItem("Rename"))
                {
                    renamingGroupIndex = gi;
                    renamingCategory = category;
                    renameBuffer = group.Name;
                    renameGroupNeedsFocus = true;
                    renameGroupWasActive = false;
                }
                if (ImGui.MenuItem("Delete"))
                {
                    DeleteGroup(category, gi);
                    ImGui.EndPopup();
                    ImGui.PopID();
                    return;
                }
                ImGui.EndPopup();
            }
            if (ImGui.BeginDragDropTarget())
            {
                if (!ImGui.AcceptDragDropPayload("DL_MOD").IsNull && draggedModDir != null && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                    MoveToGroup(draggedFromCategory!, draggedFromGroupName, category, group.Name);
                ImGui.EndDragDropTarget();
            }
        }
        ImGui.SameLine();
        ImU8String label1 = new ImU8String(7, 1);
        label1.AppendLiteral("✎##ren_");
        label1.AppendFormatted<int>(gi);
        if (ImGui.SmallButton(label1))
        {
            renamingGroupIndex = gi;
            renamingCategory = category;
            renameBuffer = group.Name;
            renameGroupNeedsFocus = true;
            renameGroupWasActive = false;
        }
        ImGui.SameLine();
        ImU8String label2 = new ImU8String(7, 1);
        label2.AppendLiteral("X##del_");
        label2.AppendFormatted<int>(gi);
        if (ImGui.SmallButton(label2))
        {
            DeleteGroup(category, gi);
            ImGui.PopID();
        }
        else
        {
            if (!group.IsCollapsed)
                DrawGroupItems(tabEntries, category, gi, group);
            ImGui.PopID();
        }
    }

    private void DrawGroupItems(
      List<EmoteModEntry> tabEntries,
      string category,
      int gi,
      ModGroup group)
    {
        var byDir = tabEntries.ToLookup(e => e.ModDirectory);
        List<EmoteModEntry> list = [.. group.ModDirectories.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(byDir.Contains)
            .OrderByDescending(config.FavoriteMods.Contains)
            .ThenBy(d => byDir[d].First().ModDisplayName)
            .SelectMany(d => byDir[d])];

        if (starFilter > 0)
            list = [.. list.Where(e => GetStarRating(e.ModDirectory) >= starFilter)];
        if (list.Count == 0)
        {
            ImGui.Indent(16f);
            ImGui.TextColored(in ColorMuted, "(empty — drag mods here)");
            ImGui.Unindent(16f);
        }
        else
        {
            ImGui.Indent(16f);
            var rowIndex = 0;
            int index1;
            for (var index2 = 0; index2 < list.Count; index2 = index1)
            {
                var modDirectory = list[index2].ModDirectory;
                index1 = index2 + 1;
                while (index1 < list.Count && list[index1].ModDirectory == modDirectory)
                    ++index1;
                var emoteCount = index1 - index2;
                if (emoteCount == 1)
                {
                    DrawEntryRow(list[index2], rowIndex, category, group.Name);
                }
                else
                {
                    DrawEntryRow(list[index2], rowIndex, category, group.Name, true, emoteCount);
                    if (expandedMods.Contains(modDirectory))
                    {
                        for (var index3 = index2; index3 < index1; ++index3)
                            DrawChildRow(list[index3], category);
                    }
                }
                ++rowIndex;
            }
            ImGui.Unindent(16f);
        }
    }

    private void DrawEntryRow(
      EmoteModEntry entry,
      int rowIndex,
      string category,
      string? groupName,
      bool isParent = false,
      int emoteCount = 0)
    {
        var strId1 = new ImU8String(2, 3);
        strId1.AppendFormatted<string>(category);
        strId1.AppendLiteral("_");
        strId1.AppendFormatted<string>(groupName ?? "ung");
        strId1.AppendLiteral("_");
        strId1.AppendFormatted<string>(entry.ModDirectory);
        ImGui.PushID(strId1);
        var flag1 = activeMods.Contains(entry.ModDirectory);
        var flag2 = config.FavoriteMods.Contains(entry.ModDirectory);
        var flag3 = selectedMods.Contains(entry.ModDirectory);
        ImGui.SmallButton("≡");
        if (ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoHoldToOpenOthers))
        {
            draggedModDir = entry.ModDirectory;
            draggedFromCategory = category;
            draggedFromGroupName = groupName;
            ImGui.SetDragDropPayload("DL_MOD", (ReadOnlySpan<byte>)DragPayloadData);
            ImGui.Text(entry.ModDisplayName);
            ImGui.EndDragDropSource();
        }
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, flag2 ? ColorWarning : ColorMuted);
        if (ImGui.SmallButton((flag2 ? "★" : "☆")))
            ToggleFavorite(entry.ModDirectory);
        ImGui.PopStyleColor();
        ImGui.SameLine();
        EnsureRowWidthsCached();
        var x1 = ImGui.GetStyle().ItemSpacing.X;
        var offsetFromStartX1 = ImGui.GetContentRegionMax().X - cachedResetW;
        var offsetFromStartX2 = offsetFromStartX1 - x1 - cachedPnbW;
        var offsetFromStartX3 = offsetFromStartX2 - x1 - cachedSettingsW;
        var offsetFromStartX4 = offsetFromStartX3 - x1 - cachedPerformW;
        var offsetFromStartX5 = offsetFromStartX4 - x1 - cachedCatBtnW;
        var offsetFromStartX6 = (float)((double)offsetFromStartX5 - (double)x1 - 130.0);
        var x2 = Math.Max(40f, offsetFromStartX6 - x1 - ImGui.GetCursorPosX());
        if (flag1)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ColorActive);
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.2f, 0.5f, 0.2f, 0.4f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.2f, 0.5f, 0.2f, 0.5f));
        }
        else if (flag3)
        {
            ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.26f, 0.59f, 0.98f, 0.35f));
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.26f, 0.59f, 0.98f, 0.45f));
        }
        int num1 = ImGui.Selectable((flag1 ? "● " + entry.ModDisplayName : "  " + entry.ModDisplayName), flag1 | flag3, size: new Vector2(x2, 0.0f)) ? 1 : 0;
        if (flag1)
            ImGui.PopStyleColor(3);
        else if (flag3)
            ImGui.PopStyleColor(2);
        if (num1 != 0)
        {
            var io = ImGui.GetIO();
            if (io.KeyCtrl)
            {
                if (!selectedMods.Remove(entry.ModDirectory))
                    selectedMods.Add(entry.ModDirectory);
                selectionAnchorDir = entry.ModDirectory;
            }
            else
            {
                if (io.KeyShift && selectionAnchorDir != null && cachedTabDrawOrder.TryGetValue(category, out var stringList))
                {
                    var val1 = stringList.IndexOf(selectionAnchorDir);
                    var val2 = stringList.IndexOf(entry.ModDirectory);
                    if (val1 >= 0 && val2 >= 0)
                    {
                        var num2 = Math.Min(val1, val2);
                        var num3 = Math.Max(val1, val2);
                        for (var index = num2; index <= num3; ++index)
                            selectedMods.Add(stringList[index]);
                    }
                }
                else
                {
                    ClearSelection();
                    HandleRowClick(entry);
                }
            }
        }
        if (ImGui.BeginPopupContextItem("##rowctx"))
        {
            if (!selectedMods.Contains(entry.ModDirectory))
            {
                ClearSelection();
                selectedMods.Add(entry.ModDirectory);
                selectionAnchorDir = entry.ModDirectory;
            }
            int count = selectedMods.Count;
            if (count > 1)
            {
                ImU8String text = new ImU8String(14, 1);
                text.AppendFormatted<int>(count);
                text.AppendLiteral(" mods selected");
                ImGui.TextDisabled(text);
            }
            if (count == 1)
            {
                if (ImGui.MenuItem("Open in Penumbra"))
                    penumbra.OpenModInPenumbra(entry.ModDirectory);
                ImGui.Separator();
            }
            if (ImGui.BeginMenu("Move to category"))
            {
                foreach (string category1 in categories)
                {
                    if (ImGui.MenuItem(category1))
                        SetSelectedModsCategory(category1);
                }
                ImGui.EndMenu();
            }
            List<ModGroup> categoryGroups = GetCategoryGroups(category);
            if (categoryGroups.Count > 0 && ImGui.BeginMenu("Move to group"))
            {
                foreach (ModGroup modGroup in categoryGroups)
                {
                    if (ImGui.MenuItem(modGroup.Name))
                        MoveSelectedModsToGroup(category, modGroup.Name);
                }
                ImGui.EndMenu();
            }
            if (count == 1 && groupName != null)
            {
                ImGui.Separator();
                if (ImGui.MenuItem("Remove from group"))
                    RemoveFromGroup(category, groupName, entry.ModDirectory);
            }
            if (count > 1)
            {
                ImGui.Separator();
                if (ImGui.MenuItem("Clear selection"))
                    ClearSelection();
            }
            ImGui.Separator();
            string label;
            if (count <= 1)
                label = "Block mod";
            else
                label = $"Block {count} mods";
            if (ImGui.MenuItem(label))
            {
                foreach (string modDir in selectedMods.ToList<string>())
                    BlockMod(modDir);
            }
            ImGui.EndPopup();
        }
        if (ImGui.BeginDragDropTarget())
        {
            if (!ImGui.AcceptDragDropPayload("DL_MOD").IsNull && draggedModDir != null && draggedModDir != entry.ModDirectory && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                MoveDraggedMod(entry.ModDirectory, category, groupName, rowIndex);
            ImGui.EndDragDropTarget();
        }
        ImGui.SameLine(offsetFromStartX6);
        if (isParent)
        {
            bool flag4 = expandedMods.Contains(entry.ModDirectory);
            ImU8String label = new ImU8String(6, 2);
            label.AppendFormatted<string>(flag4 ? "▼" : "▶");
            label.AppendLiteral(" ");
            label.AppendFormatted<int>(emoteCount);
            label.AppendLiteral("##exp");
            if (ImGui.SmallButton(label))
            {
                if (flag4)
                    expandedMods.Remove(entry.ModDirectory);
                else
                    expandedMods.Add(entry.ModDirectory);
                RebuildRenderRows();
            }
        }
        else
            ImGui.TextDisabled(entry.EmoteDisplayName);
        ImGui.SameLine(offsetFromStartX5);
        var modCategory = GetModCategory(entry);
        var strId2 = "cat_popup";
        ImU8String label1 = new ImU8String(5, 1);
        label1.AppendFormatted<string>(modCategory);
        label1.AppendLiteral("##cat");
        if (ImGui.SmallButton(label1))
            ImGui.OpenPopup(strId2);
        if (ImGui.BeginPopup(strId2))
        {
            ImGui.TextDisabled("Category");
            ImGui.Separator();
            foreach (var category2 in categories)
            {
                if (ImGui.Selectable(category2, category2 == modCategory))
                {
                    SetModCategory(entry.ModDirectory, category2);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }
        ImGui.SameLine(offsetFromStartX4);
        if (ImGui.SmallButton("Perform##perf"))
        {
            ClearSelection();
            HandleRowClick(entry);
        }
        ImGui.SameLine(offsetFromStartX3);
        if (ImGui.SmallButton("Settings##set"))
            settingsWindow.Toggle(entry);
        ImGui.SameLine(offsetFromStartX2);
        if (ImGui.SmallButton("Pnb##pnb"))
            penumbra.OpenModInPenumbra(entry.ModDirectory);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Open in Penumbra");
        ImGui.SameLine(offsetFromStartX1);
        if (ImGui.SmallButton("Reset##rst"))
            OnResetClicked(entry);
        ImGui.PopID();
    }

    private void DrawChildRow(EmoteModEntry entry, string category)
    {
        var strId = new ImU8String(8, 3);
        strId.AppendLiteral("child_");
        strId.AppendFormatted(category);
        strId.AppendLiteral("_");
        strId.AppendFormatted(entry.ModDirectory);
        strId.AppendLiteral("_");
        strId.AppendFormatted(entry.EmoteCommand);
        ImGui.PushID(strId);
        EnsureRowWidthsCached();
        var x = ImGui.GetStyle().ItemSpacing.X;
        var offsetFromStartX = (double)ImGui.GetContentRegionMax().X - (double)cachedResetW - (double)x - (double)cachedPnbW - (double)x - (double)cachedSettingsW - (double)x - (double)cachedPerformW;
        ImGui.SetCursorPosX((float)(offsetFromStartX - (double)x - (double)cachedCatBtnW - (double)x - 130.0));
        ref readonly var local = ref ColorMuted;
        var imU8String = new ImU8String(2, 1);
        imU8String.AppendLiteral("↳ ");
        imU8String.AppendFormatted(entry.EmoteDisplayName);
        var text = imU8String;
        ImGui.TextColored(in local, text);
        ImGui.SameLine((float)offsetFromStartX);
        if (ImGui.SmallButton("Perform##cperf"))
            HandleRowClick(entry);
        ImGui.PopID();
    }

    private string GetModCategory(EmoteModEntry entry)
    {
        if (config.BlockedMods.Contains(entry.ModDirectory))
            return "Unblock";

        return !config.ModCategories.TryGetValue(entry.ModDirectory, out var str) ? "Other" : str;
    }

    private void SetModCategory(string modDirectory, string category)
    {
        config.ModCategories[modDirectory] = category;
        config.Save();
        MarkCacheDirty();
        log.Debug($"[DanceLibrary] Category set: {modDirectory} → {category}");
    }

    private void HandleRowClick(EmoteModEntry entry)
    {
        if (moveModeEnabled)
        {
            var category = categories[Math.Clamp(moveModeTargetIdx, 0, categories.Length - 1)];
            SetModCategory(entry.ModDirectory, category);
            log.Debug($"[DanceLibrary] Move Mode: {entry.ModDirectory} → {category}");
        }
        else
            OnPerformClicked(entry);
    }

    private List<ModGroup> GetCategoryGroups(string category)
    {
        if (!config.CategoryGroups.TryGetValue(category, out var categoryGroups))
            config.CategoryGroups[category] = categoryGroups = [];
        return categoryGroups;
    }

    private List<EmoteModEntry> GetUngroupedEntries(List<EmoteModEntry> tabEntries, string category)
    {
        var groupedDirs = GetCategoryGroups(category).SelectMany(g => g.ModDirectories).ToHashSet();
        return [.. tabEntries.Where(e => !groupedDirs.Contains(e.ModDirectory))];
    }

    private List<string> GetOrderList(string category, string? groupName)
    {
        if (groupName != null)
            return GetCategoryGroups(category).FirstOrDefault(g => g.Name == groupName)?.ModDirectories ?? [];

        if (!config.UngroupedOrder.TryGetValue(category, out var orderList))
            config.UngroupedOrder[category] = orderList = [];
        return orderList;
    }

    private List<EmoteModEntry> OrderWithFavoritesFirst(
      List<EmoteModEntry> entries,
      string category,
      string? groupName)
    {
        var orderList = GetOrderList(category, groupName);
        var byDir = entries.ToLookup(e => e.ModDirectory);
        var knownDirs = new HashSet<string>(orderList);
        var list = orderList.Distinct(StringComparer.OrdinalIgnoreCase).Where(byDir.Contains).SelectMany(d => byDir[d]).ToList();
        list.AddRange(entries.Where(e => !knownDirs.Contains(e.ModDirectory)));
        return [.. list.OrderByDescending(e => config.FavoriteMods.Contains(e.ModDirectory)).ThenBy(e => e.ModDisplayName)];
    }

    private void UpdateUngroupedOrderForCategory(List<EmoteModEntry> tabEntries, string category)
    {
        var groupedDirs = GetCategoryGroups(category).SelectMany(g => g.ModDirectories).ToHashSet();
        var orderList = GetOrderList(category, null);
        var ungroupedSet = orderList.ToHashSet();
        var list = tabEntries
            .Select(e => e.ModDirectory)
            .Distinct()
            .Where(d => !groupedDirs.Contains(d) && !ungroupedSet.Contains(d))
            .ToList();

        if (list.Count <= 0)
            return;
        foreach (string str in list)
            orderList.Add(str);
        config.Save();
    }

    private void AddGroup(string category)
    {
        GetCategoryGroups(category).Add(new ModGroup());
        config.Save();
        MarkCacheDirty();
        log.Debug("[DanceLibrary] Added new group to " + category);
    }

    private void DeleteGroup(string category, int groupIndex)
    {
        var categoryGroups = GetCategoryGroups(category);
        if (groupIndex < 0 || groupIndex >= categoryGroups.Count)
            return;
        var list = categoryGroups[groupIndex].ModDirectories.ToList<string>();
        categoryGroups.RemoveAt(groupIndex);
        var orderList = GetOrderList(category, null);
        foreach (string str in list)
        {
            if (!orderList.Contains(str))
                orderList.Add(str);
        }
        config.Save();
        MarkCacheDirty();
        log.Debug($"[DanceLibrary] Deleted group #{groupIndex} in {category}; moved {list.Count} mods to ungrouped");
    }

    private void ToggleFavorite(string modDirectory)
    {
        if (!config.FavoriteMods.Remove(modDirectory))
            config.FavoriteMods.Add(modDirectory);
        config.Save();
        MarkCacheDirty();
        log.Debug($"[DanceLibrary] Toggled favorite: {modDirectory} → {config.FavoriteMods.Contains(modDirectory)}");
    }

    private int GetStarRating(string modDirectory)
    {
        return config.ModStarRatings.GetValueOrDefault<string, int>(modDirectory, 0);
    }

    private void RemoveFromGroup(string category, string groupName, string modDirectory)
    {
        var modGroup = GetCategoryGroups(category).FirstOrDefault(g => g.Name == groupName);
        if (modGroup == null || !modGroup.ModDirectories.Remove(modDirectory))
            return;
        var orderList = GetOrderList(category, null);
        if (!orderList.Contains(modDirectory))
            orderList.Add(modDirectory);
        config.Save();
        MarkCacheDirty();
        log.Debug($"[DanceLibrary] Removed '{modDirectory}' from group '{groupName}' in {category}");
    }

    private void MoveDraggedMod(
      string targetModDir,
      string targetCategory,
      string? targetGroupName,
      int targetIndex)
    {
        if (draggedModDir == null)
            return;
        GetOrderList(draggedFromCategory!, draggedFromGroupName).Remove(draggedModDir);
        var orderList = GetOrderList(targetCategory, targetGroupName);
        int num1 = targetIndex;
        if (draggedFromCategory == targetCategory && draggedFromGroupName == targetGroupName)
        {
            int num2 = orderList.IndexOf(draggedModDir);
            if (num2 >= 0 && num2 < targetIndex)
                --num1;
        }
        int index = Math.Clamp(num1, 0, orderList.Count);
        if (!orderList.Contains(draggedModDir))
            orderList.Insert(index, draggedModDir);
        if (draggedFromCategory != targetCategory)
            SetModCategory(draggedModDir, targetCategory);
        config.Save();
        MarkCacheDirty();
        log.Debug($"[DanceLibrary] Moved {draggedModDir} to {targetCategory}/{targetGroupName ?? "ungrouped"} at index {index}");
        draggedModDir = null;
    }

    private void MoveToGroup(
      string fromCategory,
      string? fromGroupName,
      string toCategory,
      string toGroupName)
    {
        if (draggedModDir == null)
            return;
        GetOrderList(fromCategory, fromGroupName).Remove(draggedModDir);
        var orderList = GetOrderList(toCategory, toGroupName);
        if (!orderList.Contains(draggedModDir))
            orderList.Add(draggedModDir);
        if (fromCategory != toCategory)
            SetModCategory(draggedModDir, toCategory);
        config.Save();
        MarkCacheDirty();
        log.Debug($"[DanceLibrary] Moved {draggedModDir} into group '{toGroupName}' in {toCategory}");
        draggedModDir = null;
    }

    private void OnPerformClicked(EmoteModEntry entry) => PerformDanceAsync(entry).ConfigureAwait(false);

    private void OnResetClicked(EmoteModEntry entry)
    {
        log.Info($"[DanceLibrary] Reset clicked: {entry.ModDisplayName} — resetting all {activeMods.Count} active mod(s)");
        foreach (var modDirectory in activeMods.ToList<string>())
        {
            log.Debug("[DanceLibrary] Resetting active mod: " + modDirectory);
            ResetMod(modDirectory);
        }
    }

    private void ResetMod(string modDirectory)
    {
        penumbra.RemoveTemporaryModSettings(modDirectory);
        var playerCollectionId = penumbra.GetPlayerCollectionId();
        if (playerCollectionId.HasValue)
        {
            penumbra.TryInheritMod(playerCollectionId.Value, modDirectory);
            log.Debug("[DanceLibrary] Set to inherit: " + modDirectory);
        }
        else
            log.Warning("[DanceLibrary] Reset: could not get collection ID to inherit " + modDirectory);
        activeMods.Remove(modDirectory);
    }

    private void ResetCategory(string cat)
    {
        if (cat == "Unblock" || !cachedTabEntries.TryGetValue(cat, out var emoteModEntryList) || emoteModEntryList.Count == 0)
            return;
        log.Info($"[DanceLibrary] Reset Category '{cat}': resetting {emoteModEntryList.Count} mod(s)");
        foreach (var emoteModEntry in emoteModEntryList)
            ResetMod(emoteModEntry.ModDirectory);
    }

    private void ResetAllMods()
    {
        List<EmoteModEntry> allEntries;
        lock (@lock)
            allEntries = this.allEntries;
        var list = allEntries.Where(e => !config.BlockedMods.Contains(e.ModDirectory)).ToList();
        log.Info($"[DanceLibrary] Reset All: resetting {list.Count} mod(s) (skipping {allEntries.Count - list.Count} blocked)");
        foreach (var emoteModEntry in list)
            ResetMod(emoteModEntry.ModDirectory);
    }

    private void BlockMod(string modDir)
    {
        log.Info("[DanceLibrary] Blocking mod: " + modDir);
        ResetMod(modDir);
        selectedMods.Remove(modDir);
        if (selectionAnchorDir == modDir)
            selectionAnchorDir = null;
        config.BlockedMods.Add(modDir);
        config.Save();
        MarkCacheDirty();
    }

    private void UnblockMod(string modDir)
    {
        log.Info("[DanceLibrary] Unblocking mod: " + modDir);
        config.BlockedMods.Remove(modDir);
        if (!config.ModCategories.ContainsKey(modDir))
            config.ModCategories[modDir] = "Other";
        config.Save();
        MarkCacheDirty();
    }

    private async Task PerformDanceAsync(EmoteModEntry entry)
    {
        try
        {
            log.Info($"[DanceLibrary] Performing: {entry.ModDisplayName} ({entry.EmoteCommand})");
            foreach (string modDirectory in activeMods.ToList<string>())
            {
                ResetMod(modDirectory);
                log.Debug("[DanceLibrary] Deactivated mod before perform: " + modDirectory);
            }
            var options = new Dictionary<string, IReadOnlyList<string>>();
            if (config.ModOptionOverrides.TryGetValue(entry.ModDirectory, out var dictionary))
            {
                foreach ((var key, var stringList) in dictionary)
                {
                    if (stringList.Count > 0)
                        options[key] = stringList;
                }
                log.Debug("[DanceLibrary] Perform: using plugin-stored options for " + entry.ModDirectory);
            }
            else
            {
                var playerCollectionId = penumbra.GetPlayerCollectionId();
                if (playerCollectionId.HasValue)
                {
                    (bool enabled, int priority, Dictionary<string, List<string>> options)? currentModSettings = penumbra.GetCurrentModSettings(playerCollectionId.Value, entry.ModDirectory);
                    if (currentModSettings.HasValue)
                    {
                        foreach ((var key, var stringList) in currentModSettings.Value.options)
                            options[key] = stringList;
                    }
                }
                log.Debug("[DanceLibrary] Perform: using Penumbra current settings for " + entry.ModDirectory);
            }
            if (!penumbra.SetTemporaryModSettings(entry.ModDirectory, (IReadOnlyDictionary<string, IReadOnlyList<string>>)options))
                log.Warning("[DanceLibrary] SetTemporaryModSettings failed for: " + entry.ModDirectory);
            else
                activeMods.Add(entry.ModDirectory);
            await Task.Delay(300);
            await framework.RunOnTick(() => chatSender.SendCommand(entry.EmoteCommand));
            log.Info("[DanceLibrary] Dance performed: " + entry.EmoteCommand);
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] PerformDanceAsync failed for: " + entry.ModDisplayName);
        }
    }

    private void MarkCacheDirty() => tabCacheDirty = true;

    private void ClearSelection()
    {
        selectedMods.Clear();
        selectionAnchorDir = null;
    }

    private string GetModCategoryByDir(string modDir)
    {
        return !config.ModCategories.TryGetValue(modDir, out var str) ? "Other" : str;
    }

    private string? GetModGroupInCategory(string modDir, string category)
    {
        foreach (var categoryGroup in GetCategoryGroups(category))
        {
            if (categoryGroup.ModDirectories.Contains(modDir))
                return categoryGroup.Name;
        }
        return null;
    }

    private void MoveModToGroup(
      string modDir,
      string fromCategory,
      string? fromGroupName,
      string toCategory,
      string toGroupName)
    {
        GetOrderList(fromCategory, fromGroupName).Remove(modDir);
        var orderList = GetOrderList(toCategory, toGroupName);
        if (!orderList.Contains(modDir))
            orderList.Add(modDir);
        if (!(fromCategory != toCategory))
            return;
        config.ModCategories[modDir] = toCategory;
    }

    private void SetSelectedModsCategory(string targetCategory)
    {
        foreach (var key in selectedMods.ToList())
        {
            config.ModCategories[key] = targetCategory;
            log.Debug($"[DanceLibrary] Bulk-category: {key} → {targetCategory}");
        }
        config.Save();
        MarkCacheDirty();
        ClearSelection();
    }

    private void MoveSelectedModsToGroup(string toCategory, string toGroupName)
    {
        foreach (var modDir in selectedMods.ToList())
        {
            var modCategoryByDir = GetModCategoryByDir(modDir);
            var modGroupInCategory = GetModGroupInCategory(modDir, modCategoryByDir);
            MoveModToGroup(modDir, modCategoryByDir, modGroupInCategory, toCategory, toGroupName);
            log.Debug($"[DanceLibrary] Bulk-group: {modDir} → '{toGroupName}' in {toCategory}");
        }
        config.Save();
        MarkCacheDirty();
        ClearSelection();
    }

    private void RebuildTabDrawOrder(string category)
    {
        var stringList = new List<string>();
        foreach (var categoryGroup in GetCategoryGroups(category))
        {
            foreach (var modDirectory in categoryGroup.ModDirectories)
            {
                if (!stringList.Contains(modDirectory))
                    stringList.Add(modDirectory);
            }
        }
        if (cachedUngroupedOrdered.TryGetValue(category, out var emoteModEntryList))
        {
            foreach (var emoteModEntry in emoteModEntryList)
            {
                if (!stringList.Contains(emoteModEntry.ModDirectory))
                    stringList.Add(emoteModEntry.ModDirectory);
            }
        }
        cachedTabDrawOrder[category] = stringList;
    }

    private void RebuildCategories()
    {
        categories = [.. BuiltInCategories.Concat(config.CustomCategories).Append<string>("Other")];
        moveModeTargetIdx = Math.Clamp(moveModeTargetIdx, 0, categories.Length - 1);
        rowWidthsCached = false;
        MarkCacheDirty();
    }

    private void AddCustomTab(string name)
    {
        if (Array.Exists(categories, c => c.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return;
        config.CustomCategories.Add(name);
        config.Save();
        RebuildCategories();
        log.Debug("[DanceLibrary] Added custom tab: " + name);
    }

    private void DeleteCustomTab(string name)
    {
        config.CustomCategories.Remove(name);
        config.Save();
        RebuildCategories();
        log.Debug("[DanceLibrary] Deleted custom tab: " + name);
    }

    private void RenameCustomTab(string oldName, string newName)
    {
        int index = config.CustomCategories.IndexOf(oldName);
        if (index < 0 || string.IsNullOrWhiteSpace(newName))
            return;
        config.CustomCategories[index] = newName;
        foreach (var key in config.ModCategories.Keys.ToList<string>())
        {
            if (config.ModCategories[key] == oldName)
                config.ModCategories[key] = newName;
        }
        if (config.UngroupedOrder.Remove(oldName, out var stringList))
            config.UngroupedOrder[newName] = stringList;
        if (config.CategoryGroups.Remove(oldName, out var modGroupList))
            config.CategoryGroups[newName] = modGroupList;
        config.Save();
        RebuildCategories();
        log.Debug($"[DanceLibrary] Renamed tab: {oldName} → {newName}");
    }

    private void RebuildTabCache(List<EmoteModEntry> allEntries)
    {
        log.Debug("[DanceLibrary] RebuildTabCache: rebuilding tab content cache");
        var flag = false;
        foreach (string category in categories)
        {
            var seenInCategory = new HashSet<string>((IEqualityComparer<string>)StringComparer.OrdinalIgnoreCase);
            foreach (var categoryGroup in GetCategoryGroups(category))
            {
                var count = categoryGroup.ModDirectories.Count;
                categoryGroup.ModDirectories.RemoveAll(d => !seenInCategory.Add(d));
                if (categoryGroup.ModDirectories.Count != count)
                {
                    log.Warning($"[DanceLibrary] Removed {count - categoryGroup.ModDirectories.Count} duplicate(s) from group '{categoryGroup.Name}' in category '{category}'");
                    flag = true;
                }
            }

            if (config.UngroupedOrder.TryGetValue(category, out var stringList) && stringList.Count > 1)
            {
                var count = stringList.Count;
                var seenUo = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                stringList.RemoveAll(d => !seenUo.Add(d));
                if (stringList.Count != count)
                {
                    log.Warning($"[DanceLibrary] Removed {count - stringList.Count} duplicate(s) from UngroupedOrder for '{category}'");
                    flag = true;
                }
            }
        }
        if (flag)
            config.Save();
        cachedTabEntries.Clear();
        cachedTabEntries["Unblock"] = new List<EmoteModEntry>();
        foreach (var category in categories)
            cachedTabEntries[category] = new List<EmoteModEntry>();
        foreach (var allEntry in allEntries)
        {
            var key = GetModCategory(allEntry);
            if (!cachedTabEntries.ContainsKey(key))
                key = "Other";
            cachedTabEntries[key].Add(allEntry);
        }
        cachedUngroupedOrdered.Clear();
        foreach (var category in categories)
        {
            var cachedTabEntry = cachedTabEntries[category];
            UpdateUngroupedOrderForCategory(cachedTabEntry, category);
            var ungroupedEntries = GetUngroupedEntries(cachedTabEntry, category);
            cachedUngroupedOrdered[category] = OrderWithFavoritesFirst(ungroupedEntries, category, null);
        }
        cachedEntriesRef = allEntries;
        tabCacheDirty = false;
        RebuildRenderRows();
    }

    private void RebuildRenderRows()
    {
        cachedRenderRows.Clear();
        foreach (var category in categories)
        {
            if (!cachedUngroupedOrdered.TryGetValue(category, out var emoteModEntryList))
            {
                cachedRenderRows[category] = [];
            }
            else
            {
                var renderRowList = new List<RenderRow>(emoteModEntryList.Count);
                int index1;
                for (var index2 = 0; index2 < emoteModEntryList.Count; index2 = index1)
                {
                    var modDirectory = emoteModEntryList[index2].ModDirectory;
                    index1 = index2 + 1;
                    while (index1 < emoteModEntryList.Count && emoteModEntryList[index1].ModDirectory == modDirectory)
                        ++index1;
                    var emoteCount = index1 - index2;
                    if (emoteCount == 1)
                    {
                        renderRowList.Add(new RenderRow(emoteModEntryList[index2], RowKind.Single));
                    }
                    else
                    {
                        renderRowList.Add(new RenderRow(emoteModEntryList[index2], RowKind.MultiParent, emoteCount));
                        if (expandedMods.Contains(modDirectory))
                        {
                            for (var index3 = index2; index3 < index1; ++index3)
                                renderRowList.Add(new RenderRow(emoteModEntryList[index3], RowKind.MultiChild));
                        }
                    }
                }
                cachedRenderRows[category] = renderRowList;
            }
        }
        foreach (var category in categories)
            RebuildTabDrawOrder(category);
        log.Debug("[DanceLibrary] RebuildRenderRows: rebuilt render lists for all categories");
    }

    private void EnsureRowWidthsCached()
    {
        if (rowWidthsCached)
            return;
        var x = ImGui.GetStyle().FramePadding.X;
        cachedResetW = ImGui.CalcTextSize("Reset").X + x * 2f;
        cachedPnbW = ImGui.CalcTextSize("Pnb").X + x * 2f;
        cachedSettingsW = ImGui.CalcTextSize("Settings").X + x * 2f;
        cachedPerformW = ImGui.CalcTextSize("Perform").X + x * 2f;
        cachedCatBtnW = (float)((double)categories.Max(c => ImGui.CalcTextSize(c).X) + (double)x * 2.0 + 4.0);
        rowWidthsCached = true;
        log.Debug($"[DanceLibrary] Row widths cached: Reset={cachedResetW:F0} Pnb={cachedPnbW:F0} Settings={cachedSettingsW:F0} Perform={cachedPerformW:F0} Cat={cachedCatBtnW:F0}");
    }

    public void Dispose()
    {
        activeMods.Clear();
        log.Debug("[DanceLibrary] MainWindow disposed");
    }

    private enum ScanState
    {
        NotScanned,
        Scanning,
        Done,
        Error,
    }

    private enum RowKind
    {
        Single,
        MultiParent,
        MultiChild,
    }

    private readonly struct RenderRow(EmoteModEntry entry, RowKind kind, int emoteCount = 0)
    {
        public readonly EmoteModEntry Entry = entry;
        public readonly RowKind Kind = kind;
        public readonly int EmoteCount = emoteCount;
    }
}
