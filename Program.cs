using System.Drawing;
using Microsoft.Win32;
using System.Globalization;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Json;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Exceptions;
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
    readonly Panel progressSlide = new(), cameraSlide = new();
    readonly Panel cameraHost = new();
    readonly SurfacePanel hero = new(){BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=18};
    readonly Panel canvas = new(){BackColor=UiTheme.Background};
    readonly SurfacePanel sidebar = new(){BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=18};
    readonly Panel mainContent = new(){BackColor=UiTheme.Background};
    readonly Panel filamentSection = new(){BackColor=UiTheme.Background};
    readonly Panel librarySection = new(){BackColor=UiTheme.Background};
    readonly List<FilamentCard> sectionFilamentCards = new();
    readonly FlowLayoutPanel librarySpoolCards = new(){AutoScroll=true,WrapContents=true,FlowDirection=FlowDirection.LeftToRight};
    readonly Label sectionFilamentSummary = new();
    readonly Button printerNavButton = new(), filamentNavButton = new(), libraryNavButton = new(), settingsNavButton = new();
    readonly Button pausePrintButton = new(), stopPrintButton = new();
    readonly Button chamberLightButton = new();
    readonly SpeedModeSelector speedModeCombo;
    readonly Label speedModeCaption = new();
    readonly Label connectionCaption = new(), connectionStatus = new();
    readonly CameraPictureBox lanCameraPicture = new(){Dock=DockStyle.Fill,Visible=false};
    readonly Label cameraMessage = new();
    readonly CarouselDot[] carouselDots = { new(), new() };
    readonly Button libraryButton = new();
    readonly Label amsReadStatus = new();
    readonly Button closeButton = new();
    readonly Button expandWidgetButton = new();
    readonly NotifyIcon trayIcon = new();
    readonly ToolStripMenuItem disableWidgetMenuItem = new(AppPreferences.Current.Language == "en" ? "Disable widget mode" : "Desactivar modo widget");
    readonly System.Windows.Forms.Timer positionSaveTimer = new(){Interval=600};
    readonly System.Windows.Forms.Timer carouselTimer = new(){Interval=15000};
    readonly System.Windows.Forms.Timer carouselAnimationTimer = new(){Interval=15};
    readonly System.Windows.Forms.Timer cameraDimensionsTimer = new(){Interval=300};
    readonly System.Windows.Forms.Timer usageRetryTimer = new(){Interval=60000};
    readonly string libraryFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BambuP1SWidget", "filament-library.json");
    FilamentLibrary filamentLibrary = new();
    string cloudToken = ""; string cloudRefreshToken = ""; string cloudLoginData = ""; string cloudUsageSerial = ""; string cameraDevVersion = "";
    string cameraDeviceId = Guid.NewGuid().ToString("D");
    string lastState = ""; string activeJob = ""; string? activeTaskId; bool finishHandled;
    string?[] activePrintSpoolIds=Array.Empty<string?>();
    readonly Dictionary<int,int> latestAmsRemainingPercent=new();
    readonly Dictionary<int,(string? SpoolId,int Percent)> activeAmsLastReadings=new();
    readonly Dictionary<(int Slot,string SpoolId),double> activeLanAmsUsage=new();
    long printCommandSequence, statusSequence, amsReadRequestId;
    bool amsReadAwaiting;
    bool updatingPrintOptions;
    bool? chamberLightOn;
    int? reportedSpeedMode;
    IMqttClient? client;
    string serial = "";
    string region = "us";
    bool desktopWidget = AppPreferences.Current.DesktopWidget;
    bool desktopAttached;
    Point? lastDesktopLocation;
    static readonly uint TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    DateTime? bedZeroSince; DateTime? nozzleZeroSince; int bedZeroCount; int nozzleZeroCount;
    CancellationTokenSource? cameraCancellation;
    IntPtr cameraSession;
    int activeSlide;
    Panel? outgoingSlide;
    Panel? incomingSlide;
    long slideAnimationStartedAt;
    int slideAnimationDirection;
    const int SlideAnimationDurationMs=340;
    int cameraLayoutExtraHeight;
    int cameraVideoWidth=16;
    int cameraVideoHeight=9;
    bool cameraReady;
    bool usageLookupInProgress;
    Color? currentFilamentColor;
    AppSettingsView? settingsView;
    string activeSection = "printer";
    string settingsPreviousConnectionMode = "cloud";
    bool settingsOpenedFromWidget;
    Point? settingsWidgetLocation;

    public MonitorWindow()
    {
        speedModeCombo=new SpeedModeSelector(english
            ?new[]{"Silent","Standard","Sport"}
            :new[]{"Silencioso","Estándar","Sport"});
        Text="BambuPulse · P1S"; Icon=LoadPrinterIcon(); ClientSize=new Size(824,790); AutoScaleMode=AutoScaleMode.None;
        FormBorderStyle=FormBorderStyle.None; MaximizeBox=false; DoubleBuffered=true; TopMost=false;
        BackColor=UiTheme.Background; ForeColor=UiTheme.Text; Font=new Font("Segoe UI",9);
        Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,Width-1,Height-1),16));
        LoadLibrary();
        sidebar.Bounds=new Rectangle(16,16,140,ClientSize.Height-32);Controls.Add(sidebar);
        mainContent.Bounds=new Rectangle(172,16,636,ClientSize.Height-16);Controls.Add(mainContent);
        canvas.Bounds=new Rectangle(238,16,504,ClientSize.Height-16);Controls.Add(canvas);
        BuildSidebar();
        BuildFilamentSection();
        BuildLibrarySection();
        AttachWindowDragHandlers(sidebar);
        AttachWindowDragHandlers(mainContent);
        canvas.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};

        var eyebrow=MakeLabel("BAMBU LAB  ·  MONITOR",8,UiTheme.Muted,true);eyebrow.SetBounds(0,8,230,16);canvas.Controls.Add(eyebrow);
        var brand=MakeLabel("P1S",20,UiTheme.Text,true);brand.SetBounds(0,23,180,30);canvas.Controls.Add(brand);
        foreach(var dragTarget in new Control[]{eyebrow,brand})dragTarget.MouseDown+=(_,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
        connection.Text=english?"OFFLINE":"SIN CONEXIÓN";connection.Font=new Font("Segoe UI",8,FontStyle.Bold);connection.ForeColor=UiTheme.Muted;connection.TextAlign=ContentAlignment.MiddleCenter;connection.Dock=DockStyle.Fill;
        connectionPill.Bounds=new Rectangle(352,13,122,30);connectionPill.BackColor=UiTheme.Surface;connectionPill.BorderColor=UiTheme.Border;connectionPill.Radius=15;connectionPill.Controls.Add(connection);canvas.Controls.Add(connectionPill);
        closeButton.Text="×";closeButton.Font=new Font("Segoe UI",15);closeButton.Size=new Size(27,28);closeButton.Anchor=AnchorStyles.Top|AnchorStyles.Right;UiTheme.StyleButton(closeButton,quiet:true);closeButton.ForeColor=UiTheme.Muted;closeButton.Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,26,27),8));closeButton.Click+=(_,_)=>Close();Controls.Add(closeButton);
        Resize+=(_,_)=>UpdateCloseButton();
        UpdateCloseButton();
        expandWidgetButton.Text="↗";expandWidgetButton.Font=UiTheme.Font(10,true);expandWidgetButton.SetBounds(443,14,27,28);
        UiTheme.StyleButton(expandWidgetButton,quiet:true);expandWidgetButton.ForeColor=UiTheme.TextSoft;
        expandWidgetButton.AccessibleName=english?"Open full interface":"Abrir interfaz completa";
        expandWidgetButton.Visible=false;expandWidgetButton.Click+=(_,_)=>OpenFullInterface();Controls.Add(expandWidgetButton);

        hero.Bounds=new Rectangle(0,68,504,210);canvas.Controls.Add(hero);
        progressSlide.Dock=DockStyle.Fill;progressSlide.BackColor=UiTheme.Surface;hero.Controls.Add(progressSlide);
        printerSketch.Bounds=new Rectangle(14,40,132,164);printerSketch.BackColor=UiTheme.Surface;progressSlide.Controls.Add(printerSketch);
        statePill.Bounds=new Rectangle(12,10,hero.ClientSize.Width-24,24);statePill.BackColor=UiTheme.PanelSuccess;statePill.BorderColor=UiTheme.BorderSuccess;statePill.Radius=11;
        hero.Resize+=(_,_)=>statePill.SetBounds(12,10,Math.Max(0,hero.ClientSize.Width-24),24);
        status.Text=english?"NOT SET UP":"SIN CONFIGURAR";status.Font=new Font("Segoe UI",7,FontStyle.Bold);status.ForeColor=UiTheme.Muted;status.TextAlign=ContentAlignment.MiddleCenter;status.Dock=DockStyle.Fill;statePill.Controls.Add(status);progressSlide.Controls.Add(statePill);
        job.Text=english?"Waiting for printer":"Esperando impresora";job.Font=UiTheme.Font(18,true);job.ForeColor=UiTheme.Text;job.AutoEllipsis=true;job.SetBounds(164,45,314,31);progressSlide.Controls.Add(job);
        jobSubtitle.Text="Bambu Cloud · P1S";jobSubtitle.Font=UiTheme.Font(8);jobSubtitle.ForeColor=UiTheme.TextSoft;jobSubtitle.AutoEllipsis=true;jobSubtitle.SetBounds(165,77,310,17);progressSlide.Controls.Add(jobSubtitle);
        percent.Text="—%";percent.Font=new Font("Segoe UI Variable Display, Segoe UI",29,FontStyle.Bold);percent.ForeColor=UiTheme.Text;percent.SetBounds(160,96,138,44);progressSlide.Controls.Add(percent);
        layerInfo.Text=english?"— / — layers":"— / — capas";layerInfo.Font=UiTheme.Font(8,true);layerInfo.ForeColor=UiTheme.TextSoft;layerInfo.TextAlign=ContentAlignment.BottomRight;layerInfo.SetBounds(389,112,88,23);progressSlide.Controls.Add(layerInfo);
        progress.BackColor=UiTheme.Border;progress.ProgressColor=UiTheme.Accent;progress.SetBounds(165,149,312,7);progressSlide.Controls.Add(progress);
        remaining.Text=english?"— remaining":"— restantes";remaining.Font=UiTheme.Font(8,true);remaining.ForeColor=UiTheme.TextSoft;remaining.SetBounds(165,166,150,17);progressSlide.Controls.Add(remaining);
        finishInfo.Text=english?"Finish · —":"Fin · —";finishInfo.Font=UiTheme.Font(8);finishInfo.ForeColor=UiTheme.Muted;finishInfo.TextAlign=ContentAlignment.MiddleRight;finishInfo.SetBounds(325,166,152,17);progressSlide.Controls.Add(finishInfo);

        cameraSlide.Dock=DockStyle.Fill;cameraSlide.BackColor=UiTheme.Surface;cameraSlide.Visible=false;hero.Controls.Add(cameraSlide);
        cameraHost.Dock=DockStyle.Fill;cameraHost.BackColor=Color.FromArgb(12,13,14);cameraSlide.Controls.Add(cameraHost);
        cameraSlide.Resize+=(_,_)=>UpdateCameraSlideRegion();
        cameraSlide.Resize+=(_,_)=>CenterCameraMessage();
        cameraHost.Controls.Add(lanCameraPicture);
        cameraHost.Resize+=(_,_)=>{if(cameraSession!=IntPtr.Zero)CameraCloudNative.ResizeVideo(cameraSession,cameraHost.ClientSize.Width,cameraHost.ClientSize.Height);};
        cameraDimensionsTimer.Tick+=(_,_) =>
        {
            if(cameraSession==IntPtr.Zero)return;
            if(CameraCloudNative.TryGetVideoSize(cameraSession,out var width,out var height))
            {
                UpdateCameraCardSize(width,height);
                cameraDimensionsTimer.Stop();
            }

        };
        cameraMessage.Text=english?"Waiting for Bambu Cloud":"Esperando a Bambu Cloud";cameraMessage.Font=UiTheme.Font(9,true);cameraMessage.ForeColor=UiTheme.Text;cameraMessage.TextAlign=ContentAlignment.MiddleCenter;cameraMessage.BackColor=Color.FromArgb(210,16,17,19);cameraMessage.SetBounds(28,66,448,48);cameraSlide.Controls.Add(cameraMessage);
        cameraMessage.BringToFront();
        for(var i=0;i<carouselDots.Length;i++)
        {
            var index=i;
            carouselDots[i].Bounds=new Rectangle(455+i*22,279,18,14);
            carouselDots[i].Selected=i==0;
            carouselDots[i].Visible=true;
            carouselDots[i].Click+=(_,_)=>ShowSlide(index);
            canvas.Controls.Add(carouselDots[i]);
            carouselDots[i].BringToFront();
        }

        var temperatureTitle=MakeLabel(english?"TEMPERATURES":"TEMPERATURAS",8,UiTheme.Muted,true);temperatureTitle.SetBounds(0,294,250,18);canvas.Controls.Add(temperatureTitle);
        var nozzleCard=new SurfacePanel{Bounds=new Rectangle(0,318,242,116),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=16};nozzleGauge.Dock=DockStyle.Fill;nozzleCard.Controls.Add(nozzleGauge);canvas.Controls.Add(nozzleCard);
        var bedCard=new SurfacePanel{Bounds=new Rectangle(262,318,242,116),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=16};bedGauge.Dock=DockStyle.Fill;bedCard.Controls.Add(bedGauge);canvas.Controls.Add(bedCard);

        var filamentTitle=MakeLabel("AMS · SPOOL SLOTS",8,UiTheme.Muted,true);if(!english)filamentTitle.Text="AMS · BOBINAS";filamentTitle.SetBounds(0,451,160,18);canvas.Controls.Add(filamentTitle);
        inventorySummary.Text=english?"4 slots":"4 ranuras";inventorySummary.Font=new Font("Segoe UI",8);inventorySummary.ForeColor=UiTheme.Muted;inventorySummary.TextAlign=ContentAlignment.MiddleRight;inventorySummary.SetBounds(162,451,204,18);canvas.Controls.Add(inventorySummary);
        libraryButton.Text=english?"FILAMENTS":"BIBLIOTECA";libraryButton.Font=new Font("Segoe UI",7,FontStyle.Bold);libraryButton.SetBounds(385,443,119,32);libraryButton.BackColor=UiTheme.SurfaceRaised;libraryButton.ForeColor=UiTheme.TextSoft;libraryButton.FlatStyle=FlatStyle.Flat;libraryButton.FlatAppearance.BorderColor=UiTheme.Border;UiTheme.StyleButton(libraryButton,quiet:true);libraryButton.Click+=(_,_)=>OpenFilamentLibrary();canvas.Controls.Add(libraryButton);
        var filamentGrid=new SurfacePanel{Bounds=new Rectangle(0,489,504,126),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=16};canvas.Controls.Add(filamentGrid);
        for(var i=0;i<4;i++){var card=new FilamentCard(i+1){Bounds=new Rectangle(8+i*122,6,118,112)};filamentCards.Add(card);filamentGrid.Controls.Add(card);if(i<3)filamentGrid.Controls.Add(new Panel{Bounds=new Rectangle(128+i*122,20,1,82),BackColor=UiTheme.Border});}

        updated.Text=english?"Waiting for connection":"Esperando conexión";updated.Font=new Font("Segoe UI",8);updated.ForeColor=UiTheme.Muted;updated.AutoEllipsis=true;updated.SetBounds(0,645,330,32);canvas.Controls.Add(updated);
        printerNavButton.Click+=(_,_)=>ShowSection("printer");
        filamentNavButton.Click+=(_,_)=>ShowSection("filament");
        libraryNavButton.Click+=(_,_)=>ShowSection("library");
        settingsNavButton.Click+=async(_,_)=>await OpenSettings();
        RenderLibraryFilament();
        InitializeTrayMenu();
        UpdateCameraCardSize(cameraVideoWidth,cameraVideoHeight);
        positionSaveTimer.Tick+=(_,_)=>{positionSaveTimer.Stop();SaveDesktopLocation();};
        carouselTimer.Tick+=(_,_) =>
        {
            if(activeSection!="printer")return;
            if(activeSlide==0&&!cameraReady)return;
            ShowSlide(activeSlide==0?1:0);
        };
        carouselAnimationTimer.Tick+=(_,_)=>AdvanceSlideAnimation();
        hero.Resize+=(_,_)=>{if(carouselAnimationTimer.Enabled)FinishSlideAnimation();};
        Move+=(_,_)=>{if(desktopWidget){positionSaveTimer.Stop();positionSaveTimer.Start();}};
        FormClosing+=(_,_)=>{positionSaveTimer.Stop();carouselTimer.Stop();carouselAnimationTimer.Stop();cameraDimensionsTimer.Stop();carouselTimer.Dispose();carouselAnimationTimer.Dispose();cameraDimensionsTimer.Dispose();SaveDesktopLocation();StopCameraStream();trayIcon.Visible=false;trayIcon.Dispose();trayIconImage?.Dispose();settingsView?.Dispose();};
        UiLocalization.Apply(this);
        ShowSection("printer");
        Shown+=async(_,_)=>{if(AppPreferences.Current.DesktopWidget)ApplyDesktopWidgetMode(true);await LoadConfig();if(activeSection=="printer")StartCameraStream();if(filamentLibrary.History.Any(entry=>entry.Pending)){if(!string.IsNullOrWhiteSpace(cloudToken)&&!string.IsNullOrWhiteSpace(cloudUsageSerial)){usageRetryTimer.Start();await RetryPendingPrintsAsync();}else SetText(updated,english?"Automatic usage needs a matching Bambu Cloud account.":"El consumo automático requiere una cuenta Bambu Cloud vinculada a esta impresora.");}};
        usageRetryTimer.Tick+=async(_,_)=>await RetryPendingPrintsAsync();
        FormClosed+=(_,_)=>usageRetryTimer.Stop();
    }

    void BuildSidebar()
    {
        var product=MakeLabel("BAMBU PULSE",9,UiTheme.Muted,true);
        product.SetBounds(14,18,112,18);
        sidebar.Controls.Add(product);
        var printer=MakeLabel("P1S",19,UiTheme.Text,true);
        printer.SetBounds(14,38,112,28);
        sidebar.Controls.Add(printer);

        printerNavButton.Text=english?"▣   Printer":"▣   Impresora";
        filamentNavButton.Text=english?"▦   AMS":"▦   AMS";
        libraryNavButton.Text=english?"◉   Filament library":"◉   Biblioteca";
        settingsNavButton.Text=english?"⚙   Settings":"⚙   Ajustes";
        var navigation=new[]{printerNavButton,filamentNavButton,libraryNavButton,settingsNavButton};
        for(var i=0;i<navigation.Length;i++)
        {
            var button=navigation[i];
            button.SetBounds(8,88+i*42,124,34);
            button.TextAlign=ContentAlignment.MiddleLeft;
            button.Padding=new Padding(8,0,0,0);
            button.FlatStyle=FlatStyle.Flat;
            button.FlatAppearance.BorderSize=0;
            button.FlatAppearance.MouseOverBackColor=UiTheme.SurfaceHover;
            button.Cursor=Cursors.Hand;
            button.Font=UiTheme.Font(8,true);
            button.UseVisualStyleBackColor=false;
            button.TabStop=false;
            sidebar.Controls.Add(button);
        }

        connectionCaption.Text=english?"CONNECTION":"CONEXIÓN";
        connectionCaption.Font=UiTheme.Font(7,true);
        connectionCaption.ForeColor=UiTheme.Muted;
        connectionCaption.SetBounds(14,sidebar.Height-54,112,14);
        sidebar.Controls.Add(connectionCaption);
        connectionStatus.Text=english?"Printer monitor":"Monitor de impresora";
        connectionStatus.Font=UiTheme.Font(8);
        connectionStatus.ForeColor=UiTheme.TextSoft;
        connectionStatus.AutoEllipsis=true;
        connectionStatus.SetBounds(14,sidebar.Height-36,112,18);
        sidebar.Controls.Add(connectionStatus);
        chamberLightButton.Text=english?"Chamber light":"Luz de cámara";
        chamberLightButton.SetBounds(8,sidebar.Height-228,124,32);
        chamberLightButton.Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,chamberLightButton.Width,chamberLightButton.Height),9));
        StylePrintActionButton(chamberLightButton);
        chamberLightButton.Padding=new Padding(34,0,6,0);
        chamberLightButton.Paint+=(_,e)=>DrawLightButtonIcon(e.Graphics,chamberLightButton.ClientRectangle,
            chamberLightButton.Enabled,chamberLightOn==true);
        chamberLightButton.Click+=async(_,_)=>await ToggleChamberLightAsync();
        sidebar.Controls.Add(chamberLightButton);
        speedModeCaption.Text=english?"SPEED MODE":"VELOCIDAD";
        speedModeCaption.Font=UiTheme.Font(7,true);
        speedModeCaption.ForeColor=UiTheme.Muted;
        speedModeCaption.SetBounds(12,sidebar.Height-190,112,14);
        sidebar.Controls.Add(speedModeCaption);
        speedModeCombo.SetBounds(8,sidebar.Height-170,124,32);
        speedModeCombo.SelectedIndexChanged+=async(_,_)=>await OnSpeedModeSelectedAsync();
        sidebar.Controls.Add(speedModeCombo);
        pausePrintButton.Text=english?"Pause":"Pausar";
        pausePrintButton.SetBounds(8,sidebar.Height-128,124,32);
        pausePrintButton.Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,pausePrintButton.Width,pausePrintButton.Height),9));
        StylePrintActionButton(pausePrintButton);
        pausePrintButton.Paint+=(_,e)=>DrawPauseButtonIcon(e.Graphics,pausePrintButton.ClientRectangle,
            pausePrintButton.Enabled,lastState is "pause" or "paused");
        pausePrintButton.Click+=async(_,_)=>await TogglePrintPauseAsync();
        sidebar.Controls.Add(pausePrintButton);
        stopPrintButton.Text=english?"Stop":"Parar";
        stopPrintButton.SetBounds(8,sidebar.Height-90,124,32);
        stopPrintButton.Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,stopPrintButton.Width,stopPrintButton.Height),9));
        StylePrintActionButton(stopPrintButton);
        stopPrintButton.Paint+=(_,e)=>DrawStopButtonIcon(e.Graphics,stopPrintButton.ClientRectangle,stopPrintButton.Enabled);
        stopPrintButton.Click+=async(_,_)=>await StopPrintAsync();
        sidebar.Controls.Add(stopPrintButton);
        UpdatePrintControls();
        UpdateSectionNavigation();
    }

    void LayoutMainPanels()
    {
        sidebar.Bounds=new Rectangle(16,16,140,Math.Max(0,ClientSize.Height-32));
        mainContent.Bounds=new Rectangle(172,16,636,Math.Max(0,ClientSize.Height-16));
        canvas.Bounds=new Rectangle(238,16,504,Math.Max(0,ClientSize.Height-16));
        connectionCaption.Top=sidebar.ClientSize.Height-54;
        connectionStatus.Top=sidebar.ClientSize.Height-36;
        chamberLightButton.Top=sidebar.ClientSize.Height-228;
        speedModeCaption.Top=sidebar.ClientSize.Height-190;
        speedModeCombo.Top=sidebar.ClientSize.Height-170;
        pausePrintButton.Top=sidebar.ClientSize.Height-128;
        stopPrintButton.Top=sidebar.ClientSize.Height-90;
    }

    void AttachWindowDragHandlers(Control root)
    {
        if(root is ButtonBase or TextBoxBase or ComboBox or ListBox or SpoolDropdown or SpeedModeSelector)
            return;
        if(root is Panel or Label)
            root.MouseDown+=DragWindowFromControl;
        foreach(Control child in root.Controls)
            AttachWindowDragHandlers(child);
    }

    void DragWindowFromControl(object? sender,MouseEventArgs e)
    {
        if(e.Button==MouseButtons.Left)
        {
            ReleaseCapture();
            SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);
        }
    }

    void UpdatePrintControls()
    {
        var connected=client?.IsConnected==true;
        var paused=lastState is "pause" or "paused";
        var active=lastState is "running" or "printing" or "pause" or "paused";
        pausePrintButton.Text=paused
            ?(english?"Resume":"Reanudar")
            :(english?"Pause":"Pausar");
        pausePrintButton.Enabled=connected&&active;
        stopPrintButton.Enabled=connected&&active;
        chamberLightButton.Enabled=connected;
        speedModeCombo.Enabled=connected&&active;
        chamberLightButton.Text=chamberLightOn.HasValue
            ?chamberLightOn.Value?(english?"Light: On":"Luz: encendida"):(english?"Light: Off":"Luz: apagada")
            :(english?"Chamber light":"Luz de cámara");
        StylePrintActionButton(chamberLightButton,isResume:chamberLightOn==true);
        chamberLightButton.Padding=new Padding(34,0,6,0);
        chamberLightButton.Invalidate();
        if(reportedSpeedMode is >=0 and <=2)
        {
            updatingPrintOptions=true;
            try{speedModeCombo.SelectedIndex=reportedSpeedMode.Value;}
            finally{updatingPrintOptions=false;}
        }
        StylePrintActionButton(pausePrintButton,isResume:paused);
        StylePrintActionButton(stopPrintButton,isStop:true);
        pausePrintButton.Padding=new Padding(34,0,6,0);
        stopPrintButton.Padding=new Padding(34,0,6,0);
    }

    static void DrawLightButtonIcon(Graphics graphics,Rectangle bounds,bool enabled,bool isOn)
    {
        var color=enabled?(isOn?UiTheme.Warning:UiTheme.TextSoft):UiTheme.Muted;
        using var pen=new Pen(color,1.8f)
        {
            StartCap=System.Drawing.Drawing2D.LineCap.Round,
            EndCap=System.Drawing.Drawing2D.LineCap.Round
        };
        graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.DrawEllipse(pen,13,6,14,14);
        graphics.DrawLine(pen,16,19,17,22);
        graphics.DrawLine(pen,24,19,23,22);
        graphics.DrawLine(pen,17,22,23,22);
        graphics.DrawLine(pen,18,25,22,25);
    }

    static void DrawStopButtonIcon(Graphics graphics,Rectangle bounds,bool enabled)
    {
        using var brush=new SolidBrush(enabled?UiTheme.Error:UiTheme.Muted);
        using var path=SurfacePanel.RoundedPath(new Rectangle(13,bounds.Height/2-5,10,10),2);
        graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.FillPath(brush,path);
    }

    async Task ToggleChamberLightAsync()
    {
        var turnOn=chamberLightOn!=true;
        var sequence=Interlocked.Increment(ref printCommandSequence).ToString(CultureInfo.InvariantCulture);
        await SendPrinterRequestAsync(new
        {
            system=new
            {
                sequence_id=sequence,
                command="ledctrl",
                led_node="chamber_light",
                led_mode=turnOn?"on":"off",
                led_on_time=500,
                led_off_time=500,
                loop=0
            }
        },turnOn
            ?(english?"Turning chamber light on…":"Encendiendo la luz…")
            :(english?"Turning chamber light off…":"Apagando la luz…"));
    }

    async Task OnSpeedModeSelectedAsync()
    {
        if(updatingPrintOptions||speedModeCombo.SelectedIndex<0)return;
        var selected=speedModeCombo.SelectedIndex;
        var payload=new{print=new{command="print_speed",param=selected.ToString(CultureInfo.InvariantCulture)}};
        if(await SendPrinterRequestAsync(payload,english?"Changing speed mode…":"Cambiando modo de velocidad…"))
            reportedSpeedMode=selected;
    }

    async Task<bool> SendPrinterRequestAsync(object payload,string progressMessage)
    {
        if(client?.IsConnected!=true||string.IsNullOrWhiteSpace(serial))
        {
            SetText(updated,english?"Printer is not connected.":"La impresora no está conectada.");
            return false;
        }
        try
        {
            var message=new MqttApplicationMessageBuilder()
                .WithTopic($"device/{serial}/request")
                .WithPayload(JsonSerializer.Serialize(payload))
                .Build();
            await client.PublishAsync(message);
            SetText(updated,progressMessage);
            return true;
        }
        catch(Exception ex)
        {
            SetText(updated,(english?"Could not send printer command: ":"No se pudo enviar el comando a la impresora: ")+ex.Message);
            return false;
        }
    }

    static void DrawPauseButtonIcon(Graphics graphics,Rectangle bounds,bool enabled,bool isResume)
    {
        var color=enabled?(isResume?UiTheme.Success:UiTheme.TextSoft):UiTheme.Muted;
        using var brush=new SolidBrush(color);
        graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var centerY=bounds.Height/2f;
        if(isResume)
        {
            var points=new[]{new PointF(13,centerY-7),new PointF(13,centerY+7),new PointF(25,centerY)};
            graphics.FillPolygon(brush,points);
            return;
        }
        graphics.FillRectangle(brush,13,centerY-7,4,14);
        graphics.FillRectangle(brush,21,centerY-7,4,14);
    }

    static void StylePrintActionButton(Button button,bool isStop=false,bool isResume=false)
    {
        var enabled=button.Enabled;
        button.FlatStyle=FlatStyle.Flat;
        button.FlatAppearance.BorderSize=0;
        button.FlatAppearance.MouseOverBackColor=!enabled?UiTheme.SurfaceRaised:isStop?UiTheme.PanelError:isResume?UiTheme.PanelSuccess:UiTheme.SurfaceHover;
        button.FlatAppearance.MouseDownBackColor=isStop?UiTheme.BorderError:isResume?UiTheme.BorderSuccess:UiTheme.Border;
        button.BackColor=!enabled?UiTheme.SurfaceRaised:isStop?UiTheme.PanelError:isResume?UiTheme.PanelSuccess:UiTheme.SurfaceRaised;
        button.ForeColor=!enabled?UiTheme.Muted:isStop?UiTheme.Error:isResume?UiTheme.Success:UiTheme.TextSoft;
        button.Font=UiTheme.Font(8,true);
        button.TextAlign=ContentAlignment.MiddleLeft;
        button.Padding=new Padding(12,0,6,0);
        button.Cursor=enabled?Cursors.Hand:Cursors.Default;
        button.UseVisualStyleBackColor=false;
    }

    async Task TogglePrintPauseAsync()
    {
        var command=lastState is "pause" or "paused"?"resume":"pause";
        await SendPrintCommandAsync(command);
    }

    async Task StopPrintAsync()
    {
        var confirm=MessageBox.Show(this,
            english?"Stop the current print? This cannot be undone.":"¿Quieres parar la impresión actual? Esta acción no se puede deshacer.",
            english?"Stop print":"Parar impresión",MessageBoxButtons.YesNo,MessageBoxIcon.Warning);
        if(confirm!=DialogResult.Yes)return;
        await SendPrintCommandAsync("stop");
    }

    async Task SendPrintCommandAsync(string command)
    {
        if(client?.IsConnected!=true||string.IsNullOrWhiteSpace(serial))
        {
            SetText(updated,english?"Printer is not connected.":"La impresora no está conectada.");
            UpdatePrintControls();
            return;
        }

        try
        {
            var sequence=Interlocked.Increment(ref printCommandSequence).ToString(CultureInfo.InvariantCulture);
            var payload=JsonSerializer.Serialize(new{print=new{command,sequence_id=sequence,param=""}});
            var message=new MqttApplicationMessageBuilder()
                .WithTopic($"device/{serial}/request")
                .WithPayload(payload)
                .Build();
            await client.PublishAsync(message);
            SetText(updated,command switch
            {
                "pause"=>english?"Pause requested…":"Pausa solicitada…",
                "resume"=>english?"Resume requested…":"Reanudación solicitada…",
                _=>english?"Stop requested…":"Parada solicitada…"
            });
        }
        catch(Exception ex)
        {
            SetText(updated,(english?"Could not send printer command: ":"No se pudo enviar el comando a la impresora: ")+ex.Message);
        }
    }

    void BuildFilamentSection()
    {
        filamentSection.Dock=DockStyle.Fill;
        var kicker=MakeLabel(english?"AMS · FILAMENTS":"AMS · FILAMENTOS",8,UiTheme.Muted,true);
        kicker.SetBounds(18,20,280,16);
        filamentSection.Controls.Add(kicker);
        var title=MakeLabel(english?"AMS spool slots":"Bobinas del AMS",20,UiTheme.Text,true);
        title.SetBounds(18,40,430,32);
        filamentSection.Controls.Add(title);
        var subtitle=MakeLabel(english?"Assigned spools shown in their AMS bays":"Bobinas asignadas, colocadas en sus ranuras",9,UiTheme.Muted);
        subtitle.SetBounds(18,76,500,20);
        filamentSection.Controls.Add(subtitle);

        sectionFilamentSummary.SetBounds(18,126,390,20);
        sectionFilamentSummary.Font=UiTheme.Font(8,true);
        sectionFilamentSummary.ForeColor=UiTheme.TextSoft;
        filamentSection.Controls.Add(sectionFilamentSummary);

        var inventory=new SurfacePanel
        {
            Bounds=new Rectangle(18,158,600,274),
            BackColor=UiTheme.SurfaceRaised,
            BorderColor=UiTheme.Border,
            Radius=18
        };
        filamentSection.Controls.Add(inventory);
        var amsName=MakeLabel("AMS  ·  4 SLOTS",8,UiTheme.TextSoft,true);
        amsName.SetBounds(18,13,220,18);
        inventory.Controls.Add(amsName);
        var statusDot=new Panel{Bounds=new Rectangle(570,17,8,8),BackColor=UiTheme.Accent};
        using(var path=new System.Drawing.Drawing2D.GraphicsPath()){path.AddEllipse(0,0,8,8);statusDot.Region=new Region(path);}
        inventory.Controls.Add(statusDot);
        var innerWindow=new SurfacePanel
        {
            Bounds=new Rectangle(12,40,576,216),
            BackColor=UiTheme.Background,
            BorderColor=UiTheme.Border,
            Radius=14
        };
        inventory.Controls.Add(innerWindow);
        for(var i=0;i<4;i++)
        {
            var bay=new SurfacePanel
            {
                Bounds=new Rectangle(8+i*142,8,136,200),
                BackColor=UiTheme.Surface,
                BorderColor=UiTheme.Border,
                Radius=12
            };
            innerWindow.Controls.Add(bay);
            var slotLabel=MakeLabel($"SLOT {i+1:00}",7,UiTheme.Muted,true);
            slotLabel.SetBounds(8,8,120,14);
            bay.Controls.Add(slotLabel);
            var card=new FilamentCard(i+1){Bounds=new Rectangle(4,28,128,164)};
            sectionFilamentCards.Add(card);
            bay.Controls.Add(card);
        }

        var manage= new Button
        {
            Text=english?"Manage AMS slots":"Gestionar ranuras AMS",
            Bounds=new Rectangle(18,456,210,38)
        };
        UiTheme.StyleButton(manage,primary:true);
        manage.Click+=(_,_)=>OpenFilamentLibrary();
        filamentSection.Controls.Add(manage);

        var readAms=new Button
        {
            Text=english?"Read RFID spools":"Leer bobinas RFID",
            Bounds=new Rectangle(244,456,190,38)
        };
        UiTheme.StyleButton(readAms);
        readAms.Click+=async(_,_)=>await ReadAmsSpoolsAsync();
        filamentSection.Controls.Add(readAms);

        amsReadStatus.SetBounds(18,500,600,22);
        amsReadStatus.Font=UiTheme.Font(8);
        amsReadStatus.ForeColor=UiTheme.Muted;
        amsReadStatus.Text=english
            ?"RFID reads material and color; remaining grams may not be available."
            :"El RFID lee material y color; el peso restante puede no estar disponible.";
        filamentSection.Controls.Add(amsReadStatus);

        var historyHint=MakeLabel(
            english?"Exact per-print usage is read from Bambu Cloud and deducted by AMS slot."
                    :"El consumo exacto se consulta en Bambu Cloud y se descuenta por ranura.",
            8,UiTheme.Muted);
        historyHint.SetBounds(18,530,570,34);
        if(AppPreferences.Current.ConnectionMode=="lan")
        {
            historyHint.Text=english
                ?"LAN-only: estimated usage comes from AMS remaining-percentage changes; no Cloud lookup."
                :"Solo LAN: consumo estimado según el cambio del porcentaje restante del AMS; sin consulta Cloud.";
        }
        else
        {
            historyHint.Text=english
                ?"Exact per-print usage is read from Bambu Cloud and deducted by AMS slot."
                :"El consumo exacto se consulta en Bambu Cloud y se descuenta por ranura.";
        }
        filamentSection.Controls.Add(historyHint);
        mainContent.Controls.Add(filamentSection);
        filamentSection.Visible=false;
    }

    void BuildLibrarySection()
    {
        librarySection.Dock=DockStyle.Fill;
        var kicker=MakeLabel(english?"SPOOL COLLECTION":"COLECCIÓN DE BOBINAS",8,UiTheme.Muted,true);
        kicker.SetBounds(18,20,360,16);
        librarySection.Controls.Add(kicker);
        var title=MakeLabel(english?"Filament library":"Biblioteca de filamentos",20,UiTheme.Text,true);
        title.SetBounds(18,40,430,32);
        librarySection.Controls.Add(title);
        var subtitle=MakeLabel(english?"Your materials, colors and remaining filament":"Tus materiales, colores y filamento restante",9,UiTheme.Muted);
        subtitle.SetBounds(18,76,560,20);
        librarySection.Controls.Add(subtitle);

        var add= new Button
        {
            Text=english?"＋  Manage library":"＋  Gestionar biblioteca",
            Bounds=new Rectangle(18,116,200,38)
        };
        UiTheme.StyleButton(add,primary:true);
        add.Click+=(_,_)=>OpenFilamentLibrary();
        librarySection.Controls.Add(add);

        librarySpoolCards.SetBounds(12,168,612,560);
        librarySpoolCards.Padding=new Padding(6);
        librarySpoolCards.BackColor=UiTheme.Background;
        librarySection.Controls.Add(librarySpoolCards);
        mainContent.Controls.Add(librarySection);
        librarySection.Visible=false;
        RenderLibrarySpools();
    }

    void RenderLibrarySpools()
    {
        if(librarySpoolCards.IsDisposed)return;
        librarySpoolCards.SuspendLayout();
        try
        {
            foreach(Control card in librarySpoolCards.Controls)card.Dispose();
            librarySpoolCards.Controls.Clear();
            if(filamentLibrary.Spools.Count==0)
            {
                var empty=MakeLabel(english?"No spools yet. Add one to your library.":"Todavía no hay bobinas. Añade una a tu biblioteca.",9,UiTheme.Muted);
                empty.Margin=new Padding(8,12,8,8);
                empty.Size=new Size(500,32);
                librarySpoolCards.Controls.Add(empty);
                return;
            }

            foreach(var spool in filamentLibrary.Spools)
            {
                var card=new SpoolVisualCard(spool)
                {
                    Margin=new Padding(8),
                    Size=new Size(254,174),
                    BackColor=UiTheme.Background
                };
                librarySpoolCards.Controls.Add(card);
            }
        }
        finally{librarySpoolCards.ResumeLayout();}
    }

    void ShowSection(string section)
    {
        if(section is not ("printer" or "filament" or "library" or "settings"))return;
        if(section!="printer")
        {
            carouselTimer.Stop();
            StopCameraStream();
        }
        activeSection=section;
        canvas.Visible=section=="printer";
        filamentSection.Visible=section=="filament";
        librarySection.Visible=section=="library";
        if(settingsView is not null)settingsView.Visible=section=="settings";
        mainContent.Visible=section!="printer";
        UpdateSectionNavigation();
        UpdateCloseButton();
        if(section=="printer")
        {
            ApplyProgressColor();
            StartCameraStream();
            carouselTimer.Stop();
            carouselTimer.Start();
        }
    }

    void UpdateSectionNavigation()
    {
        SetNavigationButton(printerNavButton,activeSection=="printer");
        SetNavigationButton(filamentNavButton,activeSection=="filament");
        SetNavigationButton(libraryNavButton,activeSection=="library");
        SetNavigationButton(settingsNavButton,activeSection=="settings");
    }

    static void SetNavigationButton(Button button,bool selected)
    {
        button.BackColor=selected?UiTheme.PanelInfo:UiTheme.Surface;
        button.ForeColor=selected?UiTheme.Accent:UiTheme.TextSoft;
        button.FlatAppearance.MouseOverBackColor=selected?UiTheme.PanelInfo:UiTheme.SurfaceHover;
    }

    void ShowSlide(int index)
    {
        if(index is not (0 or 1))return;
        if(carouselAnimationTimer.Enabled)FinishSlideAnimation();
        if(index==activeSlide)return;
        var oldSlide=activeSlide==0?progressSlide:cameraSlide;
        var newSlide=index==0?progressSlide:cameraSlide;
        activeSlide=index;
        for(var i=0;i<carouselDots.Length;i++)carouselDots[i].Selected=i==index;
        ApplyProgressColor();
        if(index==1)StartCameraStream();
        BeginSlideAnimation(oldSlide,newSlide,index==1?1:-1);
        if(activeSection=="printer")
        {
            carouselTimer.Stop();
            carouselTimer.Start();
        }
    }

    void BeginSlideAnimation(Panel from,Panel to,int direction)
    {
        var width=hero.ClientSize.Width;
        var height=hero.ClientSize.Height;
        if(width<1||height<1)
        {
            from.Visible=false;
            to.Dock=DockStyle.Fill;
            to.Visible=true;
            to.BringToFront();
            return;
        }

        from.Dock=DockStyle.None;
        to.Dock=DockStyle.None;
        from.Bounds=new Rectangle(0,0,width,height);
        to.Bounds=new Rectangle(direction*width,0,width,height);
        from.Visible=true;
        to.Visible=true;
        from.BringToFront();
        to.BringToFront();
        outgoingSlide=from;
        incomingSlide=to;
        slideAnimationDirection=direction;
        slideAnimationStartedAt=Environment.TickCount64;
        carouselAnimationTimer.Start();
    }

    void AdvanceSlideAnimation()
    {
        if(outgoingSlide is null||incomingSlide is null)
        {
            FinishSlideAnimation();
            return;
        }

        var elapsed=Math.Clamp((Environment.TickCount64-slideAnimationStartedAt)/(double)SlideAnimationDurationMs,0,1);
        var eased=1-Math.Pow(1-elapsed,3);
        var offset=(int)Math.Round(hero.ClientSize.Width*eased);
        var width=hero.ClientSize.Width;
        var height=hero.ClientSize.Height;
        outgoingSlide.Bounds=new Rectangle(-slideAnimationDirection*offset,0,width,height);
        incomingSlide.Bounds=new Rectangle(slideAnimationDirection*(width-offset),0,width,height);
        if(elapsed>=1)FinishSlideAnimation();
    }

    void FinishSlideAnimation()
    {
        carouselAnimationTimer.Stop();
        var selected=activeSlide==0?progressSlide:cameraSlide;
        var other=activeSlide==0?cameraSlide:progressSlide;
        other.Visible=false;
        other.Dock=DockStyle.Fill;
        selected.Dock=DockStyle.Fill;
        selected.Visible=true;
        selected.BringToFront();
        outgoingSlide=null;
        incomingSlide=null;
    }

    void StartCameraStream()
    {
        if(activeSection!="printer"||cameraReady||cameraCancellation is not null||cameraSession!=IntPtr.Zero)return;
        cameraMessage.BringToFront();
        if(AppPreferences.Current.ConnectionMode=="lan")
        {
            StartLanCameraStream();
            return;
        }
        lanCameraPicture.Visible=false;
        if(string.IsNullOrWhiteSpace(cloudToken)||string.IsNullOrWhiteSpace(serial))
        {
            SetCameraMessage(english?"Connect to Bambu Cloud to view the camera":"Conéctate a Bambu Cloud para ver la cámara");
            return;
        }

        SetCameraMessage(english?"Requesting secure camera session from Bambu Cloud…":"Solicitando sesión segura de cámara a Bambu Cloud…");
        var cancellation=new CancellationTokenSource();
        cameraCancellation=cancellation;
        _=RunCloudCameraAsync(cancellation);
    }

    void StartLanCameraStream()
    {
        var prefs=AppPreferences.Current;
        if(string.IsNullOrWhiteSpace(prefs.LanAddress)||string.IsNullOrWhiteSpace(prefs.LanSerial)||
           string.IsNullOrWhiteSpace(prefs.LanAccessCodeProtected))
        {
            SetCameraMessage(english?"Set up LAN in Settings to view the camera":"Configura LAN en Ajustes para ver la cámara");
            return;
        }

        string accessCode;
        try { accessCode=UnprotectSetting(prefs.LanAccessCodeProtected); }
        catch(Exception ex)
        {
            SetCameraMessage((english?"LAN camera configuration error: ":"Error de configuración de cámara LAN: ")+ex.Message);
            return;
        }

        lanCameraPicture.Visible=true;
        SetCameraMessage(english?"Connecting to the printer's LAN camera…":"Conectando con la cámara LAN de la impresora…");
        var cancellation=new CancellationTokenSource();
        cameraCancellation=cancellation;
        _=RunLanCameraAsync(prefs.LanAddress,accessCode,cancellation);
    }

    async Task RunLanCameraAsync(string address,string accessCode,CancellationTokenSource cancellation)
    {
        try
        {
            var stream=new LanCameraStream(address,accessCode);
            await stream.RunAsync(image=>
            {
                if(IsDisposed||!IsHandleCreated||cancellation.IsCancellationRequested)
                {
                    image.Dispose();
                    return;
                }
                try
                {
                    BeginInvoke(new Action(()=>
                    {
                        if(IsDisposed||cameraCancellation!=cancellation||activeSection!="printer")
                        {
                            image.Dispose();
                            return;
                        }
                        var previous=lanCameraPicture.Image;
                        lanCameraPicture.Image=image;
                        UpdateCameraCardSize(image.Width,image.Height);
                        cameraMessage.Visible=false;
                        cameraReady=true;
                        previous?.Dispose();
                    }));
                }
                catch(InvalidOperationException) when(IsDisposed||!IsHandleCreated) { image.Dispose(); }
            },cancellation.Token);
        }
        catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
        catch(Exception ex)
        {
            if(!cancellation.IsCancellationRequested&&!IsDisposed&&cameraCancellation==cancellation)
            {
                try
                {
                    BeginInvoke(new Action(()=>
                    {
                        if(!IsDisposed&&cameraCancellation==cancellation)
                            SetCameraMessage((english?"LAN camera unavailable: ":"Cámara LAN no disponible: ")+ex.Message);
                    }));
                }
                catch(InvalidOperationException) when(IsDisposed||!IsHandleCreated) { }
            }
        }
        finally
        {
            if(cameraCancellation==cancellation)cameraCancellation=null;
            cancellation.Dispose();
        }
    }

    async Task RunCloudCameraAsync(CancellationTokenSource cancellation)
    {
        try
        {
            SetCameraMessage(english?"Validating the Bambu Cloud account…":"Validando la cuenta de Bambu Cloud…");
            var userInfo=await BuildCameraLoginInfoAsync();
            if(cancellation.IsCancellationRequested||IsDisposed||activeSection!="printer")return;
            var callbackWindow=Handle;
            CameraCloudNative.CameraProgressCallback progress=stage=>UpdateCameraStage(stage,cancellation);
            var cameraUrl=await Task.Run(()=>CameraCloudNative.GetCameraUrl(
                GetPluginDirectory(),GetStudioDirectory(),GetStudioConfigDirectory(),
                GetCertificateDirectory(),callbackWindow,region=="cn"?"CN":"US",userInfo,serial,cameraDevVersion,
                GetInstalledStudioVersion(),cameraDeviceId,progress));
            if(cancellation.IsCancellationRequested||IsDisposed||activeSection!="printer")return;
            var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            BeginInvoke(new Action(()=>
            {
                try
                {
                    if(cameraCancellation!=cancellation||activeSection!="printer"){completion.TrySetResult(true);return;}
                    cameraSession=CameraCloudNative.StartVideo(GetPluginDirectory(),GetStudioDirectory(),cameraUrl,
                        cameraHost.Handle,cameraHost.ClientSize.Width,cameraHost.ClientSize.Height);
                    CameraCloudNative.ResizeVideo(cameraSession,cameraHost.ClientSize.Width,cameraHost.ClientSize.Height);
                    cameraDimensionsTimer.Start();
                    cameraMessage.Visible=false;
                    cameraReady=true;
                    completion.TrySetResult(true);
                }
                catch(Exception ex){completion.TrySetException(ex);}
            }));
            await completion.Task;
        }
        catch(Exception ex)
        {
            if(!cancellation.IsCancellationRequested&&!IsDisposed&&cameraCancellation==cancellation)
                SetCameraMessage((english?"Cloud camera unavailable: ":"Cámara Cloud no disponible: ")+ex.Message);
        }
        finally
        {
            if(cameraCancellation==cancellation)cameraCancellation=null;
            cancellation.Dispose();
        }
    }

    void UpdateCameraStage(int stage,CancellationTokenSource cancellation)
    {
        if(IsDisposed||!IsHandleCreated)return;
        var message=stage switch
        {
            0=>english?"Preparing secure camera session…":"Preparando sesión segura de cámara…",
            1=>english?"Loading Bambu Studio Cloud components…":"Cargando componentes Cloud de Bambu Studio…",
            2=>english?"Initializing the Cloud networking agent…":"Iniciando el agente de red Cloud…",
            3=>english?"Applying the Bambu Cloud account…":"Aplicando la cuenta de Bambu Cloud…",
            4=>english?"Connecting the camera session to Bambu Cloud…":"Conectando la sesión de cámara a Bambu Cloud…",
            5=>english?"Waiting for Bambu Cloud connection…":"Esperando la conexión con Bambu Cloud…",
            6=>english?"Requesting the printer camera stream…":"Solicitando el vídeo de la impresora…",
            7=>english?"Waiting for Bambu Cloud to create the camera session…":"Esperando a que Bambu Cloud cree la sesión de cámara…",
            8=>english?"Camera session received; opening video…":"Sesión recibida; abriendo el vídeo…",
            _=>english?"Connecting to Bambu Cloud…":"Conectando con Bambu Cloud…"
        };
        try
        {
            BeginInvoke(new Action(()=>
            {
                if(!IsDisposed&&cameraCancellation==cancellation&&activeSection=="printer"&&activeSlide==1)
                    SetCameraMessage(message);
            }));
        }
        catch(InvalidOperationException) when(IsDisposed||!IsHandleCreated) { }
    }

    void StopCameraStream()
    {
        cameraDimensionsTimer.Stop();
        var cancellation=cameraCancellation;
        cameraCancellation=null;
        cancellation?.Cancel();
        cameraReady=false;
        lanCameraPicture.Visible=false;
        var previousImage=lanCameraPicture.Image;
        lanCameraPicture.Image=null;
        previousImage?.Dispose();
        if(cameraSession!=IntPtr.Zero)
        {
            CameraCloudNative.StopVideo(cameraSession);
            cameraSession=IntPtr.Zero;
        }
    }

    void UpdateCameraCardSize(int imageWidth,int imageHeight)
    {
        const int baseHeight=210;
        if(imageWidth>0&&imageHeight>0)
        {
            cameraVideoWidth=imageWidth;
            cameraVideoHeight=imageHeight;
        }
        var targetHeight=Math.Max(baseHeight,
            (int)Math.Round(hero.Width*(double)cameraVideoHeight/cameraVideoWidth));
        var nextExtraHeight=targetHeight-baseHeight;
        var delta=nextExtraHeight-cameraLayoutExtraHeight;
        if(delta==0)return;

        cameraLayoutExtraHeight=nextExtraHeight;
        hero.Height=targetHeight;
        LayoutProgressSlide();
        foreach(Control control in canvas.Controls)
        {
            if(control!=hero&&control.Top>=279)
                control.Top+=delta;
        }

        if(desktopWidget)
        {
            ClientSize=new Size(540,647+cameraLayoutExtraHeight);
            canvas.Bounds=new Rectangle(16,16,504,754+cameraLayoutExtraHeight);
        }
        else
        {
            ClientSize=new Size(824,790+cameraLayoutExtraHeight);
            SetCornerRegion(ClientSize.Height);
            LayoutMainPanels();
        }
    }

    void LayoutProgressSlide()
    {
        var extra=cameraLayoutExtraHeight;
        if(extra==0)
        {
            printerSketch.Bounds=new Rectangle(14,40,132,164);
            statePill.SetBounds(12,10,Math.Max(0,hero.ClientSize.Width-24),24);
            job.Top=45;
            jobSubtitle.Top=77;
            percent.Top=96;
            layerInfo.Top=112;
            progress.Top=149;
            remaining.Top=166;
            finishInfo.Top=166;
            return;
        }

        printerSketch.SetBounds(14,Math.Max(40,(hero.Height-164)/2),132,164);
        statePill.SetBounds(12,10,Math.Max(0,hero.ClientSize.Width-24),24);
        job.Top=40;
        jobSubtitle.Top=72;
        percent.Top=96+(int)Math.Round(extra*.42);
        layerInfo.Top=112+(int)Math.Round(extra*.42);
        progress.Top=149+(int)Math.Round(extra*.72);
        remaining.Top=hero.Height-25;
        finishInfo.Top=hero.Height-25;
    }

    void CenterCameraMessage()
    {
        cameraMessage.SetBounds(28,Math.Max(0,(cameraSlide.ClientSize.Height-cameraMessage.Height)/2),
            Math.Max(0,cameraSlide.ClientSize.Width-56),cameraMessage.Height);
    }

    void UpdateCameraSlideRegion()
    {
        if(cameraSlide.Width<2||cameraSlide.Height<2)return;
        var old=cameraSlide.Region;
        cameraSlide.Region=new Region(SurfacePanel.RoundedPath(
            new Rectangle(0,0,cameraSlide.Width-1,cameraSlide.Height-1),18));
        old?.Dispose();
    }

    void SetCameraMessage(string text)
    {
        cameraMessage.Text=text;
        cameraMessage.Visible=true;
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
        trayIconImage=CreateTrayIcon();trayIcon.Icon=trayIconImage;trayIcon.Text="BambuPulse · P1S";trayIcon.ContextMenuStrip=menu;trayIcon.Visible=false;
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
            WindowState=FormWindowState.Normal;updated.Visible=false;expandWidgetButton.Visible=true;ClientSize=new Size(540,647+cameraLayoutExtraHeight);SetCornerRegion(ClientSize.Height);
            sidebar.Visible=false;mainContent.Visible=false;canvas.Bounds=new Rectangle(16,16,504,754+cameraLayoutExtraHeight);
            ShowInTaskbar=true;
            point=ClampToWorkingArea(point,ClientSize);Location=point;
            // Keep this as a regular, non-topmost window. WorkerW parenting is undocumented
            // and can make WinForms windows disappear on current Windows builds.
            desktopAttached=false;desktopWidget=true;lastDesktopLocation=point;disableWidgetMenuItem.Enabled=true;trayIcon.Visible=true;UpdateCloseButton();
            Show();BringToFront();
            return;
        }

        Point? location=preferredLocation;
        if(desktopAttached){location=DesktopWidgetHost.Detach(this);desktopAttached=false;}
        desktopWidget=false;updated.Visible=true;expandWidgetButton.Visible=false;ClientSize=new Size(824,790+cameraLayoutExtraHeight);SetCornerRegion(ClientSize.Height);
        LayoutMainPanels();sidebar.Visible=true;
        closeButton.SetBounds(780,14,27,28);
        ShowInTaskbar=true;disableWidgetMenuItem.Enabled=false;trayIcon.Visible=false;UpdateCloseButton();
        mainContent.Visible=activeSection!="printer";
        canvas.Visible=activeSection=="printer";
        if(location.HasValue){lastDesktopLocation=location;Location=ClampToWorkingArea(location.Value,ClientSize);}
        Show();BringToFront();
    }

    void UpdateCloseButton()
    {
        closeButton.Visible=!desktopWidget;
        closeButton.SetBounds(Math.Max(0,ClientSize.Width-closeButton.Width-17),14,
            closeButton.Width,closeButton.Height);
        if(closeButton.Visible)closeButton.BringToFront();
    }

    void OpenFullInterface()
    {
        AppPreferences.Save(AppPreferences.Current.Theme,AppPreferences.Current.Language,false);
        ShowSection("printer");
        ApplyDesktopWidgetMode(false,Location);
    }

    void SetCornerRegion(int height)
    {
        var old=Region;Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,Width-1,height-1),16));old?.Dispose();
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
        if(message.Msg==CameraCloudNative.DispatchCallbacksMessage)
        {
            CameraCloudNative.DispatchCallbacks();
            return;
        }
        base.WndProc(ref message);
        if(desktopWidget&&(message.Msg==0x007E||message.Msg==TaskbarCreatedMessage))
        {
            var point=DesktopWidgetHost.TryGetScreenPosition(Handle,out var current)?current:lastDesktopLocation;
            BeginInvoke(new Action(()=>ApplyDesktopWidgetMode(true,point)));
        }
    }
    static Label MakeLabel(string text,float size,Color color,bool bold=false)=>new(){Text=text,Font=new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular),ForeColor=color,AutoEllipsis=true};
    void UpdateFilament(JsonElement print)
    {
        RenderLibraryFilament();
        var tray=Num(print,"tray_now");
        var traySlot=tray.HasValue&&tray.Value%1==0&&tray.Value>=0&&tray.Value<4?(int?)((int)tray.Value+1):null;
        var printState=Str(print,"gcode_state","state")?.ToLowerInvariant();
        var activeSlot=printState is "running" or "printing"?traySlot:null;
        printerSketch.ActiveAmsSlot=activeSlot;
        if(traySlot.HasValue)
        {
            var spool=filamentLibrary.Spools.FirstOrDefault(x=>x.Id==filamentLibrary.Slots[traySlot.Value-1]);
            currentFilamentColor=spool is null?null:ParseFilamentColor(spool.Hex);
            if(spool!=null)
            {
                SetText(jobSubtitle,$"{spool.Material} · {spool.Name}");
                ApplyProgressColor();
                return;
            }
        }
        SetText(jobSubtitle,AppPreferences.Current.ConnectionMode=="lan"?"LAN · P1S":"Bambu Cloud · P1S");
        ApplyProgressColor();
    }
    void ApplyProgressColor() =>
        progress.ProgressColor=AppPreferences.Current.ProgressColorByFilament
            ?currentFilamentColor??UiTheme.Accent
            :UiTheme.Accent;
    void LoadLibrary(){try{if(File.Exists(libraryFile))filamentLibrary=JsonSerializer.Deserialize<FilamentLibrary>(File.ReadAllText(libraryFile))??new FilamentLibrary();}catch{filamentLibrary=new FilamentLibrary();}if(filamentLibrary.Slots==null||filamentLibrary.Slots.Length!=4)filamentLibrary.Slots=new string?[4];filamentLibrary.History??=new();filamentLibrary.Spools??=new();}
    void SaveLibrary(){try{Directory.CreateDirectory(Path.GetDirectoryName(libraryFile)!);File.WriteAllText(libraryFile,JsonSerializer.Serialize(filamentLibrary,new JsonSerializerOptions{WriteIndented=true}));RenderLibraryFilament();RenderLibrarySpools();}catch(Exception ex){SetText(updated,"No se pudo guardar biblioteca: "+ex.Message);}}
    void OpenFilamentLibrary(){using var window=new FilamentManager(filamentLibrary,SaveLibrary);window.ShowDialog(this);if(!IsDisposed){RenderLibraryFilament();RenderLibrarySpools();}}
    void RenderLibraryFilament()
    {
        var assigned=filamentLibrary.Slots.Count(id=>id!=null&&filamentLibrary.Spools.Any(spool=>spool.Id==id));
        var pending=filamentLibrary.History.Count(entry=>entry.Pending);
        for(var i=0;i<4;i++)
        {
            var spool=filamentLibrary.Spools.FirstOrDefault(item=>item.Id==filamentLibrary.Slots[i]);
            var percentage=spool is {Capacity:>0}?(int)Math.Round(100*spool.Remaining/spool.Capacity):0;
            var color=spool is null?null:ParseFilamentColor(spool.Hex);
            filamentCards[i].SetInventory(spool?.Material,spool?.Name,spool is null?null:percentage,color,spool?.Remaining,spool?.Capacity);
            if(i<sectionFilamentCards.Count)
                sectionFilamentCards[i].SetInventory(spool?.Material,spool?.Name,spool is null?null:percentage,color,spool?.Remaining,spool?.Capacity);
        }

        var summary=pending>0?$"! {pending} consumo pendiente":$"{assigned}/4 asignadas";
        SetText(inventorySummary,summary);
        inventorySummary.ForeColor=pending>0?UiTheme.Warning:UiTheme.Muted;
        if(pending>0){libraryButton.ForeColor=UiTheme.Warning;libraryButton.FlatAppearance.BorderColor=UiTheme.BorderWarning;}
        else{libraryButton.ForeColor=UiTheme.TextSoft;libraryButton.FlatAppearance.BorderColor=UiTheme.Border;}
        SetText(sectionFilamentSummary,pending>0
            ?(english?$"{assigned}/4 assigned · {pending} pending print entries":$"{assigned}/4 asignadas · {pending} consumos pendientes")
            :(english?$"{assigned}/4 AMS slots assigned":$"{assigned}/4 ranuras AMS asignadas"));
        sectionFilamentSummary.ForeColor=pending>0?UiTheme.Warning:UiTheme.TextSoft;
    }
    static Color? ParseFilamentColor(string? raw){if(string.IsNullOrWhiteSpace(raw))return null;var hex=raw.Trim().TrimStart('#');if(hex.Length<6)return null;try{return Color.FromArgb(Convert.ToInt32(hex.Substring(0,2),16),Convert.ToInt32(hex.Substring(2,2),16),Convert.ToInt32(hex.Substring(4,2),16));}catch{return null;}}
    async Task LoadConfig()
    {
        try
        {
            if (AppPreferences.Current.ConnectionMode == "lan")
            {
                var prefs=AppPreferences.Current;
                if(string.IsNullOrWhiteSpace(prefs.LanAddress)||string.IsNullOrWhiteSpace(prefs.LanSerial)||
                   string.IsNullOrWhiteSpace(prefs.LanAccessCodeProtected))
                {
                    connection.Text=english?"LAN SETUP REQUIRED":"CONFIGURA LAN";
                    updated.Text=english?"Open Settings to enter the printer IP, serial, and access code."
                        :"Abre Ajustes e introduce la IP, el número de serie y el código de acceso.";
                    return;
                }
                var accessCode=UnprotectSetting(prefs.LanAccessCodeProtected);
                await ConnectLan(prefs.LanAddress,prefs.LanSerial,accessCode);
                return;
            }
            if (!File.Exists(configFile)) return;
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(configFile));
            serial = doc.RootElement.GetProperty("serial").GetString() ?? "";
            cloudUsageSerial=serial;
            region = doc.RootElement.GetProperty("region").GetString() ?? "us";
            var token = ReadProtectedSetting(doc.RootElement,"token");
            cloudToken=token;
            cloudRefreshToken=ReadProtectedSetting(doc.RootElement,"refreshToken");
            cloudLoginData=ReadProtectedSetting(doc.RootElement,"loginData");
            cameraDevVersion=doc.RootElement.TryGetProperty("devVersion",out var version)?version.GetString()??"": "";
            if(doc.RootElement.TryGetProperty("cameraDeviceId",out var deviceId)&&
               !string.IsNullOrWhiteSpace(deviceId.GetString()))
                cameraDeviceId=deviceId.GetString()!;
            else
                await SaveCloudConfig();
            await Connect(token);
        }
        catch (Exception ex)
        {
            connection.Text=english?"CONNECTION ERROR":"ERROR DE CONEXIÓN";
            updated.Text=ex.Message;
        }
    }

    void LoadCloudUsageCredentials()
    {
        cloudToken="";
        cloudUsageSerial="";
        if(!File.Exists(configFile))return;
        using var doc=JsonDocument.Parse(File.ReadAllText(configFile));
        var root=doc.RootElement;
        if(!root.TryGetProperty("serial",out var savedSerial)||string.IsNullOrWhiteSpace(savedSerial.GetString()))return;
        cloudUsageSerial=savedSerial.GetString()!;
        if(!string.Equals(cloudUsageSerial,AppPreferences.Current.LanSerial.Trim(),StringComparison.OrdinalIgnoreCase))
        {
            cloudUsageSerial="";
            return;
        }
        if(root.TryGetProperty("region",out var savedRegion)&&!string.IsNullOrWhiteSpace(savedRegion.GetString()))
            region=savedRegion.GetString()!;
        if(root.TryGetProperty("token",out var token)&&!string.IsNullOrWhiteSpace(token.GetString()))
            cloudToken=ReadProtectedSetting(root,"token");
    }

    static string ReadProtectedSetting(JsonElement root,string name)
    {
        if(!root.TryGetProperty(name,out var value)||string.IsNullOrWhiteSpace(value.GetString()))return "";
        var encrypted=Convert.FromBase64String(value.GetString()!);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser));
    }

    static string UnprotectSetting(string encrypted) =>
        Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted),null,DataProtectionScope.CurrentUser));

    string GetPluginDirectory()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"BambuStudio","plugins");
    string GetStudioDirectory()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Bambu Studio");
    string GetStudioConfigDirectory()=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"BambuStudio");
    string GetCertificateDirectory()=>Path.Combine(GetStudioDirectory(),"resources","cert");

    string GetInstalledStudioVersion()
    {
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        {
            using var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view);
            using var uninstall=hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if(uninstall==null)continue;
            foreach(var name in uninstall.GetSubKeyNames())
            {
                using var app=uninstall.OpenSubKey(name);
                if(!string.Equals(app?.GetValue("DisplayName") as string,"Bambu Studio",StringComparison.OrdinalIgnoreCase))
                    continue;
                var version=app?.GetValue("DisplayVersion") as string;
                if(!string.IsNullOrWhiteSpace(version))return version;
            }
        }
        throw new InvalidOperationException(english
            ?"Could not read the installed Bambu Studio version required for Cloud camera authentication."
            :"No se pudo leer la versión instalada de Bambu Studio necesaria para autenticar la cámara Cloud.");
    }

    async Task<string> BuildCameraLoginInfoAsync()
    {
        if(string.IsNullOrWhiteSpace(cloudLoginData))
            throw new InvalidOperationException(english
                ?"Sign in again to enable the secure Cloud camera session."
                :"Vuelve a iniciar sesión para habilitar la cámara segura por Cloud.");

        var tokenInfo=JsonNode.Parse(cloudLoginData) as JsonObject
            ??throw new InvalidDataException("La respuesta de inicio de sesión no tiene un formato válido.");
        var accessToken=tokenInfo["accessToken"]?.GetValue<string>();
        var refreshToken=tokenInfo["refreshToken"]?.GetValue<string>();
        if(string.IsNullOrWhiteSpace(accessToken)||string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidDataException("La respuesta de inicio de sesión no incluye el token de acceso de Bambu Cloud.");

        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(25)};
        using var request=new HttpRequestMessage(HttpMethod.Get,"https://api.bambulab.com/v1/user-service/my/profile");
        request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",accessToken);
        using var response=await http.SendAsync(request);
        if(!response.IsSuccessStatusCode)
            throw new InvalidOperationException(english
                ?$"Bambu Cloud could not load the account profile for the camera session (HTTP {(int)response.StatusCode}). Sign in again and retry."
                :$"Bambu Cloud no pudo cargar el perfil para la sesión de cámara (HTTP {(int)response.StatusCode}). Vuelve a iniciar sesión e inténtalo de nuevo.");

        using var profile=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var profileRoot=profile.RootElement;
        var user=new JsonObject
        {
            ["uid"]=GetRequiredProfileString(profileRoot,"uidStr"),
            ["name"]=GetRequiredProfileString(profileRoot,"name"),
            ["account"]=GetRequiredProfileString(profileRoot,"account"),
            ["avatar"]=GetRequiredProfileString(profileRoot,"avatar")
        };
        var data=new JsonObject
        {
            ["refresh_token"]=refreshToken,
            ["token"]=accessToken,
            ["expires_in"]=GetRequiredLoginNumberString(tokenInfo,"expiresIn"),
            ["refresh_expires_in"]=GetRequiredLoginNumberString(tokenInfo,"refreshExpiresIn"),
            ["user"]=user
        };
        return new JsonObject{["data"]=data}.ToJsonString();
    }

    static string GetRequiredProfileString(JsonElement profile,string property)
    {
        if(!profile.TryGetProperty(property,out var value)||value.ValueKind!=JsonValueKind.String)
            throw new InvalidDataException($"El perfil de Bambu Cloud no incluye el campo requerido {property}.");
        return value.GetString()??"";
    }

    static string GetRequiredLoginNumberString(JsonObject login,string property)
    {
        if(login[property] is not JsonValue value||
           !double.TryParse(value.ToJsonString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var number)||
           !double.IsFinite(number))
            throw new InvalidDataException($"La respuesta de Bambu Cloud no incluye un valor válido para {property}.");
        return number.ToString("F6",CultureInfo.InvariantCulture);
    }

    Task OpenSettings()
    {
        if(activeSection=="settings"&&settingsView is not null)return Task.CompletedTask;
        var currentPreferences=AppPreferences.Current;
        var wasDesktopWidget=desktopWidget;
        if(wasDesktopWidget)
        {
            settingsWidgetLocation=desktopAttached?DesktopWidgetHost.Detach(this):Location;
            desktopAttached=false;
            settingsOpenedFromWidget=true;
            desktopWidget=false;
            updated.Visible=true;
            ClientSize=new Size(824,790);
            SetCornerRegion(790);
            sidebar.Visible=true;
            LayoutMainPanels();
            expandWidgetButton.Visible=false;
            UpdateCloseButton();
        }

        string lanAccessCode;
        try
        {
            lanAccessCode=string.IsNullOrWhiteSpace(currentPreferences.LanAccessCodeProtected)
                ? ""
                : UnprotectSetting(currentPreferences.LanAccessCodeProtected);
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,(english?"Could not decrypt the saved LAN access code.\n":"No se pudo descifrar el código de acceso LAN guardado.\n")+ex.Message,
                english?"LAN connection":"Conexión LAN",MessageBoxButtons.OK,MessageBoxIcon.Error);
            if(settingsOpenedFromWidget)RestoreWidgetAfterSettings();
            return Task.CompletedTask;
        }

        settingsPreviousConnectionMode=currentPreferences.ConnectionMode;
        if(settingsView is null)
        {
            settingsView=new AppSettingsView(currentPreferences.Theme,currentPreferences.Language,connection.Text,
                currentPreferences.DesktopWidget,currentPreferences.ConnectionMode,currentPreferences.LanAddress,
                string.IsNullOrWhiteSpace(currentPreferences.LanSerial)?serial:currentPreferences.LanSerial,lanAccessCode,
                currentPreferences.ProgressColorByFilament)
            {
                Location=new Point(8,0),
                Visible=false
            };
            AttachWindowDragHandlers(settingsView);
            settingsView.SaveRequested+=async(_,_)=>await SaveSettingsAsync();
            settingsView.CancelRequested+=(_,_)=>CancelSettings();
            settingsView.AccountRequested+=async(_,_)=>await ConfigureCloud();
            mainContent.Controls.Add(settingsView);
            settingsView.BringToFront();
        }
        else
        {
            settingsView.Dispose();
            settingsView=new AppSettingsView(currentPreferences.Theme,currentPreferences.Language,connection.Text,
                currentPreferences.DesktopWidget,currentPreferences.ConnectionMode,currentPreferences.LanAddress,
                string.IsNullOrWhiteSpace(currentPreferences.LanSerial)?serial:currentPreferences.LanSerial,lanAccessCode,
                currentPreferences.ProgressColorByFilament)
            {
                Location=new Point(8,0),
                Visible=false
            };
            AttachWindowDragHandlers(settingsView);
            settingsView.SaveRequested+=async(_,_)=>await SaveSettingsAsync();
            settingsView.CancelRequested+=(_,_)=>CancelSettings();
            settingsView.AccountRequested+=async(_,_)=>await ConfigureCloud();
            mainContent.Controls.Add(settingsView);
        }
        ShowSection("settings");
        return Task.CompletedTask;
    }

    async Task SaveSettingsAsync()
    {
        if(settingsView is null)return;
        var previousLanguage=AppPreferences.Current.Language;
        try
        {
            AppPreferences.Save(settingsView.SelectedTheme,settingsView.SelectedLanguage,settingsView.DesktopWidgetEnabled);
            AppPreferences.SaveProgressColor(settingsView.ProgressColorByFilament);
            AppPreferences.SaveConnection(settingsView.ConnectionMode,settingsView.LanAddress,settingsView.LanSerial,
                string.IsNullOrWhiteSpace(settingsView.LanAccessCode)?"":ProtectSetting(settingsView.LanAccessCode));
            ApplyProgressColor();
        }
        catch(Exception ex)
        {
            MessageBox.Show(this,"No se pudieron guardar los ajustes.\n"+ex.Message,"Ajustes",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return;
        }

        if(!string.Equals(settingsView.SelectedTheme,UiTheme.Mode,StringComparison.OrdinalIgnoreCase) ||
           !string.Equals(settingsView.SelectedLanguage,previousLanguage,StringComparison.OrdinalIgnoreCase))
        {
            Application.Restart();
            Close();
            return;
        }

        try
        {
            if(settingsView.ConnectionMode=="lan")
                await ConnectLan(settingsView.LanAddress,settingsView.LanSerial,settingsView.LanAccessCode);
            else if(settingsPreviousConnectionMode=="lan")
            {
                if(string.IsNullOrWhiteSpace(cloudToken))await LoadConfig();
                else await Connect(cloudToken);
            }
        }
        catch(Exception ex)
        {
            connection.Text=english?"CONNECTION ERROR":"ERROR DE CONEXIÓN";
            updated.Text=ex.Message;
        }
        settingsPreviousConnectionMode=settingsView.ConnectionMode;

        if(settingsOpenedFromWidget)
        {
            var enableWidget=settingsView.DesktopWidgetEnabled;
            var location=settingsWidgetLocation??Location;
            settingsOpenedFromWidget=false;
            ShowSection("printer");
            ApplyDesktopWidgetMode(enableWidget,location);
        }
        else if(settingsView.DesktopWidgetEnabled!=desktopWidget)
        {
            var enableWidget=settingsView.DesktopWidgetEnabled;
            if(enableWidget)ShowSection("printer");
            ApplyDesktopWidgetMode(enableWidget,Location);
        }
    }

    void CancelSettings()
    {
        ShowSection("printer");
        if(settingsOpenedFromWidget)RestoreWidgetAfterSettings();
    }

    void RestoreWidgetAfterSettings()
    {
        var location=settingsWidgetLocation??Location;
        settingsOpenedFromWidget=false;
        AppPreferences.Save(AppPreferences.Current.Theme,AppPreferences.Current.Language,true);
        ApplyDesktopWidgetMode(true,location);
    }

    async Task ConfigureCloud()
    {
        using var dialog = new ConfigDialog(serial, region);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            serial = dialog.Serial; region = dialog.CloudRegion;
            var token = dialog.Token;cloudToken=token;cloudRefreshToken=dialog.RefreshToken;cloudLoginData=dialog.LoginData;cameraDevVersion=dialog.DeviceVersion;
            var encrypted=ProtectSetting(token);
            var encryptedRefreshToken=ProtectSetting(cloudRefreshToken);
            var encryptedLoginData=ProtectSetting(cloudLoginData);
            await SaveCloudConfig(encrypted,encryptedRefreshToken,encryptedLoginData);
            cloudToken=token;await Connect(token);
            if(filamentLibrary.History.Any(entry=>entry.Pending)){usageRetryTimer.Start();await RetryPendingPrintsAsync();}
        }
        catch (Exception ex) { connection.Text = "No se pudo conectar"; updated.Text = ex.Message; }
    }

    static string ProtectSetting(string value)=>Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));

    async Task SaveCloudConfig(string? encryptedToken=null,string? encryptedRefreshToken=null,string? encryptedLoginData=null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configFile)!);
        encryptedToken??=ProtectSetting(cloudToken);
        encryptedRefreshToken??=ProtectSetting(cloudRefreshToken);
        encryptedLoginData??=ProtectSetting(cloudLoginData);
        var config=new { serial, region, devVersion=cameraDevVersion, cameraDeviceId,
            token=encryptedToken, refreshToken=encryptedRefreshToken, loginData=encryptedLoginData };
        await File.WriteAllTextAsync(configFile,JsonSerializer.Serialize(config));
    }

    async Task Connect(string token)
    {
        var user = await UserIdAsync(token, region);
        var host = region == "cn" ? "cn.mqtt.bambulab.com" : "us.mqtt.bambulab.com";
        await ConnectMqtt(host, "u_"+user, token, false);
    }

    async Task ConnectLan(string address, string printerSerial, string accessCode)
    {
        if(!System.Net.IPAddress.TryParse(address,out var ip)||
           ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||
           !IsPrivateLanAddress(ip.GetAddressBytes()))
            throw new InvalidOperationException(english
                ?"The printer address must be a private IPv4 address."
                :"La dirección de la impresora debe ser una IPv4 privada.");
        if(string.IsNullOrWhiteSpace(printerSerial)||string.IsNullOrWhiteSpace(accessCode))
            throw new InvalidOperationException(english
                ?"The printer serial number and LAN access code are required."
                :"Se necesitan el número de serie y el código de acceso LAN.");

        serial=printerSerial.Trim();
        await ConnectMqtt(ip.ToString(),"bblp",accessCode,true);
    }

    static bool IsPrivateLanAddress(byte[] bytes) =>
        bytes[0]==10 ||
        (bytes[0]==172&&bytes[1]>=16&&bytes[1]<=31) ||
        (bytes[0]==192&&bytes[1]==168) ||
        (bytes[0]==169&&bytes[1]==254);

    async Task ConnectMqtt(string host,string username,string password,bool lan)
    {
        if (client is not null) { try { await client.DisconnectAsync(); } catch { } client.Dispose(); }
        client = new MqttFactory().CreateMqttClient();
        client.ApplicationMessageReceivedAsync += e =>
        {
            try { var payload = Encoding.UTF8.GetString(e.ApplicationMessage.PayloadSegment); using var d=JsonDocument.Parse(payload); var root=d.RootElement.Clone(); BeginInvoke(new Action(() => Render(root))); } catch { }
            return Task.CompletedTask;
        };
        client.ConnectedAsync += async _ =>
        {
            BeginInvoke(new Action(() =>
            {
                connection.Text=lan?(english?"LAN ACTIVE":"LAN ACTIVA"):(english?"CLOUD ACTIVE":"CLOUD ACTIVA");
                connection.ForeColor=UiTheme.Accent;connectionPill.BackColor=UiTheme.SurfaceRaised;
                connectionPill.BorderColor=UiTheme.BorderSuccess;connectionPill.Invalidate();
                updated.Text=(english?"Updated · ":"Actualizado · ")+DateTime.Now.ToString("HH:mm");
                UpdatePrintControls();
            }));
            await client.SubscribeAsync($"device/{serial}/report", MqttQualityOfServiceLevel.AtMostOnce);
            await RequestPrinterStatusAsync();
        };
        client.DisconnectedAsync += _ =>
        {
            if(!IsDisposed)BeginInvoke(new Action(()=>
            {
                connection.Text=english?"DISCONNECTED":"SIN CONEXIÓN";connection.ForeColor=UiTheme.Muted;
                connectionPill.BackColor=UiTheme.Surface;connectionPill.BorderColor=UiTheme.Border;
                connectionPill.Invalidate();
                updated.Text=english?"Disconnected · check network or credentials":"Sin conexión · revisa red o credenciales";
                UpdatePrintControls();
            }));
            return Task.CompletedTask;
        };
        var optionsBuilder=new MqttClientOptionsBuilder()
            .WithTcpServer(host,8883)
            .WithCredentials(username,password)
            .WithClientId("bambu_widget_"+Guid.NewGuid().ToString("N"))
            .WithCleanSession();
        if(lan)
            optionsBuilder.WithTlsOptions(o=>
            {
                o.UseTls();
                o.WithAllowUntrustedCertificates();
                o.WithIgnoreCertificateChainErrors();
                o.WithIgnoreCertificateRevocationErrors();
                o.WithCertificateValidationHandler(_=>true);
            });
        else
            optionsBuilder.WithTlsOptions(o=>o.UseTls());
        var options=optionsBuilder.Build();
        await client.ConnectAsync(options);
        updated.Text=english?"Connecting…":"Conectando…";
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
        var amsResult=SyncAmsSpools(root);
        if(amsResult is not null)
        {
            if(amsResult.Changed)
            {
                SaveLibrary();
                RenderLibraryFilament();
            }
            if(amsReadAwaiting)
            {
                amsReadAwaiting=false;
                amsReadStatus.Text=amsResult.Recognized>0
                    ?(english?$"RFID read: {amsResult.Recognized} spool(s), {amsResult.Added} added to library."
                             :$"Lectura RFID: {amsResult.Recognized} bobina(s), {amsResult.Added} añadida(s) a la biblioteca.")
                    :(english?"AMS updated; no readable RFID spools were found."
                             :"AMS actualizado; no se encontraron bobinas RFID legibles.");
                amsReadStatus.ForeColor=amsResult.Recognized>0?UiTheme.Success:UiTheme.Muted;
            }
        }
        chamberLightOn=ReadChamberLightState(root)
            ??ReadLightMode(Find(root,"led_mode"))
            ??chamberLightOn;
        var speed=Num(p,"spd_lvl","speed_level");
        if(speed.HasValue&&speed.Value%1==0&&speed.Value>=0&&speed.Value<=3)
            reportedSpeedMode=(int)speed.Value;
        UpdatePrintControls();
        var reportedVersion=Str(p,"dev_version","firmware_version");
        if(!string.IsNullOrWhiteSpace(reportedVersion))cameraDevVersion=reportedVersion;
        var rawState=Str(p,"gcode_state","state"); var isIdle=false;
        var incomingJob=Str(p,"gcode_file","subtask_name","task_name");if(!string.IsNullOrWhiteSpace(incomingJob))activeJob=Path.GetFileNameWithoutExtension(incomingJob);
        var incomingTask=ScalarText(p,"task_id","print_id");if(!string.IsNullOrWhiteSpace(incomingTask))activeTaskId=incomingTask;
        if(!string.IsNullOrWhiteSpace(rawState))
        {
            var state=rawState.ToLowerInvariant();
            var isRunning=state is "running" or "printing";
            var isTerminal=state is "finish" or "finished" or "failed" or "cancelled" or "aborted";
            if(isRunning)
            {
                if(lastState is not ("running" or "printing"))
                {
                    finishHandled=false;
                    activeTaskId=incomingTask;
                    BeginAmsUsageTracking();
                }
                if(!string.IsNullOrWhiteSpace(incomingJob))activeJob=Path.GetFileNameWithoutExtension(incomingJob);
                TrackLanAmsUsage(true);
            }
            else if(isTerminal)
            {
                if(lastState is "running" or "printing")
                {
                    TrackLanAmsUsage(true);
                    if(!finishHandled)
                    {
                        finishHandled=true;
                        _=RecordCompletedPrintAsync(
                            activeJob,
                            activeTaskId,
                            state is "finish" or "finished",
                            activePrintSpoolIds.ToArray(),
                            activeLanAmsUsage.ToDictionary(item=>item.Key,item=>item.Value));
                    }
                    activeAmsLastReadings.Clear();
                    activeLanAmsUsage.Clear();
                }
            }
            else
            {
                TrackLanAmsUsage(false);
            }
            lastState=state;
            UpdatePrintControls();
        }
        else if(lastState is "running" or "printing")
        {
            TrackLanAmsUsage(true);
        }
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
        var nozzleTarget=Num(p,"nozzle_target_temper","nozzle_target_temp","nozzle_target_temperature","nozzle_target")
            ??Num(root,"nozzle_target_temper","nozzle_target_temp","nozzle_target_temperature","nozzle_target");
        var bedTarget=Num(p,"bed_target_temper","bed_target_temp","bed_target_temperature","bed_target")
            ??Num(root,"bed_target_temper","bed_target_temp","bed_target_temperature","bed_target");
        if(nozzleTarget.HasValue)nozzleGauge.TargetTemperature=nozzleTarget;
        if(bedTarget.HasValue)bedGauge.TargetTemperature=bedTarget;
        UpdateTemperature(nozzleGauge,Num(p,"nozzle_temper","nozzle_temp"),300,ref nozzleZeroSince,ref nozzleZeroCount);
        UpdateTemperature(bedGauge,Num(p,"bed_temper","bed_temp"),120,ref bedZeroSince,ref bedZeroCount);
        UpdateFilament(p);
        SetText(updated,(english?"Updated · ":"Actualizado · ")+DateTime.Now.ToString("HH:mm"));
    }
    async Task ReadAmsSpoolsAsync()
    {
        if(client?.IsConnected!=true)
        {
            amsReadStatus.Text=english?"Connect to the printer before reading AMS spools."
                                     :"Conecta la impresora antes de leer las bobinas del AMS.";
            amsReadStatus.ForeColor=UiTheme.Warning;
            return;
        }

        var requestId=Interlocked.Increment(ref amsReadRequestId);
        amsReadAwaiting=true;
        amsReadStatus.Text=english?"Requesting the latest AMS RFID status…"
                                  :"Solicitando el último estado RFID del AMS…";
        amsReadStatus.ForeColor=UiTheme.Muted;
        try
        {
            await RequestPrinterStatusAsync();
        }
        catch(Exception ex) when(ex is MqttCommunicationException or InvalidOperationException)
        {
            if(requestId==Interlocked.Read(ref amsReadRequestId))amsReadAwaiting=false;
            if(IsDisposed||amsReadStatus.IsDisposed)return;
            amsReadStatus.Text=(english?"Could not request AMS status: ":"No se pudo solicitar el estado del AMS: ")+ex.Message;
            amsReadStatus.ForeColor=UiTheme.Warning;
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(8));
        if(IsDisposed||amsReadStatus.IsDisposed)return;
        if(amsReadAwaiting&&requestId==Interlocked.Read(ref amsReadRequestId))
        {
            amsReadAwaiting=false;
            amsReadStatus.Text=english?"No AMS response received. Check the printer connection and try again."
                                      :"No se recibió respuesta del AMS. Comprueba la conexión e inténtalo de nuevo.";
            amsReadStatus.ForeColor=UiTheme.Warning;
        }
    }
    async Task RequestPrinterStatusAsync()
    {
        if(client?.IsConnected!=true)throw new InvalidOperationException(english?"Printer is disconnected.":"La impresora está desconectada.");
        var sequence=Interlocked.Increment(ref statusSequence).ToString(CultureInfo.InvariantCulture);
        var request=new MqttApplicationMessageBuilder()
            .WithTopic($"device/{serial}/request")
            .WithPayload(JsonSerializer.Serialize(new{pushing=new{command="pushall",sequence_id=sequence}}))
            .Build();
        await client.PublishAsync(request);
    }
    sealed record AmsSyncResult(bool Changed,int Recognized,int Added);
    AmsSyncResult? SyncAmsSpools(JsonElement report)
    {
        var ams=Find(report,"ams");
        if(ams.ValueKind==JsonValueKind.Undefined)return null;
        if(ams.ValueKind==JsonValueKind.Object&&TryGetProperty(ams,"ams",out var nestedAms))ams=nestedAms;
        if(ams.ValueKind!=JsonValueKind.Array)return null;

        var changed=false;
        var recognized=0;
        var added=0;
        var amsIndex=0;
        foreach(var unit in ams.EnumerateArray())
        {
            var unitId=ReadInt(unit,"ams_id","id")??amsIndex;
            var trays=GetProperty(unit,"tray");
            if(trays.ValueKind==JsonValueKind.Array)
            {
                var trayIndex=0;
                foreach(var tray in trays.EnumerateArray())
                {
                    var trayId=ReadInt(tray,"id")??trayIndex;
                    var slot=unitId*4+trayId;
                    if(slot>=0&&slot<filamentLibrary.Slots.Length)
                    {
                        var material=ScalarText(tray,"tray_type","type");
                        var profileId=ScalarText(tray,"tray_info_idx");
                        var tagUid=NormalizeRfidId(ScalarText(tray,"tag_uid"));
                        var trayUuid=NormalizeRfidId(ScalarText(tray,"tray_uuid"));
                        if(double.TryParse(ScalarText(tray,"remain"),NumberStyles.Float,CultureInfo.InvariantCulture,out var remainingPercent)
                            &&remainingPercent>=0&&remainingPercent<=100)
                            latestAmsRemainingPercent[slot]=(int)Math.Round(remainingPercent);
                        if(string.Equals(material,"Empty",StringComparison.OrdinalIgnoreCase)&&string.IsNullOrWhiteSpace(profileId))
                        {
                            latestAmsRemainingPercent.Remove(slot);
                            activeAmsLastReadings.Remove(slot);
                            var assigned=filamentLibrary.Spools.FirstOrDefault(x=>x.Id==filamentLibrary.Slots[slot]);
                            if(assigned is {RfidTagUid:not null} or {RfidTrayUuid:not null})
                            {
                                filamentLibrary.Slots[slot]=null;
                                changed=true;
                            }
                        }
                        else if(!string.IsNullOrWhiteSpace(material)&&IsUsableRfid(tagUid, trayUuid))
                        {
                            recognized++;
                            var spool=filamentLibrary.Spools.FirstOrDefault(x=>
                                IsSameRfid(x.RfidTagUid,tagUid)||IsSameRfid(x.RfidTrayUuid,trayUuid)||
                                IsSameRfid(x.RfidTagUid,trayUuid)||IsSameRfid(x.RfidTrayUuid,tagUid));
                            if(spool is null)
                            {
                                var name=ResolveRfidSpoolName(material,profileId,ScalarText(tray,"tray_sub_brands"));
                                const double capacity=1000;
                                var remainPercent=ReadNumber(tray,"remain");
                                spool=new Spool
                                {
                                    Name=name,
                                    Material=material,
                                    Brand=ResolveRfidBrand(name,profileId),
                                    Hex=NormalizeRfidColor(ScalarText(tray,"tray_color")),
                                    Capacity=capacity,
                                    Remaining=remainPercent is >=0 and <=100?capacity*remainPercent.Value/100:capacity,
                                    RfidTagUid=NormalizeRfidId(tagUid),
                                    RfidTrayUuid=NormalizeRfidId(trayUuid),
                                    RfidProfileId=profileId
                                };
                                filamentLibrary.Spools.Add(spool);
                                added++;
                                changed=true;
                            }
                            else
                            {
                                var subBrand=ScalarText(tray,"tray_sub_brands");
                                var name=!string.IsNullOrWhiteSpace(profileId)||!string.IsNullOrWhiteSpace(subBrand)
                                    ?ResolveRfidSpoolName(material,profileId,subBrand)
                                    :spool.Name;
                                var rawColor=ScalarText(tray,"tray_color");
                                var color=string.IsNullOrWhiteSpace(rawColor)?spool.Hex:NormalizeRfidColor(rawColor);
                                if(spool.Name!=name){spool.Name=name;changed=true;}
                                if(spool.Material!=material){spool.Material=material;changed=true;}
                                var brand=ResolveRfidBrand(name,profileId);
                                if(spool.Brand!=brand){spool.Brand=brand;changed=true;}
                                if(spool.Hex!=color){spool.Hex=color;changed=true;}
                                if(tagUid is not null&&spool.RfidTagUid!=tagUid){spool.RfidTagUid=tagUid;changed=true;}
                                if(trayUuid is not null&&spool.RfidTrayUuid!=trayUuid){spool.RfidTrayUuid=trayUuid;changed=true;}
                                if(profileId is not null&&spool.RfidProfileId!=profileId){spool.RfidProfileId=profileId;changed=true;}
                            }
                            if(filamentLibrary.Slots[slot]!=spool.Id)
                            {
                                filamentLibrary.Slots[slot]=spool.Id;
                                changed=true;
                            }
                        }
                    }
                    trayIndex++;
                }
            }
            amsIndex++;
        }
        return new AmsSyncResult(changed,recognized,added);
    }
    static string ResolveRfidSpoolName(string material,string? profileId,string? subBrand)
    {
        if(!string.IsNullOrWhiteSpace(subBrand))return subBrand.Trim();
        if(!string.IsNullOrWhiteSpace(profileId)&&BambuFilamentNames.TryGetValue(profileId,out var knownName))return knownName;
        return string.IsNullOrWhiteSpace(profileId)?material:$"{material} · {profileId}";
    }
    static string ResolveRfidBrand(string name,string? profileId)=>
        (name.StartsWith("Bambu ",StringComparison.OrdinalIgnoreCase)||
         profileId is not null&&new[]{"GFA","GFB","GFC","GFG","GFN","GFS","GFT","GFU"}.Any(prefix=>profileId.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)))
            ?"Bambu Lab":"RFID";
    static readonly IReadOnlyDictionary<string,string> BambuFilamentNames=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["GFA00"]="Bambu PLA Basic",["GFA01"]="Bambu PLA Matte",["GFA02"]="Bambu PLA Metal",
        ["GFA05"]="Bambu PLA Silk",["GFA06"]="Bambu PLA Silk+",["GFA07"]="Bambu PLA Marble",
        ["GFA08"]="Bambu PLA Sparkle",["GFA09"]="Bambu PLA Tough",["GFA50"]="Bambu PLA-CF",
        ["GFB00"]="Bambu ABS",["GFB01"]="Bambu ASA",["GFC00"]="Bambu PC",
        ["GFG00"]="Bambu PETG Basic",["GFG01"]="Bambu PETG Translucent",["GFG02"]="Bambu PETG HF",
        ["GFG50"]="Bambu PETG-CF",["GFN03"]="Bambu PA-CF",["GFN04"]="Bambu PAHT-CF",
        ["GFS00"]="Bambu Support W",["GFS01"]="Bambu Support G",["GFT01"]="Bambu PET-CF",
        ["GFU00"]="Bambu TPU 95A HF",["GFU01"]="Bambu TPU 95A",["GFU02"]="Bambu TPU for AMS"
    };
    static string NormalizeRfidColor(string? raw)
    {
        if(string.IsNullOrWhiteSpace(raw))return "B2DA6F";
        var color=raw.Trim().TrimStart('#');
        if(color=="00000000")return "B2DA6F";
        if(color.Length>=8)color=color[..6];
        if(color.Length!=6)return "B2DA6F";
        try{_ = Convert.ToInt32(color,16);return color.ToUpperInvariant();}
        catch(FormatException){return "B2DA6F";}
    }
    static string? NormalizeRfidId(string? value)=>IsUsableRfid(value)?value!.Trim().Replace("-","",StringComparison.Ordinal).ToUpperInvariant():null;
    static bool IsSameRfid(string? a,string? b)=>!string.IsNullOrWhiteSpace(a)&&!string.IsNullOrWhiteSpace(b)&&string.Equals(a,b,StringComparison.OrdinalIgnoreCase);
    static bool IsUsableRfid(params string?[] values)=>values.Any(value=>
    {
        if(string.IsNullOrWhiteSpace(value))return false;
        var id=value.Trim().Replace("-","",StringComparison.Ordinal);
        return id.Length>=8&&id.All(Uri.IsHexDigit)&&id.Any(character=>character!='0');
    });
    static JsonElement GetProperty(JsonElement element,string key)=>TryGetProperty(element,key,out var value)?value:default;
    static bool TryGetProperty(JsonElement element,string key,out JsonElement value)
    {
        if(element.ValueKind==JsonValueKind.Object)
        {
            foreach(var property in element.EnumerateObject())
                if(string.Equals(property.Name,key,StringComparison.OrdinalIgnoreCase)){value=property.Value;return true;}
        }
        value=default;
        return false;
    }
    static int? ReadInt(JsonElement element,params string[] keys)
    {
        foreach(var key in keys)
        {
            var value=GetProperty(element,key);
            if(value.ValueKind==JsonValueKind.Number&&value.TryGetInt32(out var number))return number;
            if(value.ValueKind==JsonValueKind.String&&int.TryParse(value.GetString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out number))return number;
        }
        return null;
    }
    static double? ReadNumber(JsonElement element,params string[] keys)
    {
        foreach(var key in keys)
        {
            var value=GetProperty(element,key);
            if(value.ValueKind==JsonValueKind.Number&&value.TryGetDouble(out var number))return number;
            if(value.ValueKind==JsonValueKind.String&&double.TryParse(value.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out number))return number;
        }
        return null;
    }
    void BeginAmsUsageTracking()
    {
        activePrintSpoolIds=filamentLibrary.Slots.ToArray();
        activeAmsLastReadings.Clear();
        activeLanAmsUsage.Clear();
        foreach(var (slot,percent) in latestAmsRemainingPercent)
            activeAmsLastReadings[slot]=(activePrintSpoolIds[slot],percent);
    }
    void TrackLanAmsUsage(bool countDecrease)
    {
        if(AppPreferences.Current.ConnectionMode!="lan")return;
        foreach(var (slot,percent) in latestAmsRemainingPercent)
        {
            if(slot<0||slot>=filamentLibrary.Slots.Length)continue;
            var spoolId=filamentLibrary.Slots[slot];
            if(!activeAmsLastReadings.TryGetValue(slot,out var previous)||previous.SpoolId!=spoolId)
            {
                activeAmsLastReadings[slot]=(spoolId,percent);
                continue;
            }

            var decrease=previous.Percent-percent;
            if(countDecrease&&decrease>0&&!string.IsNullOrWhiteSpace(spoolId))
            {
                var spool=filamentLibrary.Spools.FirstOrDefault(item=>item.Id==spoolId);
                if(spool is {Capacity:>0})
                {
                    var grams=spool.Capacity*decrease/100.0;
                    var key=(slot,spoolId);
                    activeLanAmsUsage[key]=activeLanAmsUsage.GetValueOrDefault(key)+grams;
                }
            }
            activeAmsLastReadings[slot]=(spoolId,percent);
        }
    }
    static void UpdateTemperature(TemperatureGauge gauge,double? reading,double max,ref DateTime? zeroSince,ref int zeroCount)
    {
        if(!reading.HasValue||reading.Value<0||reading.Value>max)return;
        if(reading.Value>0){zeroSince=null;zeroCount=0;gauge.Temperature=reading.Value;return;}
        zeroSince??=DateTime.UtcNow;zeroCount++;
        if(zeroCount>=4&&(DateTime.UtcNow-zeroSince.Value).TotalSeconds>=5)gauge.Temperature=0;
    }
    async Task RecordCompletedPrintAsync(string jobName,string? taskId,bool completedSuccessfully,string?[] slotSpoolIds,Dictionary<(int Slot,string SpoolId),double> lanUsage)
    {
        var lanOnly=AppPreferences.Current.ConnectionMode=="lan";
        var record=new PrintEntry{Job=(completedSuccessfully?"":"Fallida · ")+(string.IsNullOrWhiteSpace(jobName)?"Impresión finalizada":jobName),Date=DateTime.Now,TaskId=taskId,Pending=!lanOnly,SlotSpoolIds=slotSpoolIds};
        if(lanOnly)
        {
            foreach(var ((slot,spoolId),grams) in lanUsage)
            {
                if(slot<0||slot>=filamentLibrary.Slots.Length||grams<=0)continue;
                var spool=filamentLibrary.Spools.FirstOrDefault(item=>item.Id==spoolId);
                if(spool is null)continue;
                record.Slices.Add(new PrintSlice{Slot=slot+1,SpoolId=spoolId,Grams=grams,Source="LAN AMS (%)"});
                spool.Remaining=Math.Max(0,spool.Remaining-grams);
            }
            filamentLibrary.History.Add(record);
            SaveLibrary();
            RenderLibraryFilament();
            SetText(updated,record.Slices.Count>0
                ?(english?"Estimated AMS usage added to history":"Consumo estimado del AMS añadido al historial")
                :(english?"Print saved; AMS did not report a usable remaining percentage."
                         :"Impresión guardada; el AMS no informó un porcentaje restante válido."));
            return;
        }

        filamentLibrary.History.Add(record);SaveLibrary();RenderLibraryFilament();
        if(!string.IsNullOrWhiteSpace(cloudToken)&&!string.IsNullOrWhiteSpace(cloudUsageSerial))usageRetryTimer.Start();
        SetText(updated,english?"Looking up per-slot filament usage…":"Buscando el consumo de filamento por ranura…");
        if(usageLookupInProgress)return;
        usageLookupInProgress=true;
        try
        {
            for(var retry=0;retry<8&&record.Pending&&!string.IsNullOrWhiteSpace(cloudToken)&&!string.IsNullOrWhiteSpace(cloudUsageSerial);retry++)
            {
                if(retry>0)await Task.Delay(3000);
                await TryResolvePendingPrintAsync(record);
            }
        }
        finally
        {
            usageLookupInProgress=false;
            if(!filamentLibrary.History.Any(entry=>entry.Pending))usageRetryTimer.Stop();
        }
        if(record.Pending&&string.IsNullOrWhiteSpace(cloudToken))
            SetText(updated,english?"Automatic usage needs a matching Bambu Cloud account.":"El consumo automático requiere una cuenta Bambu Cloud vinculada a esta impresora.");
        else if(record.Pending&&string.IsNullOrWhiteSpace(cloudUsageSerial))
            SetText(updated,english?"Automatic usage is unavailable: the Cloud printer serial does not match.":"Consumo automático no disponible: el número de serie Cloud no coincide.");
        else if(record.Pending)
            SetText(updated,english?"Usage is pending; it will be retried automatically.":"Consumo pendiente; se volverá a intentar automáticamente.");
    }
    async Task RetryPendingPrintsAsync()
    {
        if(usageLookupInProgress||string.IsNullOrWhiteSpace(cloudToken)||string.IsNullOrWhiteSpace(cloudUsageSerial)||IsDisposed||!filamentLibrary.History.Any(entry=>entry.Pending))return;
        usageLookupInProgress=true;
        try
        {
            foreach(var entry in filamentLibrary.History.Where(item=>item.Pending&&!item.Slices.Any(slice=>slice.Source=="Manual")).ToList())
                await TryResolvePendingPrintAsync(entry);
        }
        finally
        {
            usageLookupInProgress=false;
            if(!filamentLibrary.History.Any(entry=>entry.Pending))usageRetryTimer.Stop();
        }
    }
    async Task TryResolvePendingPrintAsync(PrintEntry record)
    {
        if(string.IsNullOrWhiteSpace(cloudToken)||string.IsNullOrWhiteSpace(cloudUsageSerial))return;
        try
        {
            var result=await FetchCloudUsageAsync(record.Job,record.TaskId,record.Date);
            if(!string.IsNullOrWhiteSpace(result.TaskId)&&!string.Equals(record.TaskId,result.TaskId,StringComparison.Ordinal))
            {
                record.TaskId=result.TaskId;
                SaveLibrary();
            }
            if(result.Usage.Count==0)return;
            var slices=new List<PrintSlice>();
            foreach(var (slot,grams) in result.Usage)
            {
                if(slot<1||slot>4||grams<=0)return;
                var spoolIds=record.SlotSpoolIds is {Length:4} snapshot&&snapshot.Any(id=>!string.IsNullOrWhiteSpace(id))
                    ?snapshot
                    :filamentLibrary.Slots;
                var spoolId=spoolIds[slot-1];
                if(string.IsNullOrWhiteSpace(spoolId)||!filamentLibrary.Spools.Any(spool=>spool.Id==spoolId))return;
                slices.Add(new PrintSlice{Slot=slot,SpoolId=spoolId,Grams=grams,Source="Bambu Cloud"});
            }
            foreach(var prior in record.Slices.Where(slice=>slice.Source=="Bambu Cloud"))
            {
                var spool=filamentLibrary.Spools.FirstOrDefault(item=>item.Id==prior.SpoolId);
                if(spool!=null)spool.Remaining=Math.Min(spool.Capacity,spool.Remaining+prior.Grams);
            }
            record.Slices.RemoveAll(slice=>slice.Source=="Bambu Cloud");
            record.Slices.AddRange(slices);
            foreach(var slice in slices)
            {
                var spool=filamentLibrary.Spools.First(item=>item.Id==slice.SpoolId);
                spool.Remaining=Math.Max(0,spool.Remaining-slice.Grams);
            }
            record.Pending=false;
            SaveLibrary();
            RenderLibraryFilament();
            SetText(updated,english?"Filament usage added to history":"Consumo añadido al historial");
        }
        catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or IOException)
        {
            SetText(updated,(english?"Automatic usage lookup failed: ":"Falló la consulta automática de consumo: ")+ex.Message);
        }
    }
    sealed record CloudUsageResult(string? TaskId,List<(int Slot,double Grams)> Usage);
    async Task<CloudUsageResult> FetchCloudUsageAsync(string jobName,string? taskId,DateTime completedAt)
    {
        if(string.IsNullOrWhiteSpace(cloudToken)||string.IsNullOrWhiteSpace(cloudUsageSerial))return new(taskId,new());var api=region=="cn"?"https://api.bambulab.cn":"https://api.bambulab.com";using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
        async Task<JsonDocument?> Get(string path,bool allowNotFound=false)
        {
            using var req=new HttpRequestMessage(HttpMethod.Get,api+path);req.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",cloudToken);using var res=await http.SendAsync(req);
            if(allowNotFound&&res.StatusCode==System.Net.HttpStatusCode.NotFound)return null;
            if(!res.IsSuccessStatusCode)throw new HttpRequestException($"Bambu Cloud respondió con HTTP {(int)res.StatusCode}.");
            return JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        }
        static List<(int Slot,double Grams)> ParseUsage(JsonDocument? task)
        {
            if(task==null)return new();
            var mapping=Find(task.RootElement,"amsDetailMapping");
            if(mapping.ValueKind!=JsonValueKind.Array)return new();
            var gramsBySlot=new Dictionary<int,double>();
            foreach(var item in mapping.EnumerateArray())
            {
                var grams=Num(item,"weight","used_g","filament_used_g");
                var slot=GetCloudAmsSlot(item);
                if(!grams.HasValue||grams<=0||!slot.HasValue||slot.Value<1||slot.Value>4)continue;
                gramsBySlot[slot.Value]=gramsBySlot.GetValueOrDefault(slot.Value)+grams.Value;
            }
            return gramsBySlot.OrderBy(x=>x.Key).Select(x=>(x.Key,x.Value)).ToList();
        }
        if(!string.IsNullOrWhiteSpace(taskId))
        {
            using var direct=await Get("/v1/user-service/my/task/"+Uri.EscapeDataString(taskId),allowNotFound:true);
            var directUsage=ParseUsage(direct);
            if(directUsage.Count>0)return new(taskId,directUsage);
        }
        using var list=await Get("/v1/user-service/my/tasks?deviceId="+Uri.EscapeDataString(cloudUsageSerial)+"&limit=30");
        if(list==null)return new(taskId,new());
        var arr=Find(list.RootElement,"hits");if(arr.ValueKind!=JsonValueKind.Array)arr=Find(list.RootElement,"tasks");
        var target=Path.GetFileNameWithoutExtension(jobName??"").Replace("Fallida · ","",StringComparison.OrdinalIgnoreCase).Trim();
        if(arr.ValueKind!=JsonValueKind.Array||target.Length==0)return new(taskId,new());
        var candidates=new List<(string Id,int NameScore,double TimeDistance)>();
        foreach(var item in arr.EnumerateArray())
        {
            var device=ScalarText(item,"deviceId","device_id");
            if(!string.IsNullOrWhiteSpace(device)&&!string.Equals(device,cloudUsageSerial,StringComparison.OrdinalIgnoreCase))continue;
            var title=ScalarText(item,"title","designTitle","designTitleTranslated","subtask_name","subtaskName","fileName","name");
            var id=ScalarText(item,"id","task_id","taskId");
            if(id==null||title==null)continue;
            title=Path.GetFileNameWithoutExtension(title).Trim();
            var exact=string.Equals(title,target,StringComparison.OrdinalIgnoreCase);
            if(!exact&&!title.Contains(target,StringComparison.OrdinalIgnoreCase)&&!target.Contains(title,StringComparison.OrdinalIgnoreCase))continue;
            var end=ScalarText(item,"endTime","end_time","finishTime","finish_time");
            var distance=DateTimeOffset.TryParse(end,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal,out var ended)?Math.Abs((ended.LocalDateTime-completedAt).TotalSeconds):double.MaxValue;
            candidates.Add((id,exact?0:1,distance));
        }
        var matchingCandidates=candidates.OrderBy(item=>item.NameScore).ThenBy(item=>item.TimeDistance).Take(5).ToList();
        string? matchedTaskId=null;
        foreach(var candidate in matchingCandidates)
        {
            matchedTaskId??=candidate.Id;
            using var detail=await Get("/v1/user-service/my/task/"+Uri.EscapeDataString(candidate.Id),allowNotFound:true);
            var usage=ParseUsage(detail);
            if(usage.Count>0)return new(candidate.Id,usage);
        }
        return new(matchedTaskId??taskId,new());
    }
    static string? ScalarText(JsonElement element,params string[] keys)
    {
        foreach(var key in keys)
        {
            if(element.ValueKind!=JsonValueKind.Object)continue;
            if(!element.TryGetProperty(key,out var value))
            {
                var found=false;
                foreach(var property in element.EnumerateObject())
                    if(string.Equals(property.Name,key,StringComparison.OrdinalIgnoreCase)){value=property.Value;found=true;break;}
                if(!found)continue;
            }
            if(value.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(value.GetString()))return value.GetString();
            if(value.ValueKind==JsonValueKind.Number)return value.ToString();
        }
        return null;
    }
    static int? GetCloudAmsSlot(JsonElement item)
    {
        // Cloud's flat "ams" field is 0-based; use it rather than treating an AMS unit id as an index.
        var ams=Num(item,"ams");
        if(ams.HasValue)
            return ams.Value%1==0&&ams.Value>=0&&ams.Value<4?(int)ams.Value+1:null;

        var tray=Num(item,"slotId","slot_id");
        if(!tray.HasValue||tray.Value%1!=0||tray.Value<0||tray.Value>=4)return null;
        var unit=Num(item,"amsId","ams_id");
        if(!unit.HasValue)return (int)tray.Value+1;
        if(unit.Value%1!=0||unit.Value<0||unit.Value>=4)return null;
        return (int)unit.Value*4+(int)tray.Value+1;
    }
    static string FormatRemaining(double minutes){var total=Math.Max(0,(int)Math.Round(minutes));return total>=60?$"{total/60} h {total%60:00} min":$"{total} min";}
    static void SetText(Label label,string text){text=UiLocalization.T(text);if(!string.Equals(label.Text,text,StringComparison.Ordinal))label.Text=text;}
    static JsonElement Find(JsonElement e,string key)
    {
        if(e.ValueKind==JsonValueKind.Object){if(e.TryGetProperty(key,out var v))return v;foreach(var x in e.EnumerateObject()){var n=Find(x.Value,key);if(n.ValueKind!=JsonValueKind.Undefined)return n;}}
        if(e.ValueKind==JsonValueKind.Array)foreach(var x in e.EnumerateArray()){var n=Find(x,key);if(n.ValueKind!=JsonValueKind.Undefined)return n;} return default;
    }
    static bool? ReadChamberLightState(JsonElement report)
    {
        if(report.ValueKind==JsonValueKind.Object)
        {
            string? node=null;
            foreach(var key in new[]{"node","led_node"})
                if(report.TryGetProperty(key,out var nodeValue)&&nodeValue.ValueKind==JsonValueKind.String)
                {
                    node=nodeValue.GetString();
                    break;
                }
            if(string.Equals(node,"chamber_light",StringComparison.OrdinalIgnoreCase))
            {
                if(report.TryGetProperty("mode",out var mode))return ReadLightMode(mode);
                if(report.TryGetProperty("led_mode",out mode))return ReadLightMode(mode);
            }
            if(report.TryGetProperty("chamber_light",out var chamberLight))
            {
                var mode=ReadLightMode(chamberLight);
                if(mode.HasValue)return mode;
                if(chamberLight.ValueKind==JsonValueKind.Object)
                {
                    JsonElement modeValue;
                    if(chamberLight.TryGetProperty("mode",out modeValue))return ReadLightMode(modeValue);
                    if(chamberLight.TryGetProperty("led_mode",out modeValue))return ReadLightMode(modeValue);
                }
            }
            foreach(var property in report.EnumerateObject())
            {
                var result=ReadChamberLightState(property.Value);
                if(result.HasValue)return result;
            }
        }
        else if(report.ValueKind==JsonValueKind.Array)
            foreach(var item in report.EnumerateArray())
            {
                var result=ReadChamberLightState(item);
                if(result.HasValue)return result;
            }
        return null;
    }
    static bool? ReadLightMode(JsonElement mode)
    {
        if(mode.ValueKind!=JsonValueKind.String)return null;
        return mode.GetString()?.ToLowerInvariant() switch
        {
            "on"=>true,
            "off"=>false,
            _=>null
        };
    }
    static string? Str(JsonElement e,params string[] keys){foreach(var k in keys){var x=Find(e,k);if(x.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(x.GetString()))return x.GetString();}return null;}
    static double? Num(JsonElement e,params string[] keys){foreach(var k in keys){var x=Find(e,k);if(x.ValueKind==JsonValueKind.Number&&x.TryGetDouble(out var n))return n;if(x.ValueKind==JsonValueKind.String&&(double.TryParse(x.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out n)||double.TryParse(x.GetString(),out n)))return n;}return null;}
}

