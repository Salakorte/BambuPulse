Attempting to perform the InitializeDefaultDrives operation on the 'FileSystem' provider failed.
using System.Drawing;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

var appPreferences=AppPreferences.Load();
UiTheme.SetMode(appPreferences.Theme);
ApplicationConfiguration.Initialize();
Application.Run(new MonitorWindow());

sealed class MonitorWindow : Form
{
    readonly bool english = AppPreferences.Current.Language == "en";
    readonly string configFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BambuP1SWidget", "settings.json");
    readonly Label status = new(), connection = new(), job = new(), jobSubtitle = new(), percent = new(), remaining = new(), updated = new(), inventorySummary = new(), layerInfo = new(), finishInfo = new();
    readonly TemperatureGauge nozzleGauge = new(){Caption=AppPreferences.Current.Language == "en" ? "Nozzle" : "Boquilla",Maximum=300,ColdThreshold=35,HotThreshold=165};
    readonly TemperatureGauge bedGauge = new(){Caption=AppPreferences.Current.Language == "en" ? "Bed" : "Cama",Maximum=120,ColdThreshold=24,HotThreshold=50};
    readonly SurfacePanel statePill=new();
    readonly List<FilamentCard> filamentCards = new();
    readonly ProgressTrack progress = new();
    readonly PrinterSketch printerSketch = new();
    readonly Button settingsButton = new();
    readonly Button libraryButton = new();
    readonly Button closeButton = new();
    readonly NotifyIcon trayIcon = new();
    readonly ToolStripMenuItem disableWidgetMenuItem = new(AppPreferences.Current.Language == "en" ? "Disable widget mode" : "Desactivar modo widget");
    readonly System.Windows.Forms.Timer positionSaveTimer = new(){Interval=600};
    readonly string libraryFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BambuP1SWidget", "filament-library.json");
    FilamentLibrary filamentLibrary = new();
    string cloudToken = ""; string lastState = ""; string activeJob = ""; string? activeTaskId; bool finishHandled;
    IMqttClient? client;
    string serial = "";
    string region = "us";
    bool desktopWidget = AppPreferences.Current.DesktopWidget;
    bool desktopAttached;
    Point? lastDesktopLocation;
    static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    DateTime? bedZeroSince; DateTime? nozzleZeroSince; int bedZeroCount; int nozzleZeroCount;

