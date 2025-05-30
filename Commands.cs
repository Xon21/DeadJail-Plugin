using Rocket.API;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using System.Collections.Generic;
using UnityEngine;
using SDG.Unturned;
using Steamworks;

namespace StrefaOdrodzenia
{
    public class CommandStrefaS : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "strefas";
        public string Help => "Tworzenie punktow strefy odrodzenia";
        public string Syntax => "<nazwa_strefy>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>() { "strefa.setpoint" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;
            string zoneName = command.Length > 0 ? command[0] : "default";

            if (!AutoSpawnPlugin.Instance.Points.ContainsKey(player.CSteamID))
                AutoSpawnPlugin.Instance.Points[player.CSteamID] = new List<Vector3>();

            AutoSpawnPlugin.Instance.Points[player.CSteamID].Add(player.Position);
            UnturnedChat.Say(player, $"Dodano punkt {AutoSpawnPlugin.Instance.Points[player.CSteamID].Count} do strefy {zoneName}", Color.green);
        }
    }

    public class CommandStrefaM : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "strefam";
        public string Help => "Tworzenie strefy odrodzenia z punktow";
        public string Syntax => "<nazwa> <czas> <permisja> <priority:t/n>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>() { "strefa.create" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;

            if (!AutoSpawnPlugin.Instance.Points.ContainsKey(player.CSteamID) ||
                AutoSpawnPlugin.Instance.Points[player.CSteamID].Count < 4)
            {
                UnturnedChat.Say(player, "Musisz ustawic minimum 4 punkty uzywajac /strefas", Color.red);
                return;
            }

            string zoneName = command.Length > 0 ? command[0] : "default";
            int trapTime = AutoSpawnPlugin.Instance.Configuration.Instance.DefaultTrapTimeSeconds;
            if (command.Length > 1 && int.TryParse(command[1], out int parsedTime))
                trapTime = parsedTime;

            string permission = command.Length > 2 ? command[2] : "";
            string priority = command.Length > 3 ? command[3].ToLower() : "n";

            if (priority != "t" && priority != "n")
            {
                UnturnedChat.Say(player, "Priority musi byc 't' lub 'n'!", Color.red);
                return;
            }

            // Sprawdź czy już istnieje strefa z priorytetem dla tej permisji
            if (priority == "t" &&
                AutoSpawnPlugin.Instance.Configuration.Instance.Zones.Exists(z =>
                    z.Priority == "t" && z.RequiredPermission == permission))
            {
                UnturnedChat.Say(player, "Już istnieje strefa z priorytetem dla tej permisji!", Color.red);
                return;
            }

            List<Vector3> points = AutoSpawnPlugin.Instance.Points[player.CSteamID];
            float minX = points[0].x;
            float maxX = points[0].x;
            float minZ = points[0].z;
            float maxZ = points[0].z;
            float minY = points[0].y;
            float maxY = points[0].y;

            for (int i = 1; i < points.Count; i++)
            {
                minX = Mathf.Min(minX, points[i].x);
                maxX = Mathf.Max(maxX, points[i].x);
                minZ = Mathf.Min(minZ, points[i].z);
                maxZ = Mathf.Max(maxZ, points[i].z);
                minY = Mathf.Min(minY, points[i].y);
                maxY = Mathf.Max(maxY, points[i].y);
            }

            minY -= 2f;
            maxY += 2f;
            Vector3 center = new Vector3((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);

            AutoSpawnPlugin.ZoneConfiguration zone = new AutoSpawnPlugin.ZoneConfiguration
            {
                ZoneName = zoneName,
                MinX = minX,
                MinZ = minZ,
                MaxX = maxX,
                MaxZ = maxZ,
                MinY = minY,
                MaxY = maxY,
                Center = new AutoSpawnPlugin.SimpleVector3(center.x, center.y, center.z),
                TrapTimeSeconds = trapTime,
                RequiredPermission = permission,
                Priority = priority // Używamy Priority zamiast Bypass
            };

            AutoSpawnPlugin.Instance.Configuration.Instance.Zones.Add(zone);
            AutoSpawnPlugin.Instance.Configuration.Save();
            AutoSpawnPlugin.Instance.Points.Remove(player.CSteamID);

            UnturnedChat.Say(player,
                $"Stworzono strefe {zoneName}! Czas: {AutoSpawnPlugin.Instance.FormatTime(trapTime)}, " +
                $"Priority: {(priority == "t" ? "TAK" : "NIE")}",
                Color.green);
        }
    }

public class CommandNFZ : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "nfz";
        public string Help => "Zwolnij gracza ze strefy odrodzenia";
        public string Syntax => "<gracz>";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>() { "strefa.release" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer admin = (UnturnedPlayer)caller;

            if (command.Length < 1)
            {
                UnturnedChat.Say(admin, $"Poprawne uzycie: /{Name} {Syntax}", Color.red);
                return;
            }

            UnturnedPlayer target = UnturnedPlayer.FromName(command[0]);
            if (target == null)
            {
                UnturnedChat.Say(admin, "Nie znaleziono gracza!", Color.red);
                return;
            }

            if (!AutoSpawnPlugin.Instance.TrappedPlayers.ContainsKey(target.CSteamID))
            {
                UnturnedChat.Say(admin, $"{target.DisplayName} nie jest w strefie!", Color.yellow);
                return;
            }

            AutoSpawnPlugin.Instance.ForceReleasePlayer(target, true, true); // Poprawne wywołanie z 3 argumentami
            EffectManager.askEffectClearByID(8490, target.Player.channel.owner.transportConnection);

            UnturnedChat.Say(admin, $"Zwolniono {target.DisplayName} ze strefy!", Color.green);
         //   UnturnedChat.Say(target, "Zostales zwolniony przez administratora!", Color.magenta);
        }
    }

    public class CommandStrefaList : IRocketCommand
    {
        public AllowedCaller AllowedCaller => AllowedCaller.Player;
        public string Name => "strefalist";
        public string Help => "Lista stref odrodzenia";
        public string Syntax => "";
        public List<string> Aliases => new List<string>();
        public List<string> Permissions => new List<string>() { "strefa.list" };

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer player = (UnturnedPlayer)caller;

            if (AutoSpawnPlugin.Instance.Configuration.Instance.Zones.Count == 0)
            {
                UnturnedChat.Say(player, "Brak stref!", Color.yellow);
                return;
            }

            UnturnedChat.Say(player, "Aktywne strefy:", Color.blue);
            foreach (var zone in AutoSpawnPlugin.Instance.Configuration.Instance.Zones)
            {
                UnturnedChat.Say(player,
                    $"{zone.ZoneName} - Czas: {AutoSpawnPlugin.Instance.FormatTime(zone.TrapTimeSeconds)} | " +
                    $"Rozmiar: {zone.MaxX - zone.MinX:F0}x{zone.MaxZ - zone.MinZ:F0}x{zone.MaxY - zone.MinY:F0}",
                    Color.cyan);
            }
        }
    }
}