sealed class PrinterSketch:Control
{
 readonly System.Windows.Forms.Timer animationTimer=new(){Interval=40};
 float nozzleX=48;int direction=1,layer=0;bool isPrinting;
 int? activeAmsSlot;
 public int? ActiveAmsSlot
 {
  get=>activeAmsSlot;
  set{if(activeAmsSlot==value)return;activeAmsSlot=value;Invalidate();}
 }
 public bool IsPrinting
 {
  get=>isPrinting;
  set
  {
   if(isPrinting==value)return;
   isPrinting=value;
   if(value)animationTimer.Start();
   else{animationTimer.Stop();layer=0;nozzleX=68;direction=1;}
   Invalidate();
  }
 }
 public PrinterSketch()
 {
  DoubleBuffered=true;
  SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
  animationTimer.Tick+=(_,_)=>
  {
   nozzleX+=direction*1.2f;
   if(nozzleX>=89){nozzleX=89;direction=-1;layer=layer>=10?0:layer+1;}
   else if(nozzleX<=48){nozzleX=48;direction=1;}
   Invalidate();
  };
 }
 protected override void Dispose(bool disposing){if(disposing)animationTimer.Dispose();base.Dispose(disposing);}
 protected override void OnPaint(PaintEventArgs e)
 {
  base.OnPaint(e);
  var g=e.Graphics;
  g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

  using var outline=new Pen(UiTheme.Muted,1.5f);
  using var fine=new Pen(UiTheme.Border,1);
  using var frameFill=new SolidBrush(UiTheme.SurfaceRaised);
  using var glassFill=new SolidBrush(Color.FromArgb(UiTheme.IsDark?150:105,UiTheme.IsDark?18:100,UiTheme.IsDark?24:115,UiTheme.IsDark?31:128));
  using var insideFill=new SolidBrush(UiTheme.IsDark?Color.FromArgb(24,30,39):Color.FromArgb(211,219,227));
  using var accent=new SolidBrush(UiTheme.Accent);
  using var softAccent=new SolidBrush(Color.FromArgb(105,UiTheme.Accent));
  using var dimAccent=new Pen(Color.FromArgb(175,UiTheme.Accent),1);
  using var dark=new SolidBrush(UiTheme.IsDark?Color.FromArgb(17,21,28):Color.FromArgb(91,101,112));
  using var screen=new SolidBrush(UiTheme.IsDark?Color.FromArgb(20,27,34):Color.FromArgb(62,74,84));

  // Four AMS spools sit on top of the enclosed P1S body.
  using(var amsPath=SurfacePanel.RoundedPath(new Rectangle(24,8,83,22),6))
  {
   g.FillPath(frameFill,amsPath);g.DrawPath(outline,amsPath);
  }
  g.DrawLine(fine,28,12,103,12);
  for(var i=0;i<4;i++)
  {
   var x=35+i*18;
   var selected=ActiveAmsSlot==i+1;
   using var spoolFill=new SolidBrush(selected?Color.FromArgb(95,UiTheme.Accent):UiTheme.Surface);
   using var spoolPen=new Pen(selected?UiTheme.Accent:UiTheme.Border,selected?1.7f:1);
   g.FillEllipse(spoolFill,x,14,13,13);
   g.DrawEllipse(spoolPen,x,14,13,13);
   g.DrawEllipse(selected?dimAccent:fine,x+4,18,5,5);
  }
  using(var amsLid=SurfacePanel.RoundedPath(new Rectangle(28,5,75,7),4))
  {
   g.DrawPath(fine,amsLid);
  }

  // Enclosure, side wall, lower plinth and the front glass door.
  using(var bodyPath=SurfacePanel.RoundedPath(new Rectangle(17,31,100,111),9))
  {
   g.FillPath(frameFill,bodyPath);g.DrawPath(outline,bodyPath);
  }
  g.FillRectangle(dark,20,37,8,96);
  g.FillRectangle(dark,106,37,8,96);
  g.FillRectangle(insideFill,29,39,76,75);
  g.DrawRectangle(fine,29,39,76,75);
  g.DrawLine(outline,34,42,34,109);
  g.DrawLine(outline,100,42,100,109);
  g.DrawLine(fine,34,47,100,47);
  g.DrawLine(fine,34,108,100,108);
  using(var glassPath=SurfacePanel.RoundedPath(new Rectangle(29,39,76,75),3))
  {
   g.FillPath(glassFill,glassPath);g.DrawPath(fine,glassPath);
  }
  g.DrawLine(fine,37,51,37,101);
  g.DrawLine(fine,97,51,97,101);
  g.DrawLine(fine,40,52,95,52);

  // Build plate and the layered part grow while the print is active.
  g.DrawLine(outline,39,108,95,108);
  g.DrawLine(fine,44,111,90,111);
  if(isPrinting)
  {
   var partHeight=7+layer*2.2f;
   var top=108-partHeight;
   var part=new RectangleF(54,top,27,partHeight);
   using var partPath=SurfacePanel.RoundedPath(Rectangle.Round(part),3);
   g.FillPath(softAccent,partPath);g.DrawPath(dimAccent,partPath);
   for(var y=top+3;y<106;y+=3)
    g.DrawLine(dimAccent,56,y,79,y);
   using var brim=new Pen(Color.FromArgb(100,UiTheme.Accent),1);
   g.DrawLine(brim,49,108,86,108);
  }

  // X rail, moving toolhead, nozzle and the short filament strand.
  g.DrawLine(outline,39,48,95,48);
  g.DrawLine(fine,39,51,95,51);
  var headX=isPrinting?nozzleX:68f;
  var headY=isPrinting?Math.Max(62,81-layer*2.2f):62;
  using(var headPath=SurfacePanel.RoundedPath(new Rectangle((int)headX-7,(int)headY,14,12),3))
  {
   g.FillPath(accent,headPath);g.DrawPath(outline,headPath);
  }
  g.FillRectangle(screen,(int)headX-3,(int)headY+3,6,3);
  g.DrawLine(outline,headX,headY+12,headX,headY+17);
  g.DrawLine(isPrinting?dimAccent:fine,headX,headY+17,headX,headY+20);
  if(isPrinting)g.FillEllipse(accent,headX-1.5f,headY+19,3,2);

  // P1S front display, door handle, seams and feet.
  using(var displayPath=SurfacePanel.RoundedPath(new Rectangle(94,57,12,18),2))
  {
   g.FillPath(screen,displayPath);g.DrawPath(fine,displayPath);
  }
  g.DrawLine(fine,97,62,103,62);
  g.DrawLine(fine,97,65,101,65);
  g.DrawLine(fine,97,69,102,69);
  g.DrawLine(outline,102,83,102,98);
  g.DrawLine(fine,104,84,104,97);
  g.DrawLine(fine,24,122,110,122);
  using(var basePath=SurfacePanel.RoundedPath(new Rectangle(18,132,98,10),3))
  {
   g.FillPath(dark,basePath);g.DrawPath(outline,basePath);
  }
  g.FillRectangle(frameFill,27,141,12,4);
  g.FillRectangle(frameFill,96,141,12,4);

  if(isPrinting)g.FillEllipse(accent,107,34,5,5);
  else g.DrawEllipse(fine,107,34,5,5);
  using var caption=new Font("Segoe UI",7,FontStyle.Bold);
  using var captionBrush=new SolidBrush(UiTheme.Muted);
  const string label="BAMBU LAB  ·  P1S";
  var size=g.MeasureString(label,caption);
  g.DrawString(label,caption,captionBrush,(Width-size.Width)/2,151);
 }
}

