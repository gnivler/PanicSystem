using System;
using System.Linq;
using BattleTech.UI;
using Harmony;
using TMPro;
using UnityEngine;
using static PanicSystem.Logger;

// ReSharper disable InconsistentNaming

namespace PanicSystem.Components
{
    public static class ColorFloaties
    {
        internal static void Colorize(CombatHUDFloatieStack __instance)
        {
            try
            {
                // default outline width is zero.  have to plug a dummy value into outline colour though...
                void SetStyle(CombatHUDStatusStackItem floatie, Color32 inner, Color32 outline, float width = 0f)
                {
                    var tmp = Traverse.Create(floatie).Field("Text").GetValue<TextMeshProUGUI>();
                    Traverse.Create(tmp).Method("SetFaceColor", inner).GetValue();
                    Traverse.Create(tmp).Method("SetOutlineColor", outline).GetValue();
                    Traverse.Create(tmp).Method("SetOutlineThickness", width).GetValue();
                }

                var floaties = __instance.gameObject.GetComponentsInChildren<CombatHUDStatusStackItem>();
                foreach (var floatie in floaties)
                {
                    var text = Traverse.Create(floatie).Field("Text").GetValue<TextMeshProUGUI>().text;
                    if (PanicSystem.ejectPhraseList.Any(x => x == text))
                    {
                        SetStyle(floatie, Color.white, Color.red, 0.1f);
                    }
                    else if (text.Contains("IMPROVED TO"))
                    {
                        SetStyle(floatie, Color.white, Color.blue, 0.1f);
                    }
                    else if (text == "PANIC LEVEL CRITICAL!")
                    {
                        SetStyle(floatie, Color.red, Color.yellow, 0.1f);
                    }
                    else if (text == "Panicked")
                    {
                        SetStyle(floatie, Color.red / 1.25f, Color.black);
                    }
                    else if (text == "Stressed")
                    {
                        SetStyle(floatie, Color.yellow / 1.25f, Color.red);
                    }
                    else if (text == "Unsettled")
                    {
                        SetStyle(floatie, Color.gray / 1.35f, Color.black);
                    }
                    // need to do this because the game leaves some ... reused objects maybe??
                    else
                    {
                        SetStyle(floatie, Color.white, Color.black);
                    }
                }
            }
            catch (Exception ex)
            {
                Log(ex);
            }
        }
    }
}
