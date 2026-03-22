using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DanceLibraryFFXIV.Windows;
using System;

namespace DanceLibraryFFXIV;

public sealed class Plugin : IDalamudPlugin, IDisposable
{
    private readonly Configuration config;
    private readonly PenumbraIpc penumbraIpc;
    private readonly ChatSender chatSender;
    private readonly ModScanner scanner;
    private readonly ModSettingsWindow settingsWindow;
    private readonly MainWindow mainWindow;

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    public Plugin()
    {
        if (PluginInterface.GetPluginConfig() is not Configuration configuration)
            configuration = new Configuration();

        config = configuration;

        penumbraIpc = new PenumbraIpc(PluginInterface, Log);
        chatSender = new ChatSender(Log);
        scanner = new ModScanner(penumbraIpc, Log);
        settingsWindow = new ModSettingsWindow(config, penumbraIpc, Log);
        mainWindow = new MainWindow(config, scanner, penumbraIpc, chatSender, settingsWindow, Framework, Log);

        PluginInterface.UiBuilder.Draw += OnDraw;
        PluginInterface.UiBuilder.OpenMainUi += OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenMainUi;
        CommandManager.AddHandler("/dl", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Dance Library window (/dl or /dancelibrary)"
        });
        CommandManager.AddHandler("/dancelibrary", new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the Dance Library window (/dl or /dancelibrary)"
        });
        Log.Info("[DanceLibrary] Plugin loaded. Use /dl to open.");
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler("/dl");
        CommandManager.RemoveHandler("/dancelibrary");
        PluginInterface.UiBuilder.Draw -= OnDraw;
        PluginInterface.UiBuilder.OpenMainUi -= OnOpenMainUi;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenMainUi;
        mainWindow.Dispose();
        settingsWindow.Dispose();
        penumbraIpc.Dispose();
        config.Save();
        Log.Info("[DanceLibrary] Plugin unloaded.");
    }

    private void OnDraw() => mainWindow.Draw();

    private void OnOpenMainUi() => mainWindow.Open();

    private void OnCommand(string command, string args) => mainWindow.Toggle();
}
