using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BattleTech;
using Harmony;
using PanicSystem.Components;
using static PanicSystem.Components.Controller;
using static PanicSystem.Logger;
using Random = UnityEngine.Random;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Patches
{
    public class LimitedEjectSequence : OrderSequence
    {
        public override void CompleteOrders()
        {
        }

        public override void OnAdded()
        {
            base.OnAdded();
            UnityGameInstance.BattleTechGame.Combat.MessageCenter.PublishMessage(new AddSequenceToStackMessage(
                new ShowActorInfoSequence(
                    actor, "NOT WHILE CONFIDENT!", FloatieMessage.MessageNature.Neutral, false)));
        }

        private readonly AbstractActor actor;
        public override bool ConsumesMovement => false;
        public override bool ConsumesFiring => false;
        public override bool IsComplete => true;

        public LimitedEjectSequence(AbstractActor actor) : base(actor)
        {
            this.actor = actor;
        }
    }

    [HarmonyPatch(typeof(EjectInvocation), "Invoke")]
    public static class EjectInvocation_Invoke_Patch
    {
        public static IStackSequence LimitEjectSequence(EjectSequence sequence, AbstractActor actor)
        {
            if (TrackedActors[GetActorIndex(actor)].PanicStatus > PanicStatus.Confident)
            {
                return sequence;
            }

            return new LimitedEjectSequence(actor);
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator ilg)
        {
            var codes = instructions.ToList();
            var insert = codes.FindIndex(x => x.opcode == OpCodes.Newobj &&
                                              x.operand is ConstructorInfo info &&
                                              info == typeof(EjectSequence).GetConstructor(new[] {typeof(AbstractActor), typeof(bool)}));
            var limitEjectSequence = AccessTools.Method(typeof(EjectInvocation_Invoke_Patch), "LimitEjectSequence");

            var injection = new List<CodeInstruction>
            {
                new CodeInstruction(OpCodes.Ldloc_1), // EjectSequence
                new CodeInstruction(OpCodes.Ldloc_0), // AbstractActor
                new CodeInstruction(OpCodes.Call, limitEjectSequence), // returns OrderSequence
                new CodeInstruction(OpCodes.Stloc_1) // overwrite so subsequent Ldloc_1 pulls new value
            };
            codes.InsertRange(insert + 2, injection);
            return codes.AsEnumerable();
        }
    }

    [HarmonyPatch(typeof(AbstractActor), "OnNewRound")]
    public static class AbstractActor_OnNewRound_Patch
    {
        public static void Prefix(AbstractActor __instance)
        {
            if (!(__instance is Mech mech) || mech.IsDead || mech.IsFlaggedForDeath && mech.HasHandledDeath)
            {
                return;
            }

            var pilot = mech.GetPilot();
            if (pilot == null)
            {
                return;
            }

            var index = GetActorIndex(mech);
            // reduce panic level
            var originalStatus = TrackedActors[index].PanicStatus;
            if (!TrackedActors[index].PanicWorsenedRecently && (int) TrackedActors[index].PanicStatus > 0)
            {
                TrackedActors[index].PanicStatus--;
            }

            if (TrackedActors[index].PanicStatus != originalStatus) // status has changed, reset modifiers
            {
                int Uid() => Random.Range(1, int.MaxValue);
                var effectManager = UnityGameInstance.BattleTechGame.Combat.EffectManager;

                // remove all PanicSystem effects first
                var effects = Traverse.Create(effectManager).Field("effects").GetValue<List<Effect>>();
                for (var i = 0; i < effects.Count; i++)
                {
                    if (effects[i].id.StartsWith("PanicSystem") && Traverse.Create(effects[i]).Field("target").GetValue<object>() == mech)
                    {
                        effectManager.CancelEffect(effects[i]);
                    }
                }

                // re-apply effects
                var message = __instance.Combat.MessageCenter;
                switch (TrackedActors[index].PanicStatus)
                {
                    case PanicStatus.Unsettled:
                        LogReport($"{mech.DisplayName} condition improved: Unsettled");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO UNSETTLED!", FloatieMessage.MessageNature.Buff, false)));
                        effectManager.CreateEffect(StatusEffect.UnsettledToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        break;
                    case PanicStatus.Stressed:
                        LogReport($"{mech.DisplayName} condition improved: Stressed");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO STRESSED!", FloatieMessage.MessageNature.Buff, false)));
                        effectManager.CreateEffect(StatusEffect.StressedToHit, "PanicSystemToHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        effectManager.CreateEffect(StatusEffect.StressedToBeHit, "PanicSystemToBeHit", Uid(), mech, mech, new WeaponHitInfo(), 0);
                        break;
                    default:
                        LogReport($"{mech.DisplayName} condition improved: Confident");
                        message.PublishMessage(new AddSequenceToStackMessage(new ShowActorInfoSequence(mech, "IMPROVED TO CONFIDENT!", FloatieMessage.MessageNature.Buff, false)));
                        break;
                }
            }

            // reset flag after reduction effect
            TrackedActors[index].PanicWorsenedRecently = false;
            SaveTrackedPilots();
        }
    }
}
