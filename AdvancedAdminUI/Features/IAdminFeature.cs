namespace AdvancedAdminUI.Features
{
    /// <summary>
    /// Interface for admin helper features
    /// </summary>
    public interface IAdminFeature
    {
        /// <summary>
        /// Name of the feature (for logging/UI)
        /// </summary>
        string FeatureName { get; }
        
        /// <summary>
        /// Whether the feature is currently enabled
        /// </summary>
        bool IsEnabled { get; }
        
        /// <summary>
        /// Called when the feature should be enabled
        /// </summary>
        void Enable();
        
        /// <summary>
        /// Called when the feature should be disabled
        /// </summary>
        void Disable();
        
        /// <summary>
        /// Called every frame when enabled
        /// </summary>
        void OnUpdate();
        
        /// <summary>
        /// Called for GUI rendering when enabled (optional - can be empty)
        /// </summary>
        void OnGUI();
        
        /// <summary>
        /// Called when the mod is shutting down - cleanup resources
        /// </summary>
        void OnApplicationQuit();
    }
}

