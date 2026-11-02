using System.Drawing;

sealed class AppSettingsDialog : Form
{
    readonly Button darkButton = new(), lightButton = new(), accountButton = new(), saveButton = new(), cancelButton = new();
    readonly Button widgetButton = new();
    readonly ComboBox languageBox = new();
    readonly Label languageNote = new();
    string selectedTheme;
    bool desktopWidget;

    public string SelectedTheme => selectedTheme;
    public string SelectedLanguage => languageBox.SelectedIndex == 1 ? "en" : "es";
    public bool DesktopWidgetEnabled => desktopWidget;

    public AppSettingsDialog(string theme, string language, string cloudStatus, bool desktopWidgetEnabled)
    {
        selectedTheme = theme == "light" ? "light" : "dark";
        desktopWidget = desktopWidgetEnabled;
        Text = "Ajustes"; ClientSize = new Size(620, 660); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; TopMost = false; ShowInTaskbar = false;
        MaximizeBox = false; MinimizeBox = false; BackColor = UiTheme.Background; ForeColor = UiTheme.Text;
        Font = new Font("Segoe UI", 9); Padding = new Padding(0);

        var icon = new SurfacePanel { Bounds = new Rectangle(24, 22, 44, 44), BackColor = UiTheme.SurfaceRaised, BorderColor = UiTheme.Border, Radius = 14 };
        var iconText = Label("⚙", 19, UiTheme.Accent, true, 0, 0, 44, 44); iconText.Dock = DockStyle.Fill; iconText.TextAlign = ContentAlignment.MiddleCenter; icon.Controls.Add(iconText); Controls.Add(icon);
        Controls.Add(Label("PREFERENCIAS", 8, UiTheme.Muted, true, 82, 22, 200, 16));
        Controls.Add(Label("Ajustes", 19, UiTheme.Text, true, 82, 39, 300, 28));

        var appearance = Card("APARIENCIA", "Elige cómo quieres ver el monitor.", 24, 88, 572, 154);
        darkButton.SetBounds(18, 64, 258, 68); darkButton.Text = "◐   Oscuro\n      Carbón y contraste suave";
        StyleThemeButton(darkButton); darkButton.Click += (_, _) => { selectedTheme = "dark"; UpdateThemeButtons(); }; appearance.Controls.Add(darkButton);
        lightButton.SetBounds(296, 64, 258, 68); lightButton.Text = "☼   Claro\n      Superficies claras y limpias";
        StyleThemeButton(lightButton); lightButton.Click += (_, _) => { selectedTheme = "light"; UpdateThemeButtons(); }; appearance.Controls.Add(lightButton);
        Controls.Add(appearance);

        var languageCard = Card("IDIOMA", "La selección queda guardada; la traducción completa se añadirá después.", 24, 254, 572, 112);
        languageBox.Bounds = new Rectangle(18, 62, 250, 30); languageBox.DropDownStyle = ComboBoxStyle.DropDownList;
        languageBox.DrawMode = DrawMode.OwnerDrawFixed; languageBox.ItemHeight = 24; languageBox.BackColor = UiTheme.SurfaceRaised;
        languageBox.ForeColor = UiTheme.Text; languageBox.FlatStyle = FlatStyle.Flat;
        languageBox.Items.Add("Español"); languageBox.Items.Add("English · próximamente");
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

        var cloud = Card("CUENTA BAMBU CLOUD", cloudStatus == "CLOUD ACTIVA" ? "Sesión configurada para consultar tu impresora." : "Conecta tu cuenta para ver el estado de la impresora.", 24, 378, 572, 96);
        accountButton.Text = "Conectar o cambiar cuenta"; accountButton.SetBounds(354, 50, 200, 34); UiTheme.StyleButton(accountButton, quiet: true);
        accountButton.Click += (_, _) => { DialogResult = DialogResult.Retry; Close(); }; cloud.Controls.Add(accountButton); Controls.Add(cloud);

        var widget = Card("MODO WIDGET", "Monitor compacto en el escritorio; ajustes desde la bandeja.", 24, 486, 572, 82);
        // Both transparent card labels need a shorter column so neither can cover the button.
        widget.Controls[0].Width = 360;
        widget.Controls[1].Width = 360;
        widgetButton.SetBounds(402, 20, 152, 42); widgetButton.FlatStyle=FlatStyle.Flat; widgetButton.FlatAppearance.BorderSize=0;
        widgetButton.Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,151,41),11));
        widgetButton.FlatAppearance.MouseOverBackColor=UiTheme.SurfaceHover;
        widgetButton.UseVisualStyleBackColor=false; widgetButton.Font=UiTheme.Font(8,true); widgetButton.Cursor=Cursors.Hand;
        widgetButton.TabStop=true; widgetButton.AccessibleName="Modo widget"; widget.Controls.Add(widgetButton); Controls.Add(widget);
        widgetButton.Click+=(_,_)=>{desktopWidget=!desktopWidget;UpdateWidgetButton();};

        var note = Label("En modo widget, abre Ajustes desde el icono de la bandeja del sistema.", 8, UiTheme.Muted, false, 26, 576, 560, 19); Controls.Add(note);
        cancelButton.Text = "Cancelar"; cancelButton.SetBounds(352, 606, 110, 36); UiTheme.StyleButton(cancelButton, quiet: true); cancelButton.DialogResult = DialogResult.Cancel; Controls.Add(cancelButton);
        saveButton.Text = "Guardar ajustes"; saveButton.SetBounds(472, 606, 124, 36); UiTheme.StyleButton(saveButton, primary: true); saveButton.DialogResult = DialogResult.OK; Controls.Add(saveButton);
        AcceptButton = saveButton; CancelButton = cancelButton;
        UpdateThemeButtons(); UpdateLanguageNote(); UpdateWidgetButton();
    }

    SurfacePanel Card(string title, string subtitle, int x, int y, int width, int height)
    {
        var card = new SurfacePanel { Bounds = new Rectangle(x, y, width, height), BackColor = UiTheme.Surface, BorderColor = UiTheme.Border, Radius = 16 };
        card.Controls.Add(Label(title, 8, UiTheme.TextSoft, true, 18, 14, width - 36, 16));
        card.Controls.Add(Label(subtitle, 8, UiTheme.Muted, false, 18, 32, width - 36, 18));
        return card;
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

    static void StyleSelected(Button button, bool selected)
    {
        button.FlatAppearance.BorderColor = selected ? UiTheme.Accent : UiTheme.Border;
        button.BackColor = selected ? UiTheme.IsDark ? Color.FromArgb(29, 49, 38) : Color.FromArgb(231, 247, 237) : UiTheme.SurfaceRaised;
        button.ForeColor = selected ? UiTheme.Text : UiTheme.TextSoft;
    }

    void UpdateLanguageNote() => languageNote.Text = languageBox.SelectedIndex == 1
        ? "English is prepared for a future translation."
        : "Idioma actual: Español";

    void UpdateWidgetButton()
    {
        widgetButton.Text=desktopWidget?"●   Widget activo":"＋   Activar widget";
        widgetButton.BackColor=desktopWidget?UiTheme.Accent:UiTheme.SurfaceRaised;
        widgetButton.ForeColor=desktopWidget?UiTheme.AccentInk:UiTheme.TextSoft;
        widgetButton.FlatAppearance.MouseOverBackColor=desktopWidget?UiTheme.AccentHover:UiTheme.SurfaceHover;
    }
}

