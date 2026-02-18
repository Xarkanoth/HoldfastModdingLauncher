using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HoldfastModdingLauncher.Core;

namespace HoldfastModdingLauncher.Services
{
    #region API DTOs

    public class ApiLoginResponse
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }
    }

    public class ApiUserInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("lastLoginAt")]
        public DateTime? LastLoginAt { get; set; }

        public bool IsMaster => string.Equals(Role, "Master", StringComparison.OrdinalIgnoreCase);
    }

    public class ApiModInfo
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("modKey")]
        public string ModKey { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    public class ApiVersionInfo
    {
        [JsonPropertyName("modKey")]
        public string ModKey { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("updatedAt")]
        public DateTime UpdatedAt { get; set; }
    }

    public class ApiUserDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("lastLoginAt")]
        public DateTime? LastLoginAt { get; set; }
    }

    public class ApiPermissionDto
    {
        [JsonPropertyName("modId")]
        public int ModId { get; set; }

        [JsonPropertyName("modKey")]
        public string ModKey { get; set; } = string.Empty;

        [JsonPropertyName("modName")]
        public string ModName { get; set; } = string.Empty;

        [JsonPropertyName("grantedAt")]
        public DateTime GrantedAt { get; set; }
    }

    public class ApiAuditLogDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("userId")]
        public int? UserId { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("details")]
        public string Details { get; set; } = string.Empty;

        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    public class ApiLauncherVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;

        [JsonPropertyName("releaseNotes")]
        public string ReleaseNotes { get; set; } = string.Empty;
    }

    public class StoredAuth
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("serverUrl")]
        public string ServerUrl { get; set; } = string.Empty;
    }

    #endregion

    public class ApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private StoredAuth _auth;
        private ApiUserInfo _currentUser;
        private bool _isAvailable;

        private static readonly string AuthFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HoldfastModding", "auth.json");

        public bool IsAuthenticated => _auth != null && !string.IsNullOrEmpty(_auth.AccessToken);
        public bool IsAvailable => _isAvailable;
        public ApiUserInfo CurrentUser => _currentUser;
        public bool IsMaster => _currentUser?.IsMaster == true;

        public event Action<bool> AuthStateChanged;

        public ApiClient()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HoldfastModdingLauncher");
            LoadStoredAuth();
        }

        public string BaseUrl
        {
            get
            {
                var url = LauncherSettings.Instance.ModServerUrl;
                return string.IsNullOrEmpty(url) ? null : url.TrimEnd('/');
            }
        }

        public bool IsConfigured => !string.IsNullOrEmpty(BaseUrl);

        #region Auth

        public async Task<(bool Success, string Error)> LoginAsync(string username, string password)
        {
            if (!IsConfigured) return (false, "Server URL not configured");

            try
            {
                var payload = JsonSerializer.Serialize(new { username, password });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{BaseUrl}/api/auth/login", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    Logger.LogWarning($"Login failed: {response.StatusCode} - {errorBody}");
                    return (false, response.StatusCode == HttpStatusCode.Unauthorized
                        ? "Invalid username or password"
                        : $"Server error: {response.StatusCode}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var loginResponse = JsonSerializer.Deserialize<ApiLoginResponse>(json);
                if (loginResponse == null)
                    return (false, "Invalid server response");

                _auth = new StoredAuth
                {
                    AccessToken = loginResponse.AccessToken,
                    RefreshToken = loginResponse.RefreshToken,
                    ExpiresAt = loginResponse.ExpiresAt,
                    ServerUrl = BaseUrl ?? string.Empty
                };

                SaveStoredAuth();
                ApplyAuthHeader();

                _currentUser = await FetchCurrentUserAsync();
                _isAvailable = true;
                AuthStateChanged?.Invoke(true);

                Logger.LogInfo($"API login successful: {_currentUser?.Username ?? username}");
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                Logger.LogError($"API login error: {ex.Message}");
                return (false, $"Connection error: {ex.Message}");
            }
        }

        public async Task<bool> TryRestoreSessionAsync()
        {
            if (!IsConfigured || _auth == null || string.IsNullOrEmpty(_auth.AccessToken))
                return false;

            if (_auth.ServerUrl != BaseUrl)
            {
                ClearAuth();
                return false;
            }

            try
            {
                if (_auth.ExpiresAt < DateTime.UtcNow.AddMinutes(5))
                {
                    var refreshed = await RefreshTokenAsync();
                    if (!refreshed) return false;
                }

                ApplyAuthHeader();
                _currentUser = await FetchCurrentUserAsync();
                if (_currentUser == null)
                {
                    ClearAuth();
                    return false;
                }

                _isAvailable = true;
                AuthStateChanged?.Invoke(true);
                Logger.LogInfo($"API session restored for {_currentUser.Username}");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to restore API session: {ex.Message}");
                ClearAuth();
                return false;
            }
        }

        public void Logout()
        {
            ClearAuth();
            _currentUser = null;
            _isAvailable = false;
            AuthStateChanged?.Invoke(false);
            Logger.LogInfo("API logout");
        }

        private async Task<bool> RefreshTokenAsync()
        {
            if (_auth == null || string.IsNullOrEmpty(_auth.RefreshToken))
                return false;

            try
            {
                var payload = JsonSerializer.Serialize(new { refreshToken = _auth.RefreshToken });
                var content = new StringContent(payload, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{BaseUrl}/api/auth/refresh", content);

                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                var loginResponse = JsonSerializer.Deserialize<ApiLoginResponse>(json);
                if (loginResponse == null) return false;

                _auth.AccessToken = loginResponse.AccessToken;
                _auth.RefreshToken = loginResponse.RefreshToken;
                _auth.ExpiresAt = loginResponse.ExpiresAt;
                SaveStoredAuth();
                ApplyAuthHeader();
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Token refresh failed: {ex.Message}");
                return false;
            }
        }

        private async Task<ApiUserInfo> FetchCurrentUserAsync()
        {
            var response = await AuthenticatedGetAsync("/api/auth/me");
            if (response == null || !response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ApiUserInfo>(json);
        }

        #endregion

        #region Mods

        public async Task<List<ApiModInfo>> GetModsAsync()
        {
            var response = await AuthenticatedGetAsync("/api/mods");
            if (response == null || !response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ApiModInfo>>(json);
        }

        public async Task<Stream> DownloadModAsync(int modId)
        {
            var response = await AuthenticatedGetAsync($"/api/mods/{modId}/download");
            if (response == null || !response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsStreamAsync();
        }

        public async Task<ApiVersionInfo> GetModVersionAsync(int modId)
        {
            var response = await AuthenticatedGetAsync($"/api/mods/{modId}/version");
            if (response == null || !response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ApiVersionInfo>(json);
        }

        #endregion

        #region Admin

        public async Task<List<ApiUserDto>> GetUsersAsync()
        {
            var response = await AuthenticatedGetAsync("/api/admin/users");
            if (response == null || !response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ApiUserDto>>(json);
        }

        public async Task<ApiUserDto> CreateUserAsync(string username, string password, string displayName = null, string role = "Member")
        {
            var payload = JsonSerializer.Serialize(new { username, password, displayName, role });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await AuthenticatedPostAsync("/api/admin/users", content);
            if (response == null || !response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ApiUserDto>(json);
        }

        public async Task<bool> UpdateUserAsync(int userId, string displayName = null, string role = null, bool? isActive = null, string password = null)
        {
            var obj = new Dictionary<string, object>();
            if (displayName != null) obj["displayName"] = displayName;
            if (role != null) obj["role"] = role;
            if (isActive.HasValue) obj["isActive"] = isActive.Value;
            if (password != null) obj["password"] = password;

            var payload = JsonSerializer.Serialize(obj);
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await AuthenticatedPutAsync($"/api/admin/users/{userId}", content);
            return response != null && response.IsSuccessStatusCode;
        }

        public async Task<bool> DeactivateUserAsync(int userId)
        {
            var response = await AuthenticatedDeleteAsync($"/api/admin/users/{userId}");
            return response != null && response.IsSuccessStatusCode;
        }

        public async Task<List<ApiPermissionDto>> GetUserPermissionsAsync(int userId)
        {
            var response = await AuthenticatedGetAsync($"/api/admin/users/{userId}/permissions");
            if (response == null || !response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ApiPermissionDto>>(json);
        }

        public async Task<bool> SetUserPermissionsAsync(int userId, List<int> modIds)
        {
            var payload = JsonSerializer.Serialize(new { modIds });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var response = await AuthenticatedPutAsync($"/api/admin/users/{userId}/permissions", content);
            return response != null && response.IsSuccessStatusCode;
        }

        public async Task<ApiModInfo> UploadModAsync(string modKey, string name, string version, string dllPath, string description = null, string category = null)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(modKey), "ModKey");
            form.Add(new StringContent(name), "Name");
            form.Add(new StringContent(version), "Version");
            if (description != null) form.Add(new StringContent(description), "Description");
            if (category != null) form.Add(new StringContent(category), "Category");

            var fileBytes = File.ReadAllBytes(dllPath);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "dllFile", Path.GetFileName(dllPath));

            var response = await AuthenticatedPostAsync("/api/admin/mods", form);
            if (response == null || !response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<ApiModInfo>(json);
        }

        public async Task<List<ApiAuditLogDto>> GetAuditLogsAsync(int count = 100)
        {
            var response = await AuthenticatedGetAsync($"/api/admin/audit?count={count}");
            if (response == null || !response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ApiAuditLogDto>>(json);
        }

        #endregion

        #region Launcher

        public async Task<ApiLauncherVersion> GetLauncherVersionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/launcher/version");
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<ApiLauncherVersion>(json);
            }
            catch
            {
                return null;
            }
        }

        public async Task<bool> CheckHealthAsync()
        {
            if (!IsConfigured) return false;

            try
            {
                var response = await _httpClient.GetAsync($"{BaseUrl}/api/health");
                _isAvailable = response.IsSuccessStatusCode;
                return _isAvailable;
            }
            catch
            {
                _isAvailable = false;
                return false;
            }
        }

        #endregion

        #region HTTP Helpers

        private async Task<HttpResponseMessage> AuthenticatedGetAsync(string path)
        {
            return await AuthenticatedRequestAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}{path}");
                return request;
            });
        }

        private async Task<HttpResponseMessage> AuthenticatedPostAsync(string path, HttpContent content)
        {
            return await AuthenticatedRequestAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}{path}");
                request.Content = content;
                return request;
            });
        }

        private async Task<HttpResponseMessage> AuthenticatedPutAsync(string path, HttpContent content)
        {
            return await AuthenticatedRequestAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Put, $"{BaseUrl}{path}");
                request.Content = content;
                return request;
            });
        }

        private async Task<HttpResponseMessage> AuthenticatedDeleteAsync(string path)
        {
            return await AuthenticatedRequestAsync(() =>
            {
                var request = new HttpRequestMessage(HttpMethod.Delete, $"{BaseUrl}{path}");
                return request;
            });
        }

        private async Task<HttpResponseMessage> AuthenticatedRequestAsync(Func<HttpRequestMessage> requestFactory)
        {
            if (!IsAuthenticated) return null;

            if (_auth.ExpiresAt < DateTime.UtcNow.AddMinutes(2))
            {
                var refreshed = await RefreshTokenAsync();
                if (!refreshed) return null;
            }

            var request = requestFactory();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.AccessToken);
            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var refreshed = await RefreshTokenAsync();
                if (!refreshed) return null;

                request = requestFactory();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _auth.AccessToken);
                response = await _httpClient.SendAsync(request);
            }

            return response;
        }

        private void ApplyAuthHeader()
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _auth?.AccessToken ?? "");
        }

        #endregion

        #region Token Storage

        private void LoadStoredAuth()
        {
            try
            {
                if (File.Exists(AuthFilePath))
                {
                    var json = File.ReadAllText(AuthFilePath);
                    _auth = JsonSerializer.Deserialize<StoredAuth>(json) ?? new StoredAuth();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to load stored auth: {ex.Message}");
                _auth = null;
            }
        }

        private void SaveStoredAuth()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(AuthFilePath));
                var json = JsonSerializer.Serialize(_auth, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AuthFilePath, json);
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to save auth: {ex.Message}");
            }
        }

        private void ClearAuth()
        {
            _auth = null;
            _httpClient.DefaultRequestHeaders.Authorization = null;
            try
            {
                if (File.Exists(AuthFilePath))
                    File.Delete(AuthFilePath);
            }
            catch { }
        }

        #endregion

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
