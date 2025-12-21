using System;
using System.Collections.Generic;
using System.Linq;

namespace AdvancedAdminUI.Settings
{
    /// <summary>
    /// Centralized class-specific settings for rambo detection, AFK detection, and line formation rules
    /// </summary>
    public static class ClassSettings
    {
        /// <summary>
        /// Default settings for a specific player class (proximity, rambo time, AFK time, etc.)
        /// </summary>
        public class ClassConfig
        {
            /// <summary>
            /// Maximum distance (in meters) players of this class can be from each other to be considered "in line"
            /// </summary>
            public float ProximityThreshold { get; set; }
            
            /// <summary>
            /// Time (in seconds) a player must be alone before being marked as "rambo"
            /// </summary>
            public float RamboTimeThreshold { get; set; }
            
            /// <summary>
            /// Minimum number of players needed in a line/chain for this class to not be considered rambo
            /// </summary>
            public int MinLineSize { get; set; }
            
            /// <summary>
            /// Time (in seconds) a player must be stationary before being marked as AFK
            /// </summary>
            public float AfkTimeThreshold { get; set; }
            
            /// <summary>
            /// Minimum movement distance (in meters) required to reset AFK timer
            /// </summary>
            public float MovementThreshold { get; set; }
            
            /// <summary>
            /// Radius (in meters) to check for "in-group" status. Player must be within this distance of enough other players.
            /// </summary>
            public float InGroupRadius { get; set; }
            
            /// <summary>
            /// Minimum number of other players required within InGroupRadius to be considered "in-group" (not rambo)
            /// </summary>
            public int InGroupMinPlayers { get; set; }
        }
        
        /// <summary>
        /// Class makeup rule defining valid combinations of classes (e.g., "Skirmishers" = max 7 Light Infantry + 1 Surgeon)
        /// </summary>
        public class ClassMakeupRule
        {
            /// <summary>
            /// Name of this makeup rule (e.g., "Skirmishers")
            /// </summary>
            public string RuleName { get; set; }
            
            /// <summary>
            /// Maximum number of primary class players allowed (e.g., 7 for Light Infantry, 5 for Rifleman)
            /// </summary>
            public int MaxPrimaryClass { get; set; }
            
            /// <summary>
            /// Maximum number of surgeons allowed in the line
            /// </summary>
            public int MaxSurgeons { get; set; }
            
            /// <summary>
            /// Name of the primary class this rule applies to (e.g., "LightInfantry", "Rifleman")
            /// </summary>
            public string PrimaryClassName { get; set; }
            
            /// <summary>
            /// Required spacing (in meters) for this formation (e.g., 5-man spacing for Skirmishers)
            /// </summary>
            public float RequiredSpacing { get; set; }
            
            /// <summary>
            /// Minimum number of members required for a valid formation (e.g., 3 for Skirmishers - less than 3 = broken)
            /// </summary>
            public int MinFormationSize { get; set; }
        }
        
        /// <summary>
        /// Line Composition Rule - validates standard line formations
        /// A valid line may include: 1 Officer, 1 Sergeant, 1 Surgeon (or Sapper/Cannoneer), 1-2 attached units (Light Infantry/Carpenter)
        /// </summary>
        public class LineCompositionRule
        {
            /// <summary>
            /// Maximum number of Officers allowed in a line
            /// </summary>
            public int MaxOfficers { get; set; } = 1;
            
            /// <summary>
            /// Maximum number of Sergeants allowed in a line
            /// </summary>
            public int MaxSergeants { get; set; } = 1;
            
            /// <summary>
            /// Maximum number of medical/support units allowed (Surgeon, Sapper, Cannoneer - interchangeable)
            /// </summary>
            public int MaxMedicalSupport { get; set; } = 1;
            
            /// <summary>
            /// Classes that count as "Infantry" for attachment limit calculations
            /// </summary>
            public HashSet<string> InfantryClasses { get; set; }
            
            /// <summary>
            /// Classes that count as "Attached" units (Light Infantry, Carpenter)
            /// </summary>
            public HashSet<string> AttachedClasses { get; set; }
            
            /// <summary>
            /// Classes that count as "Auxiliary" (non-infantry support classes)
            /// </summary>
            public HashSet<string> AuxiliaryClasses { get; set; }
            
