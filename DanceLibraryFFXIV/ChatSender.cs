using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using System;

namespace DanceLibraryFFXIV;

public sealed class ChatSender(IPluginLog log)
{
    private readonly IPluginLog log = log;

    public unsafe void SendCommand(string command)
    {
        try
        {
            var uiModulePtr = UIModule.Instance();
            if ((IntPtr)uiModulePtr == IntPtr.Zero)
            {
                log.Error("[DanceLibrary] UIModule.Instance() returned null — cannot send command");
            }
            else
            {
                var message = Utf8String.FromString(command);
                if ((IntPtr)message == IntPtr.Zero)
                {
                    log.Error("[DanceLibrary] Utf8String.FromString returned null for command: " + command);
                }
                else
                {
                    try
                    {
                        uiModulePtr->ProcessChatBoxEntry(message, IntPtr.Zero, false);
                    }
                    finally
                    {
                        message->Dtor(true);
                    }
                    log.Debug("[DanceLibrary] Sent command: " + command);
                }
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, "[DanceLibrary] Failed to send command: " + command);
        }
    }
}
