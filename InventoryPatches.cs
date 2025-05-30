using HarmonyLib;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using StrefaOdrodzenia;
using UnityEngine;

namespace StrefaOdrodzenia.Patches
{
    // Blokada wszystkich elementów ekwipunku
    [HarmonyPatch(typeof(PlayerClothing))]
    public static class ClothingPatches
    {
        // ------------------------------------------------------------
        // Wszystkie metody muszą MIEĆ IDENTYCZNĄ SYGNATURĘ jak oryginał!
        // ------------------------------------------------------------

        [HarmonyPatch("askWearHat", new[] { typeof(ItemHatAsset), typeof(byte), typeof(byte[]), typeof(bool) })]
        [HarmonyPrefix]
        static bool Prefix_Hat(PlayerClothing __instance, ItemHatAsset asset, byte quality, byte[] state, bool playEffect)
            => CheckAndBlock(__instance.player, asset == null, "nakrycia głowy");

        [HarmonyPatch("askWearGlasses", new[] { typeof(ItemGlassesAsset), typeof(byte), typeof(byte[]), typeof(bool) })]
        [HarmonyPrefix]
        static bool Prefix_Glasses(PlayerClothing __instance, ItemGlassesAsset asset, byte quality, byte[] state, bool playEffect)
            => CheckAndBlock(__instance.player, asset == null, "okularów");

        [HarmonyPatch("askWearMask", new[] { typeof(ItemMaskAsset), typeof(byte), typeof(byte[]), typeof(bool) })]
        [HarmonyPrefix]
        static bool Prefix_Mask(PlayerClothing __instance, ItemMaskAsset asset, byte quality, byte[] state, bool playEffect)
            => CheckAndBlock(__instance.player, asset == null, "maski");

        [HarmonyPatch("askWearVest", new[] { typeof(ItemVestAsset), typeof(byte), typeof(byte[]), typeof(bool) })]
        [HarmonyPrefix]
        static bool Prefix_Vest(PlayerClothing __instance, ItemVestAsset asset, byte quality, byte[] state, bool playEffect)
            => CheckAndBlock(__instance.player, asset == null, "kamizelki");

        [HarmonyPatch("askWearShirt", new[] { typeof(ItemShirtAsset), typeof(byte), typeof(byte[]), typeof(bool) })]
        [HarmonyPrefix]
        static bool Prefix_Shirt(PlayerClothing __instance, ItemShirtAsset asset, byte quality, byte[] state, bool playEffect)
            => CheckAndBlock(__instance.player, asset == null, "koszuli");

        [HarmonyPatch("askWearPants", new[] { typeof(ItemPantsAsset), typeof(byte), typeof(byte[]), typeof(bool) })]
        [HarmonyPrefix]
        static bool Prefix_Pants(PlayerClothing __instance, ItemPantsAsset asset, byte quality, byte[] state, bool playEffect)
            => CheckAndBlock(__instance.player, asset == null, "spodni");

        [HarmonyPatch("askWearBackpack", new[] { typeof(ItemBackpackAsset), typeof(byte), typeof(byte[]), typeof(bool) })]
        [HarmonyPrefix]
        static bool Prefix_Backpack(PlayerClothing __instance, ItemBackpackAsset asset, byte quality, byte[] state, bool playEffect)
            => CheckAndBlock(__instance.player, asset == null, "plecaka");

        // Wspólna metoda sprawdzająca
        private static bool CheckAndBlock(Player player, bool isRemoving, string itemName)
        {
            if (isRemoving && AutoSpawnPlugin.Instance != null)
            {
                UnturnedPlayer uPlayer = UnturnedPlayer.FromPlayer(player);
                if (AutoSpawnPlugin.Instance.IsPlayerTrapped(uPlayer))
                {
                    // UnturnedChat.Say(uPlayer, $"Nie możesz zdjąć {itemName} w strefie!", Color.red);
                    return false; // Blokuj akcję
                }
            }
            return true;
        }
    }

    // Blokada wyrzucania przedmiotów
    [HarmonyPatch(typeof(PlayerInventory))]
    public static class DropItemPatches
    {
        [HarmonyPatch("ReceiveDropItem")]
        [HarmonyPrefix]
        static bool Prefix(PlayerInventory __instance)
        {
            UnturnedPlayer player = UnturnedPlayer.FromPlayer(__instance.player);
            if (AutoSpawnPlugin.Instance?.IsPlayerTrapped(player) == true)
            {
               // UnturnedChat.Say(player, "Nie możesz wyrzucać przedmiotów w strefie!", Color.red);
                return false;
            }
            return true;
        }
    }
}