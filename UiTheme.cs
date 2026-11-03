using System.Drawing;
static class UiTheme
{
 public const int S4=4,S8=8,S12=12,S16=16,S24=24,S32=32;
 public static bool IsDark {get;private set;}=true;
 public static string Mode=>IsDark?"dark":"light";
 public static Color Background {get;private set;}=Color.FromArgb(16,17,19);
 public static Color Surface {get;private set;}=Color.FromArgb(32,32,32);
 public static Color SurfaceRaised {get;private set;}=Color.FromArgb(43,43,43);
 public static Color SurfaceHover {get;private set;}=Color.FromArgb(55,55,55);
 public static Color Border {get;private set;}=Color.FromArgb(59,59,59);
 public static Color Text {get;private set;}=Color.FromArgb(245,245,247);
 public static Color TextSoft {get;private set;}=Color.FromArgb(215,216,218);
 public static Color Muted {get;private set;}=Color.FromArgb(158,160,164);
 public static Color Accent {get;private set;}=Color.FromArgb(33,204,107);
 public static Color AccentHover {get;private set;}=Color.FromArgb(66,223,139);
 public static Color AccentInk {get;private set;}=Color.FromArgb(18,25,20);
 public static Color Success {get;private set;}=Color.FromArgb(107,220,151);
 public static Color Warning {get;private set;}=Color.FromArgb(255,184,61);
 public static Color Error {get;private set;}=Color.FromArgb(255,92,92);
 public static Color Cool {get;private set;}=Color.FromArgb(120,174,218);
 public static Color PanelSuccess {get;private set;}=Color.FromArgb(20,49,34);
 public static Color BorderSuccess {get;private set;}=Color.FromArgb(31,91,57);
 public static Color PanelWarning {get;private set;}=Color.FromArgb(57,43,22);
 public static Color BorderWarning {get;private set;}=Color.FromArgb(104,77,37);
 public static Color PanelError {get;private set;}=Color.FromArgb(55,27,27);
 public static Color BorderError {get;private set;}=Color.FromArgb(105,50,50);
 public static Color PanelInfo {get;private set;}=Color.FromArgb(25,39,57);
 public static Color BorderInfo {get;private set;}=Color.FromArgb(48,72,103);
 public static Color WarmNozzle {get;private set;}=Color.FromArgb(255,122,80);
 public static Color WarmBed {get;private set;}=Color.FromArgb(255,184,61);
 public static void SetMode(string? mode)
 {
  IsDark=!string.Equals(mode,"light",StringComparison.OrdinalIgnoreCase);
  if(IsDark)
  {
   Background=Color.FromArgb(16,17,19);Surface=Color.FromArgb(32,32,32);SurfaceRaised=Color.FromArgb(43,43,43);SurfaceHover=Color.FromArgb(55,55,55);Border=Color.FromArgb(59,59,59);Text=Color.FromArgb(245,245,247);TextSoft=Color.FromArgb(215,216,218);Muted=Color.FromArgb(158,160,164);Accent=Color.FromArgb(33,204,107);AccentHover=Color.FromArgb(66,223,139);AccentInk=Color.FromArgb(18,25,20);Success=Color.FromArgb(107,220,151);Warning=Color.FromArgb(255,184,61);Error=Color.FromArgb(255,92,92);Cool=Color.FromArgb(120,174,218);PanelSuccess=Color.FromArgb(20,49,34);BorderSuccess=Color.FromArgb(31,91,57);PanelWarning=Color.FromArgb(57,43,22);BorderWarning=Color.FromArgb(104,77,37);PanelError=Color.FromArgb(55,27,27);BorderError=Color.FromArgb(105,50,50);PanelInfo=Color.FromArgb(25,39,57);BorderInfo=Color.FromArgb(48,72,103);
  }
  else
  {
   Background=Color.FromArgb(242,243,245);Surface=Color.White;SurfaceRaised=Color.FromArgb(248,248,249);SurfaceHover=Color.FromArgb(232,234,237);Border=Color.FromArgb(216,218,222);Text=Color.FromArgb(29,29,31);TextSoft=Color.FromArgb(67,69,73);Muted=Color.FromArgb(112,114,119);Accent=Color.FromArgb(23,169,85);AccentHover=Color.FromArgb(31,190,98);AccentInk=Color.White;Success=Color.FromArgb(22,145,76);Warning=Color.FromArgb(193,126,21);Error=Color.FromArgb(198,53,53);Cool=Color.FromArgb(52,119,192);PanelSuccess=Color.FromArgb(229,246,235);BorderSuccess=Color.FromArgb(177,225,192);PanelWarning=Color.FromArgb(250,241,220);BorderWarning=Color.FromArgb(236,213,157);PanelError=Color.FromArgb(251,232,232);BorderError=Color.FromArgb(238,190,190);PanelInfo=Color.FromArgb(230,240,251);BorderInfo=Color.FromArgb(193,216,240);
  }
 }
 public static Font Font(float size,bool semibold=false)=>new("Segoe UI",size,semibold?FontStyle.Bold:FontStyle.Regular);
 public static void StyleButton(Button button,bool primary=false,bool quiet=false){button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderSize=0;button.FlatAppearance.MouseOverBackColor=primary?AccentHover:SurfaceHover;button.FlatAppearance.MouseDownBackColor=primary?Color.FromArgb(166,199,121):Color.FromArgb(54,68,56);button.BackColor=primary?Accent:quiet?Background:SurfaceRaised;button.ForeColor=primary?AccentInk:TextSoft;button.Font=Font(9,true);button.Cursor=Cursors.Hand;button.UseVisualStyleBackColor=false;button.FlatAppearance.BorderColor=button.BackColor;}
}

