using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using HoldfastModdingLauncher.Core;
using HoldfastModdingLauncher.Services;

namespace HoldfastModdingLauncher
{
    public partial class AdminPanelForm : Form
    {
        private readonly ApiClient _apiClient;

        private static readonly Color DarkBg = Color.FromArgb(18, 18, 22);
        private static readonly Color DarkPanel = Color.FromArgb(28, 28, 35);
        private static readonly Color DarkInput = Color.FromArgb(38, 38, 45);
        private static readonly Color AccentCyan = Color.FromArgb(0, 200, 200);
        private static readonly Color AccentMagenta = Color.FromArgb(200, 0, 150);
        private static readonly Color TextLight = Color.FromArgb(240, 240, 240);
        private static readonly Color TextGray = Color.FromArgb(140, 140, 140);
        private static readonly Color SuccessGreen = Color.FromArgb(80, 200, 120);
        private static readonly Color DangerRed = Color.FromArgb(220, 60, 60);

        private TabControl _tabControl;

        // Users tab
        private ListView _usersListView;
        private Button _createUserButton;
        private Button _editUserButton;
        private Button _deactivateUserButton;

        // Permissions tab
        private ComboBox _permUserCombo;
        private CheckedListBox _permModsList;
        private Button _savePermissionsButton;
        private Label _permStatusLabel;

        // Mods tab
        private ListView _modsListView;
        private Button _uploadModButton;
        private Label _modsStatusLabel;

        // Audit tab
        private ListView _auditListView;
        private Button _refreshAuditButton;

        private bool _isDragging = false;
        private Point _dragOffset;

        public AdminPanelForm(ApiClient apiClient)
        {
            _apiClient = apiClient;
            InitializeComponent();
            _ = LoadAllDataAsync();
        }

        private void InitializeComponent()
        {
            SuspendLayout();
            Text = "Admin Panel";
            Size = new Size(950, 700);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = DarkBg;
            DoubleBuffered = true;

            // Title bar
            var titleBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Color.FromArgb(12, 12, 16)
            };
            titleBar.MouseDown += (s, e) => { _isDragging = true; _dragOffset = e.Location; };
            titleBar.MouseMove += (s, e) => { if (_isDragging) Location = new Point(Location.X + e.X - _dragOffset.X, Location.Y + e.Y - _dragOffset.Y); };
            titleBar.MouseUp += (s, e) => _isDragging = false;
            Controls.Add(titleBar);

            var titleLabel = new Label
            {
                Text = "ADMIN PANEL",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = AccentMagenta,
                AutoSize = true,
                Location = new Point(12, 8)
            };
            titleBar.Controls.Add(titleLabel);

