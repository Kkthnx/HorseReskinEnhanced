using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.Menus;
using Microsoft.Xna.Framework.Graphics;
using HorseReskinEnhanced.Config;
using HorseReskinEnhanced.Menus;
using HorseReskinEnhanced.Messages;

namespace HorseReskinEnhanced
{
    public class ModEntry : Mod
    {
        internal static IMonitor SMonitor;
        internal static IModHelper SHelper;
        internal static IManifest SModManifest;
        internal static bool IsEnabled = true;
        private static ModConfig Config;
        private static HorseSkinManager _skinManager;
        private static HorseStateManager _stateManager;
        public static readonly Dictionary<Guid, int> HorseSkinMap = new();
        public static readonly Dictionary<Guid, Horse> HorseNameMap = new();
        private static readonly Dictionary<int, Lazy<Texture2D>> SkinTextureMap = new();
        private static readonly Dictionary<int, string> SkinNameMap = new();

        internal static readonly string ReskinHorseMessageId = "HorseReskin";
        internal static readonly string ReloadHorseSpritesMessageId = "HorseSpriteReload";
        internal static readonly string SkinUpdateMessageId = "SkinUpdate";
        private readonly string MinHostVersion = "1.0.0";

        private static readonly Dictionary<GameLocation, HashSet<Horse>> LocationHorseCache = new();
        private static int LastDayForCache = -1;
        private static bool IsCacheDirty = true;

        public override void Entry(IModHelper helper)
        {
            SMonitor = Monitor;
            SHelper = helper;
            SModManifest = ModManifest;

            IModEvents events = helper.Events;
            events.GameLoop.GameLaunched += OnGameLaunched;
            events.GameLoop.SaveLoaded += OnSaveLoaded;
            events.GameLoop.DayStarted += OnDayStarted;
            events.GameLoop.UpdateTicked += OnUpdateTicked;
            events.Player.Warped += OnPlayerWarped;
            events.Input.ButtonPressed += OnButtonPressed;
            events.Multiplayer.ModMessageReceived += OnModMessageReceived;
            events.Multiplayer.PeerConnected += OnPeerConnected;
            events.Multiplayer.PeerDisconnected += OnPeerDisconnected;
            events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            events.GameLoop.Saving += OnSaving;
            events.GameLoop.Saved += OnSaved;
            events.Player.InventoryChanged += OnInventoryChanged;
            events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            events.GameLoop.TimeChanged += OnTimeChanged;
            events.GameLoop.DayEnding += OnDayEnding;
            events.GameLoop.SaveCreated += OnSaveCreated;
            events.World.NpcListChanged += OnNpcListChanged;
            events.World.LocationListChanged += OnLocationListChanged;

            SHelper.ConsoleCommands.Add("list_horses", "Lists all horses on your farm.", CommandHandler.OnCommandReceived);
            SHelper.ConsoleCommands.Add("reskin_horse", "Reskin a horse by name: [horse name] [skin id].", CommandHandler.OnCommandReceived);
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            Config = SHelper.ReadConfig<ModConfig>();
            _stateManager = new HorseStateManager(SMonitor, SHelper);
            _skinManager = new HorseSkinManager(SMonitor, SHelper, SModManifest, Config);
        }

        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            if (!Config.Enabled) return;

            // Clear existing maps
            HorseSkinMap.Clear();
            HorseNameMap.Clear();
            HorseNameMap.AddRange(GetHorsesDict());
            LoadAllSprites();

            if (Context.IsMainPlayer)
            {
                foreach (var horse in HorseNameMap.Values)
                {
                    GenerateHorseSkinMap(horse);
                }

                // If in multiplayer, sync with all players
                if (Context.IsMultiplayer)
                {
                    _skinManager.SyncWithPeers();
                }
            }

            if (Context.IsMainPlayer && Context.IsMultiplayer)
            {
                var message = new SkinUpdateMessage { SkinMap = new Dictionary<Guid, int>(HorseSkinMap) };
                SHelper.Multiplayer.SendMessage(message, SkinUpdateMessageId, modIDs: new[] { SModManifest.UniqueID });
            }
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            if (!Config.Enabled) return;
            LocationHorseCache.Clear();
            LastDayForCache = Game1.Date.TotalDays;

            var previousHorses = new HashSet<Guid>(HorseNameMap.Keys);
            HorseNameMap.Clear();
            HorseNameMap.AddRange(GetHorsesDict());

            // Check for new horses
            foreach (var horse in HorseNameMap.Values)
            {
                if (!previousHorses.Contains(horse.HorseId))
                {
                    OnNewHorseAdded(horse);
                }
                ReLoadHorseSprites(horse);
            }

