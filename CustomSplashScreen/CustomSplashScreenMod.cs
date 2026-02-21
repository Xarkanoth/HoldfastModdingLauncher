using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace CustomSplashScreen
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class CustomSplashScreenMod : BaseUnityPlugin
    {
        public const string PLUGIN_GUID = "com.xarkanoth.customsplashscreen";
        public const string PLUGIN_NAME = "Custom Splash Screen";
        public const string PLUGIN_VERSION = "1.0.17";

        public static ManualLogSource Log;
        public static CustomSplashScreenMod Instance;

        private Harmony _harmony;
        internal bool _videoReplaced = false;

        // Configuration loaded from launcher settings
        internal SplashScreenConfig _config;
        private string _configPath;
        private string _customVideoPath;
        private string _splashVideosFolder;

        // Available videos - dynamically loaded from local folder
        public static Dictionary<string, SplashVideo> AvailableVideos = new Dictionary<string, SplashVideo>();


        private void Awake()
        {
            Instance = this;
            Log = Logger;

            Log.LogInfo($"===========================================");
            Log.LogInfo($"  {PLUGIN_NAME} v{PLUGIN_VERSION}");
            Log.LogInfo($"  Custom Splash Screen Video Replacer");
            Log.LogInfo($"===========================================");

            // Set up paths
            string pluginFolder = Path.GetDirectoryName(Info.Location);
            string bepInExFolder = Path.GetDirectoryName(pluginFolder); // Go up from plugins to BepInEx
            
            // Videos are stored in BepInEx/SplashVideos/ (pre-downloaded by launcher)
            _splashVideosFolder = Path.Combine(bepInExFolder, "SplashVideos");
            _customVideoPath = Path.Combine(_splashVideosFolder, "custom_splash.mp4");
            
            // Config is stored in BepInEx/config by the launcher
            _configPath = Path.Combine(bepInExFolder, "config", "com.xarkanoth.customsplashscreen.json");
            
            Log.LogInfo($"Splash videos folder: {_splashVideosFolder}");

            // Initialize available videos from local folder
            InitializeAvailableVideos();

            // Load configuration
            LoadConfig();

            // Hook into scene loading
            SceneManager.sceneLoaded += OnSceneLoaded;

            // Apply Harmony patches
            _harmony = new Harmony(PLUGIN_GUID);
            _harmony.PatchAll();

            Log.LogInfo($"Selected video: {_config?.SelectedVideoId ?? "default"}");
            Log.LogInfo("Configure splash screen via the Modding Launcher settings.");
            Log.LogInfo("Custom Splash Screen mod initialized!");
            
            // Check if we're already in the splash screen scene (it may have loaded before we initialized)
            var currentScene = SceneManager.GetActiveScene();
            Log.LogInfo($"Current scene on startup: {currentScene.name}");
            if (currentScene.name == "SplashScreenScene" && !_videoReplaced)
            {
                Log.LogInfo("Already in SplashScreenScene - attempting immediate video replacement");
                ReplaceVideoPlayer();
            }
        }

        private void InitializeAvailableVideos()
        {
            // Always include default option
            AvailableVideos["default"] = new SplashVideo
            {
                Id = "default",
                Name = "Default (Original)",
                LocalFileName = ""
            };
            
            // Scan local folder for video files
            try
            {
                if (Directory.Exists(_splashVideosFolder))
                {
                    string[] videoExtensions = { ".mp4", ".mov", ".webm", ".ogv" };
                    string[] videoFiles = Directory.GetFiles(_splashVideosFolder)
                        .Where(f => videoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                        .ToArray();
                    
                    foreach (string videoFile in videoFiles)
                    {
                        string fileName = Path.GetFileName(videoFile);
                        string videoId = Path.GetFileNameWithoutExtension(fileName)
                            .ToLowerInvariant()
                            .Replace(" ", "_")
                            .Replace("-", "_");
                        
                        // Skip if already added
                        if (AvailableVideos.ContainsKey(videoId))
                            continue;
                        
                        string displayName = Path.GetFileNameWithoutExtension(fileName)
                            .Replace("_", " ")
                            .Replace("-", " ");
                        
                        AvailableVideos[videoId] = new SplashVideo
                        {
                            Id = videoId,
                            Name = displayName,
                            LocalFileName = fileName
                        };
                        
                        Log.LogInfo($"Found video: {displayName} ({fileName})");
                    }
                    
                    Log.LogInfo($"Initialized {AvailableVideos.Count} video(s) (including default)");
                }
                else
                {
                    Log.LogInfo("SplashVideos folder does not exist yet - will be created when videos are downloaded");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to initialize available videos: {ex.Message}");
            }
        }
        
        private void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    _config = ParseConfig(json);
                    Log.LogInfo($"Loaded config: VideoId={_config?.SelectedVideoId}, Enabled={_config?.Enabled}");
                }
                else
                {
                    Log.LogInfo("No config file found, using defaults. Configure via Modding Launcher.");
                    _config = new SplashScreenConfig { SelectedVideoId = "default", Enabled = true };
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load config: {ex.Message}");
                _config = new SplashScreenConfig { SelectedVideoId = "default", Enabled = true };
            }
        }

        private SplashScreenConfig ParseConfig(string json)
        {
            var config = new SplashScreenConfig();
            
            // Simple regex parsing for JSON
            var videoIdMatch = Regex.Match(json, @"""SelectedVideoId""\s*:\s*""([^""]*)""");
            if (videoIdMatch.Success)
            {
                config.SelectedVideoId = videoIdMatch.Groups[1].Value;
            }

            var enabledMatch = Regex.Match(json, @"""Enabled""\s*:\s*(true|false)", RegexOptions.IgnoreCase);
            if (enabledMatch.Success)
            {
                config.Enabled = enabledMatch.Groups[1].Value.ToLower() == "true";
            }

            return config;
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _harmony?.UnpatchSelf();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Log.LogInfo($"Scene loaded: {scene.name}");

            if (scene.name == "SplashScreenScene" && !_videoReplaced)
            {
                if (_config == null || !_config.Enabled)
                {
                    Log.LogInfo("Custom splash screen disabled in config");
                    return;
                }

                if (string.IsNullOrEmpty(_config.SelectedVideoId) || _config.SelectedVideoId == "default")
                {
                    Log.LogInfo("Using default splash screen");
                    return;
                }

                Log.LogInfo("=== SPLASH SCREEN SCENE DETECTED ===");
                ReplaceVideoPlayer();
            }
        }

        private void ReplaceVideoPlayer()
        {
            try
            {
                // Find any VideoPlayer in the scene
                VideoPlayer[] allPlayers = FindObjectsOfType<VideoPlayer>();
                Log.LogInfo($"Found {allPlayers.Length} VideoPlayer(s) in scene");

                if (allPlayers.Length == 0)
                {
                    // Try to find by name
                    GameObject videoPlayerObj = GameObject.Find("Video Player Start");
                    if (videoPlayerObj != null)
                    {
                        VideoPlayer vp = videoPlayerObj.GetComponent<VideoPlayer>();
                        if (vp != null)
                        {
                            ApplyCustomVideo(vp);
                            return;
                        }
                    }
                    Log.LogWarning("No VideoPlayer found in scene");
                    return;
                }

                // Use the first video player found
                ApplyCustomVideo(allPlayers[0]);
            }
            catch (Exception ex)
            {
                Log.LogError($"Error replacing video: {ex.Message}");
                Log.LogError(ex.StackTrace);
            }
        }

        private void ApplyCustomVideo(VideoPlayer videoPlayer)
        {
            if (_config == null || string.IsNullOrEmpty(_config.SelectedVideoId))
            {
                Log.LogWarning("No video configured");
                return;
            }

            if (!AvailableVideos.TryGetValue(_config.SelectedVideoId, out var selectedVideo))
            {
                Log.LogWarning($"Unknown video ID: {_config.SelectedVideoId}");
                return;
            }

            if (string.IsNullOrEmpty(selectedVideo.LocalFileName))
            {
                Log.LogInfo("Default video selected, not replacing");
                return;
            }

            Log.LogInfo($"=== Applying Custom Video: {selectedVideo.Name} ===");

            // Build local file path
            string videoPath = Path.Combine(_splashVideosFolder, selectedVideo.LocalFileName);
            
            if (!File.Exists(videoPath))
            {
                Log.LogWarning($"Video file not found: {videoPath}");
                Log.LogInfo($"Please ensure the video is downloaded to: {_splashVideosFolder}");
                Log.LogInfo("Videos should be downloaded via the Modding Launcher settings.");
                return;
            }

            string videoUrl = "file:///" + videoPath.Replace("\\", "/");
            Log.LogInfo($"Using local file: {videoPath}");

            // Stop current playback
            videoPlayer.Stop();

            // Set to URL source (file:// for local files)
            videoPlayer.source = VideoSource.Url;
            videoPlayer.url = videoUrl;
            videoPlayer.aspectRatio = VideoAspectRatio.FitInside;

            // Prepare and play
            videoPlayer.prepareCompleted += (vp) =>
            {
                Log.LogInfo("Custom video prepared, playing...");
                vp.Play();
            };

            videoPlayer.errorReceived += (vp, message) =>
            {
                Log.LogError($"Video error: {message}");
            };

            videoPlayer.Prepare();
            _videoReplaced = true;

            var skipObj = new GameObject("SplashSkipHandler");
            var handler = skipObj.AddComponent<SplashSkipHandler>();
            handler.Init(videoPlayer);

            Log.LogInfo("Custom video applied successfully!");
        }
    }

    public class SplashVideo
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string LocalFileName { get; set; }
    }

    public class SplashScreenConfig
    {
        public string SelectedVideoId { get; set; } = "default";
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Harmony patch to intercept VideoPlayer.Play() calls in the splash screen
    /// </summary>
    [HarmonyPatch(typeof(VideoPlayer), "Play")]
    public static class VideoPlayerPlayPatch
    {
        // Static flag to prevent re-entry when we call Play() ourselves
        private static bool _isPlayingCustomVideo = false;
        
        static bool Prefix(VideoPlayer __instance)
        {
            try
            {
                // Prevent infinite loop - if we're already playing custom video, let it through
                if (_isPlayingCustomVideo)
                    return true;
                
                // Only intercept in splash screen scene
                var currentScene = SceneManager.GetActiveScene();
                if (currentScene.name != "SplashScreenScene")
                    return true; // Continue with original method

                // Check if we already replaced the video
                if (CustomSplashScreenMod.Instance?._videoReplaced == true)
                    return true;

                var config = CustomSplashScreenMod.Instance?._config;
                if (config == null || !config.Enabled)
                    return true;

                if (string.IsNullOrEmpty(config.SelectedVideoId) || config.SelectedVideoId == "default")
                    return true;

                CustomSplashScreenMod.Log?.LogInfo($"[Harmony] Intercepted VideoPlayer.Play() - replacing video source");
                
                // Get the video URL for the selected option
                string videoUrl = GetVideoUrl(config.SelectedVideoId);
                if (string.IsNullOrEmpty(videoUrl))
                {
                    CustomSplashScreenMod.Log?.LogWarning("[Harmony] No valid video URL for selected option");
                    return true;
                }

                // Mark as replaced FIRST to prevent any re-entry
                if (CustomSplashScreenMod.Instance != null)
                    CustomSplashScreenMod.Instance._videoReplaced = true;

                // Replace the video source
                __instance.Stop();
                __instance.source = VideoSource.Url;
                __instance.url = videoUrl;
                __instance.aspectRatio = VideoAspectRatio.FitInside;
                
                __instance.prepareCompleted += (vp) =>
                {
                    CustomSplashScreenMod.Log?.LogInfo("[Harmony] Custom video prepared, playing...");
                    _isPlayingCustomVideo = true;
                    vp.Play();
                    _isPlayingCustomVideo = false;
                };

                __instance.errorReceived += (vp, message) =>
                {
                    CustomSplashScreenMod.Log?.LogError($"[Harmony] Video error: {message}");
                };

                CustomSplashScreenMod.Log?.LogInfo($"[Harmony] Preparing custom video: {videoUrl}");
                __instance.Prepare();

                var skipObj = new GameObject("SplashSkipHandler");
                var handler = skipObj.AddComponent<SplashSkipHandler>();
                handler.Init(__instance);

                return false; // Skip the original Play() call, we'll call it after prepare
            }
            catch (Exception ex)
            {
                CustomSplashScreenMod.Log?.LogError($"[Harmony] Error in VideoPlayer patch: {ex.Message}");
                return true; // Continue with original on error
            }
        }

        private static string GetVideoUrl(string videoId)
        {
            // Use the dynamically loaded AvailableVideos dictionary
            if (!CustomSplashScreenMod.AvailableVideos.TryGetValue(videoId, out var video))
            {
                CustomSplashScreenMod.Log?.LogWarning($"[Harmony] Unknown video ID: {videoId}");
                return null;
            }
            
            if (string.IsNullOrEmpty(video.LocalFileName))
            {
                // Default video - don't replace
                return null;
            }

            // Build path to BepInEx/SplashVideos/
            string pluginFolder = Path.GetDirectoryName(CustomSplashScreenMod.Instance?.Info?.Location ?? "");
            string bepInExFolder = Path.GetDirectoryName(pluginFolder);
            string splashVideosFolder = Path.Combine(bepInExFolder, "SplashVideos");
            string videoPath = Path.Combine(splashVideosFolder, video.LocalFileName);

            if (File.Exists(videoPath))
            {
                CustomSplashScreenMod.Log?.LogInfo($"[Harmony] Found local video: {videoPath}");
                return "file:///" + videoPath.Replace("\\", "/");
            }

            CustomSplashScreenMod.Log?.LogWarning($"[Harmony] Video file not found: {videoPath}");
            CustomSplashScreenMod.Log?.LogInfo("[Harmony] Download videos via the Modding Launcher settings.");
            return null;
        }
    }

    /// <summary>
    /// Scene-attached MonoBehaviour that handles Escape-to-skip and renders the hint overlay.
    /// Lives on a GameObject inside SplashScreenScene and is destroyed with the scene.
    /// </summary>
    public class SplashSkipHandler : MonoBehaviour
    {
        private VideoPlayer _videoPlayer;
        private float _timer;
        private bool _active;

        public void Init(VideoPlayer videoPlayer)
        {
            _videoPlayer = videoPlayer;
            _active = true;
            _timer = 0f;

            videoPlayer.loopPointReached += OnVideoFinished;

            CustomSplashScreenMod.Log?.LogInfo("SplashSkipHandler attached to SplashScreenScene");
        }

        private void Update()
        {
            if (!_active || _videoPlayer == null) return;

            _timer += Time.unscaledDeltaTime;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CustomSplashScreenMod.Log?.LogInfo("User pressed Escape - skipping splash video");
                Skip();
            }
        }

        private void Skip()
        {
            _active = false;

            if (_videoPlayer != null)
            {
                _videoPlayer.loopPointReached -= OnVideoFinished;
                _videoPlayer.Stop();
                _videoPlayer = null;
            }

            int nextIndex = SceneManager.GetActiveScene().buildIndex + 1;
            if (nextIndex < SceneManager.sceneCountInBuildSettings)
            {
                SceneManager.LoadScene(nextIndex);
            }
        }

        private void OnVideoFinished(VideoPlayer vp)
        {
            _active = false;
            _videoPlayer = null;
        }

        private void OnGUI()
        {
            if (!_active) return;

            float alpha = _timer < 3f ? 0.9f : Mathf.Max(0.35f, 0.9f - (_timer - 3f) * 0.3f);

            float padding = 24f;
            float boxW = 160f;
            float boxH = 32f;
            float x = Screen.width - boxW - padding;
            float y = Screen.height - boxH - padding;
            var rect = new Rect(x, y, boxW, boxH);

            var shadowStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, alpha);

            var textStyle = new GUIStyle(shadowStyle);
            textStyle.normal.textColor = new Color(1f, 1f, 1f, alpha);

            GUI.Label(new Rect(rect.x + 1, rect.y + 1, rect.width, rect.height), "Esc to skip intro", shadowStyle);
            GUI.Label(rect, "Esc to skip intro", textStyle);
        }

        private void OnDestroy()
        {
            if (_videoPlayer != null)
                _videoPlayer.loopPointReached -= OnVideoFinished;
        }
    }
}