            var closeButton = new Button
            {
                Text = "✕  Close",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Size = new Size(90, 36),
                Location = new Point(Width - 90, 0),
                FlatStyle = FlatStyle.Flat,
                ForeColor = TextLight,
                BackColor = Color.FromArgb(140, 30, 30),
                Cursor = Cursors.Hand,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            closeButton.FlatAppearance.BorderSize = 0;
            closeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(200, 50, 50);
            closeButton.Click += (s, e) => Close();
            titleBar.Controls.Add(closeButton);

            // Visible border around the form
            Paint += (s, e) =>
            {
                using (var pen = new Pen(Color.FromArgb(60, 60, 70), 2))
                    e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            };

            _tabControl = new TabControl
            {
                Location = new Point(10, 42),
                Size = new Size(930, 648),
                Font = new Font("Segoe UI", 10F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_tabControl);

            BuildUsersTab();
            BuildPermissionsTab();
            BuildModsTab();
            BuildAuditTab();

            ResumeLayout();
        }

        #region Users Tab

        private Label _usersStatusLabel;
        private Button _resetPasswordButton;

        private void BuildUsersTab()
        {
            var tab = new TabPage("Users") { BackColor = DarkBg };
            _tabControl.TabPages.Add(tab);

            _usersListView = new ListView
            {
                Location = new Point(10, 10),
                Size = new Size(890, 490),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = DarkPanel,
                ForeColor = TextLight,
                Font = new Font("Segoe UI", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _usersListView.Columns.Add("ID", 50);
            _usersListView.Columns.Add("Username", 140);
            _usersListView.Columns.Add("Display Name", 140);
            _usersListView.Columns.Add("Role", 80);
            _usersListView.Columns.Add("Active", 60);
            _usersListView.Columns.Add("Last Login", 160);
            _usersListView.Columns.Add("Created", 160);
            tab.Controls.Add(_usersListView);

            int btnY = 510;
            _createUserButton = CreateStyledButton("+ Create User", new Point(10, btnY), AccentCyan);
            _createUserButton.Size = new Size(130, 32);
            _createUserButton.Click += CreateUserButton_Click;
            tab.Controls.Add(_createUserButton);

            _editUserButton = CreateStyledButton("Edit User", new Point(150, btnY), AccentCyan);
            _editUserButton.Click += EditUserButton_Click;
            tab.Controls.Add(_editUserButton);

            _resetPasswordButton = CreateStyledButton("Reset Password", new Point(280, btnY), AccentMagenta);
            _resetPasswordButton.Size = new Size(140, 32);
            _resetPasswordButton.Click += ResetPasswordButton_Click;
            tab.Controls.Add(_resetPasswordButton);

            _deactivateUserButton = CreateStyledButton("Deactivate", new Point(430, btnY), DangerRed);
            _deactivateUserButton.Click += DeactivateUserButton_Click;
            tab.Controls.Add(_deactivateUserButton);

            var refreshButton = CreateStyledButton("Refresh", new Point(560, btnY), TextGray);
            refreshButton.Click += async (s, e) => await LoadUsersAsync();
            tab.Controls.Add(refreshButton);

            _usersStatusLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(10, btnY + 38),
                Size = new Size(890, 20),
                BackColor = Color.Transparent
            };
            tab.Controls.Add(_usersStatusLabel);
        }

        private async void CreateUserButton_Click(object sender, EventArgs e)
        {
            using var dlg = new CreateEditUserDialog(null);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _usersStatusLabel.Text = "Creating user...";
                _usersStatusLabel.ForeColor = AccentCyan;
                try
                {
                    var user = await _apiClient.CreateUserAsync(
                        dlg.Username, dlg.Password, dlg.DisplayName, dlg.Role);
                    if (user != null)
                    {
                        _usersStatusLabel.Text = $"Created user '{user.Username}' successfully";
                        _usersStatusLabel.ForeColor = SuccessGreen;
                        await LoadUsersAsync();
                    }
                    else
                    {
                        _usersStatusLabel.Text = "Failed to create user. Username may already exist.";
                        _usersStatusLabel.ForeColor = DangerRed;
                    }
                }
                catch (Exception ex)
                {
                    _usersStatusLabel.Text = $"Error: {ex.Message}";
                    _usersStatusLabel.ForeColor = DangerRed;
                }
            }
        }

        private async void EditUserButton_Click(object sender, EventArgs e)
        {
            if (_usersListView.SelectedItems.Count == 0)
            {
                _usersStatusLabel.Text = "Select a user first";
                _usersStatusLabel.ForeColor = TextGray;
                return;
            }
            var item = _usersListView.SelectedItems[0];
            int userId = int.Parse(item.SubItems[0].Text);

            using var dlg = new CreateEditUserDialog(new CreateEditUserDialog.UserEditData
            {
                Id = userId,
                Username = item.SubItems[1].Text,
                DisplayName = item.SubItems[2].Text,
                Role = item.SubItems[3].Text,
                IsActive = item.SubItems[4].Text == "Yes"
            });

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _usersStatusLabel.Text = "Updating user...";
                _usersStatusLabel.ForeColor = AccentCyan;
                try
                {
                    bool success = await _apiClient.UpdateUserAsync(userId, dlg.DisplayName, dlg.Role,
                        dlg.IsActive, string.IsNullOrEmpty(dlg.Password) ? null : dlg.Password);
                    _usersStatusLabel.Text = success ? "User updated" : "Failed to update user";
                    _usersStatusLabel.ForeColor = success ? SuccessGreen : DangerRed;
                    if (success) await LoadUsersAsync();
                }
                catch (Exception ex)
                {
                    _usersStatusLabel.Text = $"Error: {ex.Message}";
                    _usersStatusLabel.ForeColor = DangerRed;
                }
            }
        }

