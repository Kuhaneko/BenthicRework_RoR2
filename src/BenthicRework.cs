using BepInEx;
using R2API;
using R2API.Utils;
using RoR2;
using UnityEngine;
using RiskOfOptions;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using System;
using BepInEx.Configuration;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;



using System.Collections.Generic;
using UnityEngine.AddressableAssets;



namespace BenthicRework
{
    [BepInDependency("com.rune580.riskofoptions")]

    // Soft Dependencies
    //Item Qualities Mod
    [BepInDependency("com.Gorakh.ItemQualities", BepInDependency.DependencyFlags.SoftDependency)]

    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [NetworkCompatibility(CompatibilityLevel.NoNeedForSync, VersionStrictness.DifferentModVersionsAreOk)]
    public class BenthicRework : BaseUnityPlugin
    {

        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "Braquen";
        public const string PluginName = "Benthic_Rebloomed";
        public const string PluginVersion = "1.0.0";


        public static BepInEx.PluginInfo pluginInfo;
        public static AssetBundle AssetBundle;

        public static ConfigEntry<int> UniqueItemsToUpgrade;
        public static ConfigEntry<int> ItemsAddedToStack;
        public static ConfigEntry<bool> UpgradeLunars;
        public static ConfigEntry<bool> UpgradeSelf;

        // public static ConfigEntry<ItemTier[]> CustomBadTiers;
        // public static ConfigEntry<ItemIndex[]> CustomBadItems;

        public void Awake()
        {

            Log.Init(Logger);

            pluginInfo = Info;

            var Config = new ConfigFile(System.IO.Path.Combine(Paths.ConfigPath, "braquen-BenthicRebloomed.cfg"), true);

            ItemsAddedToStack = Config.Bind("Benthic Stats", "Items added to stack", 3, "Number of items added for each Blenthic Bloom to a single stack.");
            UniqueItemsToUpgrade = Config.Bind("Benthic Stats", "Items to upgrade", 3, "Number of unique items to be upgraded.");

            UpgradeLunars = Config.Bind("Allowed Upgrades", "Allow Lunar Upgrades", false, "When enabled, bloom can grant more stacks of lunar items on stage advance.");
            UpgradeSelf = Config.Bind("Allowed Upgrades", "Allow Self Upgrade", false, "When enabled, bloom can grant more stacks of itself on stage advance. Completely unbalanced.");


            //Set the max to 100, because only god can judge.
            ModSettingsManager.AddOption(new IntSliderOption(UniqueItemsToUpgrade,
                new IntSliderConfig { min = 1, max = 100 }));
            ModSettingsManager.AddOption(new IntSliderOption(ItemsAddedToStack,
                new IntSliderConfig { min = 1, max = 100 }));
            ModSettingsManager.AddOption(new CheckBoxOption(UpgradeLunars));
            ModSettingsManager.AddOption(new CheckBoxOption(UpgradeSelf));


            ModSettingsManager.SetModDescription("Braq's Big Beautiful Benthic Bloom Betterer");
            var sprite = Addressables.LoadAssetAsync<Sprite>("RoR2/DLC1/CloverVoid/texCloverVoidIcon.png").WaitForCompletion();
            if(sprite != null) ModSettingsManager.SetModIcon(sprite);


            IL.RoR2.CharacterMaster.TryCloverVoidUpgrades += CharacterMaster_TryCloverVoidUpgrades;
        }

        private void CharacterMaster_TryCloverVoidUpgrades(ILContext il)
        {
            ILCursor c = new ILCursor(il);



            //we're replacing the whole logic so no need to keep redundant methods with cursor search
            c.Goto(0);

            c.Emit(OpCodes.Ldarg_0);

            c.EmitDelegate(AlternateBenthic);



            c.Emit(OpCodes.Ret);

        }

