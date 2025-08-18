using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using ServerSync;

namespace RepairSpecificItems
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    public class BepInExPlugin : BaseUnityPlugin
    {
        private const string pluginID = "aedenthorn.RepairSpecificItems";
        private const string pluginName = "Repair Specific Items";
        private const string pluginVersion = "0.3.4";

        internal static readonly ConfigSync configSync = new ConfigSync(pluginID) { DisplayName = pluginName, CurrentVersion = pluginVersion, MinimumRequiredVersion = pluginVersion };

        private static ConfigEntry<bool> configLocked;
        public static ConfigEntry<bool> modEnabled;
        public static ConfigEntry<bool> isDebug;
        public static ConfigEntry<bool> requireMats;
        public static ConfigEntry<bool> leftClick;
        public static ConfigEntry<bool> hideRepairButton;
        public static ConfigEntry<float> materialRequirementMult;
        public static ConfigEntry<string> reducedItemNames;
        public static ConfigEntry<float> reducedMaterialRequirementMult;
        public static ConfigEntry<string> modKey;
        public static ConfigEntry<string> titleTooltipColor;
        public static ConfigEntry<int> nexusID;
        public static List<Container> containerList = new List<Container>();

        public static BepInExPlugin context;
        public static Assembly epicLootAssembly;

        public static void Dbgl(string str = "", bool pref = true)
        {
            if (isDebug.Value)
                Debug.Log((pref ? typeof(BepInExPlugin).Namespace + " " : "") + str);
        }
        public void Awake()
        {
            context = this;
            modEnabled = config("General", "Enabled", true, "Enable this mod");
            configLocked = config("General", "LockConfiguration", true, "Configuration is locked and can be changed by server admins only.");
            isDebug = config("General", "IsDebug", false, "Enable debug logs", false);
            nexusID = config("General", "NexusID", 1011, "Nexus mod ID for updates", false);
            requireMats = config("General", "RequireMats", true, "Require materials to repair.");
            modKey = config("General", "ModifierKey", "left alt", "Key to hold in order to switch click to repair.", false);
            leftClick = config("General", "LeftClick", true, "Use left click to repair (otherwise use right click).", false);
            hideRepairButton = config("General", "HideRepairButton", true, "Hide the vanilla repair button.", false);
            materialRequirementMult = config("General", "MaterialRequirementMult", 0.5f, "Multiplier for amount of each material required.");
            reducedItemNames = config("ItemLists", "ReduceMaterials", "", $"List of materials, which use a reduced amount, when they are needed for repair.");
            reducedMaterialRequirementMult = config("General", "ReducedMaterialRequirementMult", 0.25f, "Multiplier for amount of each reduced material required. It is applied for all materials, which are specified in the 'ReduceMaterials' list.");

            configSync.AddLockingConfigEntry(configLocked);

            if (!modEnabled.Value)
                return;

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);

        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription = new ConfigDescription(
                description.Description +
                (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
                description.AcceptableValues, description.Tags);

            ConfigEntry<T> configEntry = Config.Bind(group, name, defaultValue, extendedDescription);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true) => config(group, name, defaultValue, new ConfigDescription(description), synchronizedSetting);

        public void Start()
        {
            if(Chainloader.PluginInfos.ContainsKey("randyknapp.mods.epicloot"))
                epicLootAssembly = Chainloader.PluginInfos["randyknapp.mods.epicloot"].Instance.GetType().Assembly;

        }

        [HarmonyPatch(typeof(InventoryGrid), "OnLeftClick")]
        public static class OnLeftClick_Patch
        {
            public static bool Prefix(InventoryGrid __instance, UIInputHandler clickHandler, Inventory ___m_inventory)
            {
                if (modEnabled.Value && AedenthornUtils.CheckKeyHeld(modKey.Value) && leftClick.Value && InventoryGui.instance)
                {
                    RepairClickedItem(__instance, clickHandler, ___m_inventory);
                    return false;
                }
                return true;
            }

        }

        [HarmonyPatch(typeof(InventoryGrid), "OnRightClick")]
        public static class OnRightClick_Patch
        {
            public static bool Prefix(InventoryGrid __instance, UIInputHandler element, Inventory ___m_inventory)
            {
                if (modEnabled.Value && AedenthornUtils.CheckKeyHeld(modKey.Value) && !leftClick.Value && InventoryGui.instance)
                {
                    RepairClickedItem(__instance, element, ___m_inventory);
                    return false;
                }
                return true;
            }

        }

        public static void RepairClickedItem(InventoryGrid grid, UIInputHandler element, Inventory inventory)
        {
            Vector2i buttonPos = Traverse.Create(grid).Method("GetButtonPos", new object[] { element.gameObject }).GetValue<Vector2i>();
            ItemDrop.ItemData itemData = inventory.GetItemAt(buttonPos.x, buttonPos.y);

            if (itemData == null)
                return;

            List<ItemDrop.ItemData> wornItems = new List<ItemDrop.ItemData>();
            Player.m_localPlayer.GetInventory().GetWornItems(wornItems);

            if (wornItems.Find(i => i == itemData) == null)
                return;

            if ((bool)AccessTools.Method(typeof(InventoryGui), "CanRepair").Invoke(InventoryGui.instance, new object[] { itemData }))
            {
                CraftingStation currentCraftingStation = Player.m_localPlayer.GetCurrentCraftingStation();
                itemData.m_durability = itemData.GetMaxDurability();
                if (currentCraftingStation)
                {
                    currentCraftingStation.m_repairItemDoneEffects.Create(currentCraftingStation.transform.position, Quaternion.identity, null, 1f);
                }
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, Localization.instance.Localize("$msg_repaired", new string[]
                {
                        itemData.m_shared.m_name
                }), 0, null);
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int) })]
        public static class GetTooltip_Patch
        {
            public static void Postfix(ItemDrop.ItemData item, int qualityLevel, bool crafting, ref string __result)
            {
                if (modEnabled.Value && requireMats.Value && Player.m_localPlayer)
                {

                    List<ItemDrop.ItemData> wornItems = new List<ItemDrop.ItemData>();
                    Player.m_localPlayer.GetInventory().GetWornItems(wornItems);

                    if (wornItems.Find(i => i == item) == null)
                        return;

                    Recipe recipe = RepairRecipe(item);
                    if (recipe != null)
                    {
                        bool playerEnough = true;
                        List<string> reqstring = new List<string>();
                        foreach (Piece.Requirement req in recipe.m_resources)
                        {
                            if (req.GetAmount(1) == 0)
                                continue;
                            reqstring.Add($"{req.GetAmount(1)} {Localization.instance.Localize(req.m_resItem.m_itemData.m_shared.m_name)}");
                            if (Player.m_localPlayer.GetInventory().CountItems(req.m_resItem.m_itemData.m_shared.m_name) < req.GetAmount(1))
                                playerEnough = false;
                        }

                        string repairString;

                        if (Player.m_localPlayer.HaveRequirements(recipe, false, 1))
                        {
                            repairString = "\n" + string.Format(Localization.instance.Localize("$repair_enough"), string.Join(", ", reqstring));
                        }
                        else
                        {
                            if (playerEnough)
                                repairString = "\n" + string.Format(Localization.instance.Localize("$repair_enough_external"), string.Join(", ", reqstring));
                            else
                                repairString = "\n" + string.Format(Localization.instance.Localize("$repair_not_enough"), string.Join(", ", reqstring));
                        }

                        int setEffectPos = __result.IndexOf("\n\n$item_seteffect", StringComparison.InvariantCulture);
                        if (setEffectPos >= 0)
                            __result = __result.Insert(setEffectPos, repairString);
                        else
                            __result += repairString;
                    }
                }
            }
        }
        
        [HarmonyPatch(typeof(InventoryGui), "CanRepair")]
        public static class InventoryGui_CanRepair_Patch
        {
            public static void Postfix(ItemDrop.ItemData item, ref bool __result)
            {
                if (modEnabled.Value && Environment.StackTrace.Contains("RepairClickedItem") && !Environment.StackTrace.Contains("HaveRepairableItems") && __result == true && item?.m_shared != null && Player.m_localPlayer != null)
                {
                    List<ItemDrop.ItemData> wornItems = new List<ItemDrop.ItemData>();
                    Player.m_localPlayer.GetInventory().GetWornItems(wornItems);

                    if (wornItems.Find(i => i == item) == null)
                        return;

                    Recipe recipe = RepairRecipe(item);
                    if (recipe == null)
                        return;

                    List<string> reqstring = new List<string>();
                    foreach (Piece.Requirement req in recipe.m_resources)
                    {
                        if (req?.m_resItem?.m_itemData?.m_shared == null)
                            continue;
                        reqstring.Add($"{req.GetAmount(1)} {Localization.instance.Localize(req.m_resItem.m_itemData.m_shared.m_name)}");
                    }
                    string outstring;
                    if (Player.m_localPlayer.HaveRequirements(recipe, false, 1))
                    {
                        Player.m_localPlayer.ConsumeResources(recipe.m_resources, 1);
                        outstring = string.Format(Localization.instance.Localize("$repair_used_items"), string.Join(", ", reqstring), Localization.instance.Localize(item.m_shared.m_name));
                        __result = true;
                    }
                    else
                    {
                        outstring = string.Format(Localization.instance.Localize("$repair_items_required"), string.Join(", ", reqstring), Localization.instance.Localize(item.m_shared.m_name));
                        __result = false;
                    }

                    Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, outstring, 0, null);
                    Dbgl(outstring);
                }
            }
        }

        public static Recipe RepairRecipe(ItemDrop.ItemData item)
        {
            var reduceItemNameArray = string.IsNullOrEmpty(reducedItemNames.Value) ? new string[0] : reducedItemNames.Value.Split(',');

            float percent = (item.GetMaxDurability() - item.m_durability) / item.GetMaxDurability();
            Recipe fullRecipe = ObjectDB.instance.GetRecipe(item);

            if (fullRecipe == null)
                return null;

            var fullReqs = fullRecipe.m_resources.ToList();

            bool isMagic = false;
            if (epicLootAssembly != null)
            {
                isMagic = (bool)epicLootAssembly.GetType("EpicLoot.ItemDataExtensions").GetMethod("IsMagic", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(ItemDrop.ItemData) }, null).Invoke(null, new[] { item });
            }
            if (isMagic)
            {
                int rarity = (int)epicLootAssembly.GetType("EpicLoot.ItemDataExtensions").GetMethod("GetRarity", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { item });
                List<KeyValuePair<ItemDrop, int>> magicReqs =  (List<KeyValuePair<ItemDrop, int>>)epicLootAssembly.GetType("EpicLoot.Crafting.EnchantTabController").GetMethod("GetEnchantCosts", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { item, rarity });
                foreach(var kvp in magicReqs)
                {
                    fullReqs.Add(new Piece.Requirement()
                    {
                        m_amount = kvp.Value,
                        m_resItem = kvp.Key
                    });
                }
            }

            List<Piece.Requirement> reqs = new List<Piece.Requirement>();
            Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
            for (int i = 0; i < fullReqs.Count; i++)
            {
                float amount = 0;
                float multVal = reduceItemNameArray.Contains(fullReqs[i].m_resItem.m_itemData.m_shared.m_name) ? reducedMaterialRequirementMult.Value : materialRequirementMult.Value;

                if (item.m_quality > 0)
                {
                    amount = ((2 * fullReqs[i].m_amountPerLevel * (item.m_quality - 1)) / 3.0f) + fullReqs[i].m_amount;
                }

                int fraction = Mathf.RoundToInt(amount * percent * multVal);

                if (fraction == 0 && (amount * multVal) >= 1)
                {
                    fraction = 1;
                }

                if (fraction > 0)
                {
                    var req = new Piece.Requirement()
                    {
                        m_resItem = fullReqs[i].m_resItem,
                        m_amountPerLevel = 0,
                        m_amount = fraction,
                    };

                    reqs.Add(req);
                }
            }
            if (!reqs.Any())
            {
                return null;
            }

            recipe.m_resources = reqs.ToArray();
            recipe.m_item = item.m_dropPrefab.GetComponent<ItemDrop>();
            recipe.m_craftingStation = fullRecipe.m_craftingStation;
            recipe.m_repairStation = fullRecipe.m_repairStation;
            recipe.m_minStationLevel = fullRecipe.m_minStationLevel;
            return recipe;
        }


        [HarmonyPatch(typeof(InventoryGui), "UpdateRepair")]
        public static class UpdateRepair_Patch
        {
            public static bool Prefix(InventoryGui __instance)
            {
                if (!modEnabled.Value || !hideRepairButton.Value)
                    return true;

                __instance.m_repairPanel.gameObject.SetActive(false);
                __instance.m_repairPanelSelection.gameObject.SetActive(false);
                __instance.m_repairButton.gameObject.SetActive(false);

                return false;
            }
        }
        [HarmonyPatch(typeof(Terminal), "InputText")]
        public static class InputText_Patch
        {
            public static bool Prefix(Terminal __instance)
            {
                if (!modEnabled.Value)
                    return true;
                string text = __instance.m_input.text;
                if (text.ToLower().Equals("repairmod reset"))
                {
                    context.Config.Reload();
                    context.Config.Save();
                    Traverse.Create(__instance).Method("AddString", new object[] { text }).GetValue();
                    Traverse.Create(__instance).Method("AddString", new object[] { "Repair Items config reloaded" }).GetValue();
                    return false;
                }
                return true;
            }
        }
    }
}