        private async void ResetPasswordButton_Click(object sender, EventArgs e)
        {
            if (_usersListView.SelectedItems.Count == 0)
            {
                _usersStatusLabel.Text = "Select a user first";
                _usersStatusLabel.ForeColor = TextGray;
                return;
            }

            var item = _usersListView.SelectedItems[0];
            int userId = int.Parse(item.SubItems[0].Text);
            string username = item.SubItems[1].Text;

            using var dlg = new ResetPasswordDialog(username);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _usersStatusLabel.Text = "Resetting password...";
                _usersStatusLabel.ForeColor = AccentCyan;
                try
                {
                    bool success = await _apiClient.UpdateUserAsync(userId, password: dlg.NewPassword);
                    _usersStatusLabel.Text = success
                        ? $"Password reset for '{username}'"
                        : "Failed to reset password";
                    _usersStatusLabel.ForeColor = success ? SuccessGreen : DangerRed;
                }
                catch (Exception ex)
                {
                    _usersStatusLabel.Text = $"Error: {ex.Message}";
                    _usersStatusLabel.ForeColor = DangerRed;
                }
            }
        }

        private async void DeactivateUserButton_Click(object sender, EventArgs e)
        {
            if (_usersListView.SelectedItems.Count == 0)
            {
                _usersStatusLabel.Text = "Select a user first";
                _usersStatusLabel.ForeColor = TextGray;
                return;
            }
            var item = _usersListView.SelectedItems[0];
            int userId = int.Parse(item.SubItems[0].Text);
            string username = item.SubItems[1].Text;

            if (MessageBox.Show($"Deactivate user '{username}'?\n\nThey will no longer be able to log in.",
                "Confirm Deactivation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                try
                {
                    bool success = await _apiClient.DeactivateUserAsync(userId);
                    _usersStatusLabel.Text = success
                        ? $"User '{username}' deactivated"
                        : "Failed to deactivate user";
                    _usersStatusLabel.ForeColor = success ? SuccessGreen : DangerRed;
                    if (success) await LoadUsersAsync();
                }
                catch (Exception ex)
                {
                    _usersStatusLabel.Text = $"Error: {ex.Message}";
                    _usersStatusLabel.ForeColor = DangerRed;
                }
            }
        }

        #endregion

        #region Permissions Tab

        private void BuildPermissionsTab()
        {
            var tab = new TabPage("Permissions") { BackColor = DarkBg };
            _tabControl.TabPages.Add(tab);

            var userLabel = new Label
            {
                Text = "Select User:",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextLight,
                Location = new Point(10, 15),
                AutoSize = true
            };
            tab.Controls.Add(userLabel);

            _permUserCombo = new ComboBox
            {
                Location = new Point(120, 12),
                Size = new Size(250, 28),
                Font = new Font("Segoe UI", 10F),
                BackColor = DarkInput,
                ForeColor = TextLight,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat
            };
            _permUserCombo.SelectedIndexChanged += async (s, e) => await LoadPermissionsForSelectedUserAsync();
            tab.Controls.Add(_permUserCombo);

            var modsLabel = new Label
            {
                Text = "Accessible Mods:",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = AccentCyan,
                Location = new Point(10, 55),
                AutoSize = true
            };
            tab.Controls.Add(modsLabel);

            _permModsList = new CheckedListBox
            {
                Location = new Point(10, 80),
                Size = new Size(400, 340),
                BackColor = DarkPanel,
                ForeColor = TextLight,
                Font = new Font("Segoe UI", 10F),
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true
            };
            tab.Controls.Add(_permModsList);

            _savePermissionsButton = CreateStyledButton("Save Permissions", new Point(10, 430), SuccessGreen);
            _savePermissionsButton.Size = new Size(160, 32);
            _savePermissionsButton.Click += SavePermissionsButton_Click;
            tab.Controls.Add(_savePermissionsButton);

            _permStatusLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(180, 436),
                AutoSize = true
            };
            tab.Controls.Add(_permStatusLabel);
        }

