using System;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Vozisce
{
    public sealed class AlignmentEditor : UserControl
    {
        AlignmentDefinition definition;
        readonly DataGridView grid=new DataGridView {Dock=DockStyle.Fill,AutoGenerateColumns=false,AllowUserToAddRows=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect,MultiSelect=false};
        readonly Label status=new Label {Dock=DockStyle.Bottom,Height=52,Padding=new Padding(8)};
        readonly Timer timer=new Timer {Interval=350};
        readonly NumericUpDown x=Number(-100000000,100000000,3),y=Number(-100000000,100000000,3),heading=Number(-36000,36000,4),step=Number(.02m,5,2);
        readonly AlignmentCanvas canvas=new AlignmentCanvas();
        bool loading;
        AlignmentGeometry dragGeometry;
        public AlignmentGeometry Geometry {get;private set;}
        public bool HasInvalidDraft {get;private set;}
        public AlignmentDefinition Definition {get {return definition.Copy();}}
        public event Action<AlignmentGeometry> GeometryChanged;
        public event Action<string> GeometryInvalid;
        public event Action PickFixedArcsRequested;
        static NumericUpDown Number(decimal min,decimal max,int decimals) {return new NumericUpDown {Minimum=min,Maximum=max,DecimalPlaces=decimals,Width=110,Increment=.5m};}
        public AlignmentEditor(Axis axis)
        {
            Dock=DockStyle.Fill;
            var root=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));root.RowStyles.Add(new RowStyle(SizeType.Absolute,74));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));Controls.Add(root);
            var start=new FlowLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(5)};
            foreach(var pair in new[]{new object[]{"Začetek X",x},new object[]{"Y",y},new object[]{"Smer [°]",heading},new object[]{"Korak klotoide [m]",step}}){start.Controls.Add(new Label {Text=(string)pair[0],AutoSize=true,Margin=new Padding(3,7,3,0)});start.Controls.Add((Control)pair[1]);}
            root.Controls.Add(start,0,0);
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(5),AutoScroll=true};root.Controls.Add(buttons,0,1);
            Button(buttons,"+ Prema",()=>Add(AlignmentKind.Line));Button(buttons,"+ Krožni lok",()=>Add(AlignmentKind.Arc));Button(buttons,"+ Klotoida",()=>Add(AlignmentKind.Clothoid));
            Button(buttons,"Fiksiraj lok",Fix);Button(buttons,"Klotoida med fiksna loka",Connect);Button(buttons,"Izberi dva loka v risbi",()=> {if(PickFixedArcsRequested!=null)PickFixedArcsRequested();});
            Button(buttons,"↑",()=>MoveElement(-1));Button(buttons,"↓",()=>MoveElement(1));Button(buttons,"Odstrani",Remove);Button(buttons,"Nova os",()=>LoadDefinition(new AlignmentDefinition {X=(double)x.Value,Y=(double)y.Value,Heading=(double)heading.Value,Elements=new List<AlignmentElement>{new AlignmentElement {Kind=AlignmentKind.Line,Length=80}}},true));
            var split=new SplitContainer {Dock=DockStyle.Fill,Orientation=Orientation.Horizontal};split.Panel1.Controls.Add(grid);split.Panel2.Controls.Add(canvas);root.Controls.Add(split,0,2);
            root.Controls.Add(new Label {Dock=DockStyle.Fill,Padding=new Padding(8),Text="Prilepi ohranja priključno tangento. R: + levo, − desno; R=0 pri klotoidi pomeni premo. Vleci rdeča krajišča/točke oblike, oranžna središča ali zelene smerne točke. Po premiku se preverita zveznost in tangentnost. V risbi premikaj križne bloke na VR_OS_ROCAJI z odprtim urejevalnikom."},0,3);
            Controls.Add(status);
            grid.Columns.Add(new DataGridViewComboBoxColumn {DataPropertyName="Kind",HeaderText="Element",DataSource=new[]{new Choice<AlignmentKind>(AlignmentKind.Line,"Prema"),new Choice<AlignmentKind>(AlignmentKind.Arc,"Krožni lok"),new Choice<AlignmentKind>(AlignmentKind.Clothoid,"Klotoida")},DisplayMember="Text",ValueMember="Value",Width=115});
            grid.Columns.Add(new DataGridViewComboBoxColumn {DataPropertyName="Attachment",HeaderText="Povezava",DataSource=new[]{new Choice<Attachment>(Attachment.Append,"Prilepi"),new Choice<Attachment>(Attachment.Fixed,"Fiksno"),new Choice<Attachment>(Attachment.BetweenFixed,"Med fiksna loka")},DisplayMember="Text",ValueMember="Value",Width=130});
            foreach(var pair in new[]{new[]{"Length","L / ocena L [m]"},new[]{"Radius0","R / R0 [m]"},new[]{"Radius1","R1 [m]"},new[]{"X","Fiksni X"},new[]{"Y","Fiksni Y"},new[]{"Heading","Fiksna smer [°]"}})grid.Columns.Add(new DataGridViewTextBoxColumn {DataPropertyName=pair[0],HeaderText=pair[1],Width=100,DefaultCellStyle=new DataGridViewCellStyle {Format="0.###"}});
            grid.CellValueChanged+=(s,e)=> {if(!loading && e.RowIndex>=0 && e.RowIndex<definition.Elements.Count)definition.Elements[e.RowIndex].SourceHandle=null;Schedule();};grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.DataError+=(s,e)=> {timer.Stop();e.ThrowException=false;HasInvalidDraft=true;status.Text="Popravi neveljaven vnos v tabeli osi.";if(GeometryInvalid!=null)GeometryInvalid(status.Text);};
            grid.UserDeletedRow+=(s,e)=>Schedule();
            foreach(var n in new[]{x,y,heading,step})n.ValueChanged+=(s,e)=>Schedule();
            timer.Tick+=(s,e)=>{timer.Stop();Rebuild(true);};
            canvas.DragStarted+=()=>dragGeometry=Geometry;
            canvas.Dragged+=(grip,p)=>MoveGrip(grip,p,dragGeometry);
            canvas.DragEnded+=()=>dragGeometry=null;
            LoadDefinition(AlignmentDefinition.FromAxis(axis),false);
        }
        sealed class Choice<T> {public T Value {get;set;}public string Text {get;set;}public Choice(T value,string text){Value=value;Text=text;}}
        static void Button(Control parent,string text,Action action) {var button=new Button {Text=text,AutoSize=true};button.Click+=(s,e)=>action();parent.Controls.Add(button);}
        int Selected {get {return grid.CurrentRow==null?-1:grid.CurrentRow.Index;}}
        void Schedule() {if(loading)return;definition.X=(double)x.Value;definition.Y=(double)y.Value;definition.Heading=(double)heading.Value;definition.Step=(double)step.Value;canvas.Definition=definition;canvas.Invalidate();timer.Stop();timer.Start();}
        public void LoadDefinition(AlignmentDefinition value,bool publish)
        {
            timer.Stop();loading=true;
            try {definition=value.Copy();x.Value=(decimal)definition.X;y.Value=(decimal)definition.Y;heading.Value=(decimal)definition.Heading;step.Value=(decimal)definition.Step;grid.DataSource=new BindingList<AlignmentElement>(definition.Elements);canvas.Definition=definition;}
            finally{loading=false;}
            Rebuild(publish);
        }
        public void SetAxis(Axis axis) {LoadDefinition(AlignmentDefinition.FromAxis(axis),false);}
        public bool MoveGrip(AlignmentGrip grip,XY target,AlignmentGeometry origin=null)
        {
            try {
                var result=AlignmentEditing.Move(origin??Geometry,grip,target);
                LoadDefinition(result.Geometry.Definition,false);status.Text=result.Message;
                timer.Start();return true;
            }catch(ArgumentException ex){timer.Stop();HasInvalidDraft=true;status.Text="Premik ni potrjen: "+ex.Message;if(GeometryInvalid!=null)GeometryInvalid(status.Text);return false;}
        }
        public bool Rebuild(bool publish)
        {
            timer.Stop();
            try {
                if(!grid.EndEdit())throw new ArgumentException("Popravi vnos v tabeli osi.");BindingContext[grid.DataSource].EndCurrentEdit();
                Geometry=AlignmentBuilder.Build(definition);HasInvalidDraft=false;canvas.Geometry=Geometry;canvas.Invalidate();
                status.Text="Os: "+Geometry.Length.ToString("F3")+" m · "+Geometry.Elements.Count+" elementov · stiki G0/G1 ustrezni"+
                    (Geometry.Joints.Count>0?" · največji razmik "+Geometry.Joints.Max(j=>j.Gap).ToString("0.######")+" m, kot "+Geometry.Joints.Max(j=>j.AngleDegrees).ToString("0.######")+"°":"")+" · "+string.Join(" ",Geometry.Notes);
                if(publish && GeometryChanged!=null)GeometryChanged(Geometry);
                return true;
            }catch(Exception ex){status.Text=ex.Message;HasInvalidDraft=publish;if(publish && GeometryInvalid!=null)GeometryInvalid(ex.Message);return false;}
        }
        void Bind(int select) {loading=true;try{grid.DataSource=new BindingList<AlignmentElement>(definition.Elements);if(select>=0 && select<grid.Rows.Count)grid.CurrentCell=grid.Rows[select].Cells[0];}finally{loading=false;}Schedule();}
        void Add(AlignmentKind kind)
        {
            double k=Geometry==null?0:Geometry.Elements.Last().K1;
            var element=new AlignmentElement {Kind=kind,Length=kind==AlignmentKind.Line?80:40,Radius0=kind==AlignmentKind.Line?0:kind==AlignmentKind.Arc?(Math.Abs(k)>1e-10?1/k:100):(Math.Abs(k)>1e-10?1/k:0),Radius1=kind==AlignmentKind.Clothoid?(Math.Abs(k)<1e-10?100:0):0};
            definition.Elements.Add(element);Bind(definition.Elements.Count-1);
        }
        void Fix() {int i=Selected;if(i<0 || Geometry==null || i>=Geometry.Elements.Count)return;var e=definition.Elements[i];if(e.Kind!=AlignmentKind.Arc){status.Text="Fiksiranje je namenjeno krožnemu loku.";return;}var built=Geometry.Elements[i];e.Attachment=Attachment.Fixed;e.X=built.Start.X;e.Y=built.Start.Y;e.Heading=built.Heading*180/Math.PI;Bind(i);}
        void Connect() {int i=Selected;if(i<0 || i+1>=definition.Elements.Count){status.Text="Izberi prvi od dveh zaporednih fiksnih lokov.";return;}if(definition.Elements[i].Attachment!=Attachment.Fixed || definition.Elements[i+1].Attachment!=Attachment.Fixed || definition.Elements[i].Kind!=AlignmentKind.Arc || definition.Elements[i+1].Kind!=AlignmentKind.Arc){status.Text="Povezava zahteva dva zaporedna fiksna krožna loka.";return;}definition.Elements.Insert(i+1,new AlignmentElement {Kind=AlignmentKind.Clothoid,Attachment=Attachment.BetweenFixed,Length=40});Bind(i+1);}
        void MoveElement(int direction) {int i=Selected,j=i+direction;if(i<0 || j<0 || j>=definition.Elements.Count)return;var e=definition.Elements[i];definition.Elements.RemoveAt(i);definition.Elements.Insert(j,e);Bind(j);}
        void Remove() {int i=Selected;if(i<0)return;definition.Elements.RemoveAt(i);Bind(Math.Min(i,definition.Elements.Count-1));}
        protected override void Dispose(bool disposing) {if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
    sealed class AlignmentCanvas : Control
    {
        public AlignmentGeometry Geometry;public AlignmentDefinition Definition;
        public event Action<AlignmentGrip,XY> Dragged;
        public event Action DragStarted,DragEnded;
        readonly ToolTip toolTip=new ToolTip();
        double scale=1,minX,minY;AlignmentGrip drag;
        public AlignmentCanvas(){Dock=DockStyle.Fill;DoubleBuffered=true;BackColor=Color.White;MinimumSize=new Size(10,100);}
        PointF Map(XY p){return new PointF(20+(float)((p.X-minX)*scale),Height-20-(float)((p.Y-minY)*scale));}
        XY Unmap(Point p){return new XY(minX+(p.X-20)/scale,minY+(Height-20-p.Y)/scale);}
        List<AlignmentGrip> Handles(){return Geometry==null?new List<AlignmentGrip>():AlignmentEditing.Grips(Geometry);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var points=new List<XY>();if(Geometry!=null){for(double s=0;s<Geometry.Length;s+=Math.Max(.25,Geometry.Length/1500))points.Add(Geometry.Point(s));points.Add(Geometry.Point(Geometry.Length));}var bounds=points.Concat(Handles().Select(h=>h.Point)).Concat(Geometry==null?new List<XY>():Geometry.TangentPolygon).ToList();if(bounds.Count==0)return;if(drag==null){minX=bounds.Min(p=>p.X);minY=bounds.Min(p=>p.Y);scale=Math.Max(.00001,Math.Min(Math.Max(1,Width-40)/Math.Max(1,bounds.Max(p=>p.X)-minX),Math.Max(1,Height-40)/Math.Max(1,bounds.Max(p=>p.Y)-minY)));}e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using(var axis=new Pen(Color.RoyalBlue,2))using(var tangent=new Pen(Color.DarkGoldenrod,1)){tangent.DashStyle=System.Drawing.Drawing2D.DashStyle.Dash;if(Geometry!=null && Geometry.TangentPolygon.Count>1)e.Graphics.DrawLines(tangent,Geometry.TangentPolygon.Select(Map).ToArray());if(points.Count>1)e.Graphics.DrawLines(axis,points.Select(Map).ToArray());}
            if(Geometry!=null)for(int i=0;i<Geometry.Elements.Count;i++){var element=Geometry.Elements[i];foreach(bool end in new[]{false,true}){PointF p=Map(element.Point(end?element.Length:0));using(var brush=new SolidBrush(ForeColor))e.Graphics.DrawString((i+1)+(end?" K":" Z"),Font,brush,p.X+4,p.Y+(end?3:-16));}}
            foreach(var h in Handles()){PointF p=Map(h.Point);using(var brush=new SolidBrush(h.Kind==AlignmentGripKind.Center?Color.DarkOrange:h.Kind==AlignmentGripKind.Tangent?Color.SeaGreen:Color.IndianRed))e.Graphics.FillRectangle(brush,p.X-4,p.Y-4,8,8);}
        }
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;foreach(var h in Handles()){var p=Map(h.Point);if(Math.Abs(p.X-e.X)<9 && Math.Abs(p.Y-e.Y)<9){drag=h;Capture=true;if(DragStarted!=null)DragStarted();break;}}}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(drag!=null && Dragged!=null)Dragged(drag,Unmap(e.Location));else{var hit=Handles().FirstOrDefault(h=>Math.Abs(Map(h.Point).X-e.X)<9 && Math.Abs(Map(h.Point).Y-e.Y)<9);toolTip.SetToolTip(this,hit==null?"":hit.Label);}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);drag=null;Capture=false;if(DragEnded!=null)DragEnded();Invalidate();}
        protected override void Dispose(bool disposing){if(disposing)toolTip.Dispose();base.Dispose(disposing);}
    }
}
