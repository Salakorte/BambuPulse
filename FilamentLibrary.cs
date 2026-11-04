using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
sealed class FilamentLibrary { public List<Spool> Spools {get;set;}=new(); public string?[] Slots {get;set;}=new string?[4]; public List<PrintEntry> History {get;set;}=new(); }
sealed class Spool { public string Id {get;set;}=Guid.NewGuid().ToString("N"); public string Name {get;set;}=""; public string Material {get;set;}="PLA"; public string Brand {get;set;}=""; public string Hex {get;set;}="B2DA6F"; public double Capacity {get;set;}=1000; public double Remaining {get;set;}=1000; public string? RfidTagUid {get;set;} public string? RfidTrayUuid {get;set;} public string? RfidProfileId {get;set;} }
sealed class PrintEntry { public string Id {get;set;}=Guid.NewGuid().ToString("N"); public string Job {get;set;}="Print"; public DateTime Date {get;set;}=DateTime.Now; public string? TaskId {get;set;} public bool Pending {get;set;}=true; public List<PrintSlice> Slices {get;set;}=new(); public string?[] SlotSpoolIds {get;set;}=new string?[4]; }
sealed class PrintSlice { public int Slot {get;set;} public string? SpoolId {get;set;} public double Grams {get;set;} public string Source {get;set;}="Manual"; }
sealed class FilamentManager:Form
{
 readonly FilamentLibrary d; readonly Action save; readonly FlowLayoutPanel cards=new(); readonly ListBox history=new(); readonly List<SpoolDropdown> combos=new(); readonly Label summary=new(),pending=new(); readonly Button closeButton=new(); string? selectedSpool,selectedHistory; bool loading;
 public FilamentManager(FilamentLibrary data,Action persist)
 {
  d=data;save=persist;Text="Biblioteca de filamentos";ClientSize=new Size(960,720);FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.CenterParent;TopMost=false;ShowInTaskbar=false;BackColor=UiTheme.Background;ForeColor=UiTheme.Text;Font=new Font("Segoe UI",9);Region=new Region(SurfacePanel.RoundedPath(new Rectangle(0,0,Width-1,Height-1),18));
  var header=new SurfacePanel{Bounds=new Rectangle(16,14,928,116),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=18};Controls.Add(header);header.MouseDown+=DragWindow;
  var kicker=L("TALLER  ·  INVENTARIO",10,13,440,16);kicker.Font=UiTheme.Font(8,true);kicker.ForeColor=UiTheme.Muted;header.Controls.Add(kicker);
  var brand=L("Biblioteca de filamentos",22,31,500,28);brand.Font=UiTheme.Font(17,true);brand.ForeColor=UiTheme.Text;header.Controls.Add(brand);
  var subtitle=L("Bobinas, materiales y consumo de impresión",22,62,500,18);subtitle.ForeColor=UiTheme.Muted;header.Controls.Add(subtitle);
  foreach(var dragTarget in new Control[]{kicker,brand,subtitle})dragTarget.MouseDown+=DragWindow;
  var addTop=Primary("＋  Nueva bobina",700,18,174);addTop.Click+=(_,_)=>Add();header.Controls.Add(addTop);
  closeButton.Text="×";closeButton.Font=new Font("Segoe UI",15);closeButton.SetBounds(884,19,28,28);UiTheme.StyleButton(closeButton,quiet:true);closeButton.ForeColor=UiTheme.Muted;Round(closeButton,8);closeButton.Click+=(_,_)=>Close();header.Controls.Add(closeButton);
  var navA=Nav("BIBLIOTECA",20,84,132);var navB=Nav("RANURAS AMS",158,84,132);var navC=Nav("HISTORIAL",296,84,132);header.Controls.Add(navA);header.Controls.Add(navB);header.Controls.Add(navC);
  var host=new Panel{Bounds=new Rectangle(14,138,932,566),BackColor=UiTheme.Background};Controls.Add(host);var a=Page(host);var b=Page(host);var c=Page(host);host.Controls.Add(c);host.Controls.Add(b);host.Controls.Add(a);a.Visible=true;b.Visible=false;c.Visible=false;navA.Click+=(_,_)=>ShowPage(host,a,navA,navB,navC);navB.Click+=(_,_)=>ShowPage(host,b,navB,navA,navC);navC.Click+=(_,_)=>ShowPage(host,c,navC,navA,navB);SetNav(navA,true);
  var title=L("TUS BOBINAS",16,8,420,28);title.Font=new Font("Segoe UI",12,FontStyle.Bold);title.ForeColor=UiTheme.Text;a.Controls.Add(title);summary.SetBounds(16,39,850,24);summary.ForeColor=UiTheme.Muted;a.Controls.Add(summary);
  cards.SetBounds(12,70,900,420);cards.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;cards.AutoScroll=true;cards.WrapContents=true;cards.FlowDirection=FlowDirection.LeftToRight;cards.BackColor=UiTheme.Background;a.Controls.Add(cards);Btn(a,"Editar bobina",14,506,132,Edit);Btn(a,"Eliminar",158,506,115,Remove);a.Controls.Add(L("Selecciona una tarjeta · doble clic para editar",296,514,570,22));
  b.Controls.Add(L("ASIGNA LAS BOBINAS CARGADAS. No hace falta NFC ni RFID.",16,16,740,30));for(int i=0;i<4;i++){int n=i;var row=new SurfacePanel{Bounds=new Rectangle(16,42+i*78,900,62),BackColor=UiTheme.Surface,BorderColor=UiTheme.Border,Radius=12};row.Controls.Add(L($"AMS  ·  RANURA {i+1:00}",16,19,175,24));var cb=new SpoolDropdown{Bounds=new Rectangle(205,11,650,40),BackColor=UiTheme.SurfaceRaised};cb.SelectionChanged+=(_,id)=>{if(!loading){d.Slots[n]=id;save();}};combos.Add(cb);row.Controls.Add(cb);b.Controls.Add(row);}
  pending.SetBounds(16,14,830,30);pending.ForeColor=UiTheme.Warning;c.Controls.Add(pending);history.SetBounds(14,48,900,455);history.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left|AnchorStyles.Right;history.BackColor=UiTheme.Surface;history.ForeColor=UiTheme.Text;history.BorderStyle=BorderStyle.None;history.Font=new Font("Segoe UI",10);history.DrawMode=DrawMode.OwnerDrawFixed;history.ItemHeight=38;history.DrawItem+=(_,e)=>{if(e.Index<0)return;e.DrawBackground();var item=history.Items[e.Index];using var brush=new SolidBrush(UiTheme.Text);e.Graphics.DrawString(item?.ToString()??"",history.Font,brush,new RectangleF(e.Bounds.X+12,e.Bounds.Y+10,e.Bounds.Width-24,e.Bounds.Height-8));};history.SelectedIndexChanged+=(_,_)=>selectedHistory=history.SelectedItem is EntryItem it?it.Id:null;c.Controls.Add(history);Btn(c,"Registrar / corregir consumo",14,518,250,Manual);UiLocalization.Apply(this);RefreshData();
 }
 static Panel Page(Control host)=>new(){Bounds=new Rectangle(0,0,932,566),BackColor=UiTheme.Background};
 [DllImport("user32.dll")] static extern bool ReleaseCapture();
 [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd,int message,IntPtr wParam,IntPtr lParam);
 void DragWindow(object? sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}}
 static void Round(Control control,int radius){if(control.Width<4||control.Height<4)return;using var path=SurfacePanel.RoundedPath(new Rectangle(1,1,control.Width-3,control.Height-3),Math.Max(2,radius-1));control.Region=new Region(path);}
 static Button Nav(string text,int x,int y,int w){var button=new Button{Text=text,Bounds=new Rectangle(x,y,w,26)};UiTheme.StyleButton(button,quiet:true);button.Font=UiTheme.Font(8,true);button.ForeColor=UiTheme.Muted;button.FlatAppearance.MouseOverBackColor=UiTheme.SurfaceHover;Round(button,9);return button;}
 static Button Primary(string text,int x,int y,int w){var button=new Button{Text=text,Bounds=new Rectangle(x,y,w,40)};UiTheme.StyleButton(button,primary:true);Round(button,11);return button;}
 void ShowPage(Panel host,Panel page,Button selected,Button other1,Button other2){foreach(Control control in host.Controls)control.Visible=control==page;SetNav(selected,true);SetNav(other1,false);SetNav(other2,false);}
 void SetNav(Button button,bool active){button.BackColor=active?UiTheme.SurfaceRaised:UiTheme.Surface;button.ForeColor=active?UiTheme.Accent:UiTheme.Muted;button.FlatAppearance.MouseOverBackColor=active?UiTheme.SurfaceRaised:UiTheme.SurfaceHover;}
 static Label L(string s,int x,int y,int w,int h)=>new(){Text=UiLocalization.T(s),Bounds=new Rectangle(x,y,w,h),ForeColor=UiTheme.TextSoft,AutoEllipsis=true};
 static Button B(string s,int x,int y,int w){var button=new Button{Text=UiLocalization.T(s),Bounds=new Rectangle(x,y,w,38)};UiTheme.StyleButton(button);Round(button,11);return button;}
 void Btn(Control c,string s,int x,int y,int w,Action act){var b=B(s,x,y,w);b.Click+=(_,_)=>act();c.Controls.Add(b);}
 void RefreshData(){loading=true;cards.Controls.Clear();if(d.Spools.Count==0)cards.Controls.Add(L("Tu biblioteca está vacía. Añade una bobina para empezar.",24,28,560,44));foreach(var spool in d.Spools){var tile=new SpoolVisualCard(spool){Margin=new Padding(UiTheme.S8),Size=new Size(254,174),Selected=spool.Id==selectedSpool};tile.Click+=(_,_)=>{selectedSpool=spool.Id;foreach(Control x in cards.Controls)if(x is SpoolVisualCard card)card.Selected=card.Spool.Id==selectedSpool;};tile.DoubleClick+=(_,_)=>{selectedSpool=spool.Id;Edit();};cards.Controls.Add(tile);}summary.Text=$"{d.Spools.Count} bobinas   ·   {d.Spools.Sum(x=>x.Remaining):0} g disponibles   ·   {d.History.Count(x=>x.Pending)} consumos por revisar";for(int i=0;i<4;i++)combos[i].SetSpools(d.Spools,d.Slots[i]);history.Items.Clear();if(d.History.Count==0)history.Items.Add("Aún no hay impresiones registradas. Las completadas aparecerán aquí.");foreach(var h in d.History.OrderByDescending(x=>x.Date))history.Items.Add(new EntryItem(h.Id,$"{(h.Pending?"◷  PENDIENTE":"✓  REGISTRADA")}     {h.Date:dd/MM/yyyy HH:mm}     {h.Job}     ·     {(h.Pending?"Indica el consumo":$"{h.Slices.Sum(x=>x.Grams):0} g")}"));pending.Text=$"{d.History.Count(x=>x.Pending)} impresión(es) necesitan revisar el consumo";loading=false;save();}
 void Add(){var spool=SpoolEditor.Show(this,null);if(spool!=null){d.Spools.Add(spool);selectedSpool=spool.Id;RefreshData();}}
 void Edit(){var spool=d.Spools.FirstOrDefault(x=>x.Id==selectedSpool);if(spool==null){MessageBox.Show(this,"Selecciona primero una bobina.");return;}var result=SpoolEditor.Show(this,spool);if(result!=null){d.Spools[d.Spools.FindIndex(x=>x.Id==spool.Id)]=result;RefreshData();}}
 void Remove(){var spool=d.Spools.FirstOrDefault(x=>x.Id==selectedSpool);if(spool==null)return;if(MessageBox.Show(this,$"¿Eliminar «{spool.Name}»?","Eliminar bobina",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;d.Spools.Remove(spool);for(int i=0;i<4;i++)if(d.Slots[i]==spool.Id)d.Slots[i]=null;selectedSpool=null;RefreshData();}
 void Manual(){if(selectedHistory==null){MessageBox.Show(this,"Selecciona una impresión del historial.");return;}var entry=d.History.FirstOrDefault(x=>x.Id==selectedHistory);if(entry==null)return;var raw=Microsoft.VisualBasic.Interaction.InputBox($"Gramos usados en {entry.Job}","Consumo manual",entry.Slices.Sum(x=>x.Grams).ToString("0.0"));if(!double.TryParse(raw,out double grams)||grams<0)return;var slotText=Microsoft.VisualBasic.Interaction.InputBox("¿De qué ranura AMS salió? (1-4)","Bobina utilizada","1");if(!int.TryParse(slotText,out int slot)||slot<1||slot>4)return;var prior=entry.Slices.FirstOrDefault(x=>x.Slot==slot);if(prior!=null){var old=d.Spools.FirstOrDefault(x=>x.Id==prior.SpoolId);if(old!=null)old.Remaining=Math.Min(old.Capacity,old.Remaining+prior.Grams);entry.Slices.Remove(prior);}var id=d.Slots[slot-1];entry.Slices.Add(new PrintSlice{Slot=slot,SpoolId=id,Grams=grams,Source="Manual"});var spool=d.Spools.FirstOrDefault(x=>x.Id==id);if(spool!=null)spool.Remaining=Math.Max(0,spool.Remaining-grams);entry.Pending=false;RefreshData();}
 sealed record EntryItem(string Id,string Text){public override string ToString()=>Text;}
 sealed class SpoolEditor
 {
  public static Spool? Show(IWin32Window owner,Spool? old){var selected=ReadColor(old?.Hex??"B2DA6F");using var f=new Form{Text=old==null?"Añadir bobina":"Editar bobina",ClientSize=new Size(500,440),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,BackColor=UiTheme.Background,ForeColor=UiTheme.Text,Font=new Font("Segoe UI",9)};
   var title=L(old==null?"NUEVA BOBINA":"DATOS DE LA BOBINA",22,18,420,28);title.Font=new Font("Segoe UI",12,FontStyle.Bold);title.ForeColor=UiTheme.Text;f.Controls.Add(title);f.Controls.Add(L("Registra el material y el peso para controlar tu inventario.",22,49,440,24));
   var name=Field(f,"Nombre de la bobina",22,91,210,old?.Name??"");var mat=Field(f,"Material",258,91,210,old?.Material??"PLA");var brand=Field(f,"Marca",22,163,210,old?.Brand??"");var cap=Field(f,"Peso inicial (g)",258,163,210,(old?.Capacity??1000).ToString("0"));var rem=Field(f,"Peso restante (g)",22,235,210,(old?.Remaining??1000).ToString("0"));f.Controls.Add(L("Color",258,235,200,20));
   var colorBtn=new Button{Bounds=new Rectangle(258,259,210,43),Text="Elegir color…",TextAlign=ContentAlignment.MiddleLeft};UiTheme.StyleButton(colorBtn);Round(colorBtn,10);colorBtn.Paint+=(_,e)=>{e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;SpoolArt.Draw(e.Graphics,selected,new Rectangle(9,8,27,27),UiTheme.SurfaceRaised);};colorBtn.Click+=(_,_)=>{using var chooser=new ColorDialog{Color=selected,FullOpen=true,AnyColor=true};if(chooser.ShowDialog(f)==DialogResult.OK){selected=chooser.Color;colorBtn.Invalidate();}};f.Controls.Add(colorBtn);
   f.Controls.Add(L("La muestra del color se actualiza al elegirlo.",258,309,220,22));
   var cancel=B("Cancelar",258,351,98);cancel.DialogResult=DialogResult.Cancel;f.Controls.Add(cancel);var save=B("Guardar bobina",370,351,98);save.Click+=(_,_)=>{if(string.IsNullOrWhiteSpace(name.Text)){MessageBox.Show(f,"Escribe un nombre.");return;}if(!double.TryParse(cap.Text,out var capacity)||!double.TryParse(rem.Text,out var remaining)||capacity<=0||remaining<0){MessageBox.Show(f,"Comprueba los pesos introducidos.");return;}f.Tag=new Spool{Id=old?.Id??Guid.NewGuid().ToString("N"),Name=name.Text.Trim(),Material=mat.Text.Trim(),Brand=brand.Text.Trim(),Hex=ToHex(selected),Capacity=capacity,Remaining=Math.Min(remaining,capacity)};f.DialogResult=DialogResult.OK;f.Close();};f.Controls.Add(save);f.AcceptButton=save;f.CancelButton=cancel;return f.ShowDialog(owner)==DialogResult.OK?f.Tag as Spool:null;
  }
  static TextBox Field(Form f,string label,int x,int y,int w,string value){f.Controls.Add(L(label,x,y,w,20));var box=new TextBox{Text=value,Bounds=new Rectangle(x,y+23,w,31),BackColor=UiTheme.SurfaceRaised,ForeColor=UiTheme.Text,BorderStyle=BorderStyle.FixedSingle,Font=new Font("Segoe UI",10)};f.Controls.Add(box);return box;}
  static Color ReadColor(string hex){try{hex=hex.TrimStart('#');return Color.FromArgb(Convert.ToInt32(hex[..2],16),Convert.ToInt32(hex.Substring(2,2),16),Convert.ToInt32(hex.Substring(4,2),16));}catch{return UiTheme.Accent;}}static string ToHex(Color c)=>$"{c.R:X2}{c.G:X2}{c.B:X2}";
 }
}
sealed class SpoolDropdown:Control
{
 readonly List<Spool> spools=new(); readonly ContextMenuStrip pickerMenu; string? selectedId; bool hovered;
 public event Action<object?,string?>? SelectionChanged;
 public SpoolDropdown(){DoubleBuffered=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;Font=new Font("Segoe UI",9);pickerMenu=new ContextMenuStrip{ShowImageMargin=false,ShowCheckMargin=false,BackColor=UiTheme.Surface,ForeColor=UiTheme.TextSoft,Padding=new Padding(4),Renderer=new ToolStripProfessionalRenderer(new SpoolPickerColorTable())};MouseEnter+=(_,_)=>{hovered=true;Invalidate();};MouseLeave+=(_,_)=>{hovered=false;Invalidate();};Click+=(_,_)=>OpenPicker();}
 public void SetSpools(IEnumerable<Spool> values,string? selected){spools.Clear();spools.AddRange(values);selectedId=selected;Invalidate();}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;var bounds=new Rectangle(1,1,Width-3,Height-3);using var path=SurfacePanel.RoundedPath(bounds,9);using var fill=new SolidBrush(hovered?UiTheme.SurfaceHover:UiTheme.SurfaceRaised);using var pen=new Pen(UiTheme.Border,1);g.FillPath(fill,path);g.DrawPath(pen,path);var spool=spools.FirstOrDefault(x=>x.Id==selectedId);string label=spool==null?"Sin asignar":$"{spool.Name}   ·   {spool.Material}   ·   {spool.Remaining:0} g";if(spool!=null)SpoolArt.Draw(g,SpoolVisualCard.ColorForHex(spool.Hex),new Rectangle(12,8,22,22),UiTheme.SurfaceRaised);using var brush=new SolidBrush(spool==null?UiTheme.Muted:UiTheme.Text);using var fmt=new StringFormat{LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter};g.DrawString(label,Font,brush,new RectangleF(spool==null?14:44,0,Width-76,Height),fmt);using var arrow=new SolidBrush(UiTheme.Muted);var cx=Width-22;var cy=Height/2;Point[] triangle={new(cx-4,cy-2),new(cx+4,cy-2),new(cx,cy+3)};g.FillPolygon(arrow,triangle);}
 void OpenPicker()
 {
  pickerMenu.Items.Clear();
  AddPickerOption("Sin asignar",null,selectedId==null);
  foreach(var spool in spools)
   AddPickerOption($"{spool.Name}   ·   {spool.Material}   ·   {spool.Remaining:0} g",spool.Id,spool.Id==selectedId);
  pickerMenu.Show(this,new Point(0,Height+4));
 }
 void AddPickerOption(string label,string? id,bool selected)
 {
  var item=new ToolStripMenuItem((selected?"✓  ":"")+label)
  {
   AutoSize=false,
   Width=Math.Max(240,Width-12),
   Height=36,
   BackColor=selected?UiTheme.PanelInfo:UiTheme.Surface,
   ForeColor=selected?UiTheme.Accent:UiTheme.TextSoft,
   Font=UiTheme.Font(8,true),
   Padding=new Padding(10,4,8,4)
  };
  item.Click+=(_,_) =>
  {
   selectedId=id;
   Invalidate();
   SelectionChanged?.Invoke(this,id);
   pickerMenu.Close();
  };
  pickerMenu.Items.Add(item);
 }
 sealed class SpoolPickerColorTable : ProfessionalColorTable
 {
  public override Color ToolStripDropDownBackground=>UiTheme.Surface;
  public override Color MenuBorder=>UiTheme.Border;
  public override Color MenuItemSelected=>UiTheme.SurfaceHover;
  public override Color MenuItemSelectedGradientBegin=>UiTheme.SurfaceHover;
  public override Color MenuItemSelectedGradientEnd=>UiTheme.SurfaceHover;
  public override Color MenuItemBorder=>UiTheme.Border;
  public override Color ImageMarginGradientBegin=>UiTheme.Surface;
  public override Color ImageMarginGradientMiddle=>UiTheme.Surface;
  public override Color ImageMarginGradientEnd=>UiTheme.Surface;
 }
}
sealed class SpoolVisualCard:Control
{
 public Spool Spool{get;}public bool Selected{get=>selected;set{selected=value;Invalidate();}}bool selected;
 bool hovered;
 public SpoolVisualCard(Spool spool){Spool=spool;DoubleBuffered=true;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);Cursor=Cursors.Hand;BackColor=UiTheme.Background;MouseEnter+=(_,_)=>{hovered=true;Invalidate();};MouseLeave+=(_,_)=>{hovered=false;Invalidate();};}
 protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(Width<2||Height<2)return;using var clip=SurfacePanel.RoundedPath(new Rectangle(0,0,Width-1,Height-1),16);Region=new Region(clip);}
 protected override void OnPaint(PaintEventArgs e)
 {
  base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;var rect=new Rectangle(1,1,Width-3,Height-3);using var path=SurfacePanel.RoundedPath(rect,16);using var bg=new SolidBrush(selected?UiTheme.SurfaceRaised:hovered?UiTheme.SurfaceHover:UiTheme.Surface);using var border=new Pen(selected?UiTheme.Accent:UiTheme.Border,selected?2:1);g.FillPath(bg,path);g.DrawPath(border,path);
  var color=ColorForHex(Spool.Hex);SpoolArt.Draw(g,color,new Rectangle(14,12,44,44),selected?UiTheme.SurfaceRaised:UiTheme.Surface);using var small=new Font("Segoe UI",8,FontStyle.Bold);using var muted=new SolidBrush(UiTheme.Muted);using var white=new SolidBrush(UiTheme.Text);g.DrawString(Spool.Material.ToUpperInvariant(),small,muted,68,18);using var nameFont=new Font("Segoe UI",10,FontStyle.Bold);using var nameFormat=new StringFormat{Trimming=StringTrimming.EllipsisCharacter};g.DrawString(Spool.Name,nameFont,white,new RectangleF(16,62,Width-30,22),nameFormat);using var amountFont=new Font("Segoe UI",20,FontStyle.Bold);g.DrawString($"{Spool.Remaining:0} g",amountFont,white,16,88);using var capFont=new Font("Segoe UI",8);g.DrawString($"de {Spool.Capacity:0} g",capFont,muted,114,102);var track=new Rectangle(16,132,Width-32,7);using var trackBrush=new SolidBrush(UiTheme.Border);g.FillRound(trackBrush,track,4);var ratio=Spool.Capacity>0?(float)Math.Clamp(Spool.Remaining/Spool.Capacity,0,1):0;var width=(int)(track.Width*ratio);if(width>0){using var fill=new SolidBrush(color);g.FillRound(fill,new Rectangle(track.X,track.Y,width,track.Height),4);}if(!string.IsNullOrWhiteSpace(Spool.Brand)){using var font=new Font("Segoe UI",7);g.DrawString(Spool.Brand,font,muted,16,147);}
 }
 public static Color ColorForHex(string hex){try{hex=hex.TrimStart('#');return Color.FromArgb(Convert.ToInt32(hex[..2],16),Convert.ToInt32(hex.Substring(2,2),16),Convert.ToInt32(hex.Substring(4,2),16));}catch{return UiTheme.Accent;}}
}
static class SpoolCardGraphics{public static void FillRound(this Graphics g,Brush brush,Rectangle r,int radius){using var p=SurfacePanel.RoundedPath(r,radius);g.FillPath(brush,p);}}
static class SpoolArt
{
 public static void Draw(Graphics g,Color color,Rectangle rect,Color centerBackground)
 {
  var size=Math.Min(rect.Width,rect.Height);var x=rect.X+(rect.Width-size)/2;var y=rect.Y+(rect.Height-size)/2;var face=new Rectangle(x,y,size-1,size-1);var inset=Math.Max(2,size/9);
  using var fill=new SolidBrush(color);using var rim=new Pen(ControlPaint.Dark(color,.38f),Math.Max(1,size*.06f));using var rings=new Pen(ControlPaint.Light(color,.48f),Math.Max(1,size*.045f));
  g.FillEllipse(fill,face);g.DrawEllipse(rim,face);g.DrawEllipse(rings,Rectangle.Inflate(face,-inset,-inset));g.DrawEllipse(rim,Rectangle.Inflate(face,-inset*2,-inset*2));
  var center=new Rectangle(x+size/3,y+size/3,size/3,size/3);
  using var spokePen=new Pen(ControlPaint.Dark(color,.2f),Math.Max(1,size*.035f));
  var cx=x+size/2f;var cy=y+size/2f;var inner=size*.24f;var outer=size*.38f;
  for(var i=0;i<8;i++)
  {
   var angle=(float)(Math.PI*i/4);
   g.DrawLine(spokePen,cx+MathF.Cos(angle)*inner,cy+MathF.Sin(angle)*inner,
    cx+MathF.Cos(angle)*outer,cy+MathF.Sin(angle)*outer);
  }
  var coreInset=Math.Max(1,size/24);
  var core=Rectangle.Inflate(center,-coreInset,-coreInset);
  using var hub=new SolidBrush(ControlPaint.Light(color,.15f));
  using var hole=new SolidBrush(centerBackground);
  using var holeBorder=new Pen(ControlPaint.Dark(color,.5f),Math.Max(1,size*.04f));
  g.FillEllipse(hub,center);g.FillEllipse(hole,core);g.DrawEllipse(holeBorder,center);
 }
}