    public MonitorWindow()
    {
        Text=english ? "Bambu Bar · P1S" : "Bambu Bar · P1S"; Icon=LoadPrinterIcon(); ClientSize=new Size(540,790); AutoScaleMode=AutoScaleMode.None;
        FormBorderStyle=FormBorderStyle.None; MaximizeBox=false; DoubleBuffered=true; TopMost=false;
        BackColor=UiTheme.Background; ForeColor=UiTheme.Text; Font=new Font("Segoe UI",9);
        Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,Width-1,Height-1),16));
        LoadLibrary();
        var canvas=new Panel{Bounds=new Rectangle(UiTheme.S16,UiTheme.S16,504,754),BackColor=BackColor}; Controls.Add(canvas);
        canvas.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};

        var eyebrow=MakeLabel("BAMBU LAB  ·  MONITOR",8,UiTheme.Muted,true);eyebrow.SetBounds(0,8,230,16);canvas.Controls.Add(eyebrow);
        var brand=MakeLabel("P1S",20,UiTheme.Text,true);brand.SetBounds(0,23,180,30);canvas.Controls.Add(brand);
        foreach(var dragTarget in new Control[]{eyebrow,brand})dragTarget.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
        connection.Text=english?"OFFLINE":"SIN CONEXIÓN";connection.Font=new Font("Segoe UI",8,FontStyle.Bold);connection.ForeColor=UiTheme.Muted;connection.TextAlign=ContentAlignment.MiddleCenter;connection.Dock=DockStyle.Fill;
        connectionPill.Bounds=new Rectangle(352,13,122,30);connectionPill.BackColor=UiTheme.Surface;connectionPill.BorderColor=UiTheme.Border;connectionPill.Radius=15;connectionPill.Controls.Add(connection);canvas.Controls.Add(connectionPill);
        closeButton.Text="×";closeButton.Font=new Font("Segoe UI",15);closeButton.SetBounds(477,14,27,28);UiTheme.StyleButton(closeButton,quiet:true);closeButton.ForeColor=UiTheme.Muted;closeButton.Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,26,27),8));closeButton.Click+=(_,_)=>Close();canvas.Controls.Add(closeButton);

        var hero=new SurfacePanel{Bounds=new Rectangle(0,68,504,210),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=18};canvas.Controls.Add(hero);
        printerSketch.Bounds=new Rectangle(14,26,132,164);printerSketch.BackColor=UiTheme.Surface;hero.Controls.Add(printerSketch);
        statePill.Bounds=new Rectangle(164,16,116,23);statePill.BackColor=UiTheme.PanelSuccess;statePill.BorderColor=UiTheme.BorderSuccess;statePill.Radius=11;
        status.Text=english?"NOT SET UP":"SIN CONFIGURAR";status.Font=new Font("Segoe UI",7,FontStyle.Bold);status.ForeColor=UiTheme.Muted;status.TextAlign=ContentAlignment.MiddleCenter;status.Dock=DockStyle.Fill;statePill.Controls.Add(status);hero.Controls.Add(statePill);
        job.Text=english?"Waiting for printer":"Esperando impresora";job.Font=UiTheme.Font(18,true);job.ForeColor=UiTheme.Text;job.AutoEllipsis=true;job.SetBounds(164,45,314,31);hero.Controls.Add(job);
        jobSubtitle.Text="Bambu Cloud · P1S";jobSubtitle.Font=UiTheme.Font(8);jobSubtitle.ForeColor=UiTheme.TextSoft;jobSubtitle.AutoEllipsis=true;jobSubtitle.SetBounds(165,77,310,17);hero.Controls.Add(jobSubtitle);
        percent.Text="—%";percent.Font=new Font("Segoe UI Variable Display, Segoe UI",29,FontStyle.Bold);percent.ForeColor=UiTheme.Text;percent.SetBounds(160,96,138,44);hero.Controls.Add(percent);
        layerInfo.Text=english?"— / — layers":"— / — capas";layerInfo.Font=UiTheme.Font(8,true);layerInfo.ForeColor=UiTheme.TextSoft;layerInfo.TextAlign=ContentAlignment.BottomRight;layerInfo.SetBounds(389,112,88,23);hero.Controls.Add(layerInfo);
        progress.BackColor=UiTheme.Border;progress.ProgressColor=UiTheme.Accent;progress.SetBounds(165,149,312,7);hero.Controls.Add(progress);
        remaining.Text=english?"— remaining":"— restantes";remaining.Font=UiTheme.Font(8,true);remaining.ForeColor=UiTheme.TextSoft;remaining.SetBounds(165,166,150,17);hero.Controls.Add(remaining);
        finishInfo.Text=english?"Finish · —":"Fin · —";finishInfo.Font=UiTheme.Font(8);finishInfo.ForeColor=UiTheme.Muted;finishInfo.TextAlign=ContentAlignment.MiddleRight;finishInfo.SetBounds(325,166,152,17);hero.Controls.Add(finishInfo);

        var temperatureTitle=MakeLabel(english?"TEMPERATURES":"TEMPERATURAS",8,UiTheme.Muted,true);temperatureTitle.SetBounds(0,294,250,18);canvas.Controls.Add(temperatureTitle);
        var nozzleCard=new SurfacePanel{Bounds=new Rectangle(0,318,242,116),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=16};nozzleGauge.Dock=DockStyle.Fill;nozzleCard.Controls.Add(nozzleGauge);canvas.Controls.Add(nozzleCard);
        var bedCard=new SurfacePanel{Bounds=new Rectangle(262,318,242,116),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=16};bedGauge.Dock=DockStyle.Fill;bedCard.Controls.Add(bedGauge);canvas.Controls.Add(bedCard);

        var filamentTitle=MakeLabel("AMS · SPOOL SLOTS",8,UiTheme.Muted,true);if(!english)filamentTitle.Text="AMS · BOBINAS";filamentTitle.SetBounds(0,451,160,18);canvas.Controls.Add(filamentTitle);
        inventorySummary.Text=english?"4 slots":"4 ranuras";inventorySummary.Font=new Font("Segoe UI",8);inventorySummary.ForeColor=UiTheme.Muted;inventorySummary.TextAlign=ContentAlignment.MiddleRight;inventorySummary.SetBounds(162,451,204,18);canvas.Controls.Add(inventorySummary);
        libraryButton.Text=english?"FILAMENTS":"BIBLIOTECA";libraryButton.Font=new Font("Segoe UI",7,FontStyle.Bold);libraryButton.SetBounds(385,443,119,32);libraryButton.BackColor=UiTheme.SurfaceRaised;libraryButton.ForeColor=UiTheme.TextSoft;libraryButton.FlatStyle=FlatStyle.Flat;libraryButton.FlatAppearance.BorderColor=UiTheme.Border;UiTheme.StyleButton(libraryButton,quiet:true);libraryButton.Click+=(_,_)=>OpenFilamentLibrary();canvas.Controls.Add(libraryButton);
        var filamentGrid=new SurfacePanel{Bounds=new Rectangle(0,489,504,126),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=16};canvas.Controls.Add(filamentGrid);
        for(var i=0;i<4;i++){var card=new FilamentCard(i+1){Bounds=new Rectangle(8+i*122,6,118,112)};filamentCards.Add(card);filamentGrid.Controls.Add(card);if(i<3)filamentGrid.Controls.Add(new Panel{Bounds=new Rectangle(128+i*122,20,1,82),BackColor=UiTheme.Border});}

        updated.Text=english?"Waiting for connection":"Esperando conexión";updated.Font=new Font("Segoe UI",8);updated.ForeColor=UiTheme.Muted;updated.AutoEllipsis=true;updated.SetBounds(0,645,330,32);canvas.Controls.Add(updated);
        settingsButton.Text=english?"SETTINGS":"AJUSTES";settingsButton.Font=new Font("Segoe UI",8,FontStyle.Bold);settingsButton.SetBounds(385,638,119,34);settingsButton.BackColor=UiTheme.SurfaceRaised;settingsButton.ForeColor=UiTheme.TextSoft;settingsButton.FlatStyle=FlatStyle.Flat;settingsButton.FlatAppearance.BorderColor=UiTheme.Border;settingsButton.FlatAppearance.MouseOverBackColor=UiTheme.SurfaceHover;UiTheme.StyleButton(settingsButton);settingsButton.Click+=async(_,_)=>await OpenSettings();canvas.Controls.Add(settingsButton);
        RenderLibraryFilament();
        InitializeTrayMenu();
        positionSaveTimer.Tick+=(_,_)=>{positionSaveTimer.Stop();SaveDesktopLocation();};
        Move+=(_,_)=>{if(desktopWidget){positionSaveTimer.Stop();positionSaveTimer.Start();}};
        FormClosing+=(_,_)=>{positionSaveTimer.Stop();SaveDesktopLocation();trayIcon.Visible=false;trayIcon.Dispose();trayIconImage?.Dispose();};
        UiLocalization.Apply(this);
        Shown+=async(_,_)=>{if(AppPreferences.Current.DesktopWidget)ApplyDesktopWidgetMode(true);await LoadConfig();};
    }

    readonly SurfacePanel connectionPill=new();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,int message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    Icon? trayIconImage;
    void InitializeTrayMenu()
    {
        var menu=new ContextMenuStrip{ShowImageMargin=false};
        var settingsItem=new ToolStripMenuItem(english?"Settings…":"Ajustes…");
        settingsItem.Click+=async(_,_)=>await OpenSettings();
        disableWidgetMenuItem.Enabled=desktopWidget;
        disableWidgetMenuItem.Click+=(_,_)=>
        {
            SaveDesktopLocation();
            AppPreferences.Save(AppPreferences.Current.Theme,AppPreferences.Current.Language,false);
            ApplyDesktopWidgetMode(false,lastDesktopLocation);
        };
        var exitItem=new ToolStripMenuItem("Salir");
        exitItem.Click+=(_,_)=>Close();
        menu.Items.Add(settingsItem);menu.Items.Add(disableWidgetMenuItem);menu.Items.Add(new ToolStripSeparator());menu.Items.Add(exitItem);
        trayIconImage=CreateTrayIcon();trayIcon.Icon=trayIconImage;trayIcon.Text="Bambu Bar · P1S";trayIcon.ContextMenuStrip=menu;trayIcon.Visible=false;
        trayIcon.MouseDoubleClick+=async(_,e)=>{if(e.Button==MouseButtons.Left)await OpenSettings();};
    }

    static Icon CreateTrayIcon()
    {
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets","bambu-printer.ico");
        if(File.Exists(iconPath))return new Icon(iconPath);
        using var appIcon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if(appIcon is not null)return (Icon)appIcon.Clone();
        using var bitmap=new Bitmap(32,32);
        using(var graphics=Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);
            using var path=SurfacePanel.RoundedPath(new Rectangle(1,1,30,30),8);
            using var background=new SolidBrush(UiTheme.SurfaceRaised);graphics.FillPath(background,path);
            using var rim=new Pen(UiTheme.Accent,2);graphics.DrawEllipse(rim,7,7,18,18);graphics.DrawEllipse(rim,10,10,12,12);
            using var hole=new SolidBrush(UiTheme.SurfaceRaised);graphics.FillEllipse(hole,13,13,6,6);
            using var highlight=new SolidBrush(UiTheme.Accent);graphics.FillEllipse(highlight,22,6,4,4);
        }
        var handle=bitmap.GetHicon();
        try{return (Icon)Icon.FromHandle(handle).Clone();}
        finally{DestroyIcon(handle);}
    }

    static Icon LoadPrinterIcon()
    {
        var iconPath=Path.Combine(AppContext.BaseDirectory,"Assets","bambu-printer.ico");
        return File.Exists(iconPath)
            ? new Icon(iconPath)
            : Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
    }

    void ApplyDesktopWidgetMode(bool enabled,Point? preferredLocation=null)
    {
        if(enabled)
        {
            Point point;
            if(preferredLocation.HasValue)point=preferredLocation.Value;
            else if(AppPreferences.Current.DesktopLeft.HasValue&&AppPreferences.Current.DesktopTop.HasValue)point=new Point(AppPreferences.Current.DesktopLeft.Value,AppPreferences.Current.DesktopTop.Value);
            else if(IsHandleCreated)point=Location;
            else point=new Point(Screen.PrimaryScreen!.WorkingArea.Right-ClientSize.Width-24,Screen.PrimaryScreen.WorkingArea.Top+80);
            WindowState=FormWindowState.Normal;updated.Visible=false;settingsButton.Visible=false;ClientSize=new Size(540,647);SetCornerRegion(647);ShowInTaskbar=true;
            point=ClampToWorkingArea(point,ClientSize);Location=point;
            // Keep this as a regular, non-topmost window. WorkerW parenting is undocumented
            // and can make WinForms windows disappear on current Windows builds.
            desktopAttached=false;desktopWidget=true;lastDesktopLocation=point;disableWidgetMenuItem.Enabled=true;trayIcon.Visible=true;
            Show();BringToFront();
            return;
        }

        Point? location=preferredLocation;
        if(desktopAttached){location=DesktopWidgetHost.Detach(this);desktopAttached=false;}
        desktopWidget=false;updated.Visible=true;settingsButton.Visible=true;ClientSize=new Size(540,790);SetCornerRegion(790);ShowInTaskbar=true;disableWidgetMenuItem.Enabled=false;trayIcon.Visible=false;
        if(location.HasValue){lastDesktopLocation=location;Location=ClampToWorkingArea(location.Value,ClientSize);}
        Show();BringToFront();
    }

    void SetCornerRegion(int height)
    {
        var old=Region;Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,539,height-1),16));old?.Dispose();
    }

    static Point ClampToWorkingArea(Point point,Size size)
    {
        var screen=Screen.AllScreens.FirstOrDefault(s=>s.WorkingArea.Contains(point))??Screen.PrimaryScreen!;
        var area=screen.WorkingArea;
        return new Point(Math.Clamp(point.X,area.Left,Math.Max(area.Left,area.Right-size.Width)),Math.Clamp(point.Y,area.Top,Math.Max(area.Top,area.Bottom-size.Height)));
    }

    void SaveDesktopLocation()
    {
        if(!desktopWidget)return;
        var point=Location;
        lastDesktopLocation=point;AppPreferences.Current.DesktopLeft=point.X;AppPreferences.Current.DesktopTop=point.Y;
        try{AppPreferences.Save(AppPreferences.Current.Theme,AppPreferences.Current.Language,desktopWidget);}catch{}
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if(desktopWidget&&(message.Msg==0x007E||message.Msg==TaskbarCreatedMessage))
        {
            var point=DesktopWidgetHost.TryGetScreenPosition(Handle,out var current)?current:lastDesktopLocation;
            BeginInvoke(new Action(()=>ApplyDesktopWidgetMode(true,point)));
        }
    }
    static Label MakeLabel(string text,float size,Color color,bool bold=false)=>new(){Text=text,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=color,AutoEllipsis=true};
    void UpdateFilament(JsonElement print){RenderLibraryFilament();var tray=Num(print,"tray_now");if(tray.HasValue&&tray.Value>=0&&tray.Value<4){var spool=filamentLibrary.Spools.FirstOrDefault(x=>x.Id==filamentLibrary.Slots[(int)tray.Value]);if(spool!=null){SetText(jobSubtitle,$"{spool.Material} · {spool.Name}");progress.ProgressColor=ParseFilamentColor(spool.Hex)??UiTheme.Accent;return;}}SetText(jobSubtitle,"Bambu Cloud · P1S");progress.ProgressColor=UiTheme.Accent;}
    void LoadLibrary(){try{if(File.Exists(libraryFile))filamentLibrary=JsonSerializer.Deserialize<FilamentLibrary>(File.ReadAllText(libraryFile))??new FilamentLibrary();}catch{filamentLibrary=new FilamentLibrary();}if(filamentLibrary.Slots==null||filamentLibrary.Slots.Length!=4)filamentLibrary.Slots=new string?[4];filamentLibrary.History??=new();filamentLibrary.Spools??=new();}
    void SaveLibrary(){try{Directory.CreateDirectory(Path.GetDirectoryName(libraryFile)!);File.WriteAllText(libraryFile,JsonSerializer.Serialize(filamentLibrary,new JsonSerializerOptions{WriteIndented=true}));}catch(Exception ex){SetText(updated,"No se pudo guardar biblioteca: "+ex.Message);}}
    void OpenFilamentLibrary(){using var window=new FilamentManager(filamentLibrary,SaveLibrary);window.ShowDialog(this);if(!IsDisposed)RenderLibraryFilament();}
    void RenderLibraryFilament(){for(var i=0;i<4;i++){var spool=filamentLibrary.Spools.FirstOrDefault(x=>x.Id==filamentLibrary.Slots[i]);if(spool==null){filamentCards[i].SetInventory(null,null,null,null,null);continue;}var pct=spool.Capacity>0?(int)Math.Round(100*spool.Remaining/spool.Capacity):0;filamentCards[i].SetInventory(spool.Material,pct,ParseFilamentColor(spool.Hex),spool.Remaining,spool.Capacity);}var assigned=filamentLibrary.Slots.Count(id=>id!=null&&filamentLibrary.Spools.Any(s=>s.Id==id));var pending=filamentLibrary.History.Count(h=>h.Pending);SetText(inventorySummary,pending>0?$"! {pending} consumo pendiente":$"{assigned}/4 asignadas");inventorySummary.ForeColor=pending>0?UiTheme.Warning:UiTheme.Muted;if(pending>0){libraryButton.ForeColor=UiTheme.Warning;libraryButton.FlatAppearance.BorderColor=UiTheme.BorderWarning;}else{libraryButton.ForeColor=UiTheme.TextSoft;libraryButton.FlatAppearance.BorderColor=UiTheme.Border;}}
    static Color? ParseFilamentColor(string? raw){if(string.IsNullOrWhiteSpace(raw))return null;var hex=raw.Trim().TrimStart('#');if(hex.Length<6)return null;try{return Color.FromArgb(Convert.ToInt32(hex.Substring(0,2),16),Convert.ToInt32(hex.Substring(2,2),16),Convert.ToInt32(hex.Substring(4,2),16));}catch{return null;}}
    async Task LoadConfig()
    {
        try
        {
            if (!File.Exists(configFile)) return;
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configFile));
            serial = doc.RootElement.GetProperty("serial").GetString() ?? "";
            region = doc.RootElement.GetProperty("region").GetString() ?? "us";
            var token = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(doc.RootElement.GetProperty("token").GetString()!), null, DataProtectionScope.CurrentUser));
            cloudToken=token;await Connect(token);
        }
        catch (Exception ex) { connection.Text = "Error de configuración"; updated.Text = ex.Message; }
    }

    async Task OpenSettings()
    {
        var wasDesktopWidget=desktopWidget;
        var previousLanguage=AppPreferences.Current.Language;
        Point? widgetLocation=null;
        if(wasDesktopWidget&&desktopAttached){widgetLocation=DesktopWidgetHost.Detach(this);desktopAttached=false;lastDesktopLocation=widgetLocation;}
        using var dialog=new AppSettingsDialog(AppPreferences.Current.Theme,AppPreferences.Current.Language,connection.Text,desktopWidget);
        var result=dialog.ShowDialog(this);
        if(result==DialogResult.Cancel){if(wasDesktopWidget)ApplyDesktopWidgetMode(true,widgetLocation);return;}
        try{AppPreferences.Save(dialog.SelectedTheme,dialog.SelectedLanguage,dialog.DesktopWidgetEnabled);}
        catch(Exception ex){if(wasDesktopWidget)ApplyDesktopWidgetMode(true,widgetLocation);MessageBox.Show(this,"No se pudieron guardar los ajustes.\n"+ex.Message,"Ajustes",MessageBoxButtons.OK,MessageBoxIcon.Error);return;}
        if(!string.Equals(dialog.SelectedTheme,UiTheme.Mode,StringComparison.OrdinalIgnoreCase) ||
           !string.Equals(dialog.SelectedLanguage,previousLanguage,StringComparison.OrdinalIgnoreCase))
        {
            Application.Restart();Close();return;
        }
        if(result==DialogResult.Retry)await ConfigureCloud();
        ApplyDesktopWidgetMode(dialog.DesktopWidgetEnabled,widgetLocation??Location);
    }

    async Task ConfigureCloud()
    {
        using var dialog = new ConfigDialog(serial, region);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            serial = dialog.Serial; region = dialog.CloudRegion;
            var token = dialog.Token;cloudToken=token;
            var encrypted = Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser));
            Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);
            await File.WriteAllTextAsync(configFile, JsonSerializer.Serialize(new { serial, region, token = encrypted }));
            cloudToken=token;await Connect(token);
        }
        catch (Exception ex) { connection.Text = "No se pudo conectar"; updated.Text = ex.Message; }
    }

    async Task Connect(string token)
    {
        var user = await UserIdAsync(token, region);
        if (client is not null) { try { await client.DisconnectAsync(); } catch { } client.Dispose(); }
        client = new MqttFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e =>
        {
            try { var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment); using var d=JsonDocument.Parse(payload); var root=d.RootElement.Clone(); BeginInvoke(new Action(() => Render(root))); } catch { }
            return Task.CompletedTask;
        };
        client.ConnectedAsync += async _ =>
        {
            BeginInvoke(new Action(() => { connection.Text = "CLOUD ACTIVA"; connection.ForeColor = UiTheme.Accent; connectionPill.BackColor = UiTheme.SurfaceRaised; connectionPill.BorderColor = UiTheme.BorderSuccess; connectionPill.Invalidate(); updated.Text = "Actualizado · " + DateTime.Now.ToString("HH:mm"); }));
            await client.SubscribeAsync($"device/{serial}/report", MqttQualityOfServiceLevel.AtMostOnce);
            await client.PublishAsync(new MqttApplicationMessageBuilder().WithTopic($"device/{serial}/request").WithPayload("{\"pushing\":{\"command\":\"pushall\",\"sequence_id\":\"1\"}}" ).Build());
        };
        client.DisconnectedAsync += _ => { if (!IsDisposed) BeginInvoke(new Action(() => { connection.Text="SIN CONEXIÓN"; connection.ForeColor=UiTheme.Muted; connectionPill.BackColor=UiTheme.Surface; connectionPill.BorderColor=UiTheme.Border; connectionPill.Invalidate(); updated.Text="Sin conexión · revisa internet"; })); return Task.CompletedTask; };
        var host = region == "cn" ? "cn.mqtt.bambulab.com" : "us.mqtt.bambulab.com";
        var options = new MqttClientOptionsBuilder().WithTcpServer(host,8883).WithTlsOptions(o=>o.UseTls()).WithCredentials("u_"+user,token).WithClientId("bambu_widget_"+Guid.NewGuid().ToString("N")).WithCleanSession().Build();
        await client.ConnectAsync(options);
        updated.Text = "Conectando…";
    }

    async Task<string> UserIdAsync(string token, string selectedRegion)
    {
        var api=selectedRegion=="cn"?"https://api.bambulab.cn":"https://api.bambulab.com";
        using var http=new HttpClient(); using var request=new HttpRequestMessage(HttpMethod.Get,api+"/v1/design-user-service/my/preference"); request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        using var response=await http.SendAsync(request); var body=await response.Content.ReadAsStringAsync();
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"Bambu Cloud no aceptó la sesión ({(int)response.StatusCode}). Vuelve a iniciar sesión.");
        using var profile=JsonDocument.Parse(body); if(profile.RootElement.TryGetProperty("uid",out var uid)) return uid.ToString();
        throw new InvalidDataException("Bambu Cloud no devolvió el identificador de usuario necesario para MQTT.");
    }

    void Render(JsonElement root)
    {
        var p=Find(root,"print"); if(p.ValueKind==JsonValueKind.Undefined) p=root;
        var rawState=Str(p,"gcode_state","state"); var isIdle=false;
        var incomingJob=Str(p,"gcode_file","subtask_name","task_name");if(!string.IsNullOrWhiteSpace(incomingJob))activeJob=Path.GetFileNameWithoutExtension(incomingJob);
        var incomingTask=Str(p,"task_id","print_id");if(!string.IsNullOrWhiteSpace(incomingTask))activeTaskId=incomingTask;
        if(!string.IsNullOrWhiteSpace(rawState)){var state=rawState.ToLowerInvariant();if(state is "running" or "printing"){if(lastState is not ("running" or "printing")){finishHandled=false;activeTaskId=incomingTask;}if(!string.IsNullOrWhiteSpace(incomingJob))activeJob=Path.GetFileNameWithoutExtension(incomingJob);}if((state is "finish" or "finished" or "failed" or "cancelled" or "aborted")&&(lastState is "running" or "printing")&&!finishHandled){finishHandled=true;_ = RecordCompletedPrintAsync(activeJob,activeTaskId,state is "finish" or "finished");}lastState=state;}
        if(!string.IsNullOrWhiteSpace(rawState))
        {
            var normalized=rawState.ToLowerInvariant();
            printerSketch.IsPrinting=normalized is "running" or "printing";
            var display=normalized switch { "running" or "printing"=>(english?"Printing":"Imprimiendo"), "idle"=>(english?"Idle":"En espera"), "prepare"=>(english?"Preparing":"Preparando"), "finish" or "finished"=>(english?"Finished":"Finalizada"), "failed"=>(english?"Print error":"Error de impresión"), "pause" or "paused"=>(english?"Paused":"En pausa"), _=>rawState };
            SetText(status,display.ToUpperInvariant());
            status.ForeColor=normalized switch { "running" or "printing" or "finish" or "finished"=>UiTheme.Success, "failed"=>UiTheme.Error, "pause" or "paused"=>UiTheme.Warning, "prepare"=>UiTheme.Cool, _=>UiTheme.Muted };
            statePill.BackColor=normalized switch { "running" or "printing" or "finish" or "finished"=>UiTheme.PanelSuccess, "failed"=>UiTheme.PanelError, "pause" or "paused"=>UiTheme.PanelWarning, "prepare"=>UiTheme.PanelInfo, _=>UiTheme.SurfaceRaised };
            statePill.BorderColor=normalized switch { "running" or "printing" or "finish" or "finished"=>UiTheme.BorderSuccess, "failed"=>UiTheme.BorderError, "pause" or "paused"=>UiTheme.BorderWarning, "prepare"=>UiTheme.BorderInfo, _=>UiTheme.Border };statePill.Invalidate();
            if(normalized=="idle") { isIdle=true; SetText(job,english?"No active print":"Sin impresión activa"); SetText(percent,"—%"); progress.Value=0; SetText(remaining,english?"— remaining":"— restantes"); SetText(layerInfo,english?"— / — layers":"— / — capas"); SetText(finishInfo,english?"Estimated finish · —":"Fin estimado · —"); }
        }
        var name=Str(p,"gcode_file","subtask_name","task_name"); if(!isIdle&&!string.IsNullOrWhiteSpace(name)) SetText(job,Path.GetFileNameWithoutExtension(name));
        var pc=Num(p,"mc_percent","percent","progress"); if(pc.HasValue&&!isIdle){SetText(percent,$"{pc:0}%");progress.Value=(int)Math.Clamp(pc.Value,0,100);}
        var layer=Num(p,"layer_num"); var totalLayers=Num(p,"total_layer_num");
        if(!isIdle&&(layer.HasValue||totalLayers.HasValue))SetText(layerInfo,$"{(layer.HasValue?layer.Value.ToString("0"):"—")} / {(totalLayers.HasValue?totalLayers.Value.ToString("0"):"—")} {(english?"layers":"capas")}");
        var min=Num(p,"mc_remaining_time","remaining_time"); if(min.HasValue&&!isIdle){SetText(remaining,FormatRemaining(min.Value)+(english?" remaining":" restantes"));SetText(finishInfo,(english?"Estimated finish · ":"Fin estimado · ")+DateTime.Now.AddMinutes(Math.Max(0,min.Value)).ToString("HH:mm"));}
        nozzleGauge.TargetTemperature=Num(p,"nozzle_target_temper","nozzle_target_temp");
        bedGauge.TargetTemperature=Num(p,"bed_target_temper","bed_target_temp");
        UpdateTemperature(nozzleGauge,Num(p,"nozzle_temper","nozzle_temp"),300,ref nozzleZeroSince,ref nozzleZeroCount);
        UpdateTemperature(bedGauge,Num(p,"bed_temper","bed_temp"),120,ref bedZeroSince,ref bedZeroCount);
        UpdateFilament(p);
        SetText(updated,(english?"Updated · ":"Actualizado · ")+DateTime.Now.ToString("HH:mm"));
    }
    static void UpdateTemperature(TemperatureGauge gauge,double? reading,double max,ref DateTime? zeroSince,ref int zeroCount)
    {
        if(!reading.HasValue||reading.Value<0||reading.Value>max)return;
        if(reading.Value>0){zeroSince=null;zeroCount=0;gauge.Temperature=reading.Value;return;}
        zeroSince??=DateTime.UtcNow;zeroCount++;
        if(zeroCount>=4&&(DateTime.UtcNow-zeroSince.Value).TotalSeconds>=5)gauge.Temperature=0;
    }
    async Task RecordCompletedPrintAsync(string jobName,string? taskId,bool completedSuccessfully)
    {
        var record=new PrintEntry{Job=(completedSuccessfully?"":"Fallida · ")+(string.IsNullOrWhiteSpace(jobName)?"Impresión finalizada":jobName),Date=DateTime.Now,Pending=true};filamentLibrary.History.Add(record);SaveLibrary();RenderLibraryFilament();SetText(updated,"Buscando consumo de filamento en Bambu Cloud…");
        try{var mapped=new List<(int Slot,double Grams)>();if(completedSuccessfully){for(var retry=0;retry<3&&mapped.Count==0;retry++){if(retry>0)await Task.Delay(2500);mapped=await FetchCloudUsageAsync(jobName,taskId);}}foreach(var (slot,grams) in mapped){if(slot<1||slot>4||grams<=0)continue;var spoolId=filamentLibrary.Slots[slot-1];record.Slices.Add(new PrintSlice{Slot=slot,SpoolId=spoolId,Grams=grams,Source="Bambu Cloud"});var spool=filamentLibrary.Spools.FirstOrDefault(x=>x.Id==spoolId);if(spool!=null)spool.Remaining=Math.Max(0,spool.Remaining-grams);}record.Pending=record.Slices.Count==0||record.Slices.Any(x=>x.SpoolId==null);}catch{}
        SaveLibrary();if(!IsDisposed)BeginInvoke(new Action(()=>{RenderLibraryFilament();SetText(updated,record.Pending?"Aviso · registra el consumo de esta impresión en Biblioteca":"Consumo añadido al historial");}));
    }
    async Task<List<(int Slot,double Grams)>> FetchCloudUsageAsync(string jobName,string? taskId)
    {
        if(string.IsNullOrWhiteSpace(cloudToken))return new();var api=region=="cn"?"https://api.bambulab.cn":"https://api.bambulab.com";using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
        async Task<JsonDocument?> Get(string path){using var req=new HttpRequestMessage(HttpMethod.Get,api+path);req.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",cloudToken);var res=await http.SendAsync(req);if(!res.IsSuccessStatusCode)return null;return JsonDocument.Parse(await res.Content.ReadAsStringAsync());}
        JsonDocument? detail=null;if(!string.IsNullOrWhiteSpace(taskId))detail=await Get("/v1/user-service/my/task/"+Uri.EscapeDataString(taskId));
        if(detail==null){using var list=await Get("/v1/user-service/my/tasks?deviceId="+Uri.EscapeDataString(serial)+"&limit=30");if(list!=null){var arr=Find(list.RootElement,"hits");if(arr.ValueKind!=JsonValueKind.Array)arr=Find(list.RootElement,"tasks");var target=Path.GetFileNameWithoutExtension(jobName??"");if(arr.ValueKind==JsonValueKind.Array)foreach(var item in arr.EnumerateArray()){var title=Str(item,"title","designTitle","subtask_name","fileName");var id=Str(item,"id","task_id");if(id!=null&&title!=null&&target.Length>0&&(title.Contains(target,StringComparison.OrdinalIgnoreCase)||target.Contains(Path.GetFileNameWithoutExtension(title),StringComparison.OrdinalIgnoreCase))){taskId=id;break;}}}if(taskId!=null)detail=await Get("/v1/user-service/my/task/"+Uri.EscapeDataString(taskId));}
        if(detail==null)return new();using(detail){var mapping=Find(detail.RootElement,"amsDetailMapping");if(mapping.ValueKind!=JsonValueKind.Array)return new();var result=new List<(int,double)>();foreach(var item in mapping.EnumerateArray()){var grams=Num(item,"weight","used_g","filament_used_g");var slotId=(int?)Num(item,"slotId","slot_id");var amsId=(int?)Num(item,"amsId","ams_id");if(!grams.HasValue||grams<=0||!slotId.HasValue)continue;result.Add((amsId.HasValue?amsId.Value*4+slotId.Value+1:slotId.Value+1,grams.Value));}return result;}
    }
    static string FormatRemaining(double minutes){var total=Math.Max(0,(int)Math.Round(minutes));return total>=60?$"{total/60} h {total%60:00} min":$"{total} min";}
    static void SetText(Label label,string text){text=UiLocalization.T(text);if(!string.Equals(label.Text,text,StringComparison.Ordinal))label.Text=text;}
    static JsonElement Find(JsonElement e,string key)
    {
        if(e.ValueKind==JsonValueKind.Object){if(e.TryGetProperty(key,out var v))return v;foreach(var x in e.EnumerateObject()){var n=Find(x.Value,key);if(n.ValueKind!=JsonValueKind.Undefined)return n;}}
        if(e.ValueKind==JsonValueKind.Array)foreach(var x in e.EnumerateArray()){var n=Find(x,key);if(n.ValueKind!=JsonValueKind.Undefined)return n;} return default;
    }
    static string? Str(JsonElement e,params string[] keys){foreach(var k in keys){var x=Find(e,k);if(x.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(x.GetString()))return x.GetString();}return null;}
    static double? Num(JsonElement e,params string[] keys){foreach(var k in keys){var x=Find(e,k);if(x.ValueKind==JsonValueKind.Number&&x.TryGetDouble(out var n))return n;if(x.ValueKind==JsonValueKind.String&&double.TryParse(x.GetString(),out n))return n;}return null;}
}