            // Update any mounted horses at day start
            foreach (var farmer in Game1.getAllFarmers())
            {
                if (farmer.mount != null && IsNotATractor(farmer.mount))
                {
                    var horse = farmer.mount;
                    if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId))
                    {
                        horse.Manners = skinId;
                        ReLoadHorseSprites(horse);
                        UpdateMountedHorseTexture(horse);
                    }
                }
            }
        }

        private void OnNewHorseAdded(Horse horse)
        {
            // No auto-apply or notification logic needed anymore
        }

        private void OnPeerConnected(object sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer) return;

            // Send the current skin map to the newly connected player
            var message = new SkinUpdateMessage { SkinMap = new Dictionary<Guid, int>(HorseSkinMap) };
            SHelper.Multiplayer.SendMessage(message, SkinUpdateMessageId, modIDs: new[] { SModManifest.UniqueID }, playerIDs: new[] { e.Peer.PlayerID });

            // Also send individual reload messages for each horse to ensure proper synchronization
            foreach (var kvp in HorseSkinMap)
            {
                var reloadMessage = new HorseReskinMessage(kvp.Key, kvp.Value);
                SHelper.Multiplayer.SendMessage(reloadMessage, ReloadHorseSpritesMessageId, modIDs: new[] { SModManifest.UniqueID }, playerIDs: new[] { e.Peer.PlayerID });
            }

            // Log the synchronization
            SMonitor.Log($"Synchronized horse skins with player {e.Peer.PlayerID}", LogLevel.Debug);
        }

        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;

            // Update horse state
            _stateManager.UpdateHorseMap();

            // Handle mounting/dismounting events
            foreach (var farmer in Game1.getAllFarmers())
            {
                if (farmer.mount != null && IsNotATractor(farmer.mount))
                {
                    var horse = farmer.mount;
                    if (horse.Manners != HorseSkinMap.GetValueOrDefault(horse.HorseId))
                    {
                        _skinManager.HandleMounting(horse);
                    }
                }
            }

            // Rest of the update logic
            if (Game1.currentLocation != null)
            {
                foreach (var horse in _stateManager.GetHorsesInLocation(Game1.currentLocation))
                {
                    _skinManager.UpdateHorseSkin(horse, horse.Manners, false, false);
                }
            }
        }

        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || !Config.Enabled || !e.Button.IsActionButton() || !Game1.player.currentLocation.IsFarm || Game1.activeClickableMenu != null) return;

            var stable = IsPlayerInStable();
            if (stable != null)
            {
                if (SkinTextureMap.Count == 0)
                {
                    SMonitor.Log("Cannot open menu: no skins available", LogLevel.Warn);
                    Game1.addHUDMessage(new HUDMessage("No horse skins available. Check the assets folder.", HUDMessage.error_type));
                    return;
                }
                if (!Context.IsMainPlayer && stable.owner.Value != Game1.player.UniqueMultiplayerID)
                {
                    SMonitor.Log($"Player {Game1.player.Name} attempted to reskin horse at stable owned by {stable.owner.Value}", LogLevel.Warn);
                    Game1.addHUDMessage(new HUDMessage(
                        SHelper.Translation.Get("error.not-your-stable", new { defaultValue = "You can only reskin your own horse at your stable." }), 
                        HUDMessage.error_type
                    ));
                    return;
                }
                Game1.activeClickableMenu = new HorseReskinMenu(stable.HorseId, SkinTextureMap, SkinNameMap);
                Helper.Input.Suppress(e.Button);
            }
        }

        private void OnModMessageReceived(object sender, ModMessageReceivedEventArgs e)
        {
            if (!Config.Enabled) return;

            switch (e.Type)
            {
                case var type when type == ReloadHorseSpritesMessageId:
                    if (e.FromModID == SModManifest.UniqueID && e.ReadAs<HorseReskinMessage>() is HorseReskinMessage reskinMessage)
                    {
                        if (Context.IsMainPlayer)
                        {
                            // Host: validate and apply
                            var horse = GetHorseById(reskinMessage.HorseId);
                            if (horse != null && IsNotATractor(horse) && horse.getOwner()?.UniqueMultiplayerID == reskinMessage.RequestingPlayerId)
                            {
                                horse.Manners = reskinMessage.SkinId;
                                UpdateHorseSkinMap(reskinMessage.HorseId, reskinMessage.SkinId);
                                ReLoadHorseSprites(horse);
                                // Broadcast to all
                                SHelper.Multiplayer.SendMessage(reskinMessage, ReloadHorseSpritesMessageId, new[] { SModManifest.UniqueID });
                            }
                        }
                        else
                        {
                            // Farmhand: apply update from host
                            var horse = GetHorseById(reskinMessage.HorseId);
                            if (horse != null && IsNotATractor(horse))
                            {
                                horse.Manners = reskinMessage.SkinId;
                                UpdateHorseSkinMap(reskinMessage.HorseId, reskinMessage.SkinId);
                                ReLoadHorseSprites(horse);
                            }
                        }
                    }
                    break;

                case var type when type == SkinUpdateMessageId:
                    if (e.FromModID == SModManifest.UniqueID && e.ReadAs<SkinUpdateMessage>() is SkinUpdateMessage skinUpdateMessage)
                    {
                        // Update the skin map with the received data
                        foreach (var kvp in skinUpdateMessage.SkinMap)
                        {
                            HorseSkinMap[kvp.Key] = kvp.Value;
                            var horse = GetHorseById(kvp.Key);
                            if (horse != null && IsNotATractor(horse))
                            {
                                horse.Manners = kvp.Value;
                                ReLoadHorseSprites(horse);
                            }
                        }
                    }
                    break;
            }
        }

        private static void HandleHorseDismount(Horse horse)
        {
            if (horse == null || !IsNotATractor(horse)) return;

            // Update the horse's skin immediately
            if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId))
            {
                horse.Manners = skinId;
                ReLoadHorseSprites(horse);

                // In multiplayer, ensure all clients are updated
                if (Context.IsMultiplayer)
                {
                    var message = new HorseReskinMessage(horse.HorseId, skinId, Game1.player.UniqueMultiplayerID, false, false);
                    SHelper.Multiplayer.SendMessage(message, ReloadHorseSpritesMessageId, modIDs: new[] { SModManifest.UniqueID });
                }
            }
        }

        private void UpdateMountedHorseTexture(Horse horse)
        {
            if (horse == null || !IsNotATractor(horse)) return;

            // Update the horse's skin immediately
            if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId))
            {
                horse.Manners = skinId;
                ReLoadHorseSprites(horse);

                // In multiplayer, ensure all clients are updated
                if (Context.IsMultiplayer)
                {
                    var message = new HorseReskinMessage(horse.HorseId, skinId, Game1.player.UniqueMultiplayerID, false, false);
                    SHelper.Multiplayer.SendMessage(message, ReloadHorseSpritesMessageId, modIDs: new[] { SModManifest.UniqueID });
                }
            }
        }

        private void UpdateAllHorsesInLocation(GameLocation location)
        {
            if (location == null) return;

            try
            {
                // Get all horses that need updating (both mounted and unmounted)
                var horsesToUpdate = new HashSet<Horse>();
                // Add unmounted horses
                foreach (var character in location.characters)
                {
                    if (character is Horse horse && IsNotATractor(horse))
                    {
                        horsesToUpdate.Add(horse);
                    }
                }
                // Add mounted horses
                foreach (var farmer in location.farmers)
                {
                    if (farmer.mount != null && IsNotATractor(farmer.mount))
                    {
                        horsesToUpdate.Add(farmer.mount);
                    }
                }
                // Update all horses in one pass
                foreach (var horse in horsesToUpdate)
                {
                    if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId) && 
                        SkinTextureMap.TryGetValue(skinId, out var lazyTexture) && 
                        lazyTexture.Value != null)
                    {
                        horse.Manners = skinId;
                        horse.Sprite.spriteTexture = lazyTexture.Value;
                        horse.Sprite.UpdateSourceRect();
                    }
                }
                // Update location cache
                LocationHorseCache[location] = horsesToUpdate;
            }
            catch (Exception ex)
            {
                SMonitor.Log($"Error updating horses in location {location.Name}: {ex.Message}", LogLevel.Error);
            }
        }

        public static Dictionary<Guid, Horse> GetHorsesDict()
        {
            var horses = new Dictionary<Guid, Horse>();
            foreach (var player in Game1.getAllFarmers())
            {
                if (player.mount != null && !horses.ContainsKey(player.mount.HorseId))
                    horses.Add(player.mount.HorseId, player.mount);
            }
            var locations = Context.IsMainPlayer ? Utility.getAllCharacters() : SHelper.Multiplayer.GetActiveLocations().SelectMany(l => l.characters);
            foreach (var npc in locations.OfType<Horse>().Where(h => IsNotATractor(h) && !horses.ContainsKey(h.HorseId)))
                horses.Add(npc.HorseId, npc);
            return horses;
        }

        public static Horse GetHorseById(Guid horseId) => HorseNameMap.TryGetValue(horseId, out var horse) ? horse : null;

        public static List<Stable> GetHorseStables()
        {
            return Game1.getFarm().buildings
                .OfType<Stable>()
                .Where(s => Utility.findHorse(s.HorseId) is Horse horse && IsNotATractor(horse))
                .ToList();
        }

        public static bool IsNotATractor(Horse horse) => horse?.Name is null || !horse.Name.StartsWith("tractor/");

        private Stable IsPlayerInStable()
        {
            foreach (var stable in GetHorseStables())
            {
                var stableRect = new Rectangle(stable.tileX.Value, stable.tileY.Value, stable.tilesWide.Value, stable.tilesHigh.Value);
                if (stableRect.Contains((int)Game1.player.Tile.X, (int)Game1.player.Tile.Y))
                    return stable;
            }
            return null;
        }

        public void GenerateHorseSkinMap(Horse horse)
        {
            // No auto-apply or remember skin logic needed anymore
        }

        public static void ReLoadHorseSprites(Horse horse)
        {
            if (horse != null && 
                HorseSkinMap.TryGetValue(horse.HorseId, out int skinId) && 
                SkinTextureMap.TryGetValue(skinId, out var lazyTexture) && 
                lazyTexture.Value != null)
            {
                // Update the horse's sprite
                horse.Sprite.spriteTexture = lazyTexture.Value;
                horse.Sprite.UpdateSourceRect();
                
                // Force a sprite update by temporarily changing the sprite index
                int originalIndex = horse.Sprite.currentFrame;
                horse.Sprite.currentFrame = (horse.Sprite.currentFrame + 1) % 4;
                horse.Sprite.UpdateSourceRect();
                horse.Sprite.currentFrame = originalIndex;
                horse.Sprite.UpdateSourceRect();

                // If this horse is mounted, ensure the rider's mount reference is updated
                if (Game1.currentLocation != null)
                {
                    var rider = Game1.currentLocation.farmers.FirstOrDefault(f => f.mount == horse);
                    if (rider != null)
                    {
                        rider.mount.Sprite.spriteTexture = lazyTexture.Value;
                        rider.mount.Sprite.UpdateSourceRect();
                        rider.mount.Sprite.currentFrame = originalIndex;
                        rider.mount.Sprite.UpdateSourceRect();
                    }
                }
            }
        }

        public static void SendMultiplayerReloadSkinMessage(Guid horseId, int skinId)
        {
            if (Context.IsMainPlayer)
                SHelper.Multiplayer.SendMessage(new HorseReskinMessage(horseId, skinId), ReloadHorseSpritesMessageId, new[] { SModManifest.UniqueID });
        }

        public static void UpdateHorseSkinMap(Guid horseId, int skinId) => HorseSkinMap[horseId] = skinId;

        private static void LoadAllSprites()
        {
            SkinTextureMap.Clear();
            SkinNameMap.Clear();

            var assetPath = Path.Combine(SHelper.DirectoryPath, "assets");
            if (!Directory.Exists(assetPath))
            {
                SMonitor.Log($"Assets directory not found at {assetPath}", LogLevel.Error);
                return;
            }

            var files = Directory.GetFiles(assetPath, "*.png")
                               .OrderBy(f => Path.GetFileNameWithoutExtension(f))
                               .ToList();

            if (files.Count == 0)
            {
                SMonitor.Log("No PNG files found in assets folder", LogLevel.Warn);
                return;
            }

            SMonitor.Log($"Found {files.Count} PNG files in assets folder", LogLevel.Debug);

            int skinId = 1;
            int successCount = 0;
            var loadedTextures = new Dictionary<int, Texture2D>();

            // First pass: validate all files
            foreach (var file in files)
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    SMonitor.Log($"Validating skin {skinId} from file {fileName}", LogLevel.Debug);

                    if (!File.Exists(file))
                    {
                        SMonitor.Log($"File not found: {file}", LogLevel.Error);
                        continue;
                    }

                    // Try to load the texture to validate it
                    try
                    {
                        using (var stream = File.OpenRead(file))
                        {
                            var texture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                            if (texture == null)
                            {
                                SMonitor.Log($"Failed to load texture from file: {file}", LogLevel.Error);
                                continue;
                            }
                            loadedTextures[skinId] = texture;
                            texture.Dispose(); // Clean up test texture
                        }
                    }
                    catch (Exception ex)
                    {
                        SMonitor.Log($"Invalid texture file {file}: {ex.Message}", LogLevel.Error);
                        continue;
                    }

                    SkinNameMap[skinId] = fileName;
                    successCount++;
                    skinId++;
                }
                catch (Exception ex)
                {
                    SMonitor.Log($"Error processing file {file}: {ex.Message}", LogLevel.Error);
                }
            }

            // Second pass: create lazy-loaded textures
            foreach (var kvp in SkinNameMap)
            {
                var id = kvp.Key;
                var fileName = kvp.Value;
                SkinTextureMap[id] = new Lazy<Texture2D>(() =>
                {
                    try
                    {
                        var assetKey = $"assets/{fileName}";
                        var texture = SHelper.ModContent.Load<Texture2D>(assetKey);
                        if (texture != null)
                        {
                            SMonitor.Log($"Successfully loaded texture for skin {id}", LogLevel.Trace);
                            return texture;
                        }
                        SMonitor.Log($"Failed to load texture for skin {id}: texture is null", LogLevel.Error);
                        return null;
                    }
                    catch (Exception ex)
                    {
                        SMonitor.Log($"Failed to load texture for skin {id}: {ex.Message}", LogLevel.Error);
                        return null;
                    }
                }, true);
            }

            if (successCount == 0)
            {
                SMonitor.Log("No valid horse skins found in assets folder.", LogLevel.Warn);
            }
            else
            {
                SMonitor.Log($"Successfully loaded {successCount} horse skins", LogLevel.Info);
            }
        }

        private static bool ValidateSkinId(int skinId, long requestingPlayerId = 0)
        {
            if (!SkinTextureMap.ContainsKey(skinId))
            {
                SMonitor.Log($"Invalid skin ID: {skinId}", LogLevel.Error);
                return false;
            }
            return true;
        }

        public static void SaveHorseReskin(Guid horseId, int skinId, long requestingPlayerId = 0)
        {
            if (!Context.IsMainPlayer)
            {
                // Only host can update, farmhands must send request
                SHelper.Multiplayer.SendMessage(new HorseReskinMessage(horseId, skinId, Game1.player.UniqueMultiplayerID), ReloadHorseSpritesMessageId, new[] { SModManifest.UniqueID });
                return;
            }
            var horse = GetHorseById(horseId);
            if (horse == null || horse.getOwner()?.UniqueMultiplayerID != requestingPlayerId)
                return;
            horse.Manners = skinId;
            UpdateHorseSkinMap(horseId, skinId);
            ReLoadHorseSprites(horse);
            // Broadcast to all
            SHelper.Multiplayer.SendMessage(new HorseReskinMessage(horseId, skinId, requestingPlayerId), ReloadHorseSpritesMessageId, new[] { SModManifest.UniqueID });
        }

        public static Guid? GetHorseIdFromName(string horseName)
        {
            var horse = HorseNameMap.FirstOrDefault(h => h.Value.displayName == horseName).Key;
            if (horse == Guid.Empty)
                SMonitor.Log($"No horse named {horseName} found", LogLevel.Error);
            return horse == Guid.Empty ? null : horse;
        }

        private IEnumerable<Horse> GetHorsesIn(GameLocation location)
        {
            if (location == null) return Enumerable.Empty<Horse>();

            // Clear cache if it's a new day or cache is dirty
            if (Game1.Date.TotalDays != LastDayForCache || IsCacheDirty)
            {
                LocationHorseCache.Clear();
                LastDayForCache = Game1.Date.TotalDays;
                IsCacheDirty = false;
            }

            // Return cached horses if available and valid
            if (LocationHorseCache.TryGetValue(location, out var cachedHorses))
            {
                // Quick validation of cache
                bool cacheValid = true;
                foreach (var horse in cachedHorses)
                {
                    if (horse.currentLocation != location)
                    {
                        cacheValid = false;
                        break;
                    }
                }
                if (cacheValid)
                {
                    return cachedHorses;
                }
            }

            // Update cache with new horses
            var horses = new HashSet<Horse>();
            foreach (var character in location.characters)
            {
                if (character is Horse horse && IsNotATractor(horse))
                {
                    horses.Add(horse);
                }
            }
            LocationHorseCache[location] = horses;
            return horses;
        }

        private bool CheckHostCompatibility()
        {
            var host = SHelper.Multiplayer.GetConnectedPlayer(Game1.MasterPlayer.UniqueMultiplayerID);
            var hostMod = host?.GetMod(ModManifest.UniqueID);
            if (hostMod == null)
            {
                SMonitor.Log("Mod disabled: host does not have it installed.", LogLevel.Warn);
                return false;
            }
            if (hostMod.Version.IsOlderThan(MinHostVersion))
            {
                SMonitor.Log($"Mod disabled: host version {hostMod.Version} is older than minimum {MinHostVersion}.", LogLevel.Warn);
                return false;
            }
            if (!ModManifest.Version.Equals(hostMod.Version))
            {
                SMonitor.Log($"Mod disabled: version mismatch (host: {hostMod.Version}, you: {ModManifest.Version}).", LogLevel.Warn);
                return false;
            }
            return true;
        }

        private void OnPlayerWarped(object sender, WarpedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;

            // Handle mounted horse during warp
            if (e.Player.mount != null && IsNotATractor(e.Player.mount))
            {
                var horse = e.Player.mount;
                if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId))
                {
                    horse.Manners = skinId;
                    ReLoadHorseSprites(horse);
                    UpdateMountedHorseTexture(horse);
                }
            }

            // Update all horses in the new location
            if (e.NewLocation != null)
            {
                UpdateAllHorsesInLocation(e.NewLocation);
            }
        }

        private void OnPeerDisconnected(object sender, PeerDisconnectedEventArgs e)
        {
            if (!Context.IsMainPlayer) return;
            SMonitor.Log($"Player {e.Peer.PlayerID} disconnected. Re-synchronizing horse skins...", LogLevel.Debug);
            
            // Re-sync with remaining players
            var message = new SkinUpdateMessage { SkinMap = new Dictionary<Guid, int>(HorseSkinMap) };
            SHelper.Multiplayer.SendMessage(message, SkinUpdateMessageId, modIDs: new[] { SModManifest.UniqueID });
        }

        private static Color ParseHexColor(string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                return new Color(
                    r: Convert.ToByte(hex.Substring(0, 2), 16),
                    g: Convert.ToByte(hex.Substring(2, 2), 16),
                    b: Convert.ToByte(hex.Substring(4, 2), 16)
                );
            }
            catch
            {
                return Color.Gold; // Default fallback color
            }
        }

        private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
        {
            // Clear all maps when returning to title
            HorseSkinMap.Clear();
            HorseNameMap.Clear();
            LocationHorseCache.Clear();
            LastDayForCache = -1;
            _stateManager = new HorseStateManager(SMonitor, SHelper);
            _skinManager = new HorseSkinManager(SMonitor, SHelper, SModManifest, Config);
        }

        private void OnSaving(object sender, SavingEventArgs e)
        {
            if (!Context.IsMainPlayer) return;
            
            // Ensure all horse skins are saved before the game saves
            foreach (var horse in HorseNameMap.Values)
            {
                if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId))
                {
                    horse.Manners = skinId;
                }
            }
        }

        private void OnSaved(object sender, SavedEventArgs e)
        {
            if (!Context.IsMainPlayer) return;
            
            // After saving, ensure all players have the latest skin data
            if (Context.IsMultiplayer)
            {
                var message = new SkinUpdateMessage { SkinMap = new Dictionary<Guid, int>(HorseSkinMap) };
                SHelper.Multiplayer.SendMessage(message, SkinUpdateMessageId, modIDs: new[] { SModManifest.UniqueID });
            }
        }

        private void OnInventoryChanged(object sender, InventoryChangedEventArgs e)
        {
            if (!Context.IsWorldReady || !Config.Enabled) return;

            // Check if any horses were added or removed from the inventory
            var currentHorses = new HashSet<Guid>(HorseNameMap.Keys);
            var newHorses = GetHorsesDict();
            
            // Check for removed horses
            foreach (var horseId in currentHorses)
            {
                if (!newHorses.ContainsKey(horseId))
                {
                    HorseSkinMap.Remove(horseId);
                    HorseNameMap.Remove(horseId);
                }
            }
            
            // Check for new horses
            foreach (var kvp in newHorses)
            {
                if (!currentHorses.Contains(kvp.Key))
                {
                    HorseNameMap[kvp.Key] = kvp.Value;
                    if (Context.IsMainPlayer)
                    {
                        GenerateHorseSkinMap(kvp.Value);
                    }
                }
            }
        }

        private void OnOneSecondUpdateTicked(object sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;

            // Only log game state changes if they're significant
            if (Game1.eventUp || Game1.isWarping)
            {
                SMonitor.Log($"Game State - Event: {Game1.eventUp}, Warping: {Game1.isWarping}, Current Location: {Game1.currentLocation?.Name ?? "null"}, Time: {Game1.timeOfDay}, Day: {Game1.Date.DayOfMonth}, Season: {Game1.Date.Season}", LogLevel.Trace);
                
                // Log additional state information only if there are horses present
                if (Game1.currentLocation != null && Game1.currentLocation.characters.OfType<Horse>().Any())
                {
                    SMonitor.Log($"Location Details - Characters: {Game1.currentLocation.characters.Count}, Objects: {Game1.currentLocation.objects.Count()}, TerrainFeatures: {Game1.currentLocation.terrainFeatures.Count()}", LogLevel.Trace);
                    
                    // Ensure any horses maintain their skins
                    foreach (var character in Game1.currentLocation.characters)
                    {
                        if (character is Horse horse && IsNotATractor(horse))
                        {
                            SMonitor.Log($"Updating horse {horse.Name} (ID: {horse.HorseId}) in location {Game1.currentLocation.Name} - Current Skin: {horse.Manners}", LogLevel.Trace);
                            ReLoadHorseSprites(horse);
                        }
                    }
                }
            }
        }

        private void OnTimeChanged(object sender, TimeChangedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;

            // Only log time changes if they're significant (e.g., hour changes)
            if (e.NewTime % 100 == 0)
            {
                SMonitor.Log($"Time Changed - New Time: {e.NewTime}, Old Time: {e.OldTime}, Current Location: {Game1.currentLocation?.Name ?? "null"}, Event: {Game1.eventUp}, Warping: {Game1.isWarping}", LogLevel.Trace);
            }

            // When time changes (like during cutscenes or loading), ensure horse skins are preserved
            if (Game1.currentLocation != null)
            {
                UpdateAllHorsesInLocation(Game1.currentLocation);
            }
        }

        private void OnDayEnding(object sender, DayEndingEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;

            SMonitor.Log($"Day Ending - Current Day: {Game1.Date.DayOfMonth}, Season: {Game1.Date.Season}, Year: {Game1.Date.Year}", LogLevel.Debug);

            // Before the day ends (which can trigger cutscenes), ensure all horse skins are saved
            if (Context.IsMainPlayer)
            {
                foreach (var horse in HorseNameMap.Values)
                {
                    if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId))
                    {
                        SMonitor.Log($"Saving horse {horse.Name} (ID: {horse.HorseId}) with skin {skinId}", LogLevel.Debug);
                        horse.Manners = skinId;
                    }
                }
            }
        }

        private void OnSaveCreated(object sender, SaveCreatedEventArgs e)
        {
            if (!Config.Enabled) return;
            SMonitor.Log("New save created, initializing horse skin system...", LogLevel.Info);
            InitializeHorseSystem();
        }

        private void OnNpcListChanged(object sender, NpcListChangedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;
            
            _stateManager.MarkCacheDirty();
            
            // Check for any new horses
            foreach (var npc in e.Added)
            {
                if (npc is Horse horse && IsNotATractor(horse))
                {
                    _stateManager.UpdateHorseMap();
                    if (Context.IsMainPlayer)
                    {
                        GenerateHorseSkinMap(horse);
                    }
                }
            }

            // Check for removed horses
            foreach (var npc in e.Removed)
            {
                if (npc is Horse horse)
                {
                    HorseSkinMap.Remove(horse.HorseId);
                }
            }
        }

        private void OnLocationListChanged(object sender, LocationListChangedEventArgs e)
        {
            if (!Config.Enabled || !Context.IsWorldReady) return;
            
            _stateManager.MarkCacheDirty();
            
            // Check new locations for horses
            foreach (var location in e.Added)
            {
                foreach (var horse in _stateManager.GetHorsesInLocation(location))
                {
                    _skinManager.UpdateHorseSkin(horse, horse.Manners, false, false);
                }
            }
        }

        private void InitializeHorseSystem()
        {
            HorseSkinMap.Clear();
            HorseNameMap.Clear();
            LocationHorseCache.Clear();
            LastDayForCache = -1;
            LoadAllSprites();
        }

        private static void PersistHorseSkins(GameLocation location)
        {
            if (location == null) return;

            try
            {
                // Update all horses in the location
                var horses = location.characters.OfType<Horse>().Where(IsNotATractor).ToList();
                foreach (var horse in horses)
                {
                    if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId))
                    {
                        horse.Manners = skinId;
                        ReLoadHorseSprites(horse);
                    }
                }

                // Update any mounted horses
                var mountedHorses = location.farmers
                    .Where(f => f.mount != null && IsNotATractor(f.mount))
                    .Select(f => f.mount)
                    .Distinct()
                    .ToList();

                foreach (var horse in mountedHorses)
                {
                    if (HorseSkinMap.TryGetValue(horse.HorseId, out int skinId))
                    {
                        horse.Manners = skinId;
                        ReLoadHorseSprites(horse);
                    }
                }
            }
            catch (Exception ex)
            {
                SMonitor.Log($"Error persisting horse skins in location {location.Name}: {ex.Message}", LogLevel.Error);
            }
        }
    }

    public static class DictionaryExtensions
    {
        public static void AddRange<TKey, TValue>(this Dictionary<TKey, TValue> dict, IDictionary<TKey, TValue> source)
        {
            foreach (var kvp in source) dict[kvp.Key] = kvp.Value;
        }
    }

    public class HorseSkinManager
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        private readonly IManifest _manifest;
        private readonly ModConfig _config;
        private readonly Dictionary<Guid, int> _horseSkinMap = new();
        private readonly Dictionary<Guid, Horse> _horseNameMap = new();
        private readonly Dictionary<int, Lazy<Texture2D>> _skinTextureMap = new();
        private readonly Dictionary<int, string> _skinNameMap = new();
        private readonly Dictionary<GameLocation, HashSet<Horse>> _locationHorseCache = new();

        private const string ReloadHorseSpritesMessageId = "HorseSpriteReload";
        private const string SkinUpdateMessageId = "SkinUpdate";

        public HorseSkinManager(IMonitor monitor, IModHelper helper, IManifest manifest, ModConfig config)
        {
            _monitor = monitor;
            _helper = helper;
            _manifest = manifest;
            _config = config;
        }

        public void UpdateHorseSkin(Horse horse, int skinId, bool notify = true, bool playSound = true)
        {
            if (horse == null || !IsNotATractor(horse)) return;

            _horseSkinMap[horse.HorseId] = skinId;
            horse.Manners = skinId;
            ReLoadHorseSprites(horse);

            if (Context.IsMultiplayer)
            {
                var message = new HorseReskinMessage(horse.HorseId, skinId, Game1.player.UniqueMultiplayerID, notify, playSound);
                _helper.Multiplayer.SendMessage(message, ReloadHorseSpritesMessageId, modIDs: new[] { _manifest.UniqueID });
            }
        }

        public void HandleMounting(Horse horse)
        {
            if (horse == null || !IsNotATractor(horse)) return;

            if (_horseSkinMap.TryGetValue(horse.HorseId, out int skinId))
            {
                UpdateHorseSkin(horse, skinId, false, false);
            }
        }

        public void HandleDismounting(Horse horse)
        {
            if (horse == null || !IsNotATractor(horse)) return;

            if (_horseSkinMap.TryGetValue(horse.HorseId, out int skinId))
            {
                UpdateHorseSkin(horse, skinId, false, false);
            }
        }

        public void UpdateLocationHorses(GameLocation location)
        {
            if (location == null) return;

            try
            {
                var horsesToUpdate = new HashSet<Horse>();
                
                // Add unmounted horses
                foreach (var character in location.characters)
                {
                    if (character is Horse horse && IsNotATractor(horse))
                    {
                        horsesToUpdate.Add(horse);
                    }
                }

                // Add mounted horses
                foreach (var farmer in location.farmers)
                {
                    if (farmer.mount != null && IsNotATractor(farmer.mount))
                    {
                        horsesToUpdate.Add(farmer.mount);
                    }
                }

                foreach (var horse in horsesToUpdate)
                {
                    if (_horseSkinMap.TryGetValue(horse.HorseId, out int skinId))
                    {
                        UpdateHorseSkin(horse, skinId, false, false);
                    }
                }

                _locationHorseCache[location] = horsesToUpdate;
            }
            catch (Exception ex)
            {
                _monitor.Log($"Error updating horses in location {location.Name}: {ex.Message}", LogLevel.Error);
            }
        }

        public void SyncWithPeers()
        {
            if (!Context.IsMultiplayer) return;

            var message = new SkinUpdateMessage { SkinMap = new Dictionary<Guid, int>(_horseSkinMap) };
            _helper.Multiplayer.SendMessage(message, SkinUpdateMessageId, modIDs: new[] { _manifest.UniqueID });
        }

        public void SyncWithNewPeer(long peerId)
        {
            if (!Context.IsMultiplayer) return;

            var message = new SkinUpdateMessage { SkinMap = new Dictionary<Guid, int>(_horseSkinMap) };
            _helper.Multiplayer.SendMessage(message, SkinUpdateMessageId, modIDs: new[] { _manifest.UniqueID }, playerIDs: new[] { peerId });

            foreach (var kvp in _horseSkinMap)
            {
                var reloadMessage = new HorseReskinMessage(kvp.Key, kvp.Value);
                _helper.Multiplayer.SendMessage(reloadMessage, ReloadHorseSpritesMessageId, modIDs: new[] { _manifest.UniqueID }, playerIDs: new[] { peerId });
            }
        }

        public void ProcessSkinUpdateMessage(SkinUpdateMessage message)
        {
            foreach (var kvp in message.SkinMap)
            {
                _horseSkinMap[kvp.Key] = kvp.Value;
                var horse = GetHorseById(kvp.Key);
                if (horse != null && IsNotATractor(horse))
                {
                    UpdateHorseSkin(horse, kvp.Value, false, false);
                }
            }
        }

        public void ProcessReskinMessage(HorseReskinMessage message)
        {
            var horse = GetHorseById(message.HorseId);
            if (horse != null && IsNotATractor(horse))
            {
                UpdateHorseSkin(horse, message.SkinId, message.ShowNotification, message.PlaySound);
            }
        }

        private Horse GetHorseById(Guid horseId) => _horseNameMap.TryGetValue(horseId, out var horse) ? horse : null;

        private static bool IsNotATractor(Horse horse) => horse?.Name is null || !horse.Name.StartsWith("tractor/");

        private void ReLoadHorseSprites(Horse horse)
        {
            if (horse == null || 
                !_horseSkinMap.TryGetValue(horse.HorseId, out int skinId) || 
                !_skinTextureMap.TryGetValue(skinId, out var lazyTexture) || 
                lazyTexture.Value == null) return;

            horse.Sprite.spriteTexture = lazyTexture.Value;
            horse.Sprite.UpdateSourceRect();
            
            // Force a sprite update
            int originalIndex = horse.Sprite.currentFrame;
            horse.Sprite.currentFrame = (horse.Sprite.currentFrame + 1) % 4;
            horse.Sprite.UpdateSourceRect();
            horse.Sprite.currentFrame = originalIndex;
            horse.Sprite.UpdateSourceRect();

            // Update rider's mount reference if mounted
            if (Game1.currentLocation != null)
            {
                var rider = Game1.currentLocation.farmers.FirstOrDefault(f => f.mount == horse);
                if (rider != null)
                {
                    rider.mount.Sprite.spriteTexture = lazyTexture.Value;
                    rider.mount.Sprite.UpdateSourceRect();
                    rider.mount.Sprite.currentFrame = originalIndex;
                    rider.mount.Sprite.UpdateSourceRect();
                }
            }
        }
    }

    public class HorseStateManager
    {
        private readonly IMonitor _monitor;
        private readonly IModHelper _helper;
        private readonly Dictionary<Guid, Horse> _horseMap = new();
        private readonly Dictionary<GameLocation, HashSet<Horse>> _locationCache = new();
        private int _lastDayForCache = -1;
        private bool _isCacheDirty = true;

        public HorseStateManager(IMonitor monitor, IModHelper helper)
        {
            _monitor = monitor;
            _helper = helper;
        }

        public void UpdateHorseMap()
        {
            var newHorses = GetHorsesDict();
            var currentHorses = new HashSet<Guid>(_horseMap.Keys);

            // Remove horses that no longer exist
            foreach (var horseId in currentHorses)
            {
                if (!newHorses.ContainsKey(horseId))
                {
                    _horseMap.Remove(horseId);
                }
            }

            // Add new horses
            foreach (var kvp in newHorses)
            {
                if (!currentHorses.Contains(kvp.Key))
                {
                    _horseMap[kvp.Key] = kvp.Value;
                }
            }
        }

        public Horse GetHorseById(Guid horseId) => _horseMap.TryGetValue(horseId, out var horse) ? horse : null;

        public IEnumerable<Horse> GetHorsesInLocation(GameLocation location)
        {
            if (location == null) return Enumerable.Empty<Horse>();

            // Clear cache if it's a new day or cache is dirty
            if (Game1.Date.TotalDays != _lastDayForCache || _isCacheDirty)
            {
                _locationCache.Clear();
                _lastDayForCache = Game1.Date.TotalDays;
                _isCacheDirty = false;
            }

            // Return cached horses if available and valid
            if (_locationCache.TryGetValue(location, out var cachedHorses))
            {
                if (cachedHorses.All(h => h.currentLocation == location))
                {
                    return cachedHorses;
                }
            }

            // Update cache with new horses
            var horses = new HashSet<Horse>();
            
            // Add unmounted horses
            foreach (var character in location.characters)
            {
                if (character is Horse horse && IsNotATractor(horse))
                {
                    horses.Add(horse);
                }
            }

            // Add mounted horses
            foreach (var farmer in location.farmers)
            {
                if (farmer.mount != null && IsNotATractor(farmer.mount))
                {
                    horses.Add(farmer.mount);
                }
            }

            _locationCache[location] = horses;
            return horses;
        }

        public void MarkCacheDirty() => _isCacheDirty = true;

        public void ClearCache()
        {
            _horseMap.Clear();
            _locationCache.Clear();
            _lastDayForCache = -1;
            _isCacheDirty = true;
        }

        private Dictionary<Guid, Horse> GetHorsesDict()
        {
            var horses = new Dictionary<Guid, Horse>();
            
            // Get mounted horses
            foreach (var player in Game1.getAllFarmers())
            {
                if (player.mount != null && !horses.ContainsKey(player.mount.HorseId))
                {
                    horses.Add(player.mount.HorseId, player.mount);
                }
            }

            // Get unmounted horses
            var locations = Context.IsMainPlayer 
                ? Utility.getAllCharacters() 
                : _helper.Multiplayer.GetActiveLocations().SelectMany(l => l.characters);

            foreach (var npc in locations.OfType<Horse>().Where(h => IsNotATractor(h) && !horses.ContainsKey(h.HorseId)))
            {
                horses.Add(npc.HorseId, npc);
            }

            return horses;
        }

        private static bool IsNotATractor(Horse horse) => horse?.Name is null || !horse.Name.StartsWith("tractor/");
    }
}