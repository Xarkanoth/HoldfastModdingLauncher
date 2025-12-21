using System;
using System.Reflection;


namespace AdvancedAdminUI.Utils
{
    /// <summary>
    /// Helper class to interface with Holdfast's native C# interfaces and events
    /// Based on: https://wiki.holdfastgame.com/Script_Modding_Guide
    /// </summary>
    public static class HoldfastInterfaceHelper
    {
        private static Type _holdfastGameType = null;
        private static Type _sharedMethodsType = null;
        private static object _holdfastInstance = null;
        private static bool _initialized = false;

        public static bool TryInitialize()
        {
            if (_initialized)
                return _holdfastInstance != null;

            _initialized = true;

            try
            {
                // Load Assembly-CSharp which contains Holdfast's interfaces
                Assembly assemblyCSharp = Assembly.Load("Assembly-CSharp");
                
                // Look for IHoldfastGame interface (from HoldfastBridge namespace)
                _holdfastGameType = assemblyCSharp.GetType("HoldfastBridge.IHoldfastGame");
                if (_holdfastGameType == null)
                {
                    // Try without namespace
                    _holdfastGameType = assemblyCSharp.GetType("IHoldfastGame");
                }

                // Look for IHoldfastSharedMethods interface
                _sharedMethodsType = assemblyCSharp.GetType("HoldfastBridge.IHoldfastSharedMethods");
                if (_sharedMethodsType == null)
                {
                    _sharedMethodsType = assemblyCSharp.GetType("IHoldfastSharedMethods");
                }

                if (_holdfastGameType != null || _sharedMethodsType != null)
                {
                    AdvancedAdminUIMod.Log.LogInfo("[HoldfastInterfaceHelper] Found Holdfast interfaces!");
                    
                    // Try to find the game instance
                    // Holdfast typically has a singleton or static instance
                    Type gameManagerType = assemblyCSharp.GetType("HoldfastBridge.GameManager");
                    if (gameManagerType == null)
                    {
                        gameManagerType = assemblyCSharp.GetType("GameManager");
                    }

                    if (gameManagerType != null)
                    {
                        PropertyInfo instanceProp = gameManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        if (instanceProp == null)
                        {
                            FieldInfo instanceField = gameManagerType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
                            if (instanceField != null)
                            {
                                _holdfastInstance = instanceField.GetValue(null);
                            }
                        }
                        else
                        {
                            _holdfastInstance = instanceProp.GetValue(null);
                        }
                    }

                    if (_holdfastInstance != null)
                    {
                        AdvancedAdminUIMod.Log.LogInfo("[HoldfastInterfaceHelper] Got Holdfast instance!");
                        return true;
                    }
                    else
                    {
                        AdvancedAdminUIMod.Log.LogInfo("[HoldfastInterfaceHelper] Found interfaces but could not get instance (this is normal for client-side mods)");
                        return true; // Still return true - interfaces exist even if we can't get instance
                    }
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[HoldfastInterfaceHelper] Could not initialize: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Gets the IHoldfastGame interface type (for accessing RC commands)
        /// </summary>
        public static Type GetIHoldfastGameType()
        {
            TryInitialize();
            return _holdfastGameType;
        }

        /// <summary>
        /// Gets the IHoldfastSharedMethods interface type (for player events)
        /// </summary>
        public static Type GetIHoldfastSharedMethodsType()
        {
            TryInitialize();
            return _sharedMethodsType;
        }

        /// <summary>
        /// Gets player GameObject by playerId using Holdfast's methods
        /// </summary>
        public static UnityEngine.GameObject GetPlayerGameObject(int playerId)
        {
            if (!TryInitialize())
                return null;

            try
            {
                // Look for methods to get player GameObject
                // Common patterns in Holdfast:
                // - GetPlayerGameObject(playerId)
                // - GetPlayerById(playerId)
                Type playerManagerType = Assembly.Load("Assembly-CSharp").GetType("HoldfastBridge.PlayerManager");
                if (playerManagerType == null)
                {
                    playerManagerType = Assembly.Load("Assembly-CSharp").GetType("PlayerManager");
                }

                if (playerManagerType != null)
                {
                    MethodInfo getPlayerMethod = playerManagerType.GetMethod("GetPlayerGameObject", BindingFlags.Public | BindingFlags.Static);
                    if (getPlayerMethod == null)
                    {
                        getPlayerMethod = playerManagerType.GetMethod("GetPlayerById", BindingFlags.Public | BindingFlags.Static);
                    }

                    if (getPlayerMethod != null)
                    {
                        object result = getPlayerMethod.Invoke(null, new object[] { playerId });
                        return result as UnityEngine.GameObject;
                    }
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[HoldfastInterfaceHelper] Could not get player GameObject: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets all player IDs currently in the game
        /// </summary>
        public static int[] GetAllPlayerIds()
        {
            if (!TryInitialize())
                return new int[0];

            try
            {
                Type playerManagerType = Assembly.Load("Assembly-CSharp").GetType("HoldfastBridge.PlayerManager");
                if (playerManagerType == null)
                {
                    playerManagerType = Assembly.Load("Assembly-CSharp").GetType("PlayerManager");
                }

                if (playerManagerType != null)
                {
                    MethodInfo getAllPlayersMethod = playerManagerType.GetMethod("GetAllPlayerIds", BindingFlags.Public | BindingFlags.Static);
                    if (getAllPlayersMethod == null)
                    {
                        PropertyInfo playersProp = playerManagerType.GetProperty("AllPlayers", BindingFlags.Public | BindingFlags.Static);
                        if (playersProp != null)
                        {
                            object players = playersProp.GetValue(null);
                            // Try to extract IDs from collection
                            if (players is System.Collections.ICollection collection)
                            {
                                // This would need more specific handling based on actual structure
                            }
                        }
                    }
                    else
                    {
                        object result = getAllPlayersMethod.Invoke(null, null);
                        return result as int[];
                    }
                }
            }
            catch (Exception ex)
            {
                AdvancedAdminUIMod.Log.LogWarning($"[HoldfastInterfaceHelper] Could not get all player IDs: {ex.Message}");
            }

            return new int[0];
        }

        public static object GetHoldfastInstance()
        {
            return TryInitialize() ? _holdfastInstance : null;
        }

        /// <summary>
        /// Checks if Holdfast's native interfaces are available
        /// </summary>
        public static bool AreInterfacesAvailable()
        {
            return TryInitialize() && (_holdfastGameType != null || _sharedMethodsType != null);
        }
    }
}
