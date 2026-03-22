using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;

namespace DanceLibraryFFXIV;

public sealed class PenumbraIpc : IDisposable
{
    private readonly IDalamudPluginInterface pi;
    private readonly IPluginLog log;
    private readonly ICallGateSubscriber<(int, int)> apiVersion;
    private readonly ICallGateSubscriber<Dictionary<string, string>> getModList;
    private readonly ICallGateSubscriber<string, string, Dictionary<string, object?>> getChangedItems;
    private readonly ICallGateSubscriber<string, string, IReadOnlyDictionary<string, (string[], int)>?> getAvailableModSettings;
    private readonly ICallGateSubscriber<Guid, string, string, bool, (int, (bool, int, Dictionary<string, List<string>>, bool)?)> getCurrentModSettings;
    private readonly ICallGateSubscriber<int, (bool, bool, (Guid, string))> getCollectionForObject;
    private readonly ICallGateSubscriber<int, string, string, (bool, bool, int, IReadOnlyDictionary<string, IReadOnlyList<string>>), string, int, int> setTemporaryModSettingsPlayer;
    private readonly ICallGateSubscriber<int, string, string, int, int> removeTemporaryModSettingsPlayer;
    private readonly ICallGateSubscriber<Guid, string, string, string, string, int> trySetModSetting;
    private readonly ICallGateSubscriber<Guid, string, string, bool, int> tryInheritMod;
    private readonly ICallGateSubscriber<int, string, string, int> openMainWindow;

    public bool IsAvailable { get; private set; }

    public PenumbraIpc(IDalamudPluginInterface pi, IPluginLog log)
    {
        this.pi = pi;
        this.log = log;
        apiVersion = pi.GetIpcSubscriber<(int, int)>("Penumbra.ApiVersion.V5");
        getModList = pi.GetIpcSubscriber<Dictionary<string, string>>("Penumbra.GetModList");
        getChangedItems = pi.GetIpcSubscriber<string, string, Dictionary<string, object?>>("Penumbra.GetChangedItems.V5");
        getAvailableModSettings = pi.GetIpcSubscriber<string, string, IReadOnlyDictionary<string, (string[], int)>?>("Penumbra.GetAvailableModSettings.V5");
        getCurrentModSettings = pi.GetIpcSubscriber<Guid, string, string, bool, (int, (bool, int, Dictionary<string, List<string>>, bool)?)>("Penumbra.GetCurrentModSettings.V5");
        getCollectionForObject = pi.GetIpcSubscriber<int, (bool, bool, (Guid, string))>("Penumbra.GetCollectionForObject.V5");
        setTemporaryModSettingsPlayer = pi.GetIpcSubscriber<int, string, string, (bool, bool, int, IReadOnlyDictionary<string, IReadOnlyList<string>>), string, int, int>("Penumbra.SetTemporaryModSettingsPlayer.V5");
        removeTemporaryModSettingsPlayer = pi.GetIpcSubscriber<int, string, string, int, int>("Penumbra.RemoveTemporaryModSettingsPlayer.V5");
        trySetModSetting = pi.GetIpcSubscriber<Guid, string, string, string, string, int>("Penumbra.TrySetModSetting.V5");
        tryInheritMod = pi.GetIpcSubscriber<Guid, string, string, bool, int>("Penumbra.TryInheritMod.V5");
        openMainWindow = pi.GetIpcSubscriber<int, string, string, int>("Penumbra.OpenMainWindow.V5");
        CheckAvailability();
    }

    public void CheckAvailability()
    {
        try
        {
            var num = apiVersion.InvokeFunc().Item1;
            IsAvailable = num == 5;
            if (IsAvailable)
                log.Info("[DanceLibrary] Penumbra available — API V5");
            else
                log.Warning($"[DanceLibrary] Penumbra API version mismatch: breaking={num}, expected 5");
        }
        catch
        {
            IsAvailable = false;
            log.Debug("[DanceLibrary] Penumbra not available (ApiVersion probe failed)");
        }
    }