sealed class PrinterSketch:Control
{
 readonly System.Windows.Forms.Timer animationTimer=new(){Interval=38};
 float nozzleX=39;int direction=1,layer=0;bool isPrinting;
 public bool IsPrinting
 {
  get=>isPrinting;
  set{if(isPrinting==value)return;isPrinting=value;if(value)animationTimer.Start();else animationTimer.Stop();Invalidate();}
 }
 public PrinterSketch(){DoubleBuffered=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);animationTimer.Tick+=(_,_)=>{nozzleX+=direction*1.6f;if(nozzleX>=94){nozzleX=94;direction=-1;layer=Math.Min(10,layer+1);}else if(nozzleX<=40){nozzleX=40;direction=1;layer=Math.Min(10,layer+1);}Invalidate();};}
 protected override void Dispose(bool disposing){if(disposing)animationTimer.Dispose();base.Dispose(disposing);}
 protected override void OnPaint(PaintEventArgs e)
 {
  base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;var frame=UiTheme.Muted;var faint=UiTheme.Border;
  using var pen=new Pen(frame,2);using var thin=new Pen(faint,1);using var glass=new SolidBrush(UiTheme.SurfaceRaised);using var light=new SolidBrush(UiTheme.Accent);
  var body=new Rectangle(20,12,94,128);using var bodyPath=SurfacePanel.RoundedPath(body,13);g.FillPath(glass,bodyPath);g.DrawPath(pen,bodyPath);
  g.DrawLine(thin,25,39,109,39);g.DrawLine(thin,25,115,109,115);g.DrawLine(pen,32,39,32,112);g.DrawLine(pen,102,39,102,112);g.DrawLine(thin,34,101,100,101);
  g.DrawLine(pen,39,45,96,45);var headX=isPrinting?nozzleX:68;var headY=isPrinting?Math.Max(65,82-layer):59;
  using var head=new SolidBrush(isPrinting?UiTheme.Accent:UiTheme.Muted);using var headPath=SurfacePanel.RoundedPath(new Rectangle((int)headX-6,headY-2,12,7),3);g.FillPath(head,headPath);
  using var filamentPen=new Pen(isPrinting?UiTheme.Accent:UiTheme.Border,1.5f);g.DrawLine(filamentPen,headX,headY+5,headX,headY+10);
  if(isPrinting){using var bead=new SolidBrush(Color.FromArgb(180,UiTheme.Accent));g.FillEllipse(bead,headX-2,headY+10,4,2);}
  g.DrawLine(pen,39,101,96,101);g.DrawLine(thin,52,100,68,88);g.DrawLine(thin,68,88,84,100);g.FillEllipse(light,102,19,5,5);
  using var caption=new Font("Segoe UI",7,FontStyle.Bold);using var brush=new SolidBrush(UiTheme.Muted);var label="COREXY  ·  P1S";var size=g.MeasureString(label,caption);g.DrawString(label,caption,brush,(Width-size.Width)/2,151);
 }
}
class SurfacePanel : Panel
{
    public Color BorderColor { get; set; } = UiTheme.Border;
    int radius = 14;
    public int Radius
    {
        get => radius;
        set { var next = Math.Max(2, value); if (radius == next) return; radius = next; UpdateRegion(); Invalidate(); }
    }
    public SurfacePanel() { DoubleBuffered=true; }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        if(Width<4||Height<4)return;
        using var path=RoundedPath(new Rectangle(1,1,Width-3,Height-3),Math.Max(2,Radius-1));
        using var pen=new Pen(BorderColor,1); e.Graphics.DrawPath(pen,path);
    }
    void UpdateRegion()
    {
        if(Width<2||Height<2)return;
        using var path=RoundedPath(new Rectangle(0,0,Width-1,Height-1),Radius);
        Region=new Region(path);
    }
    internal static System.Drawing.Drawing2D.GraphicsPath RoundedPath(Rectangle rect,int radius)
    {
        var path=new System.Drawing.Drawing2D.GraphicsPath(); var d=radius*2;
        path.AddArc(rect.X,rect.Y,d,d,180,90); path.AddArc(rect.Right-d,rect.Y,d,d,270,90);
        path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); path.AddArc(rect.X,rect.Bottom-d,d,d,90,90); path.CloseFigure(); return path;
    }
}

