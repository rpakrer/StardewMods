﻿using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewValley.GameData.Locations;
using GenericModConfigMenu;


namespace RandomFruitTreesMod
{
    public class ModEntry : Mod
    {
        private Random random;
        private Dictionary<string, FruitTreeData> fruitTreeDataDict;
        private HashSet<string> modifiedLocations;
        private ModConfig config;

        private List<string> allOutdoorLocations = new List<string>();
        private List<string> nonPlantableOutdoorLocations = new List<string>();

        public override void Entry(IModHelper helper)
        {
            this.Helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            this.Helper.Events.GameLoop.Saving += this.OnSaving;
            this.random = new Random();

            // Load configuration
            this.config = helper.ReadConfig<ModConfig>();

            // Add console commands
            helper.ConsoleCommands.Add("reset_trees", "Resets trees for the specified location.\n\nUsage: reset_trees <location>", this.ResetTreesCommand);
            helper.ConsoleCommands.Add("replace_missing_trees", "Detects and replaces missing modded trees.", this.ReplaceMissingTreesCommand);

            // Register GMCM integration
            this.Helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            var gmcmApi = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcmApi != null)
            {
                gmcmApi.Register(
                    mod: this.ModManifest,
                    reset: () => this.config = new ModConfig(),
                    save: () => this.Helper.WriteConfig(this.config)
                );

                gmcmApi.AddBoolOption(
                    mod: this.ModManifest,
                    name: () => "Reset All Locations",
                    tooltip: () => "Reroll trees in all locations, will replace planted trees.",
                    getValue: () => this.config.ResetAllLocations,
                    setValue: value => this.config.ResetAllLocations = value
                );

                gmcmApi.AddBoolOption(
                    mod: this.ModManifest,
                    name: () => "Reset Non-Farmable Locations",
                    tooltip: () => "Reroll trees in non-farmable locations, might be buggy if used with mods that allow planting anywhere.",
                    getValue: () => this.config.ResetNonFarmableLocations,
                    setValue: value => this.config.ResetNonFarmableLocations = value
                );

                gmcmApi.AddBoolOption(
                    mod: this.ModManifest,
                    name: () => "Operate on New Games Only",
                    tooltip: () => "Only operates on new games (Day 1 of Spring, Year 1), unless the above config options are used.",
                    getValue: () => this.config.OperateOnNewGamesOnly,
                    setValue: value => this.config.OperateOnNewGamesOnly = value
                );

                gmcmApi.AddNumberOption(
                    mod: this.ModManifest,
                    name: () => "Replacement Probability",
                    tooltip: () => "The probability of replacing a tree with a fruit tree.",
                    getValue: () => (float)this.config.ReplacementProbability,
                    setValue: value => this.config.ReplacementProbability = (double)value,
                    min: 0f,
                    max: 1f,
                    interval: 0.01f
                );
            }
        }

        private void ResetTreesCommand(string command, string[] args)
        {
            if (args.Length != 1)
            {
                this.Monitor.Log("Usage: reset_trees <location>", LogLevel.Info);
                return;
            }

            string locationName = args[0];
            var location = Game1.getLocationFromName(locationName);

            if (location == null)
            {
                this.Monitor.Log($"Location '{locationName}' not found.", LogLevel.Info);
                return;
            }

            this.Monitor.Log($"Resetting trees in location '{locationName}'...", LogLevel.Info);
            ReplaceTreesInLocation(location);
        }

