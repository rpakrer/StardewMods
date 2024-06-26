using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.TerrainFeatures;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using SpaceShared;
using SpaceCore;
using GenericModConfigMenu;
using Microsoft.Xna.Framework.Content;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Reflection;
using StardewValley.GameData.Locations;

namespace RandomFruitTreesMod
{
    public class ModConfig
    {
        public bool ResetAllLocations { get; set; } = false;
        public bool ResetNonFarmableLocations { get; set; } = false;
        public float ReplacementProbability { get; set; } = 0.1f;
    }

    public class ModEntry : Mod
    {
        private List<string> modifiedLocations = new List<string>();
        private ModConfig config;
        private Dictionary<string, JObject> locationsData;
        private IContentHelper contentHelper;
        public override void Entry(IModHelper helper)
        {
            contentHelper = helper.Content;
            config = helper.ReadConfig<ModConfig>();
            // Hook into the events
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;

            // Register with GMCM
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;

            // Add SMAPI console command
            helper.ConsoleCommands.Add("ResetTrees", "Reset trees at the specified location.\n\nUsage: ResetTrees <Location>", ResetTreesCommand);
        }
        private void LoadLocationsData()
        {
            try
            {
                // Load the location data from Content folder
                Dictionary<string, StardewValley.GameData.Locations.LocationData> locationData =
                    contentHelper.Load<Dictionary<string, StardewValley.GameData.Locations.LocationData>>("Data\\Locations");

                // Access and process location data as needed
                foreach (var pair in locationData)
                {
                    string locationName = pair.Key;
                    StardewValley.GameData.Locations.LocationData data = pair.Value;

                    // Example processing
                    // Console.WriteLine($"Location: {locationName}, Data: {data}");
                }
            }
            catch (Exception ex)
            {
                Monitor.Log($"Error loading locations data: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            LoadModifiedLocations();

            if (config.ResetAllLocations)
            {
                ReplaceTreesInAllLocations(includeAll: true);
            }
            else if (config.ResetNonFarmableLocations)
            {
                ReplaceTreesInAllLocations(includeAll: false);
            }
        }


        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            modifiedLocations.Clear();
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm != null)
            {
                gmcm.Register(
                    mod: this.ModManifest,
                    reset: () => this.config = new ModConfig(),
                    save: () => this.Helper.WriteConfig(this.config)
                );
                gmcm.AddBoolOption(
                    mod: this.ModManifest,
                    name: () => "Reset All Locations",
                    tooltip: () => "Reset all locations, including farmable areas and planted trees.",
                    getValue: () => this.config.ResetAllLocations,
                    setValue: value => this.config.ResetAllLocations = value
                );
                gmcm.AddBoolOption(
                    mod: this.ModManifest,
                    name: () => "Reset Non-Farm Locations",
                    tooltip: () => "Reset Non-Farmable locations, may not work with mods that allow planting anywhere.",
                    getValue: () => this.config.ResetNonFarmableLocations,
                    setValue: value => this.config.ResetNonFarmableLocations = value
                );
                gmcm.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Replacement Probability",
                    tooltip: () => "Set the probability of replacing a normal tree with a fruit tree.",
                    getValue: () => this.config.ReplacementProbability,
                    setValue: value => this.config.ReplacementProbability = value
                );
            }
        }

        private void ReplaceTreesInAllLocations(bool includeAll)
        {
            foreach (var locationEntry in locationsData)
            {
                string locationName = locationEntry.Key;
                var locationData = locationEntry.Value;

                // Check if the location is outdoors and exists in the loaded data
                if (Game1.getLocationFromName(locationName) is GameLocation location && location.IsOutdoors)
                {
                    // Extract CanPlantHere from locationData
                    bool canPlantHere = locationData["CanPlantHere"]?.ToObject<bool>() ?? false;

                    if (!includeAll && canPlantHere)
                        continue; // Skip farmable locations unless includeAll is true

                    ReplaceRandomTrees(location);
                    modifiedLocations.Add(locationName); // Track modified locations to prevent repeat changes
                }
            }

            SaveModifiedLocations();
        }
        private List<string> GetFruitTreeTypes()
        {
            List<string> fruitTreeTypes = new List<string>();

            try
            {
                string fruitTreesFilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Content", "Data", "FruitTrees.json");
                string fruitTreesJson = File.ReadAllText(fruitTreesFilePath);

                JObject fruitTreesData = JObject.Parse(fruitTreesJson);

                foreach (var pair in fruitTreesData)
                {
                    string treeType = pair.Key; // Assuming pair.Key is already a string
                    fruitTreeTypes.Add(treeType);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading FruitTrees data: {ex.Message}");
                // Handle error as needed
            }

            return fruitTreeTypes;
        }
        private void ReplaceRandomTrees(GameLocation location)
        {
            List<string> fruitTreeTypes = GetFruitTreeTypes();

            foreach (var feature in location.terrainFeatures.Pairs)
            {
                if (feature.Value is Tree tree && IsNormalTree(tree))
                {
                    // Replace a random selection of normal trees with fruit trees
                    if (fruitTreeTypes.Count > 0 && Game1.random.NextDouble() < config.ReplacementProbability)
                    {
                        int randomIndex = Game1.random.Next(fruitTreeTypes.Count);
                        string fruitTreeType = fruitTreeTypes[randomIndex];
                        var fruitTree = new FruitTree(fruitTreeType);
                        location.terrainFeatures[feature.Key] = fruitTree;

                        // Remove the used fruit tree type from the list to prevent reuse in this location
                        fruitTreeTypes.RemoveAt(randomIndex);
                    }
                }
            }
        }

        private bool IsNormalTree(Tree tree)
        {
            // Check if the tree is a normal type (e.g., bushyTree, leafyTree, etc.)
            // Extend this to check for modded normal trees if needed
            return tree.treeType.Value == Tree.bushyTree || tree.treeType.Value == Tree.leafyTree || tree.treeType.Value == Tree.pineTree || tree.treeType.Value == Tree.palmTree;
        }


        private void ResetTreesCommand(string command, string[] args)
        {
            if (args.Length != 1)
            {
                Monitor.Log("Invalid command usage. Correct usage: ResetTrees <Location>", LogLevel.Error);
                return;
            }

            string locationName = args[0];
            GameLocation location = Game1.getLocationFromName(locationName);
            if (location == null)
            {
                Monitor.Log($"Location '{locationName}' not found.", LogLevel.Error);
                return;
            }

            ReplaceRandomTrees(location);
            Monitor.Log($"Trees reset at location '{locationName}'.", LogLevel.Info);
        }

        private void SaveModifiedLocations()
        {
            var savePath = Path.Combine(Constants.CurrentSavePath, "modifiedLocations.json");
            File.WriteAllText(savePath, JsonConvert.SerializeObject(modifiedLocations));
        }

        private void LoadModifiedLocations()
        {
            var savePath = Path.Combine(Constants.CurrentSavePath, "modifiedLocations.json");
            if (File.Exists(savePath))
            {
                modifiedLocations = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(savePath)) ?? new List<string>();
            }
        }
    }
}