        private async void SavePermissionsButton_Click(object sender, EventArgs e)
        {
            if (_permUserCombo.SelectedItem == null) return;
            var userData = (UserComboItem)_permUserCombo.SelectedItem;

            var selectedModIds = new List<int>();
            for (int i = 0; i < _permModsList.Items.Count; i++)
            {
                if (_permModsList.GetItemChecked(i))
                {
                    var modItem = (ModListItem)_permModsList.Items[i];
                    selectedModIds.Add(modItem.ModId);
                }
            }

            bool success = await _apiClient.SetUserPermissionsAsync(userData.UserId, selectedModIds);
            _permStatusLabel.Text = success ? "Permissions saved!" : "Failed to save permissions";
            _permStatusLabel.ForeColor = success ? SuccessGreen : DangerRed;
        }

        private async System.Threading.Tasks.Task LoadPermissionsForSelectedUserAsync()
        {
            if (_permUserCombo.SelectedItem == null) return;
            var userData = (UserComboItem)_permUserCombo.SelectedItem;

            var permissions = await _apiClient.GetUserPermissionsAsync(userData.UserId);
            if (permissions == null) return;

            var grantedModIds = new HashSet<int>(permissions.Select(p => p.ModId));

            for (int i = 0; i < _permModsList.Items.Count; i++)
            {
                var modItem = (ModListItem)_permModsList.Items[i];
                _permModsList.SetItemChecked(i, grantedModIds.Contains(modItem.ModId));
            }

            _permStatusLabel.Text = $"{grantedModIds.Count} mod(s) granted";
            _permStatusLabel.ForeColor = TextGray;
        }

        #endregion

        #region Mods Tab