    public Dictionary<string, string>? GetModList()
    {
        if (!IsAvailable)
            return null;

        try
        {
            return getModList.InvokeFunc();
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] GetModList IPC failed");
            return null;
        }
    }

    public Dictionary<string, object?>? GetChangedItems(string modDirectory)
    {
        if (!IsAvailable)
            return null;

        try
        {
            return getChangedItems.InvokeFunc(modDirectory, "");
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] GetChangedItems IPC failed for: " + modDirectory);
            return null;
        }
    }

    public IReadOnlyDictionary<string, (string[], int)>? GetAvailableModSettings(string modDirectory)
    {
        if (!IsAvailable)
            return null;

        try
        {
            return getAvailableModSettings.InvokeFunc(modDirectory, "");
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] GetAvailableModSettings IPC failed for: " + modDirectory);
            return null;
        }
    }

    public Guid? GetPlayerCollectionId()
    {
        if (!IsAvailable)
            return new Guid?();
        try
        {
            (var flag, var _, var tuple) = getCollectionForObject.InvokeFunc(0);
            if (flag)
                return new Guid?(tuple.Item1);
            log.Debug("[DanceLibrary] GetCollectionForObject: local player object not valid");
            return new Guid?();
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] GetCollectionForObject IPC failed");
            return new Guid?();
        }
    }

    public (bool enabled, int priority, Dictionary<string, List<string>> options)? GetCurrentModSettings(
      Guid collectionId,
      string modDirectory)
    {
        if (!IsAvailable)
            return new (bool, int, Dictionary<string, List<string>>)?();
        try
        {
            (var num, var nullable) = getCurrentModSettings.InvokeFunc(collectionId, modDirectory, "", false);
            if (num != 0 || !nullable.HasValue)
            {
                log.Debug($"[DanceLibrary] GetCurrentModSettings returned errorCode={num} for: {modDirectory}");
                return new (bool, int, Dictionary<string, List<string>>)?();
            }
            var tuple = nullable.Value;
            return new (bool, int, Dictionary<string, List<string>>)?((tuple.Item1, tuple.Item2, tuple.Item3));
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] GetCurrentModSettings IPC failed for: " + modDirectory);
            return new (bool, int, Dictionary<string, List<string>>)?();
        }
    }

    public bool SetTemporaryModSettings(
      string modDirectory,
      IReadOnlyDictionary<string, IReadOnlyList<string>> options)
    {
        if (!IsAvailable)
            return false;
        try
        {
            var num1 = setTemporaryModSettingsPlayer.InvokeFunc(0, modDirectory, "", (false, true, 99, options), "DanceLibraryFFXIV", 0);
            var num2 = num1 == 0 ? 1 : (num1 == 1 ? 1 : 0);

            if (num2 == 0)
                log.Warning($"[DanceLibrary] SetTemporaryModSettings returned ec={num1} for: {modDirectory}");
            return num2 != 0;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] SetTemporaryModSettings IPC failed for: " + modDirectory);
            return false;
        }
    }

    public bool RemoveTemporaryModSettings(string modDirectory)
    {
        if (!IsAvailable)
            return false;
        try
        {
            var num1 = removeTemporaryModSettingsPlayer.InvokeFunc(0, modDirectory, "", 0);
            var num2 = num1 == 0 ? 1 : (num1 == 1 ? 1 : 0);

            if (num2 == 0)
                log.Warning($"[DanceLibrary] RemoveTemporaryModSettings returned ec={num1} for: {modDirectory}");
            return num2 != 0;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] RemoveTemporaryModSettings IPC failed for: " + modDirectory);
            return false;
        }
    }

    public bool TrySetModSetting(
      Guid collectionId,
      string modDirectory,
      string groupName,
      string optionName)
    {
        if (!IsAvailable)
            return false;

        try
        {
            var num1 = trySetModSetting.InvokeFunc(collectionId, modDirectory, "", groupName, optionName);
            var num2 = num1 == 0 ? 1 : (num1 == 1 ? 1 : 0);
            if (num2 == 0)
                log.Warning($"[DanceLibrary] TrySetModSetting returned ec={num1} for: {modDirectory}/{groupName}/{optionName}");

            return num2 != 0;
        }
        catch (Exception ex)
        {
            log.Error(ex, $"[DanceLibrary] TrySetModSetting IPC failed for: {modDirectory}/{groupName}/{optionName}");
            return false;
        }
    }

    public bool TryInheritMod(Guid collectionId, string modDirectory)
    {
        if (!IsAvailable)
            return false;
        try
        {
            var num1 = tryInheritMod.InvokeFunc(collectionId, modDirectory, "", true);
            var num2 = num1 == 0 ? 1 : (num1 == 1 ? 1 : 0);

            if (num2 == 0)
                log.Warning($"[DanceLibrary] TryInheritMod returned ec={num1} for: {modDirectory}");
            return num2 != 0;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] TryInheritMod IPC failed for: " + modDirectory);
            return false;
        }
    }

    public bool OpenModInPenumbra(string modDirectory)
    {
        if (!IsAvailable)
            return false;

        try
        {
            var num1 = openMainWindow.InvokeFunc(1, modDirectory, "");
            var num2 = num1 == 0 ? 1 : (num1 == 1 ? 1 : 0);
            if (num2 == 0)
                log.Warning($"[DanceLibrary] OpenMainWindow returned ec={num1} for: {modDirectory}");
            return num2 != 0;
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] OpenMainWindow IPC failed for: " + modDirectory);
            return false;
        }
    }

    public void Dispose() => log.Debug("[DanceLibrary] PenumbraIpc disposed");
}