sealed class ProgressTrack : Control
{
    int value;
    public int Value { get=>value; set{var next=Math.Clamp(value,0,100);if(this.value==next)return;this.value=next;Invalidate();} }
    public Color ProgressColor { get; set; }=UiTheme.Accent;
    public ProgressTrack(){DoubleBuffered=true; SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true); BackColor=UiTheme.Border;}
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds=new Rectangle(0,0,Width,Height);
        using var bg= new SolidBrush(BackColor); using var bgPath=SurfacePanel.RoundedPath(bounds,Math.Max(1,Height/2)); e.Graphics.FillPath(bg,bgPath);
        var fillWidth=(int)(Width*value/100.0); if(fillWidth>0){using var fg=new SolidBrush(ProgressColor);using var fgPath=SurfacePanel.RoundedPath(new Rectangle(0,0,Math.Max(Height,fillWidth),Height),Math.Max(1,Height/2));e.Graphics.FillPath(fg,fgPath);}
    }
}
sealed class TemperatureGauge : Control
{
    double? temperature, targetTemperature;
    public string Caption { get; set; } = "";
    public double Maximum { get; set; } = 300;
    public double ColdThreshold { get; set; } = 30;
    public double HotThreshold { get; set; } = 150;
    public double? TargetTemperature { get=>targetTemperature; set{if(targetTemperature==value)return;targetTemperature=value;Invalidate();} }
    public double? Temperature
    {
        get=>temperature;
        set{if(temperature==value)return;temperature=value;Invalidate();}
    }
    public TemperatureGauge()
    {
        DoubleBuffered=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=UiTheme.Surface;
    }
    protected override void OnPaintBackground(PaintEventArgs e) { using var brush=new SolidBrush(BackColor);e.Graphics.FillRectangle(brush,ClientRectangle); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var accent=temperature.HasValue?ColorFor(temperature.Value):UiTheme.Muted;
        using var captionFont=new Font("Segoe UI",8,FontStyle.Bold);using var captionBrush=new SolidBrush(UiTheme.TextSoft);
        g.DrawString(Caption.ToUpperInvariant(),captionFont,captionBrush,16,12);
        using var valueFont=new Font("Segoe UI Variable Display, Segoe UI",32,FontStyle.Bold);using var valueBrush=new SolidBrush(temperature.HasValue?accent:UiTheme.Muted);
        var valueText=temperature.HasValue?$"{temperature.Value:0}°":"—°";var valueSize=g.MeasureString(valueText,valueFont);g.DrawString(valueText,valueFont,valueBrush,14,31);
        using var unitFont=new Font("Segoe UI",8,FontStyle.Bold);using var unitBrush=new SolidBrush(UiTheme.Muted);
        g.DrawString("C",unitFont,unitBrush,14+valueSize.Width+1,54);
        using var targetFont=new Font("Segoe UI",8,FontStyle.Bold);using var targetBrush=new SolidBrush(UiTheme.Muted);
        g.DrawString("OBJETIVO",targetFont,targetBrush,145,43);
        using var targetValueFont=new Font("Segoe UI",15,FontStyle.Bold);using var targetValueBrush=new SolidBrush(UiTheme.TextSoft);
        g.DrawString(targetTemperature.HasValue?$"{targetTemperature.Value:0}°":"—°",targetValueFont,targetValueBrush,145,58);
        var track=new Rectangle(16,96,Width-32,5);using var bg=new SolidBrush(UiTheme.Border);using var bgPath=SurfacePanel.RoundedPath(track,3);g.FillPath(bg,bgPath);
        if(temperature.HasValue){var fillWidth=(int)(track.Width*Math.Clamp(temperature.Value/Maximum,0,1));if(fillWidth>0){using var fg=new SolidBrush(accent);using var fgPath=SurfacePanel.RoundedPath(new Rectangle(track.X,track.Y,Math.Max(5,fillWidth),track.Height),3);g.FillPath(fg,fgPath);}}
    }
    Color ColorFor(double value)
    {
        var ratio=Math.Clamp(value/Maximum,0,1);
        var cold=Color.FromArgb(86,166,238);var mild=Color.FromArgb(53,211,153);var warm=Color.FromArgb(255,184,61);var hot=Color.FromArgb(248,92,92);
        if(ratio<.28)return Mix(cold,mild,ratio/.28);
        if(ratio<.58)return Mix(mild,warm,(ratio-.28)/.30);
        return Mix(warm,hot,(ratio-.58)/.42);
    }
    static Color Mix(Color a,Color b,double t)=>Color.FromArgb((int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t));
}

