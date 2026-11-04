using System.Drawing;

sealed class AppSettingsView : UserControl
{
    readonly Button darkButton = new(), lightButton = new(), accountButton = new(), saveButton = new(), cancelButton = new();
    readonly Button widgetButton = new();
    readonly RadioButton cloudModeButton = new(), lanModeButton = new();
    readonly RadioButton greenProgressButton = new(), filamentProgressButton = new();
    readonly TextBox lanAddressBox = new(), lanSerialBox = new(), lanAccessCodeBox = new();
    readonly SurfacePanel connectionCard;
    readonly Label connectionSubtitle, lanAddressLabel = new(), lanSerialLabel = new(), lanAccessCodeLabel = new();
    readonly ComboBox languageBox = new();
    readonly Label languageNote = new();
    string selectedTheme;
    bool desktopWidget;
    string connectionMode;
    readonly bool english;

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
    public event EventHandler? AccountRequested;
    public string SelectedTheme => selectedTheme;
    public string SelectedLanguage => languageBox.SelectedIndex == 1 ? "en" : "es";
    public bool DesktopWidgetEnabled => desktopWidget;
    public bool ProgressColorByFilament => filamentProgressButton.Checked;
    public string ConnectionMode => connectionMode;
    public string LanAddress => lanAddressBox.Text.Trim();
    public string LanSerial => lanSerialBox.Text.Trim();
    public string LanAccessCode => lanAccessCodeBox.Text.Trim();
    public bool ValidateLanSettings(out string message)
    {
        message = "";
        if (connectionMode != "lan") return true;
        if (!System.Net.IPAddress.TryParse(LanAddress, out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !IsPrivateAddress(address.GetAddressBytes()))
        {
            message = english
                ? "Enter the printer's private IPv4 address (for example 192.168.1.50)."
                : "Introduce la dirección IPv4 privada de la impresora (por ejemplo, 192.168.1.50).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(LanSerial) || string.IsNullOrWhiteSpace(LanAccessCode))
        {
            message = english
                ? "Enter the printer serial number and its LAN access code."
                : "Introduce el número de serie de la impresora y su código de acceso LAN.";
            return false;
        }
        return true;
    }

    static bool IsPrivateAddress(byte[] bytes) =>
        bytes[0] == 10 ||
        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
        (bytes[0] == 192 && bytes[1] == 168) ||
        (bytes[0] == 169 && bytes[1] == 254);

    public AppSettingsView(string theme, string language, string cloudStatus, bool desktopWidgetEnabled,
        string connectionMode, string lanAddress, string lanSerial, string lanAccessCode,
        bool progressColorByFilament)
    {
        selectedTheme = theme == "light" ? "light" : "dark";
        english = language == "en";
        desktopWidget = desktopWidgetEnabled;
        this.connectionMode = string.Equals(connectionMode, "lan", StringComparison.OrdinalIgnoreCase) ? "lan" : "cloud";
        Size = new Size(620, 754); BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 9); Padding = new Padding(0);

        var icon = new SurfacePanel { Bounds = new Rectangle(24, 22, 44, 44), BackColor = UiTheme.SurfaceRaised, BorderColor = UiTheme.Border, Radius = 14 };
        var iconText = Label("⚙", 19, UiTheme.Accent, true, 0, 0, 44, 44); iconText.Dock = DockStyle.Fill; iconText.TextAlign = ContentAlignment.MiddleCenter; icon.Controls.Add(iconText); Controls.Add(icon);
        Controls.Add(Label(english ? "PREFERENCES" : "PREFERENCIAS", 8, UiTheme.Muted, true, 82, 22, 200, 16));
        Controls.Add(Label(english ? "Settings" : "Ajustes", 19, UiTheme.Text, true, 82, 39, 300, 28));

        var appearance = Card(english ? "APPEARANCE" : "APARIENCIA", english ? "Choose how the monitor looks." : "Elige cómo quieres ver el monitor.", 24, 88, 572, 154);
        darkButton.SetBounds(18, 64, 258, 68); darkButton.Text = english ? "◐   Dark\n      Charcoal with soft contrast" : "◐   Oscuro\n      Carbón y contraste suave";
        StyleThemeButton(darkButton); darkButton.Click += (_, _) => { selectedTheme = "dark"; UpdateThemeButtons(); }; appearance.Controls.Add(darkButton);
        lightButton.SetBounds(296, 64, 258, 68); lightButton.Text = english ? "☼   Light\n      Clean, bright surfaces" : "☼   Claro\n      Superficies claras y limpias";
        StyleThemeButton(lightButton); lightButton.Click += (_, _) => { selectedTheme = "light"; UpdateThemeButtons(); }; appearance.Controls.Add(lightButton);
        Controls.Add(appearance);

        var languageCard = Card(english ? "LANGUAGE" : "IDIOMA", english ? "Choose the interface language." : "Elige el idioma de la interfaz.", 24, 254, 572, 112);
        languageBox.Bounds = new Rectangle(18, 62, 250, 30); languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
        languageBox.DrawMode = DrawMode.OwnerDrawFixed; languageBox.ItemHeight = 24; languageBox.BackColor = UiTheme.SurfaceRaised;
        languageBox.ForeColor = UiTheme.Text; languageBox.FlatStyle = FlatStyle.Flat;
        languageBox.Items.Add("Español"); languageBox.Items.Add("English");
        languageBox.SelectedIndex = language == "en" ? 1 : 0;
        languageBox.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var bg = new SolidBrush(selected ? UiTheme.SurfaceHover : UiTheme.SurfaceRaised);
            using var fg = new SolidBrush(e.Index == 1 ? UiTheme.Muted : UiTheme.Text);
            e.Graphics.FillRectangle(bg, e.Bounds);
            e.Graphics.DrawString(languageBox.Items[e.Index]?.ToString() ?? "", languageBox.Font, fg, e.Bounds.X + 8, e.Bounds.Y + 4);
        };
        languageBox.SelectedIndexChanged += (_, _) => UpdateLanguageNote();
        languageCard.Controls.Add(languageBox);
        languageNote.Text = "Idioma activo"; languageNote.Font = new Font("Segoe UI", 8); languageNote.ForeColor = UiTheme.Muted;
        languageNote.SetBounds(286, 66, 255, 26); languageCard.Controls.Add(languageNote); Controls.Add(languageCard);

        connectionCard = Card(english ? "CONNECTION" : "CONEXIÓN",
            english ? "Choose Bambu Cloud or connect directly to your printer on the local network."
                    : "Elige Bambu Cloud o conecta directamente con la impresora en la red local.",
            24, 378, 572, 180);
        connectionSubtitle=(Label)connectionCard.Controls[1];
        Controls.Add(connectionCard);
        cloudModeButton.Text = "Bambu Cloud";
        lanModeButton.Text = "LAN";
        foreach (var button in new[] { cloudModeButton, lanModeButton })
        {
            button.SetBounds(button == cloudModeButton ? 18 : 194, 58, 166, 32);
            button.Appearance=Appearance.Button;
            button.FlatStyle=FlatStyle.Flat;
            button.FlatAppearance.BorderSize=1;
            button.FlatAppearance.MouseOverBackColor=UiTheme.SurfaceHover;
            button.BackColor=UiTheme.SurfaceRaised;
            button.ForeColor=UiTheme.TextSoft;
            button.Font=UiTheme.Font(9,true);
            button.TextAlign=ContentAlignment.MiddleCenter;
            button.Cursor=Cursors.Hand;
            button.UseVisualStyleBackColor=false;
            connectionCard.Controls.Add(button);
        }
        cloudModeButton.CheckedChanged += (_, _) =>
        {
            if(cloudModeButton.Checked)SelectConnectionMode("cloud",cloudStatus);
        };
        lanModeButton.CheckedChanged += (_, _) =>
        {
            if(lanModeButton.Checked)SelectConnectionMode("lan",cloudStatus);
        };

        accountButton.Text = english ? "Connect or change account" : "Conectar o cambiar cuenta";
        accountButton.SetBounds(374, 58, 180, 32);
        UiTheme.StyleButton(accountButton, quiet: true);
        accountButton.Click += (_, _) => AccountRequested?.Invoke(this, EventArgs.Empty);
        connectionCard.Controls.Add(accountButton);

        AddLanField(connectionCard, english ? "PRINTER IP" : "IP DE IMPRESORA", lanAddressLabel, lanAddressBox, lanAddress, 18, false);
        AddLanField(connectionCard, english ? "SERIAL NUMBER" : "NÚMERO DE SERIE", lanSerialLabel, lanSerialBox, lanSerial, 198, false);
        AddLanField(connectionCard, english ? "ACCESS CODE" : "CÓDIGO DE ACCESO", lanAccessCodeLabel, lanAccessCodeBox, lanAccessCode, 378, true);

        var progressColorCard = Card(
            english ? "PROGRESS COLOR" : "COLOR DEL PROGRESO",
            english ? "Choose how the active print progress is colored." : "Elige el color de la barra de progreso de impresión.",
            24, 580, 572, 104);
        foreach (var button in new[] { greenProgressButton, filamentProgressButton })
        {
            button.Appearance = Appearance.Button;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = UiTheme.SurfaceHover;
            button.BackColor = UiTheme.SurfaceRaised;
            button.ForeColor = UiTheme.TextSoft;
            button.Font = UiTheme.Font(8, true);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }
        greenProgressButton.Text = english ? "Always green" : "Siempre verde";
        filamentProgressButton.Text = english ? "Active filament color" : "Color del filamento activo";
        greenProgressButton.SetBounds(18, 60, 258, 30);
        filamentProgressButton.SetBounds(296, 60, 258, 30);
        greenProgressButton.Checked = !progressColorByFilament;
        filamentProgressButton.Checked = progressColorByFilament;
        greenProgressButton.CheckedChanged += (_, _) => StyleProgressColorButtons();
        filamentProgressButton.CheckedChanged += (_, _) => StyleProgressColorButtons();
        progressColorCard.Controls.Add(greenProgressButton);
        progressColorCard.Controls.Add(filamentProgressButton);
        Controls.Add(progressColorCard);

        var widget = Card(english ? "WIDGET MODE" : "MODO WIDGET", english ? "Compact desktop monitor; settings in the system tray." : "Monitor compacto en el escritorio; ajustes desde la bandeja.", 24, 700, 572, 88);
        // Both transparent card labels need a shorter column so neither can cover the button.
        widget.Controls[0].Width = 360;
        widget.Controls[1].Width = 360;
        widgetButton.SetBounds(402, 20, 152, 42); widgetButton.FlatStyle=FlatStyle.Flat; widgetButton.FlatAppearance.BorderSize=0;
        widgetButton.Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,151,41),11));
        widgetButton.FlatAppearance.MouseOverBackColor=UiTheme.SurfaceHover;
        widgetButton.UseVisualStyleBackColor=false; widgetButton.Font=UiTheme.Font(8,true); widgetButton.Cursor=Cursors.Hand;
        widgetButton.TabStop=true; widgetButton.AccessibleName="Modo widget"; widget.Controls.Add(widgetButton); Controls.Add(widget);
        widgetButton.Click+=(_,_)=>{desktopWidget=!desktopWidget;UpdateWidgetButton();};

        var note = Label(english ? "In widget mode, open Settings from the system tray icon." : "En modo widget, abre Ajustes desde el icono de la bandeja del sistema.", 8, UiTheme.Muted, false, 26, 800, 560, 19); Controls.Add(note);
        cancelButton.Text = english ? "Cancel" : "Cancelar"; cancelButton.SetBounds(352, 710, 110, 36); UiTheme.StyleButton(cancelButton, quiet: true); Controls.Add(cancelButton);
        cancelButton.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);
        saveButton.Text = english ? "Save settings" : "Guardar ajustes"; saveButton.SetBounds(472, 710, 124, 36); UiTheme.StyleButton(saveButton, primary: true); saveButton.DialogResult = DialogResult.None; Controls.Add(saveButton);
        saveButton.Click += (_, _) =>
        {
            if (!ValidateLanSettings(out var validationMessage))
            {
                MessageBox.Show(this, validationMessage, english ? "LAN connection" : "Conexión LAN",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SaveRequested?.Invoke(this, EventArgs.Empty);
        };
        var settingsScroll = new SettingsScrollView
        {
            Bounds = new Rectangle(0, 0, 620, 686),
            ContentHeight = 828,
            BackColor = UiTheme.Background
        };
        foreach (Control control in Controls.Cast<Control>().Where(control =>
                     control != saveButton && control != cancelButton).ToArray())
        {
            Controls.Remove(control);
            settingsScroll.Content.Controls.Add(control);
        }
        Controls.Add(settingsScroll);
        cancelButton.BringToFront();
        saveButton.BringToFront();
        UpdateThemeButtons(); UpdateLanguageNote(); UpdateWidgetButton(); UpdateConnectionMode(cloudStatus); StyleProgressColorButtons();
    }

    sealed class SettingsScrollView : Panel, IMessageFilter
    {
        const int ScrollMessage = 0x020A;
        const int ScrollStep = 48;
        readonly Panel content = new() { BackColor = UiTheme.Background };
        readonly SettingsScrollBar scrollBar = new();
        int contentHeight;
        bool filterInstalled;

        public Panel Content => content;
        public int ContentHeight
        {
            get => contentHeight;
            set
            {
                contentHeight = Math.Max(0, value);
                content.Height = contentHeight;
                UpdateScrollBar();
            }
        }

        public SettingsScrollView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            content.Location = Point.Empty;
            Controls.Add(content);
            Controls.Add(scrollBar);
            scrollBar.ScrollRequested += ScrollTo;
            scrollBar.Dock = DockStyle.Right;
            scrollBar.Width = 14;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!filterInstalled)
            {
                Application.AddMessageFilter(this);
                filterInstalled = true;
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (filterInstalled)
            {
                Application.RemoveMessageFilter(this);
                filterInstalled = false;
            }
            base.OnHandleDestroyed(e);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateScrollBar();
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != ScrollMessage || !Visible || !IsHandleCreated)
                return false;

            var screenPoint = new Point(unchecked((short)(long)message.LParam),
                unchecked((short)((long)message.LParam >> 16)));
            if (!RectangleToScreen(ClientRectangle).Contains(screenPoint))
                return false;

            var delta = unchecked((short)((long)message.WParam >> 16));
            ScrollTo(ScrollPosition - Math.Sign(delta) * ScrollStep * Math.Max(1, Math.Abs(delta) / 120));
            return true;
        }

        void ScrollTo(int position)
        {
            var max = Math.Max(0, contentHeight - ClientSize.Height);
            var next = Math.Clamp(position, 0, max);
            content.Top = -next;
            scrollBar.SetScroll(next, contentHeight, ClientSize.Height);
        }

        int ScrollPosition => -content.Top;

        void UpdateScrollBar()
        {
            content.Width = Math.Max(0, ClientSize.Width - scrollBar.Width);
            scrollBar.Visible = contentHeight > ClientSize.Height;
            ScrollTo(ScrollPosition);
        }
    }

    sealed class SettingsScrollBar : Control
    {
        const int MinimumThumbHeight = 34;
        int scrollPosition;
        int contentHeight;
        int viewportHeight;
        bool dragging;
        int dragOffset;

        public event Action<int>? ScrollRequested;

        public SettingsScrollBar()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            Cursor = Cursors.Hand;
            BackColor = UiTheme.Background;
            MouseDown += HandleMouseDown;
            MouseMove += HandleMouseMove;
            MouseUp += (_, _) => dragging = false;
        }

        public void SetScroll(int position, int contentSize, int viewportSize)
        {
            scrollPosition = position;
            contentHeight = contentSize;
            viewportHeight = viewportSize;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var maxScroll = Math.Max(0, contentHeight - viewportHeight);
            if (maxScroll == 0 || Height <= 0)
                return;

            var track = new Rectangle(5, 6, Math.Max(3, Width - 10), Math.Max(0, Height - 12));
            var thumbHeight = Math.Max(MinimumThumbHeight,
                (int)Math.Round(track.Height * (double)viewportHeight / contentHeight));
            thumbHeight = Math.Min(track.Height, thumbHeight);
            var travel = track.Height - thumbHeight;
            var thumbTop = track.Top + (int)Math.Round(travel * (double)scrollPosition / maxScroll);

            using var trackBrush = new SolidBrush(UiTheme.SurfaceRaised);
            using var trackPath = SurfacePanel.RoundedPath(track, track.Width / 2);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.FillPath(trackBrush, trackPath);

            var thumb = new Rectangle(track.X, thumbTop, track.Width, thumbHeight);
            using var thumbBrush = new SolidBrush(dragging ? UiTheme.Accent : UiTheme.Muted);
            using var thumbPath = SurfacePanel.RoundedPath(thumb, thumb.Width / 2);
            e.Graphics.FillPath(thumbBrush, thumbPath);
        }

        void HandleMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            var thumb = GetThumbBounds();
            if (thumb.Contains(e.Location))
            {
                dragging = true;
                dragOffset = e.Y - thumb.Top;
                Capture = true;
                Invalidate();
                return;
            }
            ScrollRequested?.Invoke(scrollPosition + (e.Y < thumb.Top ? -viewportHeight : viewportHeight));
        }

        void HandleMouseMove(object? sender, MouseEventArgs e)
        {
            if (!dragging)
                return;
            var track = GetTrackBounds();
            var thumb = GetThumbBounds();
            var travel = track.Height - thumb.Height;
            var maxScroll = Math.Max(0, contentHeight - viewportHeight);
            if (travel <= 0 || maxScroll <= 0)
                return;
            var top = Math.Clamp(e.Y - dragOffset, track.Top, track.Bottom - thumb.Height);
            ScrollRequested?.Invoke((int)Math.Round((top - track.Top) * (double)maxScroll / travel));
        }

        Rectangle GetTrackBounds() => new(5, 6, Math.Max(3, Width - 10), Math.Max(0, Height - 12));

        Rectangle GetThumbBounds()
        {
            var track = GetTrackBounds();
            var maxScroll = Math.Max(0, contentHeight - viewportHeight);
            var thumbHeight = contentHeight <= 0
                ? track.Height
                : Math.Min(track.Height, Math.Max(MinimumThumbHeight,
                    (int)Math.Round(track.Height * (double)viewportHeight / contentHeight)));
            var travel = track.Height - thumbHeight;
            var top = maxScroll == 0 ? track.Top :
                track.Top + (int)Math.Round(travel * (double)scrollPosition / maxScroll);
            return new Rectangle(track.X, top, track.Width, thumbHeight);
        }
    }

    SurfacePanel Card(string title, string subtitle, int x, int y, int width, int height)
    {
        var card = new SurfacePanel { Bounds = new Rectangle(x, y, width, height), BackColor = UiTheme.Surface, BorderColor = UiTheme.Border, Radius = 16 };
        card.Controls.Add(Label(title, 8, UiTheme.TextSoft, true, 18, 14, width - 36, 16));
        card.Controls.Add(Label(subtitle, 8, UiTheme.Muted, false, 18, 32, width - 36, 18));
        return card;
    }

    void AddLanField(Control parent, string caption, Label label, TextBox input, string value, int x, bool password)
    {
        label.Text=caption;label.Font=UiTheme.Font(7,true);label.ForeColor=UiTheme.Muted;
        label.SetBounds(x,105,166,15);parent.Controls.Add(label);
        input.SetBounds(x, 123, 166, 30);
        input.Text = value;
        input.BackColor = UiTheme.SurfaceRaised;
        input.ForeColor = UiTheme.Text;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Font = UiTheme.Font(9);
        input.UseSystemPasswordChar = password;
        parent.Controls.Add(input);
    }

    void UpdateConnectionMode(string cloudStatus)
    {
        var lan = connectionMode == "lan";
        cloudModeButton.Checked=!lan;
        lanModeButton.Checked=lan;
        StyleModeButton(cloudModeButton,!lan);
        StyleModeButton(lanModeButton,lan);
        accountButton.Visible = !lan;
        lanAddressBox.Visible = lan;lanAddressLabel.Visible=lan;
        lanSerialBox.Visible = lan;lanSerialLabel.Visible=lan;
        lanAccessCodeBox.Visible = lan;lanAccessCodeLabel.Visible=lan;
        if(lan)
        {
            lanAddressLabel.BringToFront();lanAddressBox.BringToFront();
            lanSerialLabel.BringToFront();lanSerialBox.BringToFront();
            lanAccessCodeLabel.BringToFront();lanAccessCodeBox.BringToFront();
        }
        if (!lan)
        {
            connectionSubtitle.Text = cloudStatus is "CLOUD ACTIVA" or "CLOUD ACTIVE"
                ? (english ? "Session ready to check your printer." : "Sesión configurada para consultar tu impresora.")
                : (english ? "Connect your account to view printer status." : "Conecta tu cuenta para ver el estado de la impresora.");
        }
        else connectionSubtitle.Text = english
                ? "Enter the printer's local IPv4, serial number, and LAN access code."
                : "Introduce la IPv4 local, el número de serie y el código de acceso LAN.";
    }

    void SelectConnectionMode(string mode,string cloudStatus)
    {
        if(connectionMode==mode)return;
        connectionMode=mode;
        UpdateConnectionMode(cloudStatus);
    }

    static void StyleModeButton(RadioButton button,bool selected)
    {
        button.BackColor=selected
            ?(UiTheme.IsDark?Color.FromArgb(29,49,38):Color.FromArgb(231,247,237))
            :UiTheme.SurfaceRaised;
        button.ForeColor=selected?UiTheme.Text:UiTheme.TextSoft;
        button.FlatAppearance.BorderColor=selected?UiTheme.Accent:UiTheme.Border;
    }

    static Label Label(string text, float size, Color color, bool bold, int x, int y, int width, int height) =>
        new() { Text = text, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, Bounds = new Rectangle(x, y, width, height), AutoEllipsis = true };

    static void StyleThemeButton(Button button)
    {
        button.FlatStyle = FlatStyle.Flat; button.FlatAppearance.BorderSize = 1; button.FlatAppearance.MouseOverBackColor = UiTheme.SurfaceHover;
        button.BackColor = UiTheme.SurfaceRaised; button.ForeColor = UiTheme.TextSoft; button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        button.TextAlign = ContentAlignment.MiddleLeft; button.Cursor = Cursors.Hand; button.UseVisualStyleBackColor = false;
    }

    void UpdateThemeButtons()
    {
        StyleSelected(darkButton, selectedTheme == "dark"); StyleSelected(lightButton, selectedTheme == "light");
    }

    void StyleProgressColorButtons()
    {
        StyleModeButton(greenProgressButton, greenProgressButton.Checked);
        StyleModeButton(filamentProgressButton, filamentProgressButton.Checked);
    }

    static void StyleSelected(Button button, bool selected)
    {
        button.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
        button.BackColor = selected ? UiTheme.IsDark ? Color.FromArgb(29, 49, 38) : Color.FromArgb(231, 247, 237) : UiTheme.SurfaceRaised;
        button.ForeColor = selected ? UiTheme.Text : UiTheme.TextSoft;
    }

    void UpdateLanguageNote() => languageNote.Text = languageBox.SelectedIndex == 1
        ? "English interface selected."
        : "Idioma actual: Español";

    void UpdateWidgetButton()
    {
        widgetButton.Text=desktopWidget?(english?"●   Widget on":"●   Widget activo"):(english?"＋   Enable widget":"＋   Activar widget");
        widgetButton.BackColor=desktopWidget?UiTheme.Accent:UiTheme.SurfaceRaised;
        widgetButton.ForeColor=desktopWidget?UiTheme.AccentInk:UiTheme.TextSoft;
        widgetButton.FlatAppearance.MouseOverBackColor=desktopWidget?UiTheme.AccentHover:UiTheme.SurfaceHover;
    }

}
