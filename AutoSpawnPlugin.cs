using Rocket.API;
using Rocket.Unturned.Player;
using Rocket.Unturned.Chat;
using SDG.Unturned;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Rocket.Core.Plugins;
using HarmonyLib;
using System.Linq;
using Steamworks;
using System;
using Rocket.Unturned.Events;

namespace StrefaOdrodzenia
{
    public class AutoSpawnPlugin : RocketPlugin<AutoSpawnPlugin.PluginConfiguration>
    {
        public static AutoSpawnPlugin Instance;
        public Dictionary<CSteamID, List<Vector3>> Points = new Dictionary<CSteamID, List<Vector3>>();
        public Dictionary<CSteamID, TrappedPlayerData> TrappedPlayers = new Dictionary<CSteamID, TrappedPlayerData>();
        private Harmony harmony;

        public class TrappedPlayerDataConfig
        {
            public string SteamID;
            public double RemainingTimeSeconds;
            public string ZoneName;
        }

        public class PluginConfiguration : IRocketPluginConfiguration
        {
            public int DefaultTrapTimeSeconds = 300;
            public string NotificationPermission = "strefa.notification";
            public List<ZoneConfiguration> Zones = new List<ZoneConfiguration>();
            public List<TrappedPlayerDataConfig> SavedTrappedPlayers = new List<TrappedPlayerDataConfig>();

            public void LoadDefaults()
            {
                DefaultTrapTimeSeconds = 300;
                NotificationPermission = "strefa.notification";
                Zones = new List<ZoneConfiguration>();
                SavedTrappedPlayers = new List<TrappedPlayerDataConfig>();
            }
        }

        public class ZoneConfiguration
        {
            public string ZoneName = "Strefa Odrodzenia";
            public int TrapTimeSeconds = 300;
            public float MinX;
            public float MinZ;
            public float MaxX;
            public float MaxZ;
            public float MinY;
            public float MaxY;
            public SimpleVector3 Center;
            public string RequiredPermission = "";
            public string Priority = "n"; // Zmiana z Bypass na Priority
        }


        public class TrappedPlayerData
        {
            public Coroutine Coroutine;
            public ZoneConfiguration Zone;
            public UnturnedPlayer Player;
            public double EndTimestamp;
            public ushort EffectId = 8490;
        }

        public class SimpleVector3
        {
            public float X;
            public float Y;
            public float Z;

            public SimpleVector3() { }
            public SimpleVector3(float x, float y, float z)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        protected override void Load()
        {
            Instance = this;
            harmony = new Harmony("com.strefaodrodzenia.plugin");
            harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());

            try
            {
                Rocket.Core.Logging.Logger.Log("Harmony patches applied!");
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogError($"Błąd Harmony: {e}");
            }

            if (Configuration.Instance.Zones == null)
                Configuration.Instance.Zones = new List<ZoneConfiguration>();

            foreach (var savedData in Configuration.Instance.SavedTrappedPlayers.ToList())
            {
                try
                {
                    if (!ulong.TryParse(savedData.SteamID, out ulong steamId) || savedData.RemainingTimeSeconds <= 0)
                    {
                        Configuration.Instance.SavedTrappedPlayers.Remove(savedData);
                        continue;
                    }
                }
                catch (Exception e)
                {
                    Rocket.Core.Logging.Logger.LogError($"Błąd ładowania: {e}");
                }
            }
            Configuration.Save();

            UnturnedPlayerEvents.OnPlayerDeath += OnPlayerDeath;
            UnturnedPlayerEvents.OnPlayerRevive += OnPlayerRevive;
            Provider.onEnemyConnected += OnEnemyConnected;
            PlayerCrafting.OnCraftBlueprintRequestedV2 += OnCraftBlueprintRequestedV2;

            Rocket.Core.Logging.Logger.Log("## Plugin załadowany!");
        }

