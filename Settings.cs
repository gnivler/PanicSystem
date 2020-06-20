// ReSharper disable FieldCanBeMadeReadOnly.Global
// ReSharper disable ConvertToConstant.Global
// ReSharper disable UnassignedField.Global

namespace PanicSystem
{
    public class Settings
    {
        public bool AlwaysPanic = false;
        public bool Debug;
        public bool CombatLog;
        public bool EnableEjectPhrases;
        public bool FloatieSpam;
        public float EjectPhraseChance;
        public bool ColorizeFloaties;
        public bool CountAsKills;
        public bool LimitManualEjection;
        public float LimitManualEjectionLevel;
        
        // strings
        public string PanicSpamSaveString;
        public string PanicSpamFailString;
        public string PanicSpamRollString;
        public string PanicSpamCritSaveString;
        public string PanicSpamNoWeaponsString;
        public string PanicSpamAloneString;
        public string PanicSpamEjectResistString;
        public string PanicCritFailString;
        public string PanicImprovedString;
        public string PanicWorsenedString;
        public string[] PanicStates = new string[4];
        public string[] LimitManualEjectionTags;


        // panic
        public bool PlayersCanPanic;
        public bool EnemiesCanPanic;
        public bool VehiclesCanPanic;
        public float MinimumDamagePercentageRequired;
        public float MinimumMechStructureDamageRequired;
        public float MinimumVehicleStructureDamageRequired;
        public bool OneChangePerTurn;
        public bool LosingLimbAlwaysPanics;
        public float UnsteadyModifier;
        public float PilotHealthMaxModifier;
        public float HeadMaxModifier;
        public float CenterTorsoMaxModifier;
        public float SideTorsoMaxModifier;
        public float LeggedMaxModifier;
        public float WeaponlessModifier;
        public float AloneModifier;
        public float UnsettledAimModifier;
        public float StressedAimModifier;
        public float StressedToHitModifier;
        public float PanickedAimModifier;
        public float PanickedToHitModifier;
        public float MedianResolve;
        public float VehicleResolveFactor;
        public float ResolveMaxModifier;
        public float DistractingModifier;
        public float OverheatedModifier;
        public float ShutdownModifier;
        public float HeatDamageFactor;
        public float VehicleDamageFactor;
        public float ArmorDamageThreshold;
        public float MechHealthForCrit;
        public float CritOver;
        public float UnsettledPanicFactor;
        public float StressedPanicFactor;
        public float PanickedPanicFactor;

        // Quirks
        public bool QuirksEnabled;
        public float BraveModifier;
        public float DependableModifier;

        // ejection
        public float MaxEjectChance;
        public float EjectChanceFactor;

        public float BaseEjectionResist;
        public float BaseVehicleEjectionResist;
        public float GutsEjectionResistPerPoint;
        public float TacticsEjectionResistPerPoint;
        public float VehicleGutAndTacticsFactor;
        
        // thank you Frosty IRBTModUtils CustomDialog
        public class DialogueOptions {
            public string[] Portraits = {
                "sprites/Portraits/guiTxrPort_DEST_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_01_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_02_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_03_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_04_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_05_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_06_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_07_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_08_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_09_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_10_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_11_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_12_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_davion_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_default_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_kurita_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_liao_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_marik_utr.png",
                "sprites/Portraits/guiTxrPort_GenericMW_steiner_utr.png"
            };
            public string CallsignsPath = "BattleTech_Data/StreamingAssets/data/nameLists/name_callsign.txt";
        }
        public DialogueOptions Dialogue = new DialogueOptions();
    }
}
