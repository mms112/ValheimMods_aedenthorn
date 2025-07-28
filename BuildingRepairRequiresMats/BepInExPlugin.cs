using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace BuildingRepairRequiresMats
{
    [BepInPlugin("aedenthorn.BuildingRepairRequiresMats", "Building Repair Requires Mats", "0.1.0")]
    public class BepInExPlugin : BaseUnityPlugin
    {

        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<float> materialRequirementMult;
        public static ConfigEntry<float> enemyBuildRange;
        public static ConfigEntry<string> enemyBuildException;

        public static BepInExPlugin context;

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                Debug.Log((pref ? typeof(BepInExPlugin).Namespace + " " : "") + str);
        }

        private static bool HaveEnemyInRange(Player me, Vector3 point, float range)
        {
            foreach (Character allCharacter in Character.GetAllCharacters())
            {
                MonsterAI monster = allCharacter.GetComponent<MonsterAI>();
                if (monster != null)
                {
                    if (!monster.m_avoidLand && BaseAI.IsEnemy(me, allCharacter) &&
                        (Vector3.Distance(allCharacter.transform.position, point) < range) &&
                        (me.IsTargeted() || me.IsSensed() || monster.IsAlerted()))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public void Awake()
        {
            context = this;
            modEnabled = Config.Bind<bool>("General", "Enabled", true, "Enable this mod");
            isDebug = Config.Bind<bool>("General", "IsDebug", false, "Enable debug logs");
            materialRequirementMult = Config.Bind<float>("General", "MaterialRequirementMult", 0.5f, "Multiplier for amount of each material required.");
            enemyBuildRange = Config.Bind<float>("General", "EnemyBuildRange", 20.0f, "Minimum distance to the nearest enemy to allow building/repairing.");
            

            if (!modEnabled.Value)
                return;

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);

        }

        [HarmonyPatch(typeof(Player), nameof(Player.Repair))]
        static class Player_Repair_Patch
        {
            static bool Prefix(Player __instance)
            {
                if (!modEnabled.Value)
                {
                    return true;
                }

                if (!__instance.InPlaceMode())
                {
                    return false;
                }

                Piece hoveringPiece = __instance.GetHoveringPiece();
                if (!hoveringPiece || !__instance.CheckCanRemovePiece(hoveringPiece) || !PrivateArea.CheckAccess(hoveringPiece.transform.position))
                {
                    return false;
                }

                WearNTear component = hoveringPiece.GetComponent<WearNTear>();
                if (!component)
                {
                    return true;
                }

                float health = component.m_nview.GetZDO().GetFloat(ZDOVars.s_health, component.m_health);

                if (health >= component.m_health)
                {
                    return true;
                }

                if (HaveEnemyInRange(__instance, component.m_nview.m_zdo.m_position, enemyBuildRange.Value))
                {
                    __instance.Message(MessageHud.MessageType.Center, "Es sind Gegner in der Nähe", 0, null);
                    return false;
                }

                string outstring;
                List<string> reqstring = new List<string>();
                Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
                List<Piece.Requirement> reqs = new List<Piece.Requirement>();
                float repairFactor = ((component.m_health - health) / component.m_health) * materialRequirementMult.Value;
                bool retValue;

                foreach (Piece.Requirement req in hoveringPiece.m_resources)
                {
                    Dbgl($"{req.m_resItem.m_itemData.m_shared.m_name} {req.GetAmount(1)}");
                    int reqAmt = (int)Math.Ceiling(req.GetAmount(1) * repairFactor);
                    reqstring.Add($"{reqAmt} {Localization.instance.Localize(req.m_resItem.m_itemData.m_shared.m_name)}");

                    var rep_req = new Piece.Requirement()
                    {
                        m_resItem = req.m_resItem,
                        m_amountPerLevel = 0,
                        m_amount = reqAmt,
                    };

                    reqs.Add(rep_req);
                }

                recipe.m_resources = reqs.ToArray();
                recipe.m_item = null;
                recipe.m_craftingStation = null;
                recipe.m_repairStation = null;
                recipe.m_minStationLevel = 0;

                if (__instance.HaveRequirementItems(recipe, false, 1))
                {
                    __instance.ConsumeResources(recipe.m_resources, 1);
                    outstring = $"{string.Join(", ", reqstring)} zur Reparatur von {Localization.instance.Localize(hoveringPiece.m_name)} verwendet";
                    retValue = true;
                }
                else
                {
                    outstring = $"{string.Join(", ", reqstring)} zur Reparatur von {Localization.instance.Localize(hoveringPiece.m_name)} erforderlich";
                    retValue = false;
                }

                __instance.Message(MessageHud.MessageType.TopLeft, outstring, 0, null);
                Dbgl(outstring);
                return retValue;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
        static class Player_TryPlacePiece_Patch
        {
            static bool Prefix(Player __instance, Piece piece)
            {
                if (piece.m_resources.Length > 0)
                {
                    if (HaveEnemyInRange(__instance, __instance.m_nview.m_zdo.m_position, enemyBuildRange.Value))
                    {
                        __instance.Message(MessageHud.MessageType.Center, "Es sind Gegner in der Nähe", 0, null);
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.CheckCanRemovePiece))]
        static class Player_RemovePiece_Patch
        {
            static void Postfix(Player __instance, ref bool __result, Piece piece)
            {
                if (Environment.StackTrace.Contains("Player.RemovePiece") && (__result == true))
                {
                    WearNTear component = piece.GetComponent<WearNTear>();
                    if (!component)
                    {
                        return;
                    }

                    if (component.m_nview.GetZDO().GetFloat(ZDOVars.s_health, component.m_health) < component.m_health)
                    {
                        __result = false;
                        __instance.Message(MessageHud.MessageType.Center, "Beschädigtes Objekt kann nicht abgerissen werden", 0, null);
                    }
                }
            }
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Destroy))]
        static class WearNTear_Destroy_Patch
        {
            static void Prefix(ref bool blockDrop)
            {
                if (Environment.StackTrace.Contains("ApplyDamage"))
                {
                    blockDrop = true;
                }
            }
        }
    }
}