sealed class FilamentCard : SurfacePanel
{
    readonly Label slotLabel=new(),materialLabel=new(),levelLabel=new(),detailLabel=new();
    readonly ProgressTrack meter=new();
    readonly ColorSwatch swatch=new();
    string? lastMaterial; int? lastRemaining; Color? lastColor; bool? lastPresent;
    public bool IsOccupied=>lastPresent==true;
    public FilamentCard(int index)
    {
        BackColor=UiTheme.Surface;BorderColor=UiTheme.Surface;Radius=10;
        slotLabel.Text=$"{index:00}";slotLabel.Font=new Font("Segoe UI",7,FontStyle.Bold);slotLabel.ForeColor=UiTheme.Muted;slotLabel.TextAlign=ContentAlignment.MiddleCenter;slotLabel.SetBounds(44,4,30,12);Controls.Add(slotLabel);
        swatch.SetBounds(41,18,34,34);swatch.Fill=UiTheme.SurfaceRaised;Controls.Add(swatch);
        materialLabel.Text="Vacía";materialLabel.Font=new Font("Segoe UI",8,FontStyle.Bold);materialLabel.ForeColor=UiTheme.Text;materialLabel.AutoEllipsis=true;materialLabel.TextAlign=ContentAlignment.MiddleCenter;materialLabel.SetBounds(4,55,110,18);Controls.Add(materialLabel);
        levelLabel.Text="—";levelLabel.Font=new Font("Segoe UI",8);levelLabel.ForeColor=UiTheme.TextSoft;levelLabel.TextAlign=ContentAlignment.MiddleCenter;levelLabel.SetBounds(4,74,110,15);Controls.Add(levelLabel);
        detailLabel.Text="Añade bobina";detailLabel.Font=new Font("Segoe UI",7);detailLabel.ForeColor=UiTheme.Muted;detailLabel.AutoEllipsis=true;detailLabel.TextAlign=ContentAlignment.MiddleCenter;detailLabel.SetBounds(4,88,110,13);Controls.Add(detailLabel);
        meter.SetBounds(12,105,94,4);meter.BackColor=UiTheme.Border;meter.ProgressColor=UiTheme.Accent;Controls.Add(meter);
    }
    public void SetInventory(string? name,int? percentage,Color? color,double? grams,double? capacity){if(name==null){lastPresent=false;materialLabel.Text="Vacía";levelLabel.Text="—";detailLabel.Text="Sin bobina";swatch.Fill=UiTheme.SurfaceRaised;meter.Value=0;return;}lastPresent=true;materialLabel.Text=name;levelLabel.Text=grams.HasValue?$"{grams.Value:0} g restantes":percentage.HasValue?$"{percentage.Value}% restante":"—";detailLabel.Text=percentage.HasValue?$"{percentage.Value}% de carga":"Bobina cargada";swatch.Fill=color??UiTheme.Accent;meter.ProgressColor=color??UiTheme.Accent;meter.Value=percentage??0;}
    public void SetSlot(string? material,int? remaining,Color? color,bool? present)
    {
        if(present is null)return;
        if(present==false){lastMaterial=null;lastRemaining=null;lastColor=null;lastPresent=false;RefreshDisplay();return;}
        lastPresent=true;
        if(!string.IsNullOrWhiteSpace(material))lastMaterial=material.Trim();
        if(remaining.HasValue)lastRemaining=remaining.Value;
        if(color.HasValue)lastColor=color.Value;
        RefreshDisplay();
    }
    void RefreshDisplay()
    {
        var material=lastPresent==false?"Vacía":lastMaterial??"Bobina detectada";
        var level=lastRemaining.HasValue?$"{lastRemaining.Value}%":"—";
        var detail=lastPresent==false?"Sin filamento":lastRemaining.HasValue?"Nivel estimado por AMS":"Nivel no reportado";
        var displayColor=lastColor??UiTheme.Accent;
        if(materialLabel.Text!=material)materialLabel.Text=material;
        if(levelLabel.Text!=level)levelLabel.Text=level;
        if(detailLabel.Text!=detail)detailLabel.Text=detail;
        swatch.Fill=lastPresent==true?displayColor:UiTheme.SurfaceRaised;
        meter.ProgressColor=displayColor;meter.Value=lastRemaining??0;
    }
}

