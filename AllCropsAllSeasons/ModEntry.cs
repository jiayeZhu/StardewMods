﻿using System;
using System.Collections.Generic;
using System.Linq;
using AllCropsAllSeasons.Framework;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;

namespace AllCropsAllSeasons
{
    /// <summary>The entry class called by SMAPI.</summary>
    public class ModEntry : Mod, IAssetEditor
    {
        /*********
        ** Properties
        *********/
        /// <summary>The crop tiles which should be saved for the next day.</summary>
        private CropTileState[] SavedCrops = new CropTileState[0];


        /*********
        ** Public methods
        *********/
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            LocationEvents.CurrentLocationChanged += this.ReceiveCurrentLocationChanged;
            SaveEvents.BeforeSave += this.ReceiveBeforeSave;
        }

        /// <summary>Get whether this instance can edit the given asset.</summary>
        /// <param name="asset">Basic metadata about the asset being loaded.</param>
        public bool CanEdit<T>(IAssetInfo asset)
        {
            return
                asset.AssetNameEquals("Data/Crops") // change crop seasons
                || asset.AssetNameEquals("TerrainFeatures/hoeDirtSnow"); // change dirt texture
        }

        /// <summary>Edit a matched asset.</summary>
        /// <param name="asset">A helper which encapsulates metadata about an asset and enables changes to it.</param>
        public void Edit<T>(IAssetData asset)
        {
            // change crop seasons
            if (asset.AssetNameEquals("Data/Crops"))
            {
                asset
                    .AsDictionary<int, string>()
                    .Set((id, data) =>
                    {
                        string[] fields = data.Split('/');
                        fields[1] = "spring summer fall winter";
                        return string.Join("/", fields);
                    });
            }

            // change dirt texture
            else if (asset.AssetNameEquals("TerrainFeatures/hoeDirtSnow"))
                asset.ReplaceWith(this.Helper.Content.Load<Texture2D>("TerrainFeatures/hoeDirt", ContentSource.GameContent));
        }


        /*********
        ** Private methods
        *********/
        /****
        ** Event handlers
        ****/
        /// <summary>The method called when the player warps to a new location.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void ReceiveCurrentLocationChanged(object sender, EventArgsCurrentLocationChanged e)
        {
            // when player enters farmhouse (including on new day), back up crops in case they're
            // about to end the day
            if (e.NewLocation is FarmHouse)
                this.StashCrops();
        }

        /// <summary>The method called when the game is writing to the save file.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void ReceiveBeforeSave(object sender, EventArgs e)
        {
            // before save (but after tomorrow's day updates), fix any crops that died due to the day update
            this.RestoreCrops();
        }

        /****
        ** Methods
        ****/
        /// <summary>Take a snapshot of the current crops.</summary>
        private void StashCrops()
        {
            Farm farm = Game1.getFarm();
            this.SavedCrops = this.GetCropTiles(farm).ToArray();
        }

        /// <summary>Restore the temporarily-saved crops.</summary>
        private void RestoreCrops()
        {
            // get data
            CropTileState[] crops = this.SavedCrops;
            if (!crops.Any())
                return;
            Farm farm = Game1.getFarm();
            GameLocation greenhouse = Game1.getLocationFromName("Greenhouse");

            // ignore crops converted into giant crops
            {
                HashSet<Vector2> coveredByGiantCrop = new HashSet<Vector2>(this.GetGiantCropTiles(farm));
                crops = crops.Where(crop => !coveredByGiantCrop.Contains(crop.Tile)).ToArray();
            }

            // restore crops
            foreach (CropTileState saved in crops)
            {
                // get actual tile
                if (!farm.terrainFeatures.ContainsKey(saved.Tile) || !(farm.terrainFeatures[saved.Tile] is HoeDirt))
                    farm.terrainFeatures[saved.Tile] = new HoeDirt(); // reset dirt tile if needed (e.g. to clear debris)
                HoeDirt dirt = (HoeDirt)farm.terrainFeatures[saved.Tile];

                // reset crop tile if needed
                if (dirt.crop == null || dirt.crop.dead)
                {
                    // reset values changed by day update
                    if (dirt.state != HoeDirt.watered)
                        dirt.state = saved.State;
                    dirt.fertilizer = saved.Fertilizer;
                    dirt.crop = saved.Crop;
                    dirt.crop.dead = false;
                    dirt.dayUpdate(greenhouse, saved.Tile);
                }
            }
        }

        /// <summary>Get all tiles on the farm with a live crop.</summary>
        /// <param name="farm">The farm to search.</param>
        private IEnumerable<CropTileState> GetCropTiles(Farm farm)
        {
            foreach (KeyValuePair<Vector2, TerrainFeature> entry in farm.terrainFeatures)
            {
                Vector2 tile = entry.Key;
                HoeDirt dirt = entry.Value as HoeDirt;
                Crop crop = dirt?.crop;
                if (crop != null && !crop.dead)
                    yield return new CropTileState(tile, crop, dirt.state, dirt.fertilizer);
            }
        }

        /// <summary>Get all tiles on the farm with a giant crop.</summary>
        /// <param name="farm">The farm to search.</param>
        private IEnumerable<Vector2> GetGiantCropTiles(Farm farm)
        {
            foreach (GiantCrop giantCrop in farm.resourceClumps.OfType<GiantCrop>())
            {
                yield return giantCrop.tile; // top left tile
                yield return giantCrop.tile + new Vector2(1, 0);
                yield return giantCrop.tile + new Vector2(0, 1);
                yield return giantCrop.tile + new Vector2(1, 1);
            }
        }
    }
}
