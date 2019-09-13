namespace PanicSystem
{
    public class Settings
    {
        public bool Debug = false;
        public bool EnableEjectPhrases;
        public bool FloatieSpam = false;
        public float EjectPhraseChance = 100;
        public bool ColorizeFloaties = true;

        // panic
        public bool PlayersCanPanic = true;
        public bool EnemiesCanPanic = true;
        public float MinimumDamagePercentageRequired = 10;
        public float MinimumStructureDamageRequired = 5;
        public bool OneChangePerTurn = false;
        public bool LosingLimbAlwaysPanics = false;
        public float UnsteadyModifier = 10;
        public float PilotHealthMaxModifier = 15;
        public float HeadMaxModifier = 15;
        public float CenterTorsoMaxModifier = 45;
        public float SideTorsoMaxModifier = 20;
        public float LeggedMaxModifier = 10;
        public float WeaponlessModifier = 10;
        public float AloneModifier = 10;
        public float UnsettledAimModifier = 1;
        public float StressedAimModifier = 1;
        public float StressedToHitModifier = -1;
        public float PanickedAimModifier = 2;
        public float PanickedToHitModifier = -2;
        public float MedianResolve = 50;
        public float ResolveMaxModifier = 10;
        public float DistractingModifier = 0;
        public float OverheatedModifier = 0;
        public float ShutdownModifier = 0;
        public float HeatDamageModifier = 10;

        //deprecated public float MechHealthAlone = 50;
        public float MechHealthForCrit = 0.9f;
        public float CritOver = 70;
        public float UnsettledPanicModifier = 1f;
        public float StressedPanicModifier = 0.66f;
        public float PanickedPanicModifier = 0.33f;

        // Quirks
        public bool QuirksEnabled = true;
        public float BraveModifier = 5;
        public float DependableModifier = 5;

        // ejection
        public float MaxEjectChance = 50;
        public float EjectChanceMultiplier = 0.75f;

        // deprecated public bool ConsiderEjectingWhenAlone = false;
        public float BaseEjectionResist = 50;
        public float GutsEjectionResistPerPoint = 2;
        public float TacticsEjectionResistPerPoint = 0;
    }
}