        private void AlternateBenthic(CharacterMaster characterMaster)
        {
            int currentlyHeldCloverVoids = characterMaster.inventory.GetItemCountEffective(DLC1Content.Items.CloverVoid);
            if (currentlyHeldCloverVoids == 0) return;

            if (characterMaster.cloverVoidRng == null)
            {
                characterMaster.cloverVoidRng = new Xoroshiro128Plus(Run.instance.seed);
            }

            //the mod will now upgrade random items and not from some order
            Xoroshiro128Plus rng = characterMaster.cloverVoidRng;
            
            int baseUniqueItemsToUpgrade = UniqueItemsToUpgrade.Value;
            int baseCountToAddToStack = ItemsAddedToStack.Value;
            List<ItemIndex> itemsCollected = characterMaster.inventory.itemAcquisitionOrder;

            bool upSelf = UpgradeSelf.Value;
            bool upLunar = UpgradeLunars.Value;

            //prep list to upgrade
            List<ItemIndex> itemsToUpgrade = new List<ItemIndex>();


            //add all potential items to upgrade that aren't banned
            foreach (ItemIndex item in itemsCollected)
            {
                if ((item == DLC1Content.Items.CloverVoid.itemIndex && !upSelf) ||
                    ItemCatalog.GetItemDef(item).tier == ItemTier.Lunar && !upLunar ||
                    ItemCatalog.GetItemDef(item).tier == ItemTier.NoTier ||
                    characterMaster.inventory.GetItemCountPermanent(item) == 0) //if item is fully not permanent (all items scrapped)
                {
                    //skip banned items
                    continue;
                }
                itemsToUpgrade.Add(item);
            }



            Logger.LogDebug($"itemsToUpgrade {itemsToUpgrade.Count} >= {baseUniqueItemsToUpgrade} baseUniqueItemsToUpgrade");

            //remove random items from upgrades this time
            while (itemsToUpgrade.Count > baseUniqueItemsToUpgrade)
            {
                int index = rng.RangeInt(0, itemsToUpgrade.Count);
                itemsToUpgrade.RemoveAt(index);
            }

            ///</debug block>
            string list = "";
            foreach (ItemIndex item in itemsToUpgrade)
            {
                list += (ItemCatalog.GetItemDef(item).name+",");
            }
            
            Logger.LogDebug($"{Util.GetBestMasterName(characterMaster)} should have upgraded the following {baseUniqueItemsToUpgrade} items: {list}");
            ///<//debug block>

            int countToAddToStack = currentlyHeldCloverVoids * baseCountToAddToStack;

            //add to each stack
            foreach(ItemIndex item in itemsToUpgrade)
            {
                characterMaster.inventory.GiveItemPermanent(item, countToAddToStack);
                //CharacterMasterNotificationQueue.PushItemNotification(characterMaster, item);

                CharacterMasterNotificationQueue.SendTransformNotification(characterMaster, item, item, CharacterMasterNotificationQueue.TransformationType.CloverVoid);




                /*
                 * nice style but local language:
                
                CharacterMasterNotificationQueue.CustomOverrideInfo info = new CharacterMasterNotificationQueue.CustomOverrideInfo();
                ItemDef itemDef = ItemCatalog.GetItemDef(item);
                info.titleText = $"<style=cIsVoid>{Language.GetString("ITEM_CLOVERVOID_NAME")}:</style> {Language.GetString(itemDef.nameToken)} <style=cIsVoid>+{countToAddToStack}</style>";
                info.descriptionText = $"{Language.GetString(itemDef.pickupToken)}";
                CharacterMasterNotificationQueue.SendCustomNotification(characterMaster, item, info);
                */



            }


            if (itemsToUpgrade.Count > 0 && characterMaster.bodyInstanceObject)
            {
                Util.PlaySound("Play_item_proc_extraLife", characterMaster.bodyInstanceObject);
            }
        }

        //private void UpdateText()
        //{
        //    if (ItemsAdded.Value == 0 || StacksUpgraded.Value == 0) { return; }

        //    string token = "ITEM_CLOVERVOID_DESC";
        //    string text = "<style=cIsUtility>Adds " + ItemsAdded.Value + " </style> items to <style=cIsUtility>" + StacksUpgraded.Value + "</style> <style=cStack>(+" + StacksUpgraded.Value + " per stack)</style> item stacks at the <style=cIsUtility>start of each stage</style>. <style=cIsVoid>Corrupts all 57 Leaf Clovers</style>.";
        //    string token2 = "ITEM_CLOVERVOID_PICKUP";
        //    string text2 = "Upgrades your item stacks at the start of each stage.";

        //    try
        //    {
        //        ReplaceString(token, text);
        //        ReplaceString(token2, text2);
        //    }
        //    catch (Exception e)
        //    {
        //        Logger.LogError("Failed to change language::" + e);
        //    }
        //}

        //private void ReplaceString(string token, string newtext)
        //{
        //    LanguageAPI.Add(token, newtext);
        //}
    }
}