        private void ReplaceMissingTreesCommand(string command, string[] args)
        {
            this.Monitor.Log("Replacing missing trees...", LogLevel.Info);
            this.fruitTreeDataDict = LoadFruitTreeData(); // Ensure the latest fruit tree data is loaded

            foreach (var location in Game1.locations)
            {
                if (location.IsOutdoors)
                {
                    foreach (var kvp in location.terrainFeatures.Pairs.ToList())
                    {
                        if (kvp.Value is FruitTree fruitTree)
                        {
                            if (!fruitTreeDataDict.ContainsKey(fruitTree.treeId.ToString()))
                            {
                                this.Monitor.Log($"Replacing missing tree at {location.Name} ({kvp.Key.X}, {kvp.Key.Y})...", LogLevel.Info);
                                ReplaceWithRandomFruitTree(location, kvp.Key, fruitTree);
                            }
                        }
                    }
                }
            }
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            try
            {
                // Load fruit tree data
                this.fruitTreeDataDict = LoadFruitTreeData();

                // Load the modified locations list
                LoadModifiedLocations();

                // Populate the location lists
                PopulateLocationLists();

                // Check if we need to reset all locations
                if (config.ResetAllLocations)
                {
                    ReplaceTreesInLocations(allOutdoorLocations);
                    config.ResetAllLocations = false;
                    SaveConfig();
                }
                // Check if we need to reset non-farmable locations
                else if (config.ResetNonFarmableLocations)
                {
                    ReplaceTreesInLocations(nonPlantableOutdoorLocations);
                    config.ResetNonFarmableLocations = false;
                    SaveConfig();
                }
                // Check if we should operate only on new games
                else if (config.OperateOnNewGamesOnly && Game1.dayOfMonth == 1 && Game1.year == 1 && Game1.currentSeason == "spring")
                {
                    ReplaceTreesInLocations(allOutdoorLocations);
                }
                else
                {
                    foreach (var location in Game1.locations)
                    {
                        if (location != null && location.IsOutdoors && ShouldModifyLocation(location))
                        {
                            foreach (var kvp in location.terrainFeatures.Pairs.ToList())
                            {
                                if (kvp.Value is Tree tree)
                                {
                                    ReplaceWithRandomFruitTree(location, kvp.Key, tree);
                                }
                                else if (kvp.Value is FruitTree fruitTree)
                                {
                                    ReplaceWithRandomFruitTree(location, kvp.Key, fruitTree);
                                }
                            }
                            // Mark location as modified
                            modifiedLocations.Add(location.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error in OnSaveLoaded: {ex.Message}", LogLevel.Error);
            }
        }

        private void OnSaving(object sender, SavingEventArgs e)
        {
            // Save the modified locations list
            SaveModifiedLocations();
        }

        private bool ShouldModifyLocation(GameLocation location)
        {
            // Check if the location has already been modified
            return config.ResetAllLocations || config.ResetNonFarmableLocations || !modifiedLocations.Contains(location.Name);
        }

        private void LoadModifiedLocations()
        {
            try
            {
                string path = GetModifiedLocationsFilePath();
                if (File.Exists(path))
                {
                    modifiedLocations = new HashSet<string>(File.ReadAllLines(path));
                }
                else
                {
                    modifiedLocations = new HashSet<string>();
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error loading modified locations: {ex.Message}", LogLevel.Error);
                modifiedLocations = new HashSet<string>();
            }
        }

        private void SaveModifiedLocations()
        {
            try
            {
                string path = GetModifiedLocationsFilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllLines(path, modifiedLocations);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error saving modified locations: {ex.Message}", LogLevel.Error);
            }
        }

        private string GetModifiedLocationsFilePath()
        {
            return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StardewValley", "Saves", Constants.SaveFolderName), "ModifiedLocations.txt");
        }

        private Dictionary<string, FruitTreeData> LoadFruitTreeData()
        {
            try
            {
                var fruitTreesData = Helper.GameContent.Load<Dictionary<string, StardewValley.GameData.FruitTrees.FruitTreeData>>("Data/FruitTrees");

                if (fruitTreesData == null)
                {
                    this.Monitor.Log("Failed to load FruitTrees data.", LogLevel.Error);
                    return new Dictionary<string, FruitTreeData>();
                }

                return fruitTreesData.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new FruitTreeData
                    {
                        Seasons = kvp.Value.Seasons.Select(season => season.ToString()).ToList(),
                        Fruits = kvp.Value.Fruit.Select(fruit => fruit.Id).ToList()
                    });
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error loading FruitTrees data: {ex.Message}", LogLevel.Error);
                return new Dictionary<string, FruitTreeData>();
            }
        }

        private void ReplaceWithRandomFruitTree(GameLocation location, Vector2 tile, TerrainFeature originalTree)
        {
            // Use the configured replacement probability
            double replacementProbability = config.ReplacementProbability;

            if (random.NextDouble() <= replacementProbability)
            {
                string randomFruitTreeId = GetRandomFruitTreeType();
                int randomGrowthStage = random.Next(0, 5);

                FruitTree newFruitTree = new FruitTree(randomFruitTreeId, randomGrowthStage);
                location.terrainFeatures[tile] = newFruitTree;

                this.Monitor.Log($"Replaced tree at {location.Name} ({tile.X}, {tile.Y}) with fruit tree {randomFruitTreeId} at growth stage {randomGrowthStage}", LogLevel.Info);

                // Add fruit if the tree is fully grown and in season
                if (randomGrowthStage == 4 && fruitTreeDataDict.TryGetValue(randomFruitTreeId, out FruitTreeData fruitTreeData))
                {
                    AddFruitIfInSeason(newFruitTree, fruitTreeData);
                }
            }
        }

        private void AddFruitIfInSeason(FruitTree fruitTree, FruitTreeData fruitTreeData)
        {
            string currentSeason = char.ToUpper(Game1.currentSeason[0]) + Game1.currentSeason.Substring(1); // Capitalize first letter

            if (fruitTreeData.Seasons.Contains(currentSeason))
            {
                int numberOfFruits = random.Next(1, 4); // Random number between 1 and 3
                for (int i = 0; i < numberOfFruits; i++)
                {
                    fruitTree.TryAddFruit();
                }
                this.Monitor.Log($"Added {numberOfFruits} fruits to tree in season {currentSeason} with fruits {string.Join(",", fruitTreeData.Fruits)}", LogLevel.Info);
            }
        }

        private string GetRandomFruitTreeType()
        {
            if (fruitTreeDataDict == null || fruitTreeDataDict.Count == 0)
            {
                this.Monitor.Log("No fruit tree data available for replacement.", LogLevel.Error);
                return "1"; // Default to apple tree if no IDs are available
            }

            var keys = fruitTreeDataDict.Keys.ToList();
            int index = random.Next(keys.Count);
            return keys[index];
        }

        private void ReplaceTreesInLocations(List<string> locations)
        {
            foreach (var locationName in locations)
            {
                var location = Game1.getLocationFromName(locationName);
                if (location != null)
                {
                    foreach (var kvp in location.terrainFeatures.Pairs.ToList())
                    {
                        if (kvp.Value is Tree tree)
                        {
                            ReplaceWithRandomFruitTree(location, kvp.Key, tree);
                        }
                        else if (kvp.Value is FruitTree fruitTree)
                        {
                            ReplaceWithRandomFruitTree(location, kvp.Key, fruitTree);
                        }
                    }
                    // Mark location as modified
                    modifiedLocations.Add(location.Name);
                }
            }
        }

        private void ReplaceTreesInLocation(GameLocation location)
        {
            if (location != null)
            {
                foreach (var kvp in location.terrainFeatures.Pairs.ToList())
                {
                    if (kvp.Value is Tree tree)
                    {
                        ReplaceWithRandomFruitTree(location, kvp.Key, tree);
                    }
                    else if (kvp.Value is FruitTree fruitTree)
                    {
                        ReplaceWithRandomFruitTree(location, kvp.Key, fruitTree);
                    }
                }
                // Mark location as modified
                modifiedLocations.Add(location.Name);
            }
        }

        private void PopulateLocationLists()
        {
            try
            {
                var locationData = Helper.GameContent.Load<Dictionary<string, LocationData>>("Data/Locations");

                if (locationData == null)
                {
                    this.Monitor.Log("Failed to load LocationData.", LogLevel.Error);
                    return;
                }

                foreach (var location in Game1.locations)
                {
                    if (location != null && location.IsOutdoors)
                    {
                        allOutdoorLocations.Add(location.Name);

                        // Check if the location is not a farm and CanPlantHere is false or null
                        if (!location.IsFarm && locationData.TryGetValue(location.Name, out LocationData data))
                        {
                            bool canPlant = data?.CanPlantHere ?? false; // Assume false if data or CanPlantHere is null
                            if (!canPlant)
                            {
                                nonPlantableOutdoorLocations.Add(location.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error in PopulateLocationLists: {ex.Message}", LogLevel.Error);
            }
        }

        private void SaveConfig()
        {
            this.Helper.WriteConfig(this.config);
        }

        private class FruitTreeData
        {
            public List<string> Seasons { get; set; }
            public List<string> Fruits { get; set; }
        }

        private class ModConfig
        {
            public bool ResetAllLocations { get; set; } = false;
            public bool ResetNonFarmableLocations { get; set; } = false;
            public bool OperateOnNewGamesOnly { get; set; } = true;
            public double ReplacementProbability { get; set; } = 0.1;
        }
    }
}