sealed class ColorSwatch : Control
{
    Color fill=UiTheme.Accent;
    public Color Fill{get=>fill;set{if(fill==value)return;fill=value;Invalidate();}}
    public ColorSwatch(){DoubleBuffered=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=UiTheme.Surface;}
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect=new Rectangle(1,1,Math.Max(1,Width-3),Math.Max(1,Height-3));SpoolArt.Draw(e.Graphics,Fill,rect,UiTheme.Surface);
    }
}
sealed class ConfigDialog : Form
{
    readonly TextBox emailBox = new(); readonly TextBox codeBox = new(); readonly ComboBox printerBox = new(); readonly Label message = new(); readonly Button loginButton = new(); readonly Button resendButton = new();
    bool codeRequested;
    public string Serial => (printerBox.SelectedItem as PrinterChoice)?.Id ?? "";
    public string Token { get; private set; } = "";
    public string CloudRegion => "us";
    string Email => emailBox.Text.Trim();
    const string Api = "https://api.bambulab.com";

    public ConfigDialog(string serial, string region)
    {
        Text="Conectar con Bambu Cloud"; Size=new Size(420,395); FormBorderStyle=FormBorderStyle.FixedDialog; StartPosition=FormStartPosition.CenterParent; TopMost=false; ShowInTaskbar=false; MaximizeBox=false; MinimizeBox=false; BackColor=UiTheme.Background; ForeColor=UiTheme.Text; Font=new Font("Segoe UI",9);
        AddLabel("Correo de tu cuenta Bambu Lab",18,18); StyleInput(emailBox); emailBox.SetBounds(18,43,365,28); Controls.Add(emailBox);
        AddLabel("Código recibido por correo",18,82); StyleInput(codeBox); codeBox.SetBounds(18,107,250,28); codeBox.Enabled=false; Controls.Add(codeBox);
        resendButton.Text="Reenviar"; resendButton.SetBounds(278,107,105,28); resendButton.Enabled=false; StyleSecondary(resendButton); Controls.Add(resendButton); resendButton.Click += async (_,_) => await SendCode();
        AddLabel("Impresora vinculada",18,146); printerBox.SetBounds(18,171,365,28); printerBox.Enabled=false; printerBox.DropDownStyle=ComboBoxStyle.DropDownList; printerBox.DrawMode=DrawMode.OwnerDrawFixed; printerBox.BackColor=UiTheme.SurfaceRaised; printerBox.ForeColor=UiTheme.Text; printerBox.FlatStyle=FlatStyle.Flat; printerBox.DrawItem += (_,e) => { e.DrawBackground(); if(e.Index>=0) { var item=printerBox.Items[e.Index]; using var brush=new SolidBrush(UiTheme.Text); e.Graphics.DrawString(item?.ToString()??"",printerBox.Font,brush,e.Bounds); } e.DrawFocusRectangle(); }; Controls.Add(printerBox);
        message.Text="Te enviaremos un código. No hace falta contraseña ni copiar el número de serie."; message.Font=new Font("Segoe UI",8); message.ForeColor=UiTheme.Muted; message.Bounds=new Rectangle(18,211,365,38); Controls.Add(message);
        loginButton.Text="Enviar código"; loginButton.SetBounds(18,270,365,36); loginButton.BackColor=UiTheme.Accent; loginButton.ForeColor=UiTheme.AccentInk; loginButton.Font=new Font("Segoe UI",9,FontStyle.Bold); loginButton.FlatStyle=FlatStyle.Flat; loginButton.FlatAppearance.BorderSize=0; loginButton.Cursor=Cursors.Hand; UiTheme.StyleButton(loginButton,primary:true);loginButton.Click += async (_,_) => await Continue(); Controls.Add(loginButton); AcceptButton=loginButton;
    }
    async Task Continue()
    {
        if(!codeRequested){await SendCode();return;}
        if(Token.Length==0){await VerifyCode();return;}
        if(printerBox.SelectedItem is not PrinterChoice){message.Text="Selecciona la impresora vinculada.";return;}
        DialogResult=DialogResult.OK; Close();
    }
    async Task SendCode()
    {
        if(string.IsNullOrWhiteSpace(Email)||!Email.Contains('@')){message.Text="Escribe el correo de tu cuenta Bambu Lab.";return;}
        loginButton.Enabled=false; resendButton.Enabled=false; message.Text="Solicitando código a Bambu Lab…";
        try
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(25)};
            using var response=await http.PostAsJsonAsync(Api+"/v1/user-service/user/sendemail/code",new { email=Email, type="codeLogin" });
            var body=await response.Content.ReadAsStringAsync(); string? serverMessage=null; try{using var doc=JsonDocument.Parse(body);serverMessage=GetProp(doc.RootElement,"message");}catch{}
            if(!response.IsSuccessStatusCode)throw new InvalidOperationException(serverMessage??$"Bambu Lab rechazó la solicitud ({(int)response.StatusCode}).");
            codeRequested=true; emailBox.ReadOnly=true; codeBox.Enabled=true; resendButton.Enabled=true; loginButton.Text="Verificar código";
            message.Text="Código enviado. Revisa tu correo e introdúcelo aquí."; codeBox.Focus();
        }
        catch(Exception ex){message.Text=ex.Message;}
        finally{if(!IsDisposed)loginButton.Enabled=true;}
    }
    async Task VerifyCode()
    {
        if(string.IsNullOrWhiteSpace(codeBox.Text)){message.Text="Escribe el código que recibiste por correo.";return;}
        loginButton.Enabled=false; resendButton.Enabled=false; message.Text="Verificando código…";
        try
        {
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(25)};
            using var response=await http.PostAsJsonAsync(Api+"/v1/user-service/user/login",new { account=Email, code=codeBox.Text.Trim() });
            var body=await response.Content.ReadAsStringAsync(); using var doc=JsonDocument.Parse(body); Token=GetProp(doc.RootElement,"accessToken")??"";
            if(!response.IsSuccessStatusCode||Token.Length==0)throw new InvalidOperationException(GetProp(doc.RootElement,"message")??"El código no fue aceptado. Solicita otro e inténtalo de nuevo.");
            await LoadPrinters();
        }
        catch(Exception ex){Token="";message.Text=ex.Message;resendButton.Enabled=true;}
        finally{if(!IsDisposed)loginButton.Enabled=true;}
    }
    async Task LoadPrinters()
    {
        message.Text="Buscando impresoras vinculadas a tu cuenta…";
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(25)}; using var request=new HttpRequestMessage(HttpMethod.Get,Api+"/v1/iot-service/api/user/bind"); request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",Token);
        using var response=await http.SendAsync(request); var body=await response.Content.ReadAsStringAsync();
        if(!response.IsSuccessStatusCode)throw new InvalidOperationException($"No se pudo consultar la lista de impresoras ({(int)response.StatusCode}).");
        using var doc=JsonDocument.Parse(body); if(!doc.RootElement.TryGetProperty("devices",out var devices)||devices.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Bambu Cloud no devolvió la lista de impresoras.");
        var choices=new List<PrinterChoice>();
        foreach(var device in devices.EnumerateArray())
        {
            var id=GetProp(device,"dev_id"); if(string.IsNullOrWhiteSpace(id))continue;
            var name=GetProp(device,"name")??id; var model=GetProp(device,"dev_product_name")??GetProp(device,"dev_model_name");
            choices.Add(new PrinterChoice(id,string.IsNullOrWhiteSpace(model)?name:$"{name} · {model}"));
        }
        if(choices.Count==0)throw new InvalidOperationException("No hay impresoras vinculadas a esta cuenta Bambu.");
        printerBox.Items.Clear(); foreach(var choice in choices)printerBox.Items.Add(choice);
        if(choices.Count==1)printerBox.SelectedIndex=0;
        printerBox.Enabled=true; codeBox.Enabled=false; resendButton.Enabled=false; loginButton.Text="Conectar impresora";
        message.Text=choices.Count==1?"Impresora encontrada. Pulsa Conectar impresora.":"Elige tu P1S en la lista y pulsa Conectar impresora.";
    }
    void StyleInput(TextBox box){box.BackColor=UiTheme.SurfaceRaised;box.ForeColor=UiTheme.Text;box.BorderStyle=BorderStyle.FixedSingle;box.Font=new Font("Segoe UI",10);}
    static void StyleSecondary(Button button){UiTheme.StyleButton(button);}    static string? GetProp(JsonElement obj,string key)=>obj.ValueKind==JsonValueKind.Object&&obj.TryGetProperty(key,out var value)?value.ToString():null;
    void AddLabel(string text,int x,int y)=>Controls.Add(new Label{Text=text,ForeColor=UiTheme.TextSoft,Font=new Font("Segoe UI",8,FontStyle.Bold),Bounds=new Rectangle(x,y,350,22)});
    sealed record PrinterChoice(string Id,string Label){public override string ToString()=>Label;}
}