sealed class SpeedModeSelector : Control
{
    readonly string[] options;
    int selectedIndex=-1;
    bool hovered;
    public event EventHandler? SelectedIndexChanged;
    public int SelectedIndex
    {
        get=>selectedIndex;
        set
        {
            if(value< -1||value>=options.Length||selectedIndex==value)return;
            selectedIndex=value;
            Invalidate();
        }
    }

    public SpeedModeSelector(string[] availableOptions)
    {
        options=availableOptions;
        selectedIndex=1;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
            ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
        BackColor=UiTheme.Surface;
        ForeColor=UiTheme.TextSoft;
        Font=UiTheme.Font(8,true);
        Cursor=Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if(Width<4||Height<4)return;
        e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var fill=!Enabled?UiTheme.SurfaceRaised:hovered?UiTheme.SurfaceHover:UiTheme.SurfaceRaised;
        using var path=SurfacePanel.RoundedPath(new Rectangle(0,0,Width-1,Height-1),9);
        using var background=new SolidBrush(fill);
        e.Graphics.FillPath(background,path);
        var text=selectedIndex>=0?options[selectedIndex]:(UiLocalization.IsEnglish?"Select mode":"Selecciona modo");
        using var textBrush=new SolidBrush(Enabled?ForeColor:UiTheme.Muted);
        var textSize=e.Graphics.MeasureString(text,Font);
        e.Graphics.DrawString(text,Font,textBrush,11,
            Math.Max(0,(Height-textSize.Height)/2f));
        var arrowColor=Enabled?UiTheme.Muted:UiTheme.Border;
        using var arrow=new Pen(arrowColor,1.6f)
        {
            StartCap=System.Drawing.Drawing2D.LineCap.Round,
            EndCap=System.Drawing.Drawing2D.LineCap.Round
        };
        var x=Width-17;
        var y=Height/2f;
        e.Graphics.DrawLine(arrow,x-4,y-2,x,y+2);
        e.Graphics.DrawLine(arrow,x,y+2,x+4,y-2);
    }

    protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);hovered=true;Invalidate();}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hovered=false;Invalidate();}
    protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if(e.Button==MouseButtons.Left&&Enabled)ShowOptions();
    }

    void ShowOptions()
    {
        var menu=new ContextMenuStrip
        {
            ShowImageMargin=false,
            ShowCheckMargin=false,
            BackColor=UiTheme.SurfaceRaised,
            ForeColor=UiTheme.Text,
            Renderer=new ToolStripProfessionalRenderer(new SpeedModeColorTable()),
            Padding=new Padding(4)
        };
        for(var i=0;i<options.Length;i++)
        {
            var index=i;
            var item=new ToolStripMenuItem(options[i])
            {
                Checked=i==selectedIndex,
                CheckOnClick=false,
                BackColor=i==selectedIndex?UiTheme.PanelInfo:UiTheme.SurfaceRaised,
                ForeColor=i==selectedIndex?UiTheme.Accent:UiTheme.TextSoft,
                Font=UiTheme.Font(8,true),
                Padding=new Padding(10,6,18,6)
            };
            item.Click+=(_,_) =>
            {
                if(selectedIndex==index)return;
                selectedIndex=index;
                Invalidate();
                SelectedIndexChanged?.Invoke(this,EventArgs.Empty);
            };
            menu.Items.Add(item);
        }
        menu.Closed+=(_,_)=>menu.Dispose();
        menu.Show(this,new Point(0,Height+4));
    }

    sealed class SpeedModeColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground=>UiTheme.SurfaceRaised;
        public override Color MenuBorder=>UiTheme.Border;
        public override Color MenuItemSelected=>UiTheme.SurfaceHover;
        public override Color MenuItemSelectedGradientBegin=>UiTheme.SurfaceHover;
        public override Color MenuItemSelectedGradientEnd=>UiTheme.SurfaceHover;
        public override Color MenuItemBorder=>UiTheme.Border;
        public override Color ImageMarginGradientBegin=>UiTheme.SurfaceRaised;
        public override Color ImageMarginGradientMiddle=>UiTheme.SurfaceRaised;
        public override Color ImageMarginGradientEnd=>UiTheme.SurfaceRaised;
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
sealed class CameraPictureBox:PictureBox
{
    public CameraPictureBox()
    {
        BackColor=Color.FromArgb(12,13,14);
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var image=Image;
        if(image is null||ClientSize.Width<=0||ClientSize.Height<=0)return;

        var scale=Math.Min((float)ClientSize.Width/image.Width,(float)ClientSize.Height/image.Height);
        var width=image.Width*scale;
        var height=image.Height*scale;
        var destination=new RectangleF((ClientSize.Width-width)/2,(ClientSize.Height-height)/2,width,height);
        e.Graphics.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(image,destination);
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
        g.DrawString(AppPreferences.Current.Language=="en"?"TARGET":"OBJETIVO",targetFont,targetBrush,145,43);
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
    readonly Label materialLabel=new(),spoolLabel=new(),weightLabel=new();
    readonly FilamentSpoolIcon spoolIcon=new();
    string? lastMaterial,lastSpoolName; double? lastGrams; int? lastRemaining; Color? lastColor; bool? lastPresent;
    public bool IsOccupied=>lastPresent==true;
    public FilamentCard(int index)
    {
        BackColor=UiTheme.Surface;BorderColor=UiTheme.Surface;Radius=10;
        spoolIcon.SetBounds(29,5,60,60);Controls.Add(spoolIcon);
        materialLabel.Text="Vacía";materialLabel.Font=new Font("Segoe UI",7.5f,FontStyle.Bold);materialLabel.ForeColor=UiTheme.Text;materialLabel.AutoEllipsis=true;materialLabel.TextAlign=ContentAlignment.MiddleCenter;Controls.Add(materialLabel);
        spoolLabel.Font=new Font("Segoe UI",7);spoolLabel.ForeColor=UiTheme.Muted;spoolLabel.AutoEllipsis=true;spoolLabel.TextAlign=ContentAlignment.MiddleCenter;Controls.Add(spoolLabel);
        weightLabel.Font=new Font("Segoe UI",7.5f,FontStyle.Bold);weightLabel.ForeColor=UiTheme.TextSoft;weightLabel.AutoEllipsis=true;weightLabel.TextAlign=ContentAlignment.MiddleCenter;Controls.Add(weightLabel);
        LayoutLabels();
    }
    protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(spoolIcon is not null)LayoutLabels();}
    void LayoutLabels()
    {
        var width=Math.Max(0,ClientSize.Width-8);
        spoolIcon.SetBounds((ClientSize.Width-60)/2,5,60,60);
        spoolIcon.SetBounds((ClientSize.Width-56)/2,3,56,56);
        materialLabel.SetBounds(4,59,width,15);
        spoolLabel.SetBounds(4,74,width,13);
        weightLabel.SetBounds(4,87,width,15);
    }
    public void SetInventory(string? material,string? spoolName,int? percentage,Color? color,double? grams,double? capacity)
    {
        if(material==null)
        {
            lastPresent=false;lastMaterial=null;lastSpoolName=null;lastGrams=null;
            materialLabel.Text="Vacía";spoolLabel.Text="";weightLabel.Text="";
            spoolIcon.SetInventory(false,0,UiTheme.Muted);
            return;
        }
        lastPresent=true;lastMaterial=material;lastSpoolName=spoolName;lastGrams=grams;
        materialLabel.Text=material;
        spoolLabel.Text=spoolName??"";
        weightLabel.Text=grams.HasValue?$"{grams.Value:0} g restantes":percentage.HasValue?$"{percentage.Value}% restante":"";
        spoolIcon.SetInventory(true,percentage??0,color??UiTheme.Accent);
    }
    public void SetSlot(string? material,int? remaining,Color? color,bool? present)
    {
        if(present is null)return;
        if(present==false){lastMaterial=null;lastSpoolName=null;lastGrams=null;lastRemaining=null;lastColor=null;lastPresent=false;RefreshDisplay();return;}
        lastPresent=true;
        if(!string.IsNullOrWhiteSpace(material))lastMaterial=material.Trim();
        if(remaining.HasValue)lastRemaining=remaining.Value;
        if(color.HasValue)lastColor=color.Value;
        RefreshDisplay();
    }
    void RefreshDisplay()
    {
        var material=lastPresent==false?"Vacía":lastMaterial??"Bobina detectada";
        var displayColor=lastColor??UiTheme.Accent;
        if(materialLabel.Text!=material)materialLabel.Text=material;
        spoolLabel.Text=lastSpoolName??"";
        weightLabel.Text=lastGrams.HasValue?$"{lastGrams.Value:0} g restantes":lastRemaining.HasValue?$"{lastRemaining.Value}% restante":"";
        spoolIcon.SetInventory(lastPresent==true,lastRemaining??0,displayColor);
    }
}

