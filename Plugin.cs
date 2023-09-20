using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace Unstuck
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class UnstuckPlugin : BaseUnityPlugin
    {
        internal const string ModName = "Unstuck";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        private readonly Harmony _harmony = new(ModGUID);
        public static readonly ManualLogSource UnstuckLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
        public static FPSPlayer fpspInst => FPSPlayer.code;
        public void Awake()
        {
            hotkey = Config.Bind("1 - General", "Hotkey", new KeyboardShortcut(KeyCode.G), "Keyboard shortcut to trigger unstuck.");
            searchRadius = Config.Bind("1 - General", "Search Radius", 10.0f, "Starting search radius.");
            maxSearchRadius = Config.Bind("1 - General", "Maximum Search Radius", 30.0f, "Maximum radius to search");
            searchIncrement = Config.Bind("1 - General", "Increment Step", 0.5f, "Increment step when expanding search");
            onlyUnderwater = Config.Bind("1 - General", "Only Underwater", true, "Allow unstuck only when underwater. Pressing the key on land will not do anything if on.");


            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void Update()
        {
            if (!Input.anyKey)
                return;
            if (FPSRigidBodyWalker.code == null)
                return;
            if (fpspInst != null && hotkey.Value.IsKeyDown())
            {
                switch (onlyUnderwater.Value)
                {
                    case true when FPSRigidBodyWalker.code.isUnderWater:
                    case false:
                        MoveToSafePosition(fpspInst.transform);
                        break;
                }
            }
        }

        void MoveToSafePosition(Transform playerTransform)
        {
            // If already in a safe position, return
            if (!IsPositionOverlapping(playerTransform.position))
            {
                return;
            }

            // Search for a safe position
            Vector3 safePosition = FindSafePosition(playerTransform);

            if (safePosition != Vector3.zero) // Assuming Vector3.zero is not a valid safe position
            {
                playerTransform.position = safePosition;
            }
            else
            {
                UnstuckLogger.LogWarning("No safe position found within the maximum search radius.");
            }
        }

        bool IsPositionOverlapping(Vector3 position)
        {
            int playerBoneLayer = LayerMask.NameToLayer("Player Bone");
            int playerBoneMask = 1 << playerBoneLayer;
            int inversePlayerBoneMask = ~playerBoneMask;
            Collider[] colliders = Physics.OverlapSphere(position, searchRadius.Value, inversePlayerBoneMask);
            bool isOverlapping = false;
            // Examine each collider. If more than one and not the "Ocean", then it's overlapping.
            foreach (var collider in colliders)
            {
                if (collider.gameObject.name == "Ocean"
                    || collider.gameObject.name == "Water Build Collider"
                    || collider.gameObject.name == "WorldCreatorTerrain"
                    || collider.gameObject.name == "EnviroSky Standard" || collider.transform.root.name.ToLower().Contains("player")) continue;
                isOverlapping = true;
            }

            return isOverlapping;
        }

        Vector3 FindSafePosition(Transform playerTransform)
        {
            float currentRadius = searchRadius.Value;

            while (currentRadius <= maxSearchRadius.Value)
            {
                Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left, Vector3.up, Vector3.down };

                foreach (var direction in directions)
                {
                    Vector3 checkPosition = playerTransform.position + direction * currentRadius;

                    if (!IsPositionOverlapping(checkPosition))
                    {
                        return checkPosition; // Found a safe position
                    }
                }

                currentRadius += searchIncrement.Value; // Increase the search radius
            }

            return Vector3.zero; // No safe position found
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
            watcher.Changed += ReadConfigValues;
            watcher.Created += ReadConfigValues;
            watcher.Renamed += ReadConfigValues;
            watcher.IncludeSubdirectories = true;
            watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            watcher.EnableRaisingEvents = true;
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                UnstuckLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                UnstuckLogger.LogError($"There was an issue loading your {ConfigFileName}");
                UnstuckLogger.LogError("Please check your config entries for spelling and format!");
            }
        }


        #region ConfigOptions

        internal static ConfigEntry<KeyboardShortcut> hotkey = null!;
        internal static ConfigEntry<float> searchRadius = null!;
        internal static ConfigEntry<float> maxSearchRadius = null!;
        internal static ConfigEntry<float> searchIncrement = null!;
        internal static ConfigEntry<bool> onlyUnderwater = null!;

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order = null!;
            [UsedImplicitly] public bool? Browsable = null!;
            [UsedImplicitly] public string? Category = null!;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
        }

        internal ConfigEntry<T> TextEntryConfig<T>(string group, string name, T value, string desc)
        {
            ConfigurationManagerAttributes attributes = new()
            {
                CustomDrawer = TextAreaDrawer
            };
            return Config.Bind(group, name, value, new ConfigDescription(desc, null, attributes));
        }

        internal static void TextAreaDrawer(ConfigEntryBase entry)
        {
            GUILayout.ExpandHeight(true);
            GUILayout.ExpandWidth(true);
            entry.BoxedValue = GUILayout.TextArea((string)entry.BoxedValue, GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true));
        }

        class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() =>
                "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }

        #endregion
    }

    public static class KeyboardExtensions
    {
        public static bool IsKeyDown(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) &&
                   shortcut.Modifiers.All(Input.GetKey);
        }

        public static bool IsKeyHeld(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) &&
                   shortcut.Modifiers.All(Input.GetKey);
        }
    }
}