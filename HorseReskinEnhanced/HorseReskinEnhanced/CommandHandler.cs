using System;
using StardewModdingAPI;
using HorseReskinEnhanced.Messages;

namespace HorseReskinEnhanced
{
    public static class CommandHandler
    {
        internal static void OnCommandReceived(string command, string[] args)
        {
            if (!ModEntry.IsEnabled || !Context.IsWorldReady)
            {
                ModEntry.SMonitor.Log("Farm not loaded yet. Try again after loading.", LogLevel.Warn);
                return;
            }

            switch (command)
            {
                case "list_horses":
                    foreach (var d in ModEntry.HorseNameMap)
                        ModEntry.SMonitor.Log($"{d.Key} - {d.Value.displayName}", LogLevel.Info);
                    break;
                case "reskin_horse":
                    if (args.Length != 2) { ModEntry.SMonitor.Log("Usage: reskin_horse [name] [skin id]", LogLevel.Error); return; }
                    var horseId = ModEntry.GetHorseIdFromName(args[0]);
                    if (horseId.HasValue) ReskinHorse(horseId.Value, args[1]);
                    break;
                case "reskin_horse_id":
                    if (args.Length != 2) { ModEntry.SMonitor.Log("Usage: reskin_horse_id [id] [skin id]", LogLevel.Error); return; }
                    if (Guid.TryParse(args[0], out Guid guid)) ReskinHorse(guid, args[1]);
                    else ModEntry.SMonitor.Log("Invalid horse ID format.", LogLevel.Error);
                    break;
                default:
                    ModEntry.SMonitor.Log($"Unknown command '{command}'.", LogLevel.Error);
                    break;
            }
        }

        private static void ReskinHorse(Guid horseId, string skinId)
        {
            if (!int.TryParse(skinId, out int id))
            {
                ModEntry.SMonitor.Log("Invalid skin ID.", LogLevel.Error);
                return;
            }
            if (Context.IsMainPlayer)
                ModEntry.SaveHorseReskin(horseId, id);
            else
                ModEntry.SHelper.Multiplayer.SendMessage(new HorseReskinMessage(horseId, id), ModEntry.ReskinHorseMessageId, new[] { ModEntry.SModManifest.UniqueID });
        }
    }
}