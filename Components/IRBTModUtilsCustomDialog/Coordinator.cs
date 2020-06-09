using System.Collections.Generic;
using System.IO;
using System.Linq;
using BattleTech;
using BattleTech.UI;
using HBS.Data;
using UnityEngine;
// ReSharper disable All

// thank you Frosty IRBTModUtils CustomDialog
// https://github.com/IceRaptor/IRBTModUtils
namespace PanicSystem.Components.IRBTModUtilsCustomDialog {
    // This classes liberally borrows CWolf's amazing MissionControl mod, in particular 
    //  https://github.com/CWolfs/MissionControl/blob/master/src/Core/DataManager.cs

    // A command control class that coordinates between the messages and the generated sequences
    public static class Coordinator {

        private static CombatGameState Combat;
        private static MessageCenter MessageCenter;
        private static CombatHUDDialogSideStack SideStack;
        internal static List<string> CallSigns;

        public static bool CombatIsActive {
            get { return Combat != null && SideStack != null; }
        }

        public static void OnCustomDialogMessage(MessageCenterMessage message) {
            PanicSystemDialogMessage msg = (PanicSystemDialogMessage)message;
            if (msg == null) { return; }

            ModState.DialogueQueue.Enqueue(msg);
            if (!ModState.IsDialogStackActive) {
                //LogDebug("No existing dialog sequence, publishing a new one.");
                ModState.IsDialogStackActive = true;
                MessageCenter.PublishMessage(
                    new AddParallelSequenceToStackMessage(new CustomDialogSequence(Combat, SideStack, false))
                    );
            } else {
                //LogDebug("Existing dialog sequence exists, skipping creation.");
            }
        }

        public static void OnCombatHUDInit(CombatGameState combat, CombatHUD combatHUD) {

            Combat = combat;
            MessageCenter = combat.MessageCenter;
            SideStack = combatHUD.DialogSideStack;

            if (CallSigns == null) {
                string filePath = Path.Combine(PanicSystem.modDirectory, PanicSystem.modSettings.Dialogue.CallsignsPath);
                //LogDebug($"Reading files from {filePath}");
                CallSigns = File.ReadAllLines(filePath).ToList();
            }
            //LogDebug($"Callsign count is: {CallSigns.Count}");

        }

        public static void OnCombatGameDestroyed() {

            Combat = null;
            MessageCenter = null;
            SideStack = null;
        }

        public static CastDef CreateCast(AbstractActor actor) {
            string castDefId = $"castDef_{actor.GUID}";
            if (actor.Combat.DataManager.CastDefs.Exists(castDefId)) {
                return actor.Combat.DataManager.CastDefs.Get(castDefId);
            }

            FactionValue actorFaction = actor?.team?.FactionValue;
            bool factionExists = actorFaction.Name != "INVALID_UNSET" && actorFaction.Name != "NoFaction" && 
                actorFaction.FactionDefID != null && actorFaction.FactionDefID.Length != 0 ? true : false;

            string employerFactionName = "Military Support";
            if (factionExists) {
                //LogDebug($"Found factionDef for id:{actorFaction}");
                string factionId = actorFaction?.FactionDefID;
                FactionDef employerFactionDef = UnityGameInstance.Instance.Game.DataManager.Factions.Get(factionId);
                if (employerFactionDef == null) { /*LogDebug($"Error finding FactionDef for faction with id '{factionId}'");*/ }
                else { employerFactionName = employerFactionDef.Name.ToUpper(); }
            } else {
                //LogDebug($"FactionDefID does not exist for faction: {actorFaction}");
            }

            CastDef newCastDef = new CastDef {
                // Temp test data
                FactionValue = actorFaction,
                firstName = $"{employerFactionName} -",
                showRank = false,
                showCallsign = true,
                showFirstName = true,
                showLastName = false
            };
            // DisplayName order is first, callsign, lastname

            newCastDef.id = castDefId;
            string portraitPath = GetRandomPortraitPath();
            newCastDef.defaultEmotePortrait.portraitAssetPath = portraitPath;
            if (actor.GetPilot() != null) {
                //LogDebug("Actor has a pilot, using pilot values.");
                Pilot pilot = actor.GetPilot();
                newCastDef.callsign = pilot.Callsign;

                // Hide the faction name if it's the player's mech
                if (actor.team.IsLocalPlayer) { newCastDef.showFirstName = false; }
            } else {
                //LogDebug("Actor is not piloted, generating castDef.");
                newCastDef.callsign = GetRandomCallsign();
            }
            //LogDebug($" Generated cast with callsign: {newCastDef.callsign} and DisplayName: {newCastDef.DisplayName()} using portrait: '{portraitPath}'");

            ((DictionaryStore<CastDef>)UnityGameInstance.BattleTechGame.DataManager.CastDefs).Add(newCastDef.id, newCastDef);

            return newCastDef;
        }

        private static string GetRandomCallsign() {
            return CallSigns[Random.Range(0, CallSigns.Count)];
        }
        private static string GetRandomPortraitPath() {
            return PanicSystem.modSettings.Dialogue.Portraits[Random
                .Range(0, PanicSystem.modSettings.Dialogue.Portraits.Length)];
        }

    }
}