            public LineCompositionRule()
            {
                // Infantry Classes: Classes that count as "Infantry" for attachment limit calculations
                // Note: Rifleman when broken (< 3) must join a line as LineInfantry, so they count as Infantry
                InfantryClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "ArmyInfantryOfficer",
                    "Sergeant",
                    "ArmyLineInfantry",
                    "Grenadier",
                    "Guard",
                    "Rifleman" // Broken rifleman (< 3) act as LineInfantry, not attached skirms
                };
                
                // Attached Classes: Classes that count as "Attached" units (Light Infantry, Carpenter)
                // Note: Broken Light Infantry (< 3) can join a line as attached skirms
                AttachedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "LightInfantry", // Broken LI (< 3) can attach to lines as attached skirms
                    "Carpenter"
                };
                
                // Auxiliary Classes: Non-infantry support classes
                // Note: Surgeons are handled by proximity detection (5m to line vs 5m to skirmishers)
                // Surgeons near lines count as Medical/Support, surgeons near skirmishers are part of skirmisher groups
                AuxiliaryClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "Surgeon", // Counted separately as Medical/Support when part of line
                    "Sapper",
                    "Cannoneer",
                    "FlagBearer",
                    "Musician",
                    "Rocketeer",
                    "NavalCaptain",
                    "NavalMarine",
                    "NavalSailor",
                    "NavalSailor2",
                    "CoastGuard",
                    "Customs"
                };
            }
        }
        
        // Default settings (used for classes not explicitly configured)
        private static readonly ClassConfig DefaultConfig = new ClassConfig
        {
            ProximityThreshold = 1.0f,
            RamboTimeThreshold = 1.0f,
            MinLineSize = 3,
            AfkTimeThreshold = 30.0f, // 30 seconds for AFK detection
            MovementThreshold = 0.1f,
            InGroupRadius = 5.0f, // Default 5 meters for in-group check
            InGroupMinPlayers = 3 // Default: need 3 total players (self + 2 others)
        };
        
        // Class makeup rules - define valid combinations of classes
        private static readonly Dictionary<string, ClassMakeupRule> _makeupRules = new Dictionary<string, ClassMakeupRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["Skirmishers_LightInfantry"] = new ClassMakeupRule
            {
                RuleName = "Skirmishers_LightInfantry",
                MaxPrimaryClass = 7,
                MaxSurgeons = 1,
                PrimaryClassName = "LightInfantry",
                RequiredSpacing = 5.0f, // 5-man spacing required for Skirmishers
                MinFormationSize = 3 // Less than 3 = broken skirmishers
            },
            ["Skirmishers_Rifleman"] = new ClassMakeupRule
            {
                RuleName = "Skirmishers_Rifleman",
                MaxPrimaryClass = 5,
                MaxSurgeons = 1,
                PrimaryClassName = "Rifleman",
                RequiredSpacing = 5.0f, // 5-man spacing required for Skirmishers
                MinFormationSize = 3 // Less than 3 = broken skirmishers
            },
            ["Cavalry_Dragoon"] = new ClassMakeupRule
            {
                RuleName = "Cavalry_Dragoon",
                MaxPrimaryClass = 6, // Max 6 Dragoons
                MaxSurgeons = 0, // No surgeons in cavalry
                PrimaryClassName = "Dragoon",
                RequiredSpacing = 5.0f, // 5-man spacing required when firing while mounted
                MinFormationSize = 3 // No minimum formation size (can operate solo)
            },
            ["Cavalry_Hussar"] = new ClassMakeupRule
            {
                RuleName = "Cavalry_Hussar",
                MaxPrimaryClass = 6, // Max 6 Hussars
                MaxSurgeons = 0, // No surgeons in cavalry
                PrimaryClassName = "Hussar",
                RequiredSpacing = 30.0f, // 30m spacing (same as proximity threshold)
                MinFormationSize = 3 // No minimum formation size (can operate solo)
            }
        };
        
        // Mapping from class names to makeup rule names (which classes use which makeup rules)
        private static readonly Dictionary<string, string> _classToMakeupRule = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["LightInfantry"] = "Skirmishers_LightInfantry",
            ["Rifleman"] = "Skirmishers_Rifleman",
            ["Dragoon"] = "Cavalry_Dragoon",
            ["Hussar"] = "Cavalry_Hussar",
            ["Surgeon"] = null // Surgeon uses the rule of the primary class it's grouped with (LightInfantry or Rifleman)
        };
        
        // Line Composition Rule - validates standard line formations
        private static readonly LineCompositionRule _lineCompositionRule = new LineCompositionRule();
        
        // Class-specific configurations
        private static readonly Dictionary<string, ClassConfig> _classConfigs = new Dictionary<string, ClassConfig>(StringComparer.OrdinalIgnoreCase)
        {
            // Cavalry Classes
            ["Hussar"] = new ClassConfig
            {
                ProximityThreshold = 30.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 30.0f,
                InGroupMinPlayers = 3
            },
            ["Dragoon"] = new ClassConfig
            {
                ProximityThreshold = 30.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 30.0f,
                InGroupMinPlayers = 3
            },
            
            // Skirmisher Classes
            ["LightInfantry"] = new ClassConfig
            {
                ProximityThreshold = 5.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Rifleman"] = new ClassConfig
            {
                ProximityThreshold = 5.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            
            // Infantry Classes (standard line infantry - 1m proximity)
            ["ArmyLineInfantry"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Grenadier"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Guard"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["FlagBearer"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Musician"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            
            // Special/Support Classes (5m proximity - officers, medics, engineers)
            ["ArmyInfantryOfficer"] = new ClassConfig
            {
                ProximityThreshold = 5.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Sergeant"] = new ClassConfig
            {
                ProximityThreshold = 5.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Surgeon"] = new ClassConfig
            {
                ProximityThreshold = 5.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Sapper"] = new ClassConfig
            {
                ProximityThreshold = 10.0f, // Artillery radius
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            
            // Artillery Classes (artillery radius - wider spacing due to equipment)
            ["Cannoneer"] = new ClassConfig
            {
                ProximityThreshold = 10.0f, // Artillery radius
                RamboTimeThreshold = 1.0f,
                MinLineSize = 2, // Artillery often operates in smaller groups
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Rocketeer"] = new ClassConfig
            {
                ProximityThreshold = 10.0f, // Artillery radius
                RamboTimeThreshold = 1.0f,
                MinLineSize = 2, // Artillery often operates in smaller groups
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            
            // Naval Classes (may need different spacing due to ship operations)
            ["NavalCaptain"] = new ClassConfig
            {
                ProximityThreshold = 5.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 2, // Officers may operate independently
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["NavalMarine"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["NavalSailor"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["NavalSailor2"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Carpenter"] = new ClassConfig
            {
                ProximityThreshold = 5.0f, // Regular radius 5, but uses 10m when connecting with artillery classes
                RamboTimeThreshold = 1.0f,
                MinLineSize = 2, // Support class, may operate independently
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["CoastGuard"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 30.0f,
                MovementThreshold = 0.1f,
                InGroupRadius = 5.0f,
                InGroupMinPlayers = 3
            },
            ["Customs"] = new ClassConfig
            {
                ProximityThreshold = 1.0f,
                RamboTimeThreshold = 1.0f,
                MinLineSize = 3,
                AfkTimeThreshold = 10.0f,
                MovementThreshold = 0.1f
            }
        };
        
        /// <summary>
        /// Get configuration for a specific class by name
        /// </summary>
        public static ClassConfig GetConfig(string className)
        {
            if (string.IsNullOrEmpty(className))
                return DefaultConfig;
            
            // Try exact match first
            if (_classConfigs.TryGetValue(className, out ClassConfig config))
                return config;
            
            // Try case-insensitive partial match
            string classLower = className.ToLower();
            foreach (var kvp in _classConfigs)
            {
                if (classLower.Contains(kvp.Key.ToLower()) || kvp.Key.ToLower().Contains(classLower))
                {
                    return kvp.Value;
                }
            }
            
            // Check for common class name patterns (fallback matching)
            if (classLower.Contains("hussar"))
                return _classConfigs["Hussar"];
            if (classLower.Contains("dragoon"))
                return _classConfigs["Dragoon"];
            if (classLower.Contains("lightinfantry") || classLower.Contains("light infantry"))
                return _classConfigs["LightInfantry"];
            if (classLower.Contains("rifleman"))
                return _classConfigs["Rifleman"];
            if (classLower.Contains("armyinfantryofficer") || (classLower.Contains("officer") && !classLower.Contains("naval")))
                return _classConfigs["ArmyInfantryOfficer"];
            if (classLower.Contains("armylineinfantry") || classLower.Contains("line infantry"))
                return _classConfigs["ArmyLineInfantry"];
            if (classLower.Contains("grenadier"))
                return _classConfigs["Grenadier"];
            if (classLower.Contains("guard") && !classLower.Contains("coast"))
                return _classConfigs["Guard"];
            if (classLower.Contains("flagbearer") || classLower.Contains("flag bearer"))
                return _classConfigs["FlagBearer"];
            if (classLower.Contains("musician"))
                return _classConfigs["Musician"];
            if (classLower.Contains("sergeant"))
                return _classConfigs["Sergeant"];
            if (classLower.Contains("surgeon"))
                return _classConfigs["Surgeon"];
            if (classLower.Contains("sapper"))
                return _classConfigs["Sapper"];
            if (classLower.Contains("cannoneer"))
                return _classConfigs["Cannoneer"];
            if (classLower.Contains("rocketeer"))
                return _classConfigs["Rocketeer"];
            if (classLower.Contains("navalcaptain") || (classLower.Contains("naval") && classLower.Contains("captain")))
                return _classConfigs["NavalCaptain"];
            if (classLower.Contains("navalmarine") || (classLower.Contains("naval") && classLower.Contains("marine")))
                return _classConfigs["NavalMarine"];
            if (classLower.Contains("navalsailor2") || (classLower.Contains("naval") && classLower.Contains("sailor") && classLower.Contains("2")))
                return _classConfigs["NavalSailor2"];
            if (classLower.Contains("navalsailor") || (classLower.Contains("naval") && classLower.Contains("sailor")))
                return _classConfigs["NavalSailor"];
            if (classLower.Contains("carpenter"))
                return _classConfigs["Carpenter"];
            if (classLower.Contains("coastguard") || classLower.Contains("coast guard"))
                return _classConfigs["CoastGuard"];
            if (classLower.Contains("customs"))
                return _classConfigs["Customs"];
            
            return DefaultConfig;
        }
        
        /// <summary>
        /// Get proximity threshold for a class
        /// </summary>
        public static float GetProximityThreshold(string className)
        {
            return GetConfig(className).ProximityThreshold;
        }
        
        /// <summary>
        /// Get rambo time threshold for a class
        /// </summary>
        public static float GetRamboTimeThreshold(string className)
        {
            return GetConfig(className).RamboTimeThreshold;
        }
        
        /// <summary>
        /// Get minimum line size for a class
        /// </summary>
        public static int GetMinLineSize(string className)
        {
            return GetConfig(className).MinLineSize;
        }
        
        /// <summary>
        /// Get AFK time threshold for a class
        /// </summary>
        public static float GetAfkTimeThreshold(string className)
        {
            return GetConfig(className).AfkTimeThreshold;
        }
        
        /// <summary>
        /// Get movement threshold for a class
        /// </summary>
        public static float GetMovementThreshold(string className)
        {
            return GetConfig(className).MovementThreshold;
        }
        
        /// <summary>
        /// Get in-group radius for a class
        /// </summary>
        public static float GetInGroupRadius(string className)
        {
            return GetConfig(className).InGroupRadius;
        }
        
        /// <summary>
        /// Get minimum number of players required for in-group status
        /// </summary>
        public static int GetInGroupMinPlayers(string className)
        {
            return GetConfig(className).InGroupMinPlayers;
        }
        
        /// <summary>
        /// Get the makeup rule name for a class (if applicable)
        /// </summary>
        public static string GetMakeupRuleName(string className)
        {
            if (string.IsNullOrEmpty(className))
                return null;
            
            // Try exact match first
            if (_classToMakeupRule.TryGetValue(className, out string ruleName))
                return ruleName;
            
            // Try case-insensitive partial match
            string classLower = className.ToLower();
            foreach (var kvp in _classToMakeupRule)
            {
                if (classLower.Contains(kvp.Key.ToLower()) || kvp.Key.ToLower().Contains(classLower))
                {
                    return kvp.Value;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Get the makeup rule for a class (if applicable)
        /// </summary>
        public static ClassMakeupRule GetMakeupRule(string className)
        {
            string ruleName = GetMakeupRuleName(className);
            if (string.IsNullOrEmpty(ruleName))
                return null;
            
            if (_makeupRules.TryGetValue(ruleName, out ClassMakeupRule rule))
                return rule;
            
            return null;
        }
        
        /// <summary>
        /// Get a makeup rule by its rule name
        /// </summary>
        public static ClassMakeupRule GetMakeupRuleByName(string ruleName)
        {
            if (string.IsNullOrEmpty(ruleName))
                return null;
            
            if (_makeupRules.TryGetValue(ruleName, out ClassMakeupRule rule))
                return rule;
            
            return null;
        }
        
        /// <summary>
        /// Check if a class has a makeup rule
        /// </summary>
        public static bool HasMakeupRule(string className)
        {
            return GetMakeupRule(className) != null;
        }
        
        /// <summary>
        /// Get the required spacing for a class's makeup rule (if applicable)
        /// Returns the RequiredSpacing from the makeup rule, or falls back to ProximityThreshold
        /// </summary>
        public static float GetRequiredSpacing(string className)
        {
            var rule = GetMakeupRule(className);
            if (rule != null && rule.RequiredSpacing > 0)
                return rule.RequiredSpacing;
            
            // Default: use proximity threshold
            return GetProximityThreshold(className);
        }
        
        /// <summary>
        /// Get the minimum formation size for a class's makeup rule (if applicable)
        /// Returns the MinFormationSize from the makeup rule, or falls back to MinLineSize
        /// </summary>
        public static int GetMinFormationSize(string className)
        {
            var rule = GetMakeupRule(className);
            if (rule != null && rule.MinFormationSize > 0)
                return rule.MinFormationSize;
            
            // Default: use minimum line size
            return GetMinLineSize(className);
        }
        
        // Legacy methods for backward compatibility (deprecated - use GetMakeupRule instead)
        /// <summary>
        /// [DEPRECATED] Get skirmisher rule for a class (if applicable). Use GetMakeupRule instead.
        /// </summary>
        [Obsolete("Use GetMakeupRule instead")]
        public static ClassMakeupRule GetSkirmisherRule(string className)
        {
            return GetMakeupRule(className);
        }
        
        /// <summary>
        /// [DEPRECATED] Check if a class has skirmisher rules. Use HasMakeupRule instead.
        /// </summary>
        [Obsolete("Use HasMakeupRule instead")]
        public static bool HasSkirmisherRule(string className)
        {
            return HasMakeupRule(className);
        }
        
        /// <summary>
        /// Check if a class is considered "Infantry" for line composition rules
        /// </summary>
        public static bool IsInfantryClass(string className)
        {
            if (string.IsNullOrEmpty(className))
                return false;
            
            return _lineCompositionRule.InfantryClasses.Contains(className) ||
                   _lineCompositionRule.InfantryClasses.Any(inf => className.ToLower().Contains(inf.ToLower()) || inf.ToLower().Contains(className.ToLower()));
        }
        
        /// <summary>
        /// Check if a class is considered "Attached" (Light Infantry, Carpenter)
        /// </summary>
        public static bool IsAttachedClass(string className)
        {
            if (string.IsNullOrEmpty(className))
                return false;
            
            return _lineCompositionRule.AttachedClasses.Contains(className) ||
                   _lineCompositionRule.AttachedClasses.Any(att => className.ToLower().Contains(att.ToLower()) || att.ToLower().Contains(className.ToLower()));
        }
        
        /// <summary>
        /// Check if a class is considered "Auxiliary" (support classes)
        /// </summary>
        public static bool IsAuxiliaryClass(string className)
        {
            if (string.IsNullOrEmpty(className))
                return false;
            
            return _lineCompositionRule.AuxiliaryClasses.Contains(className) ||
                   _lineCompositionRule.AuxiliaryClasses.Any(aux => className.ToLower().Contains(aux.ToLower()) || aux.ToLower().Contains(className.ToLower()));
        }
        
        /// <summary>
        /// Validate line composition based on Line Composition Rules
        /// Returns true if the line composition is valid, false if invalid (should be highlighted red)
        /// </summary>
        public static bool ValidateLineComposition(Dictionary<string, int> classCounts)
        {
            var (isValid, _) = ValidateLineCompositionWithReason(classCounts);
            return isValid;
        }
        
        /// <summary>
        /// Validate line composition and return validation status with detailed reason
        /// </summary>
        public static (bool isValid, string reason) ValidateLineCompositionWithReason(Dictionary<string, int> classCounts)
        {
            if (classCounts == null || classCounts.Count == 0)
                return (false, "Empty composition");
            
            List<string> reasons = new List<string>();
            
            // Count classes
            int officerCount = 0;
            int sergeantCount = 0;
            int medicalSupportCount = 0; // Surgeon, Sapper, Cannoneer (interchangeable)
            int attachedCount = 0; // Light Infantry, Carpenter
            int infantryCount = 0;
            int auxiliaryCount = 0;
            
            foreach (var kvp in classCounts)
            {
                string className = kvp.Key;
                int count = kvp.Value;
                string classLower = className.ToLower();
                
                // Count Officers
                if (classLower.Contains("officer") && !classLower.Contains("naval"))
                    officerCount += count;
                
                // Count Sergeants
                else if (classLower.Contains("sergeant"))
                    sergeantCount += count;
                
                // Count Medical/Support (Surgeon, Sapper, Cannoneer - interchangeable)
                else if (classLower.Contains("surgeon") || classLower.Contains("sapper") || classLower.Contains("cannoneer"))
                    medicalSupportCount += count;
                
                // Count Attached (Light Infantry, Carpenter)
                else if (classLower.Contains("lightinfantry") || classLower.Contains("light infantry") || classLower.Contains("carpenter"))
                    attachedCount += count;
                
                // Count Infantry
                if (IsInfantryClass(className))
                    infantryCount += count;
                
                // Count Auxiliary
                if (IsAuxiliaryClass(className))
                    auxiliaryCount += count;
            }
            
            // Rule 1: Max 1 Officer
            if (officerCount > _lineCompositionRule.MaxOfficers)
                reasons.Add($"Too many Officers ({officerCount}/{_lineCompositionRule.MaxOfficers})");
            
            // Rule 2: Max 1 Sergeant
            if (sergeantCount > _lineCompositionRule.MaxSergeants)
                reasons.Add($"Too many Sergeants ({sergeantCount}/{_lineCompositionRule.MaxSergeants})");
            
            // Rule 3: Max 1 Medical/Support (Surgeon OR Sapper OR Cannoneer)
            if (medicalSupportCount > _lineCompositionRule.MaxMedicalSupport)
                reasons.Add($"Too many Medical/Support ({medicalSupportCount}/{_lineCompositionRule.MaxMedicalSupport})");
            
            // Rule 4: Attachment Member Limits
            // 5 Infantry = 1 Attached member
            // 10+ Infantry = 2 attached members max
            // Note: Broken Light Infantry (< 3) can attach to lines, but may cause "too many attached" violations
            // This is acceptable - the line will show as invalid with duration tracking
            int maxAttached = 0;
            if (infantryCount >= 10)
                maxAttached = 2;
            else if (infantryCount >= 5)
                maxAttached = 1;
            
            if (attachedCount > maxAttached)
            {
                // Special case: If we have broken Light Infantry attached, this violation is acceptable
                // but we still mark it as invalid (will show duration)
                int lightInfantryCount = 0;
                foreach (var kvp in classCounts)
                {
                    string classLower = kvp.Key.ToLower();
                    if (classLower.Contains("lightinfantry") || classLower.Contains("light infantry"))
                        lightInfantryCount += kvp.Value;
                }
                
                // If we have broken LI (< 3) attached, this is acceptable but still invalid
                if (lightInfantryCount > 0 && lightInfantryCount < 3)
                {
                    reasons.Add($"Too many Attached units ({attachedCount}/{maxAttached} for {infantryCount} Infantry) - Broken LI attached");
                }
                else
                {
                    reasons.Add($"Too many Attached units ({attachedCount}/{maxAttached} for {infantryCount} Infantry)");
                }
            }
            
            // Rule 5: A line may NOT consist solely of auxiliary classes
            int totalCount = classCounts.Values.Sum();
            if (totalCount == auxiliaryCount && totalCount > 0)
                reasons.Add("Line consists solely of auxiliary classes");
            
            string reason = reasons.Count > 0 ? string.Join(", ", reasons) : "";
            return (reasons.Count == 0, reason);
        }
    }
}