        private void OnCraftBlueprintRequestedV2(PlayerCrafting crafting, ref Blueprint blueprint, ref bool shouldAllow)
        {
            try
            {
                UnturnedPlayer player = UnturnedPlayer.FromPlayer(crafting.player);
                if (player == null) return;

                if (IsPlayerTrapped(player))
                {
                    shouldAllow = false;
                    UnturnedChat.Say(player, "Nie możesz craftować w strefie odrodzenia!", Color.red);
                }
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogError($"Błąd w OnCraftingRequested: {e}");
            }
        }        

        protected override void Unload()
        {
            foreach (var data in TrappedPlayers.Values)
            {
                if (data.Coroutine != null)
                    StopCoroutine(data.Coroutine);

                if (data.Player?.Player != null)
                {
                    EffectManager.askEffectClearByID(8490, data.Player.Player.channel.owner.transportConnection);
                }
            }

            harmony?.UnpatchAll();
            TrappedPlayers.Clear();

            UnturnedPlayerEvents.OnPlayerDeath -= OnPlayerDeath;
            UnturnedPlayerEvents.OnPlayerRevive -= OnPlayerRevive;
            Provider.onEnemyConnected -= OnEnemyConnected;
            PlayerCrafting.OnCraftBlueprintRequestedV2 += OnCraftBlueprintRequestedV2;

            Rocket.Core.Logging.Logger.Log("## Plugin wyłączony!");
        }

        private void OnCraftBlueprintRequestedV2(PlayerCrafting crafting, ref ushort itemID, ref byte blueprintIndex, ref bool shouldAllow)
        {
            try
            {
                UnturnedPlayer player = UnturnedPlayer.FromPlayer(crafting.player);
                if (player == null) return;

                if (IsPlayerTrapped(player))
                {
                    shouldAllow = false;
                    UnturnedChat.Say(player, "Nie możesz craftować w strefie odrodzenia!", Color.red);
                }
            }
            catch (Exception e)
            {
                Rocket.Core.Logging.Logger.LogError($"Błąd w OnCraftingRequested: {e}");
            }
        }

        private void OnEnemyConnected(SteamPlayer steamPlayer)
        {
            UnturnedPlayer player = UnturnedPlayer.FromSteamPlayer(steamPlayer);
            if (player == null) return;

            var savedData = Configuration.Instance.SavedTrappedPlayers
                .FirstOrDefault(x => x.SteamID == player.CSteamID.ToString());

            if (savedData == null) return;

            double endTimestamp = GetCurrentTimestamp() + savedData.RemainingTimeSeconds;
            var zone = Configuration.Instance.Zones.FirstOrDefault(z => z.ZoneName == savedData.ZoneName);

            if (zone != null)
            {
                TrapPlayer(player, zone, (float)savedData.RemainingTimeSeconds);
            }

            Configuration.Instance.SavedTrappedPlayers.Remove(savedData);
            Configuration.Save();
        }

        private void OnPlayerDeath(UnturnedPlayer player, EDeathCause cause, ELimb limb, CSteamID murderer)
        {
            if (Configuration.Instance.Zones.Count == 0) return;

            if (player.Player != null)
                player.Player.life.askRespawn(player.CSteamID, true);

            if (!TrappedPlayers.ContainsKey(player.CSteamID))
                TrappedPlayers[player.CSteamID] = new TrappedPlayerData();

            TrappedPlayers[player.CSteamID].EndTimestamp = GetCurrentTimestamp() + 10;
        }

        private void OnPlayerRevive(UnturnedPlayer player, Vector3 position, byte angle)
        {
            if (!TrappedPlayers.TryGetValue(player.CSteamID, out var data)) return;

            ZoneConfiguration zone = FindNearestZone(position, player);
            if (zone != null && player.Player != null)
            {
                Vector3 spawnPos = CalculateSpawnPosition(zone);
                player.Player.teleportToLocation(spawnPos, player.Rotation);

                // Zmiana z Bypass na Priority
                if (zone.Priority != "t")
                    TrapPlayer(player, zone);
                else
                    UnturnedChat.Say(player, "Strefa priorytetowa!", Color.green);
            }
        }