sealed class FilamentSpoolIcon : Control
{
    int value;
    Color ringColor=UiTheme.Accent;
    bool hasSpool;
    public void SetInventory(bool loaded,int percentage,Color color)
    {
        var next=Math.Clamp(percentage,0,100);
        if(value==next&&ringColor==color&&hasSpool==loaded)return;
        value=next;ringColor=color;hasSpool=loaded;Invalidate();
    }
    public FilamentSpoolIcon()
    {
        DoubleBuffered=true;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        BackColor=UiTheme.Surface;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var background=new SolidBrush(BackColor);
        e.Graphics.FillRectangle(background,ClientRectangle);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds=new RectangleF(3.5f,3.5f,Width-7f,Height-7f);
        using var track=new Pen(UiTheme.Border,3.8f);
        using var progress=new Pen(hasSpool?ringColor:UiTheme.Muted,3.8f){StartCap=System.Drawing.Drawing2D.LineCap.Round,EndCap=System.Drawing.Drawing2D.LineCap.Round};
        g.DrawEllipse(track,bounds);
        if(value>0)g.DrawArc(progress,bounds,-90,360*value/100f);
        var spoolBounds=new Rectangle(14,14,Width-28,Height-28);
        SpoolArt.Draw(g,hasSpool?ringColor:UiTheme.SurfaceRaised,spoolBounds,BackColor);
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
    public string DeviceVersion => (printerBox.SelectedItem as PrinterChoice)?.Version ?? "";
    public string Token { get; private set; } = "";
    public string RefreshToken { get; private set; } = "";
    public string LoginData { get; private set; } = "";
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
            RefreshToken=GetProp(doc.RootElement,"refreshToken")??"";
            LoginData=body;
            await LoadPrinters();
        }
        catch(Exception ex){Token="";RefreshToken="";LoginData="";message.Text=ex.Message;resendButton.Enabled=true;}
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
            choices.Add(new PrinterChoice(id,string.IsNullOrWhiteSpace(model)?name:$"{name} · {model}",GetProp(device,"dev_version")??""));
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
    sealed record PrinterChoice(string Id,string Label,string Version){public override string ToString()=>Label;}
}
