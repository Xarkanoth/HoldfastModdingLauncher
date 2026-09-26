using System.Diagnostics;

namespace HoldfastModdingLauncher
{
    /// <summary>
    /// Auto block timings. Writes the same BepInEx config the mod reads on startup.
    /// </summary>
    public class AutoBlockSettingsForm : Form
    {
        private const string ReactionKey = "BlockReactionTimeMs";
        private const string SwingMinKey = "SwitchDelayMinMs";
        private const string SwingMaxKey = "SwitchDelayMaxMs";
        private const string StayKey = "StayUpAfterSwingMs";
        private const string LongestKey = "LongestBlockMs";
        private const string NearbyKey = "NearbyWaitMs";

        private readonly string _configPath;
        private readonly Dictionary<NumericUpDown, Label> _readouts = new();
        private NumericUpDown _reaction = null!;
        private NumericUpDown _swing = null!;
        private NumericUpDown _stay = null!;
        private NumericUpDown _longest = null!;
        private NumericUpDown _nearby = null!;
        private Label _swingHelp = null!;

        public static void ShowSettings(string holdfastPath)
        {
            using var form = new AutoBlockSettingsForm(holdfastPath);
            form.ShowDialog();
        }

        public AutoBlockSettingsForm(string holdfastPath)
        {
            _configPath = Path.Combine(holdfastPath, "BepInEx", "config", "com.xarkanoth.autoblock.cfg");
            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            Theme.ApplyForm(this);
            Text = "Auto block";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(480, 660);
            Font = new Font("Segoe UI", 9F);

            Controls.Add(new Label
            {
                Text = "Instant means no wait. 1000 is one second. This applies the next time Holdfast starts.",
                ForeColor = Theme.TextMuted,
                Location = new Point(20, 16),
                Size = new Size(440, 40)
            });

            int y = 64;
            _reaction = AddRow(ref y, "After a swing starts", "How long before you block.", 0, 500, 50, out _);
            _swing = AddRow(ref y, "When the swing changes", "How long before the block follows.", 0, 500, 50, out _swingHelp);
            _stay = AddRow(ref y, "After the swing is over", "How long the block stays up.", 0, 5000, 100, out _);
            _longest = AddRow(ref y, "If nothing is happening", "The block drops after this.", 500, 10000, 100, out _);
            _nearby = AddRow(ref y, "Close, but not swinging", "How long before you block them again.", 0, 5000, 100, out _);

            _reaction.ValueChanged += (_, _) => RefreshReadouts();
            _swing.ValueChanged += (_, _) => RefreshReadouts();
            _nearby.ValueChanged += (_, _) => RefreshReadouts();
            _longest.ValueChanged += (_, _) => RefreshReadouts();
            _stay.ValueChanged += (_, _) => KeepLongestAboveStay();

            var save = new Button
            {
                Text = "Save",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(20, y + 8),
                Size = new Size(120, 36)
            };
            Theme.ApplyPrimaryButton(save);
            save.Click += (_, _) => Save();

            var cancel = new Button
            {
                Text = "Cancel",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Location = new Point(152, y + 8),
                Size = new Size(120, 36)
            };
            Theme.ApplyGhostButton(cancel);
            cancel.Click += (_, _) => Close();

            Controls.Add(save);
            Controls.Add(cancel);
        }

        private NumericUpDown AddRow(ref int y, string title, string help, int min, int max, int step, out Label helpLabel)
        {
            Controls.Add(new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Theme.Text,
                Location = new Point(20, y),
                AutoSize = true
            });
            helpLabel = new Label
            {
                Text = help,
                ForeColor = Theme.TextMuted,
                Location = new Point(20, y + 22),
                Size = new Size(440, 36)
            };
            Controls.Add(helpLabel);

            var number = new NumericUpDown
            {
                Location = new Point(20, y + 62),
                Size = new Size(90, 28),
                Minimum = min,
                Maximum = max,
                Increment = step,
                BackColor = Theme.PanelAlt,
                ForeColor = Theme.Text
            };
            var readout = new Label
            {
                ForeColor = Theme.Text,
                Location = new Point(120, y + 66),
                Size = new Size(320, 22)
            };
            Controls.Add(number);
            Controls.Add(readout);
            _readouts[number] = readout;
            y += 100;
            return number;
        }