        private void BuildModsTab()
        {
            var tab = new TabPage("Mods") { BackColor = DarkBg };
            _tabControl.TabPages.Add(tab);

            _modsListView = new ListView
            {
                Location = new Point(10, 10),
                Size = new Size(800, 400),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = DarkPanel,
                ForeColor = TextLight,
                Font = new Font("Segoe UI", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _modsListView.Columns.Add("ID", 50);
            _modsListView.Columns.Add("Mod Key", 160);
            _modsListView.Columns.Add("Name", 180);
            _modsListView.Columns.Add("Version", 80);
            _modsListView.Columns.Add("Category", 100);
            _modsListView.Columns.Add("Size", 80);
            _modsListView.Columns.Add("Updated", 140);
            tab.Controls.Add(_modsListView);

            _uploadModButton = CreateStyledButton("Upload Mod DLL", new Point(10, 420), AccentCyan);
            _uploadModButton.Size = new Size(150, 32);
            _uploadModButton.Click += UploadModButton_Click;
            tab.Controls.Add(_uploadModButton);

            var refreshModsButton = CreateStyledButton("Refresh", new Point(170, 420), TextGray);
            refreshModsButton.Click += async (s, e) => await LoadModsAsync();
            tab.Controls.Add(refreshModsButton);

            _modsStatusLabel = new Label
            {
                Text = "",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextGray,
                Location = new Point(300, 426),
                AutoSize = true
            };
            tab.Controls.Add(_modsStatusLabel);
        }

        private async void UploadModButton_Click(object sender, EventArgs e)
        {
            using var dlg = new UploadModDialog();
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _modsStatusLabel.Text = "Uploading...";
                _modsStatusLabel.ForeColor = AccentCyan;
                _uploadModButton.Enabled = false;

                try
                {
                    var result = await _apiClient.UploadModAsync(
                        dlg.ModKey, dlg.ModName, dlg.Version, dlg.DllPath,
                        dlg.Description, dlg.Category);

                    if (result != null)
                    {
                        _modsStatusLabel.Text = $"Uploaded {result.ModKey} v{result.Version}";
                        _modsStatusLabel.ForeColor = SuccessGreen;
                        await LoadModsAsync();
                    }
                    else
                    {
                        _modsStatusLabel.Text = "Upload failed";
                        _modsStatusLabel.ForeColor = DangerRed;
                    }
                }
                catch (Exception ex)
                {
                    _modsStatusLabel.Text = $"Error: {ex.Message}";
                    _modsStatusLabel.ForeColor = DangerRed;
                }
                finally
                {
                    _uploadModButton.Enabled = true;
                }
            }
        }

        #endregion

        #region Audit Tab

        private void BuildAuditTab()
        {
            var tab = new TabPage("Audit Log") { BackColor = DarkBg };
            _tabControl.TabPages.Add(tab);

            _auditListView = new ListView
            {
                Location = new Point(10, 10),
                Size = new Size(800, 430),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                BackColor = DarkPanel,
                ForeColor = TextLight,
                Font = new Font("Segoe UI", 9F),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            _auditListView.Columns.Add("ID", 50);
            _auditListView.Columns.Add("Time", 160);
            _auditListView.Columns.Add("User", 120);
            _auditListView.Columns.Add("Action", 120);
            _auditListView.Columns.Add("Details", 340);
            tab.Controls.Add(_auditListView);

            _refreshAuditButton = CreateStyledButton("Refresh", new Point(10, 450), TextGray);
            _refreshAuditButton.Click += async (s, e) => await LoadAuditAsync();
            tab.Controls.Add(_refreshAuditButton);
        }

        #endregion

        #region Data Loading

        private async System.Threading.Tasks.Task LoadAllDataAsync()
        {
            await LoadUsersAsync();
            await LoadModsAsync();
            await LoadAuditAsync();
        }

        private async System.Threading.Tasks.Task LoadUsersAsync()
        {
            var users = await _apiClient.GetUsersAsync();
            if (users == null) return;

            _usersListView.Items.Clear();
            _permUserCombo.Items.Clear();

            foreach (var user in users)
            {
                var item = new ListViewItem(user.Id.ToString());
                item.SubItems.Add(user.Username);
                item.SubItems.Add(user.DisplayName ?? "");
                item.SubItems.Add(user.Role);
                item.SubItems.Add(user.IsActive ? "Yes" : "No");
                item.SubItems.Add(user.LastLoginAt?.ToString("yyyy-MM-dd HH:mm") ?? "Never");
                item.SubItems.Add(user.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
                item.ForeColor = user.IsActive ? TextLight : TextGray;
                _usersListView.Items.Add(item);

                var roleTag = user.Role == "Master" ? " [Master - full access]" : "";
                _permUserCombo.Items.Add(new UserComboItem { UserId = user.Id, Display = $"{user.Username} ({user.DisplayName ?? ""}){roleTag}" });
            }
        }

        private async System.Threading.Tasks.Task LoadModsAsync()
        {
            var mods = await _apiClient.GetModsAsync();
            if (mods == null) return;

            _modsListView.Items.Clear();
            _permModsList.Items.Clear();

            foreach (var mod in mods)
            {
                var item = new ListViewItem(mod.Id.ToString());
                item.SubItems.Add(mod.ModKey);
                item.SubItems.Add(mod.Name);
                item.SubItems.Add(mod.Version);
                item.SubItems.Add(mod.Category ?? "");
                item.SubItems.Add(FormatBytes(mod.FileSize));
                item.SubItems.Add(mod.UpdatedAt.ToString("yyyy-MM-dd HH:mm"));
                _modsListView.Items.Add(item);

                _permModsList.Items.Add(new ModListItem { ModId = mod.Id, Display = $"{mod.Name} ({mod.ModKey})" });
            }
        }

        private async System.Threading.Tasks.Task LoadAuditAsync()
        {
            var logs = await _apiClient.GetAuditLogsAsync();
            if (logs == null) return;

            _auditListView.Items.Clear();
            foreach (var log in logs)
            {
                var item = new ListViewItem(log.Id.ToString());
                item.SubItems.Add(log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(log.Username ?? "System");
                item.SubItems.Add(log.Action);
                item.SubItems.Add(log.Details ?? "");
                _auditListView.Items.Add(item);
            }
        }

        #endregion

        #region Helpers

        private static Button CreateStyledButton(string text, Point location, Color accentColor)
        {
            var btn = new Button
            {
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Size = new Size(120, 32),
                Location = location,
                BackColor = DarkPanel,
                ForeColor = accentColor,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = accentColor;
            btn.FlatAppearance.BorderSize = 1;
            return btn;
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
            return $"{len:0.##} {sizes[order]}";
        }

        private class UserComboItem
        {
            public int UserId { get; set; }
            public string Display { get; set; } = "";
            public override string ToString() => Display;
        }

        private class ModListItem
        {
            public int ModId { get; set; }
            public string Display { get; set; } = "";
            public override string ToString() => Display;
        }

        #endregion
    }

    #region Sub-Dialogs

    public class CreateEditUserDialog : Form
    {
        public string Username { get; private set; }
        public string Password { get; private set; }
        public string DisplayName { get; private set; }
        public string Role { get; private set; }
        public bool IsActive { get; private set; } = true;

        private TextBox _usernameBox;
        private TextBox _passwordBox;
        private TextBox _displayNameBox;
        private ComboBox _roleCombo;
        private CheckBox _activeCheckBox;
        private bool _isEdit;

        public class UserEditData
        {
            public int Id { get; set; }
            public string Username { get; set; }
            public string DisplayName { get; set; }
            public string Role { get; set; }
            public bool IsActive { get; set; }
        }

        public CreateEditUserDialog(UserEditData existing)
        {
            _isEdit = existing != null;
            Text = _isEdit ? "Edit User" : "Create User";
            Size = new Size(380, 320);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(18, 18, 22);
            ForeColor = Color.FromArgb(240, 240, 240);

            int y = 15;
            AddLabel("Username:", y); _usernameBox = AddTextBox(y, existing?.Username ?? ""); y += 40;
            if (_isEdit) _usernameBox.ReadOnly = true;
            AddLabel("Password:", y); _passwordBox = AddTextBox(y, ""); _passwordBox.UseSystemPasswordChar = true; y += 40;
            if (_isEdit) _passwordBox.PlaceholderText = "(leave blank to keep)";
            AddLabel("Display Name:", y); _displayNameBox = AddTextBox(y, existing?.DisplayName ?? ""); y += 40;
            AddLabel("Role:", y);
            _roleCombo = new ComboBox
            {
                Location = new Point(130, y),
                Size = new Size(220, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(38, 38, 45),
                ForeColor = Color.FromArgb(240, 240, 240)
            };
            _roleCombo.Items.AddRange(new[] { "Member", "Master" });
            _roleCombo.SelectedItem = existing?.Role ?? "Member";
            Controls.Add(_roleCombo);
            y += 40;

            if (_isEdit)
            {
                _activeCheckBox = new CheckBox
                {
                    Text = "Active",
                    Location = new Point(130, y),
                    Checked = existing?.IsActive ?? true,
                    ForeColor = Color.FromArgb(240, 240, 240),
                    AutoSize = true
                };
                Controls.Add(_activeCheckBox);
                y += 30;
            }

            y += 10;
            var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(130, y), Size = new Size(80, 30), BackColor = Color.FromArgb(28, 28, 35), ForeColor = Color.FromArgb(0, 200, 200), FlatStyle = FlatStyle.Flat };
            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(220, y), Size = new Size(80, 30), BackColor = Color.FromArgb(28, 28, 35), ForeColor = Color.FromArgb(140, 140, 140), FlatStyle = FlatStyle.Flat };
            Controls.AddRange(new Control[] { okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;

            okButton.Click += (s, e) =>
            {
                Username = _usernameBox.Text;
                Password = _passwordBox.Text;
                DisplayName = _displayNameBox.Text;
                Role = _roleCombo.SelectedItem?.ToString() ?? "Member";
                IsActive = _activeCheckBox?.Checked ?? true;
            };
        }

        private void AddLabel(string text, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(15, y + 3), AutoSize = true, ForeColor = Color.FromArgb(140, 140, 140) });
        }

        private TextBox AddTextBox(string placeholder, int y, string value)
        {
            return AddTextBox(y, value);
        }

        private TextBox AddTextBox(int y, string value)
        {
            var tb = new TextBox
            {
                Location = new Point(130, y),
                Size = new Size(220, 26),
                Text = value,
                BackColor = Color.FromArgb(38, 38, 45),
                ForeColor = Color.FromArgb(240, 240, 240),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(tb);
            return tb;
        }
    }

    public class UploadModDialog : Form
    {
        public string ModKey { get; private set; }
        public string ModName { get; private set; }
        public string Version { get; private set; }
        public string DllPath { get; private set; }
        public string Description { get; private set; }
        public string Category { get; private set; }

        private TextBox _modKeyBox;
        private TextBox _modNameBox;
        private TextBox _versionBox;
        private TextBox _dllPathBox;
        private TextBox _descriptionBox;
        private TextBox _categoryBox;

        public UploadModDialog()
        {
            Text = "Upload Mod";
            Size = new Size(450, 380);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(18, 18, 22);
            ForeColor = Color.FromArgb(240, 240, 240);

            int y = 15;
            AddLabel("Mod Key:", y); _modKeyBox = AddTextBox(y); y += 36;
            AddLabel("Name:", y); _modNameBox = AddTextBox(y); y += 36;
            AddLabel("Version:", y); _versionBox = AddTextBox(y); y += 36;
            AddLabel("Category:", y); _categoryBox = AddTextBox(y); y += 36;
            AddLabel("Description:", y); _descriptionBox = AddTextBox(y); y += 36;
            AddLabel("DLL File:", y); _dllPathBox = AddTextBox(y); _dllPathBox.ReadOnly = true;

            var browseButton = new Button
            {
                Text = "...",
                Location = new Point(375, y),
                Size = new Size(40, 26),
                BackColor = Color.FromArgb(28, 28, 35),
                ForeColor = Color.FromArgb(0, 200, 200),
                FlatStyle = FlatStyle.Flat
            };
            browseButton.Click += (s, e) =>
            {
                using var ofd = new OpenFileDialog { Filter = "DLL files (*.dll)|*.dll" };
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _dllPathBox.Text = ofd.FileName;
                    if (string.IsNullOrEmpty(_modKeyBox.Text))
                        _modKeyBox.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
                    if (string.IsNullOrEmpty(_modNameBox.Text))
                        _modNameBox.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
                }
            };
            Controls.Add(browseButton);
            y += 46;

            var okButton = new Button { Text = "Upload", DialogResult = DialogResult.OK, Location = new Point(170, y), Size = new Size(100, 32), BackColor = Color.FromArgb(28, 28, 35), ForeColor = Color.FromArgb(0, 200, 200), FlatStyle = FlatStyle.Flat };
            var cancelButton = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(280, y), Size = new Size(100, 32), BackColor = Color.FromArgb(28, 28, 35), ForeColor = Color.FromArgb(140, 140, 140), FlatStyle = FlatStyle.Flat };
            Controls.AddRange(new Control[] { okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;

            okButton.Click += (s, e) =>
            {
                ModKey = _modKeyBox.Text;
                ModName = _modNameBox.Text;
                Version = _versionBox.Text;
                DllPath = _dllPathBox.Text;
                Description = _descriptionBox.Text;
                Category = _categoryBox.Text;

                if (string.IsNullOrEmpty(ModKey) || string.IsNullOrEmpty(ModName) ||
                    string.IsNullOrEmpty(Version) || string.IsNullOrEmpty(DllPath))
                {
                    MessageBox.Show("Mod Key, Name, Version, and DLL file are required.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                }
            };
        }

        private void AddLabel(string text, int y)
        {
            Controls.Add(new Label { Text = text, Location = new Point(15, y + 3), AutoSize = true, ForeColor = Color.FromArgb(140, 140, 140) });
        }

        private TextBox AddTextBox(int y)
        {
            var tb = new TextBox
            {
                Location = new Point(110, y),
                Size = new Size(260, 26),
                BackColor = Color.FromArgb(38, 38, 45),
                ForeColor = Color.FromArgb(240, 240, 240),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(tb);
            return tb;
        }
    }

    public class ResetPasswordDialog : Form
    {
        public string NewPassword { get; private set; }

        public ResetPasswordDialog(string username)
        {
            Text = $"Reset Password — {username}";
            Size = new Size(380, 200);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(18, 18, 22);
            ForeColor = Color.FromArgb(240, 240, 240);

            var lbl = new Label { Text = "New Password:", Location = new Point(15, 23), AutoSize = true, ForeColor = Color.FromArgb(140, 140, 140) };
            Controls.Add(lbl);

            var passwordBox = new TextBox
            {
                Location = new Point(130, 20),
                Size = new Size(220, 26),
                UseSystemPasswordChar = true,
                BackColor = Color.FromArgb(38, 38, 45),
                ForeColor = Color.FromArgb(240, 240, 240),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(passwordBox);

            var confirmLabel = new Label { Text = "Confirm:", Location = new Point(15, 63), AutoSize = true, ForeColor = Color.FromArgb(140, 140, 140) };
            Controls.Add(confirmLabel);

            var confirmBox = new TextBox
            {
                Location = new Point(130, 60),
                Size = new Size(220, 26),
                UseSystemPasswordChar = true,
                BackColor = Color.FromArgb(38, 38, 45),
                ForeColor = Color.FromArgb(240, 240, 240),
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(confirmBox);

            var okButton = new Button
            {
                Text = "Reset",
                DialogResult = DialogResult.OK,
                Location = new Point(130, 110),
                Size = new Size(100, 32),
                BackColor = Color.FromArgb(28, 28, 35),
                ForeColor = Color.FromArgb(200, 0, 150),
                FlatStyle = FlatStyle.Flat
            };
            var cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(240, 110),
                Size = new Size(100, 32),
                BackColor = Color.FromArgb(28, 28, 35),
                ForeColor = Color.FromArgb(140, 140, 140),
                FlatStyle = FlatStyle.Flat
            };
            Controls.AddRange(new Control[] { okButton, cancelButton });
            AcceptButton = okButton;
            CancelButton = cancelButton;

            okButton.Click += (s, e) =>
            {
                if (string.IsNullOrEmpty(passwordBox.Text) || passwordBox.Text.Length < 4)
                {
                    MessageBox.Show("Password must be at least 4 characters.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                if (passwordBox.Text != confirmBox.Text)
                {
                    MessageBox.Show("Passwords do not match.", "Validation",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    DialogResult = DialogResult.None;
                    return;
                }
                NewPassword = passwordBox.Text;
            };
        }
    }

    #endregion
}