        private bool PlayerHasPermissionOrInGroup(UnturnedPlayer player, string permissionOrGroup)
        {
            // Sprawdź bezpośrednią permisję
            if (player.HasPermission(permissionOrGroup))
                return true;

            // Sprawdź grupy gracza przez R.Permissions
            var playerGroups = Rocket.Core.R.Permissions.GetGroups(player, false);
            return playerGroups.Any(g => g.Id.Equals(permissionOrGroup, StringComparison.OrdinalIgnoreCase));
        }

        private ZoneConfiguration FindNearestZone(Vector3 position, UnturnedPlayer player)
        {
            // 1. Najpierw strefy z PRIORYTETEM
            foreach (var zone in Configuration.Instance.Zones)
            {
                if (zone.Priority == "t" &&
                    (string.IsNullOrEmpty(zone.RequiredPermission) ||
                     PlayerHasPermissionOrInGroup(player, zone.RequiredPermission)))
                {
                    return zone;
                }
            }

            // 2. Jeśli nie ma priorytetowych, szukaj najbliższej dostępnej
            ZoneConfiguration nearestZone = null;
            float nearestDistance = float.MaxValue;

            foreach (var zone in Configuration.Instance.Zones)
            {
                if (!string.IsNullOrEmpty(zone.RequiredPermission) &&
                    !PlayerHasPermissionOrInGroup(player, zone.RequiredPermission))
                    continue;

                Vector3 center = new Vector3(zone.Center.X, zone.Center.Y, zone.Center.Z);
                float distance = Vector3.Distance(position, center);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestZone = zone;
                }
            }

            return nearestZone;
        }