        private void LoadValues()
        {
            var values = ReadConfig();
            _reaction.Value = Clamp(values, ReactionKey, 0, _reaction);
            int swingMin = Clamp(values, SwingMinKey, 0, _swing);
            int swingMax = values.TryGetValue(SwingMaxKey, out int maxWait) ? maxWait : swingMin;
            _swing.Value = swingMin;
            if (swingMax != swingMin)
            {
                _swingHelp.Text = "How long before the block follows. Right now this varies from "
                    + FormatDelay(swingMin) + " to " + FormatDelay(swingMax) + ". Saving uses one time.";
            }

            _stay.Value = Clamp(values, StayKey, 800, _stay);
            int longest = Clamp(values, LongestKey, 3000, _longest);
            if (longest < _stay.Value)
                longest = (int)_stay.Value;
            _longest.Value = longest;
            _nearby.Value = Clamp(values, NearbyKey, 500, _nearby);
            RefreshReadouts();
        }

        private static int Clamp(Dictionary<string, int> values, string key, int fallback, NumericUpDown box)
        {
            int value = values.TryGetValue(key, out int found) ? found : fallback;
            if (value < box.Minimum) return (int)box.Minimum;
            if (value > box.Maximum) return (int)box.Maximum;
            return value;
        }

        private void KeepLongestAboveStay()
        {
            if (_longest.Value < _stay.Value)
                _longest.Value = _stay.Value;
            RefreshReadouts();
        }

        private void RefreshReadouts()
        {
            foreach (var pair in _readouts)
                pair.Value.Text = FormatDelay((int)pair.Key.Value);
        }

        private void Save()
        {
            if (IsHoldfastRunning())
            {
                ConfirmDialog.ShowWarning(
                    "Close Holdfast first. If you save while it is open, the game puts the old times back when it closes.",
                    "Holdfast is open");
                return;
            }

            try
            {
                int stay = (int)_stay.Value;
                int longest = Math.Max(stay, (int)_longest.Value);
                int swing = (int)_swing.Value;
                var updates = new Dictionary<string, int>
                {
                    [ReactionKey] = (int)_reaction.Value,
                    [SwingMinKey] = swing,
                    [SwingMaxKey] = swing,
                    [StayKey] = stay,
                    [LongestKey] = longest,
                    [NearbyKey] = (int)_nearby.Value
                };
                WriteConfig(updates);
                ConfirmDialog.ShowSuccess("Saved. This applies the next time Holdfast starts.", "Auto block");
                Close();
            }
            catch (Exception ex)
            {
                ConfirmDialog.ShowError("Could not save. " + ex.Message, "Auto block");
            }
        }

        private Dictionary<string, int> ReadConfig()
        {
            var values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(_configPath))
                return values;

            foreach (string line in File.ReadAllLines(_configPath))
            {
                if (!TryParseKey(line, out string key, out int value))
                    continue;
                values[key] = value;
            }
            return values;
        }

        private void WriteConfig(Dictionary<string, int> updates)
        {
            string? directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var lines = File.Exists(_configPath)
                ? new List<string>(File.ReadAllLines(_configPath))
                : new List<string> { "[General]" };

            foreach (var pair in updates)
                Upsert(lines, pair.Key, pair.Value);

            File.WriteAllLines(_configPath, lines);
        }

        private static void Upsert(List<string> lines, string key, int value)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                if (!TryParseKey(lines[i], out string found, out _))
                    continue;
                if (!found.Equals(key, StringComparison.OrdinalIgnoreCase))
                    continue;
                lines[i] = key + " = " + value;
                return;
            }

            int insertAt = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                if (!lines[i].Trim().Equals("[General]", StringComparison.OrdinalIgnoreCase))
                    continue;
                insertAt = i + 1;
                break;
            }

            if (insertAt == lines.Count)
            {
                bool hasGeneral = false;
                foreach (string line in lines)
                {
                    if (line.Trim().Equals("[General]", StringComparison.OrdinalIgnoreCase))
                        hasGeneral = true;
                }
                if (!hasGeneral)
                {
                    if (lines.Count > 0 && lines[lines.Count - 1].Length > 0)
                        lines.Add("");
                    lines.Add("[General]");
                    insertAt = lines.Count;
                }
            }

            lines.Insert(insertAt, key + " = " + value);
        }

        private static bool TryParseKey(string line, out string key, out int value)
        {
            key = "";
            value = 0;
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith("["))
                return false;

            int eq = trimmed.IndexOf('=');
            if (eq <= 0)
                return false;

            key = trimmed.Substring(0, eq).Trim();
            string raw = trimmed.Substring(eq + 1).Trim();
            return key.Length > 0 && int.TryParse(raw, out value);
        }

        private static string FormatDelay(int ms)
        {
            if (ms <= 0)
                return "Instant";
            if (ms < 1000)
                return ms + " ms";
            if (ms % 1000 == 0)
            {
                int seconds = ms / 1000;
                return seconds == 1 ? "1 second" : seconds + " seconds";
            }
            return (ms / 1000f).ToString("0.#") + " seconds";
        }

        private static bool IsHoldfastRunning()
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.ProcessName.Equals("Holdfast NaW", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return false;
        }
    }
}
