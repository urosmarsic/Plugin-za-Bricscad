using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;

namespace Vozisce
{
    public sealed class RoadWindow : Form
    {
        Axis axis;
        readonly Settings options;
        readonly ComboBox method=new ComboBox(),side=new ComboBox(),car1=new ComboBox(),car2=new ComboBox();
        readonly Dictionary<string,NumericUpDown> numbers=new Dictionary<string,NumericUpDown>();
        readonly CheckBox automatic=new CheckBox {Text="Predlog dolžine iz naklona roba",Checked=true,AutoSize=true};
        readonly CheckBox annotate=new CheckBox {Text="Označi največjo razširitev",AutoSize=true};
        readonly CheckBox dimension=new CheckBox {Text="Kotiraj dodatno širino levo/desno",AutoSize=true};
        readonly Timer refresh=new Timer {Interval=350};
        readonly ToolTip help=new ToolTip {AutoPopDelay=20000,InitialDelay=300,ReshowDelay=100};
        readonly bool interactive;
        bool updating,live,syncingWidths;
        readonly AlignmentEditor alignmentEditor;
        TabPage resultsPage;
        public event Action<AlignmentGeometry> AxisDefinitionChanged;
        public event Action AxisDefinitionInvalid;
        public event Action PickFixedArcsRequested;
        public event Action MoveNodeRequested;
        public AlignmentDefinition AlignmentDefinition {get {return alignmentEditor.Definition;}}
        Label header;
        public event Action<Result> DrawRequested;
        public event Action<Result> ResultUpdated;
        public event Action ResultInvalidated;
        readonly DataGridView transitions=new DataGridView(),rows=new DataGridView();
        readonly TextBox notes=new TextBox {Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};
        readonly Preview preview=new Preview();
        readonly Button draw=new Button {Text="Izriši v risbo",AutoSize=true,Enabled=false};
        readonly TabControl tabs=new TabControl {Dock=DockStyle.Fill};
        readonly List<Transition> candidates;
        public Result AcceptedResult;
        Result calculated;
        public RoadWindow(Axis a,Settings initial,bool dark,bool interactive=false,IList<Transition> initialTransitions=null)
        {
            this.interactive=interactive;
            axis=a; options=initial.Copy();
            Text="Razširitev vozišča · BricsCAD"; Width=1050;Height=820;MinimumSize=new Size(880,700);
            StartPosition=FormStartPosition.CenterParent; Font=new Font("Segoe UI",9);
            header=new Label {Text="RAZŠIRITEV VOZIŠČA   /   os "+axis.Length.ToString("F2")+" m",Height=42,Dock=DockStyle.Top,Padding=new Padding(12),Font=new Font(Font.FontFamily,11,FontStyle.Bold)};
            Controls.Add(tabs);Controls.Add(header);
            var bottom=new FlowLayoutPanel {Dock=DockStyle.Bottom,Height=48,Padding=new Padding(8),FlowDirection=FlowDirection.RightToLeft};
            Controls.Add(bottom);
            var cancel=new Button {Text="Zapri",AutoSize=true};cancel.Click+=(s,e)=>Close();
            var csv=new Button {Text="Izvoz CSV …",AutoSize=true};csv.Click+=(s,e)=>Export();
            var calc=new Button {Text="Izračunaj / predogled",AutoSize=true};calc.Click+=(s,e)=>Calculate();
            draw.Click+=(s,e)=> {if(calculated!=null){AcceptedResult=calculated;if(interactive){if(DrawRequested!=null)DrawRequested(calculated);live=true;}else{DialogResult=DialogResult.OK;Close();}}};
            bottom.Controls.Add(cancel);bottom.Controls.Add(draw);bottom.Controls.Add(csv);bottom.Controls.Add(calc);
            if(interactive) {
                var moveNode=new Button {Text="Premakni vozlišče v risbi",AutoSize=true};
                moveNode.Click+=(s,e)=>{if(MoveNodeRequested!=null)MoveNodeRequested();};bottom.Controls.Add(moveNode);
            }
            var axisPage=new TabPage("1  Cestna os");tabs.TabPages.Add(axisPage);
            alignmentEditor=new AlignmentEditor(axis);axisPage.Controls.Add(alignmentEditor);
            alignmentEditor.GeometryChanged+=geometry=> {ApplyAxis(geometry.ToAxis());if(AxisDefinitionChanged!=null)AxisDefinitionChanged(geometry);};
            alignmentEditor.GeometryInvalid+=message=> {ShowError("Cestna os: "+message);if(AxisDefinitionInvalid!=null)AxisDefinitionInvalid();};
            alignmentEditor.PickFixedArcsRequested+=()=> {if(PickFixedArcsRequested!=null)PickFixedArcsRequested();};
            var widthsPage=new TabPage("2  Vozišče in vozni pasovi");tabs.TabPages.Add(widthsPage);
            var widthGrid=new TableLayoutPanel {AutoSize=true,Dock=DockStyle.Top,ColumnCount=2,Padding=new Padding(16)};
            widthGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));widthGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));widthsPage.Controls.Add(widthGrid);
            var input=new TabPage("3  Razširitev in vozila");tabs.TabPages.Add(input);
            var scroll=new Panel {Dock=DockStyle.Fill,AutoScroll=true};input.Controls.Add(scroll);
            var grid=new TableLayoutPanel {AutoSize=true,Dock=DockStyle.Top,ColumnCount=2,Padding=new Padding(16)};
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,48));grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,52));scroll.Controls.Add(grid);
            method.Items.AddRange(new object[]{"Slovenija · pravilnik 2005, 24. člen","RAS-L 1995 · javni postopek Tirol 2003"});method.SelectedIndex=(int)options.Method;
            side.Items.AddRange(new object[]{"Enostransko v notranjost krivine","Simetrično na obe strani","Enostransko na zunanjo stran"});side.SelectedIndex=(int)options.Side;
            Add(grid,"Metoda izračuna",method);Add(grid,"Razporeditev razširitve",side);
            AddNumber(widthGrid,"width","Skupna osnovna širina voznih pasov [m]",options.BaseWidth,.5,30,2);
            AddNumber(widthGrid,"laneL","Levi vozni pas glede na smer osi [m]",options.LeftWidth,.25,29.75,2);
            AddNumber(widthGrid,"laneR","Desni vozni pas glede na smer osi [m]",options.RightWidth,.25,29.75,2);
            AddNumber(widthGrid,"edgeL","Robni pas levo – dodatno [m]",options.EdgeLeft,0,5,2);
            AddNumber(widthGrid,"edgeR","Robni pas desno – dodatno [m]",options.EdgeRight,0,5,2);
            Add(widthGrid,"Urejanje širin",new Label {AutoSize=true,MaximumSize=new Size(490,0),Text="Skupna širina je vsota levega in desnega pasu. Sprememba vsote sorazmerno spremeni oba pasova. Vozilo 1 pripada levemu, vozilo 2 desnemu pasu glede na smer osi. Robna pasova sta dodatna."});
            numbers["width"].ValueChanged+=(s,e)=>SyncWidths("width");numbers["laneL"].ValueChanged+=(s,e)=>SyncWidths("laneL");numbers["laneR"].ValueChanged+=(s,e)=>SyncWidths("laneR");
            AddNumber(grid,"station","Začetna stacionaža [m]",options.StartStation,-10000000,10000000,2);
            AddNumber(grid,"speed","Projektna hitrost – podatek poročila [km/h]",options.Speed,1,150,0);
            AddNumber(grid,"step","Korak izrisa [m]",options.SampleStep,.02,5,2);
            AddNumber(grid,"slope","Največji naklon spremembe odmika roba [m/m]",options.TaperSlope,.001,.1,3);
            AddNumber(grid,"taper","Ročno določena dolžina prehoda [m]",options.ManualTaper,1,1000,2);
            automatic.Checked=options.AutomaticTaper;Add(grid,"Prehod brez prehodnice",automatic);
            annotate.Checked=options.Annotate;dimension.Checked=options.DimensionWidening;
            Add(grid,"Oznake",annotate);Add(grid,"Kote razširitve",dimension);
            car1.Items.AddRange(Vehicle.Catalog());car2.Items.AddRange(Vehicle.Catalog());
            car1.SelectedIndex=Index(options.First);car2.SelectedIndex=Index(options.Second);
            Add(grid,"Merodajno vozilo za prvi pas",car1);Add(grid,"Merodajno vozilo za drugi pas",car2);
            foreach(string key in new[]{"1","2"}) {
                Vehicle v=key=="1"?options.First:options.Second;
                AddNumber(grid,"lop"+key,"Vozilo "+key+": Lop = medosna + sprednji previs [m]",v.Lop,.1,30,2);
                AddNumber(grid,"d"+key,"Vozilo "+key+": reducirana dolžina D (RAS) [m]",v.D,0,30,2);
                AddNumber(grid,"vw"+key,"Vozilo "+key+": tipska širina [m]",v.Width,.1,5,2);
                AddNumber(grid,"vl"+key,"Vozilo "+key+": tipska dolžina [m]",v.Length,.1,40,2);
            }
            car1.SelectedIndexChanged+=(s,e)=>VehicleChanged("1",(Vehicle)car1.SelectedItem);
            car2.SelectedIndexChanged+=(s,e)=>VehicleChanged("2",(Vehicle)car2.SelectedItem);
            Add(grid,"Viri in pomen podatkov",new Label {AutoSize=true,MaximumSize=new Size(490,0),Text="Lop: slovenski pravilnik, 24. člen. D: Tirol 2003. Tipske mere: 8. člen. Predpis o delih in opremi vozil, Priloga 1, določa zakonske največje mere (običajna širina 2,55 m), ne dejanske medosne razdalje. Vrednosti lahko prilagodiš iz homologacije vozila. Različna vozila v SI in RAS so primerljivi razredi, ne ista fizična vozila."});
            const string taperHelp="Predlog določi dolžino prehoda širine brez potrjene prehodnice. Naklon i pomeni spremembo bočnega odmika roba na meter dolžine osi; ni prečni sklon vozišča ali vzdolžni naklon nivelete. Pri dvojni paraboli: L = max(10 m, 2 × Δb / i). Primer: Δb = 0,40 m na enem robu in i = 0,02 m/m (2 cm/m) dasta L = 40 m. Manjši i pomeni daljši prehod. Pri simetrični širitvi se upošteva sprememba posameznega roba. Izklopi predlog za ročni vnos dolžine. Potrjena prehodnica uporablja svoje meje. To je geometrijski projektni predlog, ne potrjeno pravilo RAS; hitrost ni argument formule. Prekratek sosednji element omeji prehod in sproži opozorilo v poročilu.";
            Add(grid,"Pomen predloga dolžine",new Label {AutoSize=true,MaximumSize=new Size(490,0),Text=taperHelp});
            help.SetToolTip(automatic,taperHelp);help.SetToolTip(numbers["slope"],taperHelp);help.SetToolTip(numbers["taper"],"Dolžina dvojne parabole v metrih; uporabi se, ko izklopiš predlog iz naklona roba.");
            numbers["slope"].Enabled=automatic.Checked;numbers["taper"].Enabled=!automatic.Checked;
            var tp=new TabPage("4  Prehodi razširitve");tabs.TabPages.Add(tp);
            candidates=Calculation.Detect(axis);
            if(initialTransitions!=null && initialTransitions.Count>0) {candidates.Clear();candidates.AddRange(initialTransitions);}
            transitions.Dock=DockStyle.Fill;transitions.AutoGenerateColumns=false;transitions.AllowUserToAddRows=true;transitions.AllowUserToDeleteRows=true;
            transitions.Columns.Add(new DataGridViewCheckBoxColumn {DataPropertyName="Confirmed",HeaderText="Uporabi",Width=65});
            foreach(var pair in new[]{new[]{"From","Začetek s [m]"},new[]{"To","Konec s [m]"},new[]{"K0","k0 [1/m]"},new[]{"K1","k1 [1/m]"},new[]{"Fit","Prileganje"}})
                transitions.Columns.Add(new DataGridViewTextBoxColumn {DataPropertyName=pair[0],HeaderText=pair[1],Width=130,ReadOnly=pair[0]=="Fit",DefaultCellStyle=new DataGridViewCellStyle {Format=pair[0].StartsWith("K")?"F7":"F3"}});
            transitions.DataSource=new System.ComponentModel.BindingList<Transition>(candidates);
            transitions.CellValueChanged+=(s,e)=>InvalidateResult();transitions.UserDeletedRow+=(s,e)=>InvalidateResult();
            transitions.DataError+=(s,e)=> {InvalidateResult();e.ThrowException=false;transitions.Rows[e.RowIndex].ErrorText="Vnesi veljavno številčno vrednost.";};
            transitions.CurrentCellDirtyStateChanged+=(s,e)=> {if(transitions.IsCurrentCellDirty) transitions.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            tp.Controls.Add(transitions);tp.Controls.Add(new Label {Dock=DockStyle.Top,Height=75,Padding=new Padding(10),Text="Samodejno prepoznani odseki so predlogi. Preglej meje in označi Uporabi. Stacionaže so relativne od začetka osi. Znano prehodnico lahko dodaš ročno: k = ±1/R, prema k = 0. S-prehodnica ima nasprotna predznaka. Geometrija izbrane osi ostane podlaga izrisa."});
            var resultPage=new TabPage("5  Rezultati in predogled");resultsPage=resultPage;tabs.TabPages.Add(resultPage);
            var split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Horizontal};resultPage.Controls.Add(split);
            split.Panel1.Controls.Add(preview);preview.Dock=DockStyle.Fill;
            rows.Dock=DockStyle.Fill;rows.ReadOnly=true;rows.AutoGenerateColumns=false;rows.AllowUserToAddRows=false;
            foreach(var pair in new[]{new[]{"Station","Stacionaža"},new[]{"Radius","R [m], 0=∞"},new[]{"Widening","Δb [m]"},new[]{"Width","Vozni pasovi [m]"},new[]{"Left","Levo [m]"},new[]{"Right","Desno [m]"},new[]{"Taper","Prehod [m]"},new[]{"Element","Element"}})
                rows.Columns.Add(new DataGridViewTextBoxColumn {DataPropertyName=pair[0],HeaderText=pair[1],Width=105,DefaultCellStyle=new DataGridViewCellStyle {Format="F3"}});
            split.Panel2.Controls.Add(rows);
            var info=new TabPage("6  Poročilo");tabs.TabPages.Add(info);info.Controls.Add(notes);
            notes.Text="Izberi parametre in izračunaj. Pri R ≤ 20 m (SI) oziroma R ≤ 30 m (RAS/Tirol) program zahteva sled vozila. Podpora: odprta 2D polilinija v WCS XY. Normativna skladnost celotnega projekta ni avtomatsko potrjena.";
            foreach(var n in numbers.Values) n.ValueChanged+=(s,e)=>InvalidateResult();
            method.SelectedIndexChanged+=(s,e)=>InvalidateResult();side.SelectedIndexChanged+=(s,e)=>InvalidateResult();automatic.CheckedChanged+=(s,e)=>InvalidateResult();
            automatic.CheckedChanged+=(s,e)=> {numbers["slope"].Enabled=automatic.Checked;numbers["taper"].Enabled=!automatic.Checked;};
            annotate.CheckedChanged+=(s,e)=>InvalidateResult();dimension.CheckedChanged+=(s,e)=>InvalidateResult();
            refresh.Tick+=(s,e)=> {refresh.Stop();Calculate(false);};
            FormClosed+=(s,e)=> {refresh.Stop();refresh.Dispose();help.Dispose();};
            if(dark) Theme(this);
        }
        public void UpdateAxis(Axis value)
        {
            alignmentEditor.SetAxis(value);ApplyAxis(value);
        }
        public void LoadAlignment(AlignmentDefinition value) {alignmentEditor.LoadDefinition(value,true);}
        void ApplyAxis(Axis value)
        {
            updating=true;
            try {
                axis=value;candidates.Clear();candidates.AddRange(Calculation.Detect(axis));
                ((System.ComponentModel.BindingList<Transition>)transitions.DataSource).ResetBindings();
                header.Text="RAZŠIRITEV VOZIŠČA   /   os "+axis.Length.ToString("F2")+" m";
            } finally {updating=false;}
            Calculate(false);
            notes.AppendText(Environment.NewLine+"Os je spremenjena; predloge prehodnic je treba ponovno pregledati.");
        }
        void SyncWidths(string changed)
        {
            if(syncingWidths)return;syncingWidths=true;
            try {
                decimal left=numbers["laneL"].Value,right=numbers["laneR"].Value,total=numbers["width"].Value;
                if(changed=="width") {left=Math.Max(.25m,Math.Min(total-.25m,Math.Round(total*left/(left+right),2)));right=total-left;}
                else {if(left+right>30){if(changed=="laneL")right=30-left;else left=30-right;}total=left+right;}
                numbers["laneL"].Value=left;numbers["laneR"].Value=right;numbers["width"].Value=total;
            } finally{syncingWidths=false;}
        }
        public void ShowError(string message) {refresh.Stop();updating=true;try {InvalidateResult();notes.Text=message;} finally {updating=false;}}
        public void PauseLive() {live=false;refresh.Stop();}
        public void EnableLive() {live=true;}
        int Index(Vehicle v) {int i=Array.FindIndex(Vehicle.Catalog(),x=>x.Name==v.Name);return i<0?0:i;}
        void Theme(Control c) {
            c.BackColor=Color.FromArgb(40,43,48);c.ForeColor=Color.FromArgb(232,234,237);
            var g=c as DataGridView;if(g!=null){g.BackgroundColor=c.BackColor;g.EnableHeadersVisualStyles=false;g.DefaultCellStyle.BackColor=c.BackColor;g.DefaultCellStyle.ForeColor=c.ForeColor;g.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(54,59,66);g.ColumnHeadersDefaultCellStyle.ForeColor=c.ForeColor;}
            foreach(Control child in c.Controls) Theme(child);
        }
        void Add(TableLayoutPanel panel,string text,Control control) {
            int row=panel.RowCount++;panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(new Label {Text=text,AutoSize=true,Margin=new Padding(0,7,12,7)},0,row);
            control.Dock=DockStyle.Top;control.Margin=new Padding(0,4,0,5);
            var combo=control as ComboBox;if(combo!=null) combo.DropDownStyle=ComboBoxStyle.DropDownList;
            panel.Controls.Add(control,1,row);
        }
        void AddNumber(TableLayoutPanel grid,string key,string label,double value,double min,double max,int decimals) {
            var n=new NumericUpDown {Minimum=(decimal)min,Maximum=(decimal)max,DecimalPlaces=decimals,Value=(decimal)Math.Max(min,Math.Min(max,value)),Increment=decimals==3?.001m:decimals==2?.05m:1};
            numbers.Add(key,n);Add(grid,label,n);
        }
        double N(string key) {return (double)numbers[key].Value;}
        void VehicleChanged(string suffix,Vehicle v) {
            numbers["lop"+suffix].Value=(decimal)v.Lop;numbers["d"+suffix].Value=(decimal)v.D;numbers["vw"+suffix].Value=(decimal)v.Width;numbers["vl"+suffix].Value=(decimal)v.Length;InvalidateResult();
        }
        void InvalidateResult() {
            calculated=null;draw.Enabled=false;preview.Data=null;preview.Invalidate();rows.DataSource=null;
            if(interactive && live && !updating) {refresh.Stop();refresh.Start();}
        }
        Vehicle ReadVehicle(string suffix,ComboBox combo) {
            var v=((Vehicle)combo.SelectedItem).Copy();v.Lop=N("lop"+suffix);v.D=N("d"+suffix);v.Width=N("vw"+suffix);v.Length=N("vl"+suffix);
            v.Source+=" Vrednosti v poročilu so uporabniški projektni vnosi / potrjene tipske vrednosti.";return v;
        }
        void Calculate() {
            Calculate(true);
        }
        void Calculate(bool showErrors) {
            refresh.Stop();
            try {
                if(alignmentEditor.HasInvalidDraft)throw new ArgumentException("Najprej popravi definicijo cestne osi.");
                if(!transitions.EndEdit()) throw new ArgumentException("Popravi neveljaven vnos v tabeli prehodnic.");
                BindingContext[transitions.DataSource].EndCurrentEdit();
                options.Method=(Method)method.SelectedIndex;options.Side=(Side)side.SelectedIndex;
                options.BaseWidth=N("width");options.EdgeLeft=N("edgeL");options.EdgeRight=N("edgeR");options.StartStation=N("station");options.Speed=N("speed");options.SampleStep=N("step");options.TaperSlope=N("slope");options.ManualTaper=N("taper");options.AutomaticTaper=automatic.Checked;
                options.LeftLaneWidth=N("laneL");
                options.First=ReadVehicle("1",car1);options.Second=ReadVehicle("2",car2);
                options.Annotate=annotate.Checked;options.DimensionWidening=dimension.Checked;
                calculated=Calculation.Run(axis,options,candidates);rows.DataSource=calculated.Rows;preview.Data=calculated;preview.Invalidate();draw.Enabled=true;
                notes.Text="Metoda: "+options.Method+Environment.NewLine+"Vozili: "+options.First.Name+" / "+options.Second.Name+Environment.NewLine+
                    "Največja razširitev: "+calculated.Rows.Max(r=>r.Widening).ToString("F3")+" m"+Environment.NewLine+string.Join(Environment.NewLine,calculated.Notes);
                if(showErrors) tabs.SelectedTab=resultsPage;
                if(interactive && live && ResultUpdated!=null) ResultUpdated(calculated);
            } catch(Exception ex) {
                updating=true;try {InvalidateResult();} finally {updating=false;}
                notes.Text=ex.Message;
                if(interactive && live && ResultInvalidated!=null) ResultInvalidated();
                if(showErrors) MessageBox.Show(this,ex.Message,"Preveri podatke",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            }
        }
        void Export() {
            if(calculated==null) {Calculate();if(calculated==null)return;}
            using(var dialog=new SaveFileDialog {Filter="CSV (*.csv)|*.csv",FileName="razsiritev-vozisca.csv"}) if(dialog.ShowDialog(this)==DialogResult.OK) Report.Write(dialog.FileName,calculated,candidates);
        }
    }
    public static class Report
    {
        static string Number(double x) {return x.ToString("0.########",CultureInfo.InvariantCulture);}
        static string Q(string x) {return "\""+x.Replace("\"","\"\"")+"\"";}
        public static void Write(string path,Result data,IList<Transition> transitions) {
            using(var w=new StreamWriter(path,false,new System.Text.UTF8Encoding(true))) {
                w.WriteLine("# Razširitev vozišča; projektni izračun; vse dolžine [m]");
                var o=data.Settings;
                w.WriteLine("# Metoda;"+o.Method+";Stran;"+o.Side+";Hitrost [km/h];"+Number(o.Speed));
                w.WriteLine("# Osnovna širina;"+Number(o.BaseWidth)+";Rob levo;"+Number(o.EdgeLeft)+";Rob desno;"+Number(o.EdgeRight));
                w.WriteLine("# Levi vozni pas;"+Number(o.LeftWidth)+";Desni vozni pas;"+Number(o.RightWidth));
                foreach(var v in new[]{o.First,o.Second}) w.WriteLine("# Vozilo;"+Q(v.Name)+";Lop;"+Number(v.Lop)+";D;"+Number(v.D)+";tipska širina;"+Number(v.Width)+";tipska dolžina;"+Number(v.Length)+";Vir;"+Q(v.Source??"Uporabnik"));
                w.WriteLine("# Začetna stacionaža;"+Number(o.StartStation)+";Korak;"+Number(o.SampleStep)+";Samodejni predlog;"+o.AutomaticTaper+";Naklon;"+Number(o.TaperSlope)+";Ročna dolžina;"+Number(o.ManualTaper));
                w.WriteLine("# Prehod širine;dvojna parabola;Oznake;"+o.Annotate+";Kote;"+o.DimensionWidening);
                foreach(var t in data.ConfirmedTransitions) w.WriteLine("# Potrjena prehodnica;"+Number(t.From)+";"+Number(t.To)+";"+Number(t.K0)+";"+Number(t.K1));
                foreach(var note in data.Notes) w.WriteLine("# "+note);
                w.WriteLine("stacionaza_m;R_m_0_inf;razsiritev_m;sirina_pasov_m;odmik_levo_m;odmik_desno_m;prehod_m;X;Y;element");
                foreach(var r in data.Rows) w.WriteLine(string.Join(";",new[]{Number(r.Station),Number(r.Radius),Number(r.Widening),Number(r.Width),Number(r.Left),Number(r.Right),Number(r.Taper),Number(r.Center.X),Number(r.Center.Y),Q(r.Element)}));
            }
        }
    }
    sealed class Preview : Control
    {
        public Result Data;
        public Preview() {DoubleBuffered=true;}
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);if(Data==null||Data.Rows.Count<2)return;
            var pts=Data.Rows.SelectMany(r=>new[]{r.LeftEdge,r.RightEdge}).ToList();
            double minx=pts.Min(p=>p.X),miny=pts.Min(p=>p.Y),maxx=pts.Max(p=>p.X),maxy=pts.Max(p=>p.Y);
            double scale=Math.Min((Width-50)/Math.Max(.1,maxx-minx),(Height-50)/Math.Max(.1,maxy-miny));
            Func<XY,PointF> map=p=>new PointF(25+(float)((p.X-minx)*scale),Height-25-(float)((p.Y-miny)*scale));
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var axis=new Pen(Color.Gray,1)) using(var left=new Pen(Color.FromArgb(50,150,245),2)) using(var right=new Pen(Color.FromArgb(235,145,60),2)) using(var baseline=new Pen(Color.Gray,1)) {
                baseline.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;
                e.Graphics.DrawLines(baseline,Data.Rows.Select(r=>map(r.Center+(r.LeftPoint-r.Center).Unit*Data.Settings.LeftWidth)).ToArray());
                e.Graphics.DrawLines(baseline,Data.Rows.Select(r=>map(r.Center-(r.LeftPoint-r.Center).Unit*Data.Settings.RightWidth)).ToArray());
                e.Graphics.DrawLines(axis,Data.Rows.Select(r=>map(r.Center)).ToArray());
                e.Graphics.DrawLines(left,Data.Rows.Select(r=>map(r.LeftEdge)).ToArray());
                e.Graphics.DrawLines(right,Data.Rows.Select(r=>map(r.RightEdge)).ToArray());
            }
            using(var brush=new SolidBrush(ForeColor)) e.Graphics.DrawString("Levo: modro · Desno: oranžno · Osnovno vozišče: črtkano · smer: začetek → konec",Font,brush,8,5);
        }
    }
}

