namespace DesktopIconDropper;

// Kullanıcının tüm davranışları kendi zevkine göre ayarlayabileceği pencere.
// Sistem tepsisindeki ikona sağ tıklayıp "Ayarlar" ile açılır.
// Değişiklikler anında uygulanır (uygulamayı yeniden başlatmaya gerek yok).
internal class SettingsForm : Form
{
    private readonly AppSettings _settings;
    private readonly Action _onSettingsChanged;
    private bool _loading = true;

    public SettingsForm(AppSettings settings, Action onSettingsChanged)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;

        Text = "IconRave - Ayarlar";
        Size = new Size(460, 720);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScroll = true;
        Padding = new Padding(16);

        BuildUi();
        _loading = false;
    }

    private int _y = 12;

    private void BuildUi()
    {
        AddHeader("Genel");
        AddCheckBox("Windows ile birlikte başlat", _settings.StartWithWindows,
            v => { _settings.StartWithWindows = v; StartupManager.SetStartup(v); });
        AddCheckBox("Açılışta simgeleri düşür", _settings.DropIconsOnStartup,
            v => _settings.DropIconsOnStartup = v);
        AddCheckBox("Sürükleyip fırlatmaya izin ver", _settings.EnableDragThrow,
            v => _settings.EnableDragThrow = v);
        AddCheckBox("Çıkışta simgeleri eski yerine döndür", _settings.RestoreIconsOnExit,
            v => _settings.RestoreIconsOnExit = v);

        AddHeader("Fizik");
        AddSlider("Yerçekimi", 400, 3000, (int)_settings.Gravity,
            v => _settings.Gravity = v, "Simgeler ne kadar hızlı düşsün");
        AddSlider("Düşüş hızlandırması", 10, 30, (int)(_settings.FallGravityMultiplier * 10),
            v => _settings.FallGravityMultiplier = v / 10f, "Yükselirken değil, düşerken ekstra hız (x1.0 - x3.0)");
        AddSlider("Duvar sekmesi", 0, 95, (int)(_settings.WallBounciness * 100),
            v => _settings.WallBounciness = v / 100f, "Ekran kenarına çarpınca ne kadar sekesin");
        AddSlider("Zemin sekmesi", 0, 95, (int)(_settings.FloorBounciness * 100),
            v => _settings.FloorBounciness = v / 100f, "Yere değince ne kadar zıplasın");
        AddSlider("Takla miktarı", 0, 300, (int)(_settings.SpinAmount * 100),
            v => _settings.SpinAmount = v / 100f, "Uçarken ne kadar dönsün (0 = hiç dönmesin)");
        AddSlider("Fırlatma gücü", 20, 300, (int)(_settings.ThrowPowerMultiplier * 100),
            v => _settings.ThrowPowerMultiplier = v / 100f, "Elle fırlattığında ne kadar güçlü gitsin");

        AddHeader("Ses Tepkisi");
        AddCheckBox("Ses tepkisi açık", _settings.AudioReactionEnabled,
            v => _settings.AudioReactionEnabled = v);
        AddComboBox("Tepki verilecek ses", new[] { "Vokal (şarkı sözleri)", "Bas (davul/bas)", "Tiz (zil/hi-hat)", "Genel (tüm ses)" },
            (int)_settings.AudioMode, i => _settings.AudioMode = (AudioMode)i);
        AddSlider("Sıçrama gücü", 20, 300, (int)(_settings.JumpStrength * 100),
            v => _settings.JumpStrength = v / 100f, "Müzikle zıplarken ne kadar yükseğe çıksın");
        AddSlider("Algılama duyarlılığı", 20, 300, (int)(_settings.Sensitivity * 100),
            v => _settings.Sensitivity = v / 100f, "Yüksek = küçük seslere bile tepki verir");
        AddSlider("Tepki aralığı (ms)", 40, 600, _settings.CooldownMs,
            v => _settings.CooldownMs = (int)v, "İki zıplama arasındaki en kısa süre (düşük = daha sık)");
        AddSlider("Yükseklik çeşitliliği", 0, 300, (int)(_settings.JumpVariance * 100),
            v => _settings.JumpVariance = v / 100f, "Simgeler arasındaki yükseklik farkı");

        _y += 12;
        var resetButton = new Button
        {
            Text = "Varsayılanlara Dön",
            Location = new Point(16, _y),
            Size = new Size(180, 32)
        };
        resetButton.Click += (_, _) =>
        {
            _settings.ResetToDefaults();
            _settings.Save();
            _onSettingsChanged();
            // Pencereyi yeniden oluştur ki kaydırıcılar yeni değerleri göstersin
            Controls.Clear();
            _y = 12;
            _loading = true;
            BuildUi();
            _loading = false;
        };
        Controls.Add(resetButton);

        var closeButton = new Button
        {
            Text = "Kapat",
            Location = new Point(210, _y),
            Size = new Size(180, 32)
        };
        closeButton.Click += (_, _) => Close();
        Controls.Add(closeButton);

        _y += 48;
    }

    private void AddHeader(string text)
    {
        _y += 10;
        var label = new Label
        {
            Text = text,
            Font = new Font(Font, FontStyle.Bold),
            Location = new Point(12, _y),
            Size = new Size(400, 22)
        };
        Controls.Add(label);
        _y += 26;
    }

    private void AddCheckBox(string text, bool value, Action<bool> onChange)
    {
        var cb = new CheckBox
        {
            Text = text,
            Checked = value,
            Location = new Point(16, _y),
            Size = new Size(400, 24)
        };
        cb.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            onChange(cb.Checked);
            SaveAndNotify();
        };
        Controls.Add(cb);
        _y += 28;
    }

    private void AddComboBox(string label, string[] items, int selectedIndex, Action<int> onChange)
    {
        var lbl = new Label { Text = label, Location = new Point(16, _y), Size = new Size(400, 18) };
        Controls.Add(lbl);
        _y += 20;

        var combo = new ComboBox
        {
            Location = new Point(16, _y),
            Size = new Size(390, 24),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        combo.Items.AddRange(items);
        combo.SelectedIndex = Math.Clamp(selectedIndex, 0, items.Length - 1);
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            onChange(combo.SelectedIndex);
            SaveAndNotify();
        };
        Controls.Add(combo);
        _y += 34;
    }

    private void AddSlider(string label, int min, int max, int value, Action<float> onChange, string? hint = null)
    {
        var lbl = new Label
        {
            Text = label,
            Location = new Point(16, _y),
            Size = new Size(300, 18)
        };
        Controls.Add(lbl);

        var valueLabel = new Label
        {
            Text = value.ToString(),
            Location = new Point(340, _y),
            Size = new Size(70, 18),
            TextAlign = ContentAlignment.MiddleRight
        };
        Controls.Add(valueLabel);
        _y += 20;

        var slider = new TrackBar
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            Location = new Point(12, _y),
            Size = new Size(400, 40),
            TickStyle = TickStyle.None
        };
        slider.ValueChanged += (_, _) =>
        {
            valueLabel.Text = slider.Value.ToString();
            if (_loading) return;
            onChange(slider.Value);
            SaveAndNotify();
        };
        Controls.Add(slider);
        _y += 38;

        if (hint != null)
        {
            var hintLabel = new Label
            {
                Text = hint,
                Location = new Point(16, _y),
                Size = new Size(400, 16),
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 7.5f)
            };
            Controls.Add(hintLabel);
            _y += 20;
        }
    }

    private void SaveAndNotify()
    {
        _settings.Save();
        _onSettingsChanged();
    }
}
