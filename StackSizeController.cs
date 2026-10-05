using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Stack Size Controller", "AnExiledDev/patched by chrome", "4.1.5")]
    [Description("Allows configuration of most items max stack size.")]
    class StackSizeController : CovalencePlugin
    {
        [PluginReference]
        Plugin AirFuel, GetToDaChoppa, VehicleVendorOptions;

        private const string _vanillaDefaultsUri = "https://raw.githubusercontent.com/AnExiledDev/StackSizeController/master/vanilla-defaults.json";

        private Configuration _config;
        private Dictionary<string, int> _vanillaDefaults;

        private readonly List<string> _ignoreList = new List<string>
        {
            "water",
            "water.salt",
            "cardtable",
            "hat.bunnyhat",
            "rustige_egg_e"
        };

        private void Init()
        {
            _config = Config.ReadObject<Configuration>();

            if (_config == null)
            {
                Log("Generating Default Config File.");

                LoadDefaultConfig();
            }

            EnsureConfigIntegrity();

            AddCovalenceCommand("stacksizecontroller.setstack", nameof(SetStackCommand),
                "stacksizecontroller.setstack");
            AddCovalenceCommand("stacksizecontroller.setstackcat", nameof(SetStackCategoryCommand),
                "stacksizecontroller.setstackcat");
            AddCovalenceCommand("stacksizecontroller.setcategorylimit", nameof(SetCategoryLimitCommand),
                "stacksizecontroller.setstackcat");
            AddCovalenceCommand("stacksizecontroller.setallstacks", nameof(SetAllStacksCommand),
                "stacksizecontroller.setallstacks");
            AddCovalenceCommand("stacksizecontroller.itemsearch", nameof(ItemSearchCommand),
                "stacksizecontroller.itemsearch");
            AddCovalenceCommand("stacksizecontroller.listcategories", nameof(ListCategoriesCommand),
                "stacksizecontroller.listcategories");
            AddCovalenceCommand("stacksizecontroller.listcategoryitems", nameof(ListCategoryItemsCommand),
                "stacksizecontroller.listcategoryitems");
            AddCovalenceCommand("stacksizecontroller.vd", nameof(GenerateVanillaStackSizeFileCommand),
                "stacksizecontroller.vd");
        }

        private void OnServerInitialized()
        {
            try
            {
                _vanillaDefaults = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, int>>(
                    nameof(StackSizeController) + "_vanilla-defaults");
            }
            catch (Exception ex)
            {
                LogWarning("Unable to read saved defaults; using server item definitions. " + ex.Message);
            }

            if (_vanillaDefaults == null)
                _vanillaDefaults = new Dictionary<string, int>();

            // Capture every missing baseline BEFORE this plugin changes any stack sizes.
            foreach (ItemDefinition item in ItemManager.GetItemDefinitions())
                GetVanillaStackSize(item);

            SaveVanillaDefaults();
            PopulateIndividualItemStackSize();
            SetStackSizes();
            DownloadVanillaDefaults();
        }

        private void Unload()
        {
            if (_config != null && _vanillaDefaults != null && _config.RevertStackSizesToVanillaOnUnload)
            {
                RevertStackSizes();
            }
        }

        #region Configuration

        private class Configuration
        {
            public bool RevertStackSizesToVanillaOnUnload = true;
            public bool AllowStackingItemsWithDurability = true;
            public bool HidePrefixWithPluginNameInMessages;

            // Explicit category limits take precedence over item and multiplier settings.
            public Dictionary<string, int> CategoryStackSizes = new Dictionary<string, int>();
            public float GlobalStackMultiplier = 1;
            public Dictionary<string, float> CategoryStackMultipliers = GetCategoriesAndDefaults(1)
                .ToDictionary(k => k.Key,
                    k => Convert.ToSingle(k.Value));
            public Dictionary<string, float> IndividualItemStackMultipliers = new Dictionary<string, float>();

            public Dictionary<string, int> IndividualItemStackSize = new Dictionary<string, int>();

            public VersionNumber VersionNumber;
        }

        private static Dictionary<string, object> GetCategoriesAndDefaults(object defaultValue)
        {
            Dictionary<string, object> categoryDefaults = new Dictionary<string, object>();

            foreach (string category in Enum.GetNames(typeof(ItemCategory)))
            {
                categoryDefaults.Add(category, defaultValue);
            }

            return categoryDefaults;
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config);
        }

        protected override void LoadDefaultConfig()
        {
            Configuration defaultConfig = GetDefaultConfig();
            defaultConfig.VersionNumber = Version;

            Config.WriteObject(defaultConfig);

            _config = Config.ReadObject<Configuration>();
        }

        private void EnsureConfigIntegrity()
        {
            Configuration configDefault = new Configuration();

            if (_config.CategoryStackSizes == null)
                _config.CategoryStackSizes = new Dictionary<string, int>();

            if (_config.CategoryStackMultipliers == null)
            {
                _config.CategoryStackMultipliers = configDefault.CategoryStackMultipliers;
            }

            if (_config.IndividualItemStackMultipliers == null)
            {
                _config.IndividualItemStackMultipliers = configDefault.IndividualItemStackMultipliers;
            }

            if (_config.IndividualItemStackSize == null)
                _config.IndividualItemStackSize = new Dictionary<string, int>();

            foreach (string category in Enum.GetNames(typeof(ItemCategory)))
                if (!_config.CategoryStackMultipliers.ContainsKey(category))
                    _config.CategoryStackMultipliers[category] = 1f;

            _config.VersionNumber = Version;
            SaveConfig();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration();
        }

        private void UpdateIndividualItemStackMultiplier(int itemId, float multiplier)
        {
            UpdateIndividualItemStackMultiplier(ItemManager.FindItemDefinition(itemId).shortname, multiplier);
        }

        private void UpdateIndividualItemStackMultiplier(string shortname, float multiplier)
        {
            if (_config.IndividualItemStackMultipliers.ContainsKey(shortname))
            {
                _config.IndividualItemStackMultipliers[shortname] = multiplier;

                SaveConfig();

                return;
            }

            _config.IndividualItemStackMultipliers.Add(shortname, multiplier);

            SaveConfig();
        }

        private void UpdateIndividualItemStackSize(int itemId, int stackLimit)
        {
            ItemDefinition item = ItemManager.FindItemDefinition(itemId);

            if (_config.IndividualItemStackSize.ContainsKey(item.shortname))
            {
                _config.IndividualItemStackSize[item.shortname] = stackLimit;

                SaveConfig();

                return;
            }

            _config.IndividualItemStackSize.Add(item.shortname, stackLimit);

            SaveConfig();
        }

        private void UpdateIndividualItemStackSize(string shortname, int stackLimit)
        {
            if (_config.IndividualItemStackSize.ContainsKey(shortname))
            {
                _config.IndividualItemStackSize[shortname] = stackLimit;

                SaveConfig();

                return;
            }

            _config.IndividualItemStackSize.Add(shortname, stackLimit);

            SaveConfig();
        }

        private void PopulateIndividualItemStackSize()
        {
            foreach (ItemDefinition item in ItemManager.GetItemDefinitions())
            {
                if (!_config.IndividualItemStackSize.ContainsKey(item.shortname))
                    _config.IndividualItemStackSize[item.shortname] = GetVanillaStackSize(item);
            }

            SaveConfig();
        }

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NotEnoughArguments"] = "This command requires {0} arguments.",
                ["InvalidItemShortnameOrId"] =
                    "Item shortname or id is incorrect. Try stacksizecontroller.itemsearch [partial item name]",
                ["InvalidStackSize"] = "Enter a positive whole stack size or a positive multiplier (for example 2x).",
                ["NotReady"] = "Item definitions are still loading. Try again after the server has initialized.",
                ["InvalidCategory"] = "Category not found. Try stacksizecontroller.listcategories",
                ["OperationSuccessful"] = "Operation completed successfully.",
            }, this);
        }

        private string GetMessage(string key)
        {
            return lang.GetMessage(key, this);
        }

        private string GetMessage(string key, string playerId)
        {
            if (_config.HidePrefixWithPluginNameInMessages || playerId == "server_console")
            {
                return lang.GetMessage(key, this, playerId);
            }

            return $"<color=#ff760d><b>[{nameof(StackSizeController)}]</b></color> " +
                   lang.GetMessage(key, this, playerId);
        }

        #endregion

        #region Hooks

        // Credit to WhiteThunder- https://github.com/AnExiledDev/StackSizeController/pull/7
        // Fix initial fuel amount for vendor-spawned helis since they use 20% of max stack size of low grade.
        private void OnEntitySpawned(Minicopter heli)
        {
            // Ignore if a known plugin is loaded that adjusts heli fuel.
            if (AirFuel != null || GetToDaChoppa != null || VehicleVendorOptions != null)
                return;

            // Must delay for vendor-spawned helis since the creatorEntity is set after spawn.
            NextTick(() =>
            {
                if (heli == null
                    // Make sure it's a vendor-spawned heli.
                    || !heli.IsSafe()
                    // Make sure the game hasn't changed unexpectedly.
                    || heli.StartingFuelUnits() != -1)
                    return;

                var fuelItem = (heli.GetFuelSystem() as EntityFuelSystem)?.GetFuelItem();
                if (fuelItem == null
                    // Ignore other types of fuel since they will have been placed by mods.
                    || fuelItem.info.shortname != "lowgradefuel"
                    // Ignore if the fuel amount is unexpected, since a mod likely adjusted it.
                    || fuelItem.amount != fuelItem.info.stackable / 5)
                    return;

                var hookResult = Interface.CallHook("OnVendorHeliFuelAdjust", heli);
                if (hookResult is bool && (bool)hookResult == false)
                    return;

                fuelItem.amount = 100;
                fuelItem.MarkDirty();
            });
        }

        int OnMaxStackable(Item item)
        {
            if (_vanillaDefaults == null)
            {
                return item.info.stackable;
            }

            return GetStackSize(item.info);
        }

        #endregion

        #region Commands

        private void SetStackCommand(IPlayer player, string command, string[] args)
        {
            if (_vanillaDefaults == null)
            {
                player.Reply(GetMessage("NotReady", player.Id));
                return;
            }

            if (args.Length != 2)
            {
                player.Reply(
                    string.Format(GetMessage("NotEnoughArguments", player.Id), 2));

                return;
            }

            ItemDefinition itemDefinition = ItemManager.FindItemDefinition(args[0]);
            string stackSizeString = args[1];

            if (itemDefinition == null)
            {
                player.Reply(GetMessage("InvalidItemShortnameOrId", player.Id));

                return;
            }

            if (stackSizeString.EndsWith("x", StringComparison.OrdinalIgnoreCase))
            {
                float multiplier;
                if (!TryGetMultiplier(stackSizeString, out multiplier))
                {
                    player.Reply(GetMessage("InvalidStackSize", player.Id));
                    return;
                }
                UpdateIndividualItemStackMultiplier(itemDefinition.itemid, multiplier);
            }
            else
            {
                int stackSize;
                if (!int.TryParse(stackSizeString, out stackSize) || stackSize < 1)
                {
                    player.Reply(GetMessage("InvalidStackSize", player.Id));
                    return;
                }
                UpdateIndividualItemStackSize(itemDefinition.shortname, stackSize);
            }

            SetStackSizes();

            player.Reply(GetMessage("OperationSuccessful", player.Id));
        }

        private void SetAllStacksCommand(IPlayer player, string command, string[] args)
        {
            if (_vanillaDefaults == null)
            {
                player.Reply(GetMessage("NotReady", player.Id));
                return;
            }

            if (args.Length != 1)
            {
                player.Reply(
                    string.Format(GetMessage("NotEnoughArguments", player.Id), 1));
                return;
            }

            float multiplier;
            if (!TryGetMultiplier(args[0], out multiplier))
            {
                player.Reply(GetMessage("InvalidStackSize", player.Id));
                return;
            }

            foreach (string category in _config.CategoryStackMultipliers.Keys.ToList())
            {
                _config.CategoryStackMultipliers[category] = multiplier;
            }

            SaveConfig();
            SetStackSizes();

            player.Reply(GetMessage("OperationSuccessful", player.Id));
        }

        private void SetStackCategoryCommand(IPlayer player, string command, string[] args)
        {
            if (_vanillaDefaults == null)
            {
                player.Reply(GetMessage("NotReady", player.Id));
                return;
            }

            if (args.Length != 2)
            {
                player.Reply(
                    string.Format(GetMessage("NotEnoughArguments", player.Id), 2));
                return;
            }

            ItemCategory itemCategory;
            if (!Enum.TryParse<ItemCategory>(args[0], true, out itemCategory) ||
                !Enum.IsDefined(typeof(ItemCategory), itemCategory))
            {
                player.Reply(GetMessage("InvalidCategory", player.Id));
                return;
            }

            float multiplier;
            if (!TryGetMultiplier(args[1], out multiplier))
            {
                player.Reply(GetMessage("InvalidStackSize", player.Id));
                return;
            }
            _config.CategoryStackMultipliers[itemCategory.ToString()] = multiplier;

            SaveConfig();
            SetStackSizes();

            player.Reply(GetMessage("OperationSuccessful", player.Id));
        }

        private void SetCategoryLimitCommand(IPlayer player, string command, string[] args)
        {
            if (_vanillaDefaults == null)
            {
                player.Reply(GetMessage("NotReady", player.Id));
                return;
            }

            if (args.Length != 2)
            {
                player.Reply("Usage: stacksizecontroller.setcategorylimit Food 100 (use 0 to remove the limit)");
                return;
            }

            ItemCategory category;
            if (!Enum.TryParse<ItemCategory>(args[0], true, out category) ||
                !Enum.IsDefined(typeof(ItemCategory), category))
            {
                player.Reply(GetMessage("InvalidCategory", player.Id));
                return;
            }

            int limit;
            if (!int.TryParse(args[1], out limit) || limit < 0)
            {
                player.Reply("Enter a positive whole stack size, or 0 to remove the category limit.");
                return;
            }

            // Restore definitions first so removing an override also restores excluded items.
            RevertStackSizes();
            if (limit == 0)
                _config.CategoryStackSizes.Remove(category.ToString());
            else
                _config.CategoryStackSizes[category.ToString()] = limit;

            SaveConfig();
            SetStackSizes();
            int count = ItemManager.GetItemDefinitions().Count(item => item.category == category);
            player.Reply(limit == 0
                ? $"Removed the fixed stack limit for {category}. Existing stack settings restored."
                : $"Set all {count} items in {category} to exactly {limit} per stack. Future items in this category will use the same limit.");
        }

        private void ItemSearchCommand(IPlayer player, string command, string[] args)
        {
            if (_vanillaDefaults == null)
            {
                player.Reply(GetMessage("NotReady", player.Id));
                return;
            }

            if (args.Length != 1)
            {
                player.Reply(
                    string.Format(GetMessage("NotEnoughArguments", player.Id), 1));
                return;
            }

            List<ItemDefinition> itemDefinitions = ItemManager.itemList.Where(itemDefinition =>
                    itemDefinition.displayName.english.Contains(args[0]) ||
                    itemDefinition.displayDescription.english.Contains(args[0]) ||
                    itemDefinition.shortname.Equals(args[0]) ||
                    itemDefinition.shortname.Contains(args[0]))
                .ToList();

            TextTable output = new TextTable();
            output.AddColumns("Unique Id", "Shortname", "Category", "Vanilla Stack", "Custom Stack");

            foreach (ItemDefinition itemDefinition in itemDefinitions)
            {
                output.AddRow(itemDefinition.itemid.ToString(), itemDefinition.shortname,
                    itemDefinition.category.ToString(), GetVanillaStackSize(itemDefinition).ToString("N0"),
                    Mathf.Clamp(GetStackSize(itemDefinition), 0, int.MaxValue).ToString("N0"));
            }

            player.Reply(output.ToString());
        }

        private void ListCategoriesCommand(IPlayer player, string command, string[] args)
        {
            TextTable output = new TextTable();
            output.AddColumns("Category Name", "Items In Category");

            foreach (string category in Enum.GetNames(typeof(ItemCategory)))
            {
                output.AddRow(category, ItemManager.itemList.Where(x => x.category.ToString() == category).Count().ToString());
            }

            player.Reply(output.ToString());
        }

        private void ListCategoryItemsCommand(IPlayer player, string command, string[] args)
        {
            if (_vanillaDefaults == null)
            {
                player.Reply(GetMessage("NotReady", player.Id));
                return;
            }

            if (args.Length != 1)
            {
                player.Reply(string.Format(GetMessage("NotEnoughArguments", player.Id), 1));
                return;
            }

            ItemCategory itemCategory;
            if (!Enum.TryParse<ItemCategory>(args[0], true, out itemCategory) ||
                !Enum.IsDefined(typeof(ItemCategory), itemCategory))
            {
                player.Reply(GetMessage("InvalidCategory", player.Id));
                return;
            }

            TextTable output = new TextTable();
            output.AddColumns("Unique Id", "Shortname", "Category", "Vanilla Stack", "Custom Stack", "Multiplier");

            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions()
                .Where(itemDefinition => itemDefinition.category == itemCategory))
            {
                output.AddRow(itemDefinition.itemid.ToString(), itemDefinition.shortname,
                    itemDefinition.category.ToString(), GetVanillaStackSize(itemDefinition).ToString("N0"),
                    Mathf.Clamp(GetStackSize(itemDefinition), 0, int.MaxValue).ToString("N0"),
                    _config.CategoryStackMultipliers[itemDefinition.category.ToString()].ToString());
            }

            player.Reply(output.ToString());
        }

        #endregion

        #region Dev Use

        private void GenerateVanillaStackSizeFileCommand(IPlayer player, string command, string[] args)
        {
            if (_vanillaDefaults == null)
            {
                player.Reply(GetMessage("NotReady", player.Id));
                return;
            }

            GenerateVanillaStackSizeFile();
        }

        private void GenerateVanillaStackSizeFile()
        {
            RevertStackSizes();

            SortedDictionary<string, int> vanillaStackSizes = new SortedDictionary<string, int>();

            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions())
            {
                vanillaStackSizes.Add(itemDefinition.shortname, itemDefinition.stackable);
            }

            Interface.Oxide.DataFileSystem.WriteObject(nameof(StackSizeController) + "_vanilla-defaults",
                vanillaStackSizes);

            SetStackSizes();

            Log("Vanilla stack sizes file updated. Custom stack sizes restored.");
        }

        #endregion

        #region Helpers

        private void DownloadVanillaDefaults()
        {
            Log($"Refreshing known vanilla defaults from GitHub; {_vanillaDefaultsUri}");

            try
            {
                webrequest.Enqueue(_vanillaDefaultsUri, null, SetVanillaDefaults, this, RequestMethod.GET);
            }
            catch (Exception ex)
            {
                LogError($"Exception encountered while attempting to get vanilla defaults: {ex}");
            }
        }

        private void SetVanillaDefaults(int code, string response)
        {
            if (code != 200 || string.IsNullOrEmpty(response))
            {
                LogWarning($"Unable to download vanilla defaults (HTTP {code}); using saved/server defaults.");
                return;
            }

            try
            {
                var downloaded = JsonConvert.DeserializeObject<Dictionary<string, int>>(response);
                if (downloaded == null || downloaded.Count == 0)
                {
                    LogWarning("Downloaded defaults were empty; using saved/server defaults.");
                    return;
                }

                // Merge rather than replace: older lists must not remove newly discovered items.
                foreach (var entry in downloaded)
                    if (entry.Value > 0)
                        _vanillaDefaults[entry.Key] = entry.Value;

                SaveVanillaDefaults();
                PopulateIndividualItemStackSize();
                SetStackSizes();
            }
            catch (Exception ex)
            {
                LogWarning("Unable to process downloaded defaults; using saved/server defaults. " + ex.Message);
            }
        }

        private void SaveVanillaDefaults()
        {
            Interface.Oxide.DataFileSystem.WriteObject(nameof(StackSizeController) +
                "_vanilla-defaults", _vanillaDefaults);
        }

        private int GetVanillaStackSize(ItemDefinition itemDefinition)
        {
            int stackSize;
            if (_vanillaDefaults != null &&
                _vanillaDefaults.TryGetValue(itemDefinition.shortname, out stackSize) && stackSize > 0)
                return stackSize;

            stackSize = Math.Max(1, itemDefinition.stackable);
            if (_vanillaDefaults != null)
                _vanillaDefaults[itemDefinition.shortname] = stackSize;
            return stackSize;
        }

        private bool TryGetMultiplier(string value, out float multiplier)
        {
            return float.TryParse(value.TrimEnd('x', 'X'), out multiplier) &&
                !float.IsNaN(multiplier) && !float.IsInfinity(multiplier) && multiplier > 0f;
        }

        private bool TryGetCategoryStackSize(ItemDefinition item, out int limit)
        {
            return _config.CategoryStackSizes.TryGetValue(item.category.ToString(), out limit) && limit > 0;
        }

        private int GetStackSize(int itemId)
        {
            return GetStackSize(ItemManager.FindItemDefinition(itemId));
        }

        private int GetStackSize(ItemDefinition itemDefinition)
        {
            try
            {
                int categoryLimit;
                if (TryGetCategoryStackSize(itemDefinition, out categoryLimit))
                    return categoryLimit;

                if (_ignoreList.Contains(itemDefinition.shortname))
                {
                    return GetVanillaStackSize(itemDefinition);
                }

                int stackable = GetVanillaStackSize(itemDefinition);

                // Individual Limit set by shortname
                if (_config.IndividualItemStackSize.ContainsKey(itemDefinition.shortname))
                {
                    stackable = _config.IndividualItemStackSize[itemDefinition.shortname];
                }

                // Individual Multiplier set by shortname
                if (_config.IndividualItemStackMultipliers.ContainsKey(itemDefinition.shortname))
                {
                    return Mathf.RoundToInt(stackable * _config.IndividualItemStackMultipliers[itemDefinition.shortname]);
                }

                // Individual Multiplier set by item id
                if (_config.IndividualItemStackMultipliers.ContainsKey(itemDefinition.itemid.ToString()))
                {
                    return Mathf.RoundToInt(stackable * _config.IndividualItemStackMultipliers[itemDefinition.itemid.ToString()]);
                }

                // Category stack multiplier defined
                if (_config.CategoryStackMultipliers.ContainsKey(itemDefinition.category.ToString()) &&
                    _config.CategoryStackMultipliers[itemDefinition.category.ToString()] > 1.0f)
                {
                    return Mathf.RoundToInt(
                        stackable * _config.CategoryStackMultipliers[itemDefinition.category.ToString()]);
                }

                return Mathf.RoundToInt(stackable * _config.GlobalStackMultiplier);
            }
            catch (Exception ex)
            {
                LogError("Exception encountered during GetStackSize. Item: " + itemDefinition.shortname + " Ex:" + ex.ToString());

                return GetVanillaStackSize(itemDefinition);
            }
        }

        private void SetStackSizes()
        {
            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions())
            {
                int categoryLimit;
                if (TryGetCategoryStackSize(itemDefinition, out categoryLimit))
                {
                    itemDefinition.stackable = categoryLimit;
                    continue;
                }

                if (itemDefinition.condition.enabled && !_config.AllowStackingItemsWithDurability)
                {
                    itemDefinition.stackable = Mathf.Clamp(GetVanillaStackSize(itemDefinition), 1, int.MaxValue);

                    continue;
                }

                if (_ignoreList.Contains(itemDefinition.shortname))
                {
                    continue;
                }

                itemDefinition.stackable = Mathf.Clamp(GetStackSize(itemDefinition), 1, int.MaxValue);
            }
        }

        private void RevertStackSizes()
        {
            Log("Reverting stack sizes to vanilla defaults.");

            foreach (ItemDefinition itemDefinition in ItemManager.GetItemDefinitions())
            {
                int categoryLimit;
                if (TryGetCategoryStackSize(itemDefinition, out categoryLimit))
                {
                    itemDefinition.stackable = GetVanillaStackSize(itemDefinition);
                    continue;
                }

                if (itemDefinition.condition.enabled && !_config.AllowStackingItemsWithDurability)
                {
                    continue;
                }

                if (_ignoreList.Contains(itemDefinition.shortname))
                {
                    continue;
                }

                itemDefinition.stackable = Mathf.Clamp(GetVanillaStackSize(itemDefinition), 1, int.MaxValue);
            }
        }

        #endregion
    }
}