        private Vector3 CalculateSpawnPosition(ZoneConfiguration zone)
        {
            for (int i = 0; i < 20; i++)
            {
                float x = UnityEngine.Random.Range(zone.MinX + 2f, zone.MaxX - 2f);
                float z = UnityEngine.Random.Range(zone.MinZ + 2f, zone.MaxZ - 2f);
                Vector3 origin = new Vector3(x, zone.MaxY + 50f, z);

                if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, Mathf.Infinity, RayMasks.BLOCK_COLLISION))
                {
                    if (hit.point.y >= zone.MinY && hit.point.y <= zone.MaxY)
                        return new Vector3(hit.point.x, hit.point.y + 1f, hit.point.z);
                }
            }
            return new Vector3(zone.Center.X, zone.Center.Y, zone.Center.Z);
        }

        public void TrapPlayer(UnturnedPlayer player, ZoneConfiguration zone, float remainingTime = -1)
        {
            if (remainingTime == -1)
                remainingTime = zone.TrapTimeSeconds;

            if (TrappedPlayers.ContainsKey(player.CSteamID))
                ForceReleasePlayer(player, false);

            double endTimestamp = GetCurrentTimestamp() + remainingTime;

            if (!string.IsNullOrEmpty(zone.RequiredPermission) &&
        !player.HasPermission(zone.RequiredPermission))
            {
                UnturnedChat.Say(player, "Nie masz dostępu do tej strefy!", Color.red);
                return;
            }

            TrappedPlayers[player.CSteamID] = new TrappedPlayerData
            {
                Zone = zone,
                Player = player,
                EndTimestamp = endTimestamp,
                Coroutine = StartCoroutine(TrappedLoop(player, zone, endTimestamp))
            };

            Configuration.Instance.SavedTrappedPlayers.Add(new TrappedPlayerDataConfig
            {
                SteamID = player.CSteamID.ToString(),
                RemainingTimeSeconds = remainingTime,
                ZoneName = zone.ZoneName
            });
            Configuration.Save();

            if (player.Player != null)
            {
                player.Player.inventory.isStoring = true;
                player.Player.interact.enabled = false;
                EffectManager.sendUIEffect(8490, 8490, player.Player.channel.owner.transportConnection, true);
                EffectManager.sendUIEffectText(8490, player.Player.channel.owner.transportConnection, true, "ileczasu", $"Czas kary: {FormatTime(remainingTime)}");
            }

            if (!string.IsNullOrEmpty(Configuration.Instance.NotificationPermission))
            {
                foreach (SteamPlayer client in Provider.clients)
                {
                    UnturnedPlayer target = UnturnedPlayer.FromSteamPlayer(client);
                    if (target != null && target.HasPermission(Configuration.Instance.NotificationPermission))
                    {
                        UnturnedChat.Say(target,
                            $"[{player.CharacterName}] {player.DisplayName} został uwięziony w {zone.ZoneName} na {FormatTime(remainingTime)}!",
                            Color.yellow);
                    }
                }
            }
        }

        private IEnumerator TrappedLoop(UnturnedPlayer player, ZoneConfiguration zone, double endTimestamp)
        {
            while (GetCurrentTimestamp() < endTimestamp)
            {
                if (player?.Player == null || player.Player.transform == null)
                {
                    TrappedPlayers.Remove(player.CSteamID);
                    yield break;
                }

                if (TrappedPlayers.TryGetValue(player.CSteamID, out var data))
                {
                    var savedData = Configuration.Instance.SavedTrappedPlayers
                        .FirstOrDefault(x => x.SteamID == player.CSteamID.ToString());

                    if (savedData != null)
                    {
                        savedData.RemainingTimeSeconds = endTimestamp - GetCurrentTimestamp();
                        Configuration.Save();
                    }
                }

                float remaining = (float)(endTimestamp - GetCurrentTimestamp());
                EffectManager.sendUIEffectText(8490, player.Player.channel.owner.transportConnection, true, "ileczasu", $"Czas: {FormatTime(remaining)}");
                CheckPlayerPosition(player, zone);

                yield return new WaitForSeconds(0.5f);
            }

            ForceReleasePlayer(player);
        }

        private void CheckPlayerPosition(UnturnedPlayer player, ZoneConfiguration zone)
        {
            if (player?.Player == null || player.Player.transform == null) return;

            Vector3 pos = player.Position;
            bool isInside =
                pos.x >= zone.MinX - 1f && pos.x <= zone.MaxX + 1f &&
                pos.z >= zone.MinZ - 1f && pos.z <= zone.MaxZ + 1f &&
                pos.y >= zone.MinY - 2f && pos.y <= zone.MaxY + 2f;

            if (!isInside)
            {
                Vector3 newPos = CalculateSpawnPosition(zone);
                player.Player.teleportToLocation(newPos, player.Rotation);
            }
        }

        public void ForceReleasePlayer(UnturnedPlayer player, bool sendMessage = true, bool isAdminRelease = false)
        {
            if (!TrappedPlayers.ContainsKey(player.CSteamID)) return;

            var data = TrappedPlayers[player.CSteamID];
            if (data.Coroutine != null)
                StopCoroutine(data.Coroutine);

            if (player.Player != null)
            {
                EffectManager.askEffectClearByID(8490, player.Player.channel.owner.transportConnection);
                player.Player.inventory.isStoring = false;
                player.Player.interact.enabled = true;
            }

            var savedData = Configuration.Instance.SavedTrappedPlayers.FirstOrDefault(x => x.SteamID == player.CSteamID.ToString());
            if (savedData != null)
            {
                Configuration.Instance.SavedTrappedPlayers.Remove(savedData);
                Configuration.Save();
            }

            TrappedPlayers.Remove(player.CSteamID);

            if (sendMessage && player.Player != null)
            {
                string msg = isAdminRelease ?
                    "Zostałeś zwolniony przez administratora!" :
                    "Czas kary minął! Jesteś wolny!";
                UnturnedChat.Say(player, msg, Color.green);
            }
        }

        public string FormatTime(float seconds)
        {
            TimeSpan ts = TimeSpan.FromSeconds(seconds);
            return $"{ts.Minutes:00}:{ts.Seconds:00}";
        }

        private double GetCurrentTimestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public bool IsPlayerTrapped(UnturnedPlayer player)
        {
            return TrappedPlayers.ContainsKey(player.CSteamID) && !player.Player.life.isDead;
        }
    }
}