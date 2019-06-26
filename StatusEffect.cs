using System.Globalization;
using BattleTech;
using Harmony;
using SVGImporter;
using static PanicSystem.PanicSystem;

// ReSharper disable InconsistentNaming

namespace PanicSystem
{
    // ReSharper disable once ClassNeverInstantiated.Global
    internal class StatusEffect
    {
        private const string Icon = "uixSvgIcon_status_sensorsImpaired";

        // thanks Denedan!
        [HarmonyPatch(typeof(CombatGameState), "_Init")]
        internal static class CombatGameState_LoadComplete_Patch
        {
            internal static void Postfix()
            {
                var dm = UnityGameInstance.BattleTechGame.DataManager;
                var loadRequest = dm.CreateLoadRequest();
                loadRequest.AddLoadRequest<SVGAsset>(BattleTechResourceType.SVGAsset, Icon, null);
                loadRequest.ProcessRequests();
                Logger.LogDebug("Loaded icon");
            }
        }

        // base settings for the EffectData members
        private static EffectDurationData Duration =>
            new EffectDurationData()
            {
                duration = -1,
                stackLimit = 1
            };

        private static EffectTargetingData Show =>
            new EffectTargetingData
            {
                effectTriggerType = EffectTriggerType.OnHit,
                triggerLimit = 0,
                extendDurationOnTrigger = 0,
                specialRules = AbilityDef.SpecialRules.NotSet,
                auraEffectType = AuraEffectType.NotSet,
                effectTargetType = EffectTargetType.SingleTarget,
                alsoAffectCreator = false,
                range = 0f,
                forcePathRebuild = false,
                forceVisRebuild = false,
                showInTargetPreview = true,
                showInStatusPanel = true,
            };

        private static EffectTargetingData Hide
        {
            get
            {
                var result = Show;
                result.showInStatusPanel = false;
                result.showInTargetPreview = false;
                return result;
            }
        }

        //  return specific EffectData
        internal static EffectData PanickedToBeHit =>
            new EffectData
            {
                effectType = EffectType.StatisticEffect,
                targetingData = Hide,
                Description = new DescriptionDef("PanicSystemToBeHit", "Panicked", "",
                    Icon, 0, 0, false, null, null, null),
                durationData = Duration,
                statisticData = new StatisticEffectData
                {
                    statName = "ToHitThisActor",
                    operation = StatCollection.StatOperation.Float_Add,
                    modValue = modSettings.PanickedToHitModifier.ToString(CultureInfo.InvariantCulture),
                    modType = "System.Single"
                }
            };

        internal static EffectData PanickedToHit =>
            new EffectData
            {
                effectType = EffectType.StatisticEffect,
                targetingData = Show,
                Description = new DescriptionDef("PanicSystemToBeHit", "Panicked",
                    modSettings.PanickedAimModifier + " Difficulty to all of this unit's attacks\n" +
                    modSettings.PanickedToHitModifier + " Difficulty to hit this unit",
                    Icon, 0, 0, false, null, null, null),
                durationData = Duration,
                statisticData = new StatisticEffectData
                {
                    statName = "AccuracyModifier",
                    operation = StatCollection.StatOperation.Float_Add,
                    modValue = modSettings.PanickedAimModifier.ToString(CultureInfo.InvariantCulture),
                    modType = "System.Single"
                }
            };

        internal static EffectData StressedToBeHit =>
            new EffectData
            {
                effectType = EffectType.StatisticEffect,
                targetingData = Hide,
                Description = new DescriptionDef("PanicSystemToBeHit", "Stressed", "",
                    Icon, 0, 0, false, null, null, null),
                durationData = Duration,
                statisticData = new StatisticEffectData
                {
                    statName = "ToHitThisActor",
                    operation = StatCollection.StatOperation.Float_Add,
                    modValue = modSettings.StressedToHitModifier.ToString(CultureInfo.InvariantCulture),
                    modType = "System.Single"
                }
            };

        internal static EffectData StressedToHit =>
            new EffectData
            {
                effectType = EffectType.StatisticEffect,
                targetingData = Show,
                Description = new DescriptionDef("PanicSystemToBeHit", "Stressed",
                    modSettings.StressedAimModifier + " Difficulty to all of this unit's attacks\n" + modSettings.StressedToHitModifier + " Difficulty to hit this unit",
                    Icon, 0, 0, false, null, null, null),
                durationData = Duration,
                statisticData = new StatisticEffectData
                {
                    statName = "AccuracyModifier",
                    operation = StatCollection.StatOperation.Float_Add,
                    modValue = modSettings.StressedAimModifier.ToString(CultureInfo.InvariantCulture),
                    modType = "System.Single"
                }
            };

        internal static EffectData UnsettledToHit =>
            new EffectData
            {
                effectType = EffectType.StatisticEffect,
                targetingData = Show,
                Description = new DescriptionDef("PanicSystemToBeHit", "Unsettled",
                    modSettings.UnsettledAimModifier + " Difficulty to all of this unit's attacks",
                    Icon, 0, 0, false, null, null, null),
                durationData = Duration,
                statisticData = new StatisticEffectData
                {
                    statName = "AccuracyModifier",
                    operation = StatCollection.StatOperation.Float_Add,
                    modValue = modSettings.UnsettledAimModifier.ToString(CultureInfo.InvariantCulture),
                    modType = "System.Single"
                }
            };
    }
}
