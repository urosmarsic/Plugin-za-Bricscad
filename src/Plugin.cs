using System;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using CadApp=Bricscad.ApplicationServices.Application;
using CadDoc=Bricscad.ApplicationServices.Document;

[assembly: CommandClass(typeof(Vozisce.Commands))]
namespace Vozisce
{
    public sealed class Commands
    {
        static readonly Dictionary<CadDoc,LiveSession> sessions=new Dictionary<CadDoc,LiveSession>();
        [CommandMethod("VR_RAZSIRITEV",CommandFlags.Modal)]
        public void Widen()
        {
            var doc=CadApp.DocumentManager.MdiActiveDocument;if(doc==null)return;
            try {
                int units=Convert.ToInt32(CadApp.GetSystemVariable("INSUNITS"));
                if(units!=0 && units!=6) throw new ArgumentException("Risba mora biti v metrih (INSUNITS=6). Najprej pravilno pretvori geometrijo; sprememba nastavitve sama ne spremeni koordinat.");
                var prompt=new PromptEntityOptions("\nIzberi odprto 2D cestno os (POLYLINE ali LWPOLYLINE v metrih): ");
                prompt.SetRejectMessage("\nIzberi 2D polilinijo.");prompt.AddAllowedClass(typeof(Polyline),true);prompt.AddAllowedClass(typeof(Polyline2d),true);
                var selected=doc.Editor.GetEntity(prompt);if(selected.Status!=PromptStatus.OK)return;
                OpenEditor(doc,selected.ObjectId);
            } catch(System.Exception ex) {doc.Editor.WriteMessage("\nRazširitev ni izdelana: "+ex.Message);}
        }
        [CommandMethod("VR_OS_VOZLISCE",CommandFlags.Modal)]
        public void MoveAxisNode()
        {
            var doc=CadApp.DocumentManager.MdiActiveDocument;if(doc==null)return;
            try {
                LiveSession session;
                if(!sessions.TryGetValue(doc,out session)) {
                    var selection=new PromptEntityOptions("\nIzberi cestno os za urejanje vozlišč: ");
                    selection.SetRejectMessage("\nIzberi 2D polilinijo.");selection.AddAllowedClass(typeof(Polyline),true);selection.AddAllowedClass(typeof(Polyline2d),true);
                    var selected=doc.Editor.GetEntity(selection);if(selected.Status!=PromptStatus.OK)return;
                    OpenEditor(doc,selected.ObjectId);session=sessions[doc];
                }
                var geometry=session.CurrentDesign;
                if(geometry==null)throw new ArgumentException("Najprej popravi definicijo cestne osi v urejevalniku.");
                var picked=doc.Editor.GetPoint("\nKlikni blizu vozlišča ali značilnega ročaja osi: ");if(picked.Status!=PromptStatus.OK)return;
                var world=picked.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);var point=new XY(world.X,world.Y);
                var grip=AlignmentEditing.Grips(geometry).OrderBy(g=>(g.Point-point).Length).First();
                doc.Editor.WriteMessage("\nIzbran ročaj: "+grip.Label);
                double elevation;
                using(var tr=doc.Database.TransactionManager.StartTransaction()){elevation=PolylineData((Entity)tr.GetObject(session.AxisId,OpenMode.ForRead),tr).Elevation;tr.Commit();}
                var origin=new Point3d(grip.Point.X,grip.Point.Y,elevation).TransformBy(doc.Editor.CurrentUserCoordinateSystem.Inverse());
                var target=doc.Editor.GetPoint(new PromptPointOptions("\nNova lega vozlišča: "){UseBasePoint=true,BasePoint=origin});if(target.Status!=PromptStatus.OK)return;
                world=target.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
                session.MoveNode(geometry,grip,new XY(world.X,world.Y));
            }catch(System.Exception ex){doc.Editor.WriteMessage("\nVozlišče ni premaknjeno: "+ex.Message);}
        }
        static void OpenEditor(CadDoc doc,ObjectId axisId)
        {
                Axis axis;string snapshot;double elevation;SavedProject saved=new SavedProject();
                using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                    var p=(Entity)tr.GetObject(axisId,OpenMode.ForRead);
                    using(var tag=p.GetXDataForApplication("VOZISCE")) if(tag!=null) throw new ArgumentException("Izberi izvorno cestno os, ne ustvarjenega roba.");
                    ReadAxis(p,tr,out axis,out snapshot,out elevation);tr.Commit();
                }
                using(var tr=doc.Database.TransactionManager.StartTransaction()) {saved=LoadProject(doc.Database,tr,axisId.Handle.ToString());tr.Commit();}
                bool dark=true;
                try {dark=Convert.ToInt32(CadApp.GetSystemVariable("COLORTHEME"))==0;} catch { }
                LiveSession previous;if(sessions.TryGetValue(doc,out previous)) previous.Close();
                var form=new RoadWindow(axis,saved.Settings,dark,true,saved.Snapshot==snapshot?saved.Transitions:null);
                var session=new LiveSession(doc,axisId,form,snapshot);
                sessions[doc]=session;
                if(saved.Snapshot!=null) form.EnableLive();
                CadApp.ShowModelessDialog(form);
                doc.Editor.WriteMessage("\nUrejaj os v risbi; po koncu ukaza se robovi posodobijo. Spremembe parametrov zamenjajo prejšnji izris iste osi. Izvorna os ostane.");
        }
        [CommandMethod("VR_OS",CommandFlags.Modal)]
        public void NewAxis()
        {
            var doc=CadApp.DocumentManager.MdiActiveDocument;if(doc==null)return;
            try {
                int units=Convert.ToInt32(CadApp.GetSystemVariable("INSUNITS"));if(units!=0 && units!=6)throw new ArgumentException("Risba mora biti v metrih.");
                var start=doc.Editor.GetPoint("\nZačetna točka nove cestne osi: ");if(start.Status!=PromptStatus.OK)return;
                var direction=doc.Editor.GetPoint(new PromptPointOptions("\nTočka za smer in dolžino prve preme: ") {UseBasePoint=true,BasePoint=start.Value});if(direction.Status!=PromptStatus.OK)return;
                var worldStart=start.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);var worldEnd=direction.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);
                if(Math.Abs(worldEnd.Z-worldStart.Z)>1e-6)throw new ArgumentException("Točki morata biti v isti ravnini WCS XY.");
                XY p=new XY(worldStart.X,worldStart.Y),delta=new XY(worldEnd.X-worldStart.X,worldEnd.Y-worldStart.Y);
                var definition=new AlignmentDefinition {X=p.X,Y=p.Y,Heading=Math.Atan2(delta.Y,delta.X)*180/Math.PI,Elements=new List<AlignmentElement>{new AlignmentElement {Kind=AlignmentKind.Line,Length=delta.Length}}};
                var geometry=AlignmentBuilder.Build(definition);ObjectId axisId;
                using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                    Layer(doc.Database,tr,"VR_CESTNA_OS",3);var space=(BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId,OpenMode.ForWrite);
                    var source=new Polyline2d {Elevation=worldStart.Z,Layer="VR_CESTNA_OS"};axisId=space.AppendEntity(source);tr.AddNewlyCreatedDBObject(source,true);
                    ApplyAlignment(source,tr,geometry);tr.Commit();
                }
                OpenEditor(doc,axisId);
            }catch(System.Exception ex){doc.Editor.WriteMessage("\nOs ni izdelana: "+ex.Message);}
        }
        [CommandMethod("VR_OS_LOKA",CommandFlags.Modal)]
        public void PickArcs()
        {
            var doc=CadApp.DocumentManager.MdiActiveDocument;LiveSession session;
            if(doc==null || !sessions.TryGetValue(doc,out session))return;
            try {
                var rows=new List<AlignmentElement>();var ids=new List<ObjectId>();double elevation;
                using(var tr=doc.Database.TransactionManager.StartTransaction()){elevation=PolylineData((Entity)tr.GetObject(session.AxisId,OpenMode.ForRead),tr).Elevation;tr.Commit();}
                for(int i=0;i<2;i++) {
                    var prompt=new PromptEntityOptions("\nIzberi "+(i+1)+". fiksni krožni lok (ARC ali 2D polilinija z enim lokom): ");prompt.SetRejectMessage("\nIzberi ARC ali polilinijo z enim krožnim lokom.");prompt.AddAllowedClass(typeof(Arc),true);prompt.AddAllowedClass(typeof(Polyline),true);prompt.AddAllowedClass(typeof(Polyline2d),true);
                    var selected=doc.Editor.GetEntity(prompt);if(selected.Status!=PromptStatus.OK)return;
                    if(selected.ObjectId==session.AxisId || ids.Contains(selected.ObjectId))throw new ArgumentException("Izberi dva različna loka, ki nista urejana cestna os.");
                    var choice=new PromptKeywordOptions("\nSmer loka [Naprej/Obrni] <Naprej>: ");choice.Keywords.Add("Naprej");choice.Keywords.Add("Obrni");choice.AllowNone=true;choice.Keywords.Default="Naprej";
                    var answer=doc.Editor.GetKeywords(choice);if(answer.Status!=PromptStatus.OK && answer.Status!=PromptStatus.None)return;
                    using(var tr=doc.Database.TransactionManager.StartTransaction()){rows.Add(FixedArc((Entity)tr.GetObject(selected.ObjectId,OpenMode.ForRead),tr,answer.StringResult=="Obrni",elevation));tr.Commit();}ids.Add(selected.ObjectId);
                }
                var definition=new AlignmentDefinition {X=rows[0].X,Y=rows[0].Y,Heading=rows[0].Heading,Elements=new List<AlignmentElement>{rows[0],new AlignmentElement {Kind=AlignmentKind.Clothoid,Attachment=Attachment.BetweenFixed,Length=40},rows[1]}};
                session.LoadAlignment(definition);
            }catch(System.Exception ex){doc.Editor.WriteMessage("\nPovezava ni izdelana: "+ex.Message);}
        }
        static AlignmentElement FixedArc(Entity entity,Transaction tr,bool reverse,double elevation)
        {
            XY start;double heading,length,radius;
            var arc=entity as Arc;
            if(arc!=null) {
                if((arc.Normal-Vector3d.ZAxis).Length>1e-8 || Math.Abs(arc.StartPoint.Z-elevation)>1e-6)throw new ArgumentException("Loka morata biti v isti ravnini WCS XY kot os.");
                radius=arc.Radius;length=arc.TotalAngle*radius;var point=reverse?arc.EndPoint:arc.StartPoint;
                start=new XY(point.X,point.Y);heading=Math.Atan2(point.Y-arc.Center.Y,point.X-arc.Center.X)+Math.PI/2;
            } else {
                var p=PolylineData(entity,tr);if(p.Points.Count!=2 || p.Closed || (p.Normal-Vector3d.ZAxis).Length>1e-8 || Math.Abs(p.Elevation-elevation)>1e-6)throw new ArgumentException("Polilinija mora vsebovati en krožni lok v ravnini osi.");
                var segment=new Segment(p.Points[0],p.Points[1],p.Bulges[0],0);
                if(!segment.Arc)throw new ArgumentException("Izbrana polilinija je prema.");radius=1/segment.K;length=segment.L;start=reverse?segment.B:segment.A;
                var tangent=segment.Tangent(reverse?segment.L:0);heading=Math.Atan2(tangent.Y,tangent.X);
            }
            if(reverse){heading+=Math.PI;radius=-radius;}
            return new AlignmentElement {Kind=AlignmentKind.Arc,Attachment=Attachment.Fixed,X=start.X,Y=start.Y,Heading=heading*180/Math.PI,Radius0=radius,Length=length,SourceHandle=entity.Handle.ToString(),SourceReversed=reverse};
        }
        sealed class AxisPolylineData
        {
            public List<XY> Points=new List<XY>();public List<double> Bulges=new List<double>();
            public double Elevation;public bool Closed;public Vector3d Normal;
        }
        static AxisPolylineData PolylineData(Entity entity,Transaction tr)
        {
            var data=new AxisPolylineData();var light=entity as Polyline;var classic=entity as Polyline2d;
            if(light!=null) {
                data.Elevation=light.Elevation;data.Normal=light.Normal;data.Closed=light.Closed;
                for(int i=0;i<light.NumberOfVertices;i++){var point=light.GetPoint2dAt(i);data.Points.Add(new XY(point.X,point.Y));if(i<light.NumberOfVertices-1)data.Bulges.Add(light.GetBulgeAt(i));}
            } else if(classic!=null) {
                if(classic.PolyType!=Poly2dType.SimplePoly)throw new ArgumentException("2D POLYLINE mora biti tipa Simple; Fit/Spline najprej pretvori v preme in krožne loke.");
                data.Elevation=classic.Elevation;data.Normal=classic.Normal;data.Closed=classic.Closed;
                foreach(ObjectId id in classic){var vertex=(Vertex2d)tr.GetObject(id,OpenMode.ForRead);data.Points.Add(new XY(vertex.Position.X,vertex.Position.Y));data.Bulges.Add(vertex.Bulge);}
                if(data.Bulges.Count>0)data.Bulges.RemoveAt(data.Bulges.Count-1);
            } else throw new ArgumentException("Izberi POLYLINE 2D ali LWPOLYLINE; 3D polilinija ni podprta.");
            return data;
        }
        static string Snapshot(Entity entity,Transaction tr)
        {
            var p=PolylineData(entity,tr);var text=new System.Text.StringBuilder(p.Elevation.ToString("R",CultureInfo.InvariantCulture));
            for(int i=0;i<p.Points.Count;i++){var point=p.Points[i];text.Append("|").Append(point.X.ToString("R",CultureInfo.InvariantCulture)).Append(",").Append(point.Y.ToString("R",CultureInfo.InvariantCulture));if(i<p.Bulges.Count)text.Append(",").Append(p.Bulges[i].ToString("R",CultureInfo.InvariantCulture));}return text.ToString();
        }
        static void ApplyAlignment(Entity source,Transaction tr,AlignmentGeometry geometry)
        {
            if(!source.IsWriteEnabled)source.UpgradeOpen();var light=source as Polyline;var classic=source as Polyline2d;
            if(light!=null) {
                light.Closed=false;light.ConstantWidth=0;for(int i=light.NumberOfVertices-1;i>=0;i--)light.RemoveVertexAt(i);
                for(int i=0;i<geometry.Vertices.Count;i++){var v=geometry.Vertices[i];light.AddVertexAt(i,new Point2d(v.Point.X,v.Point.Y),v.Bulge,0,0);}
            } else if(classic!=null) {
                classic.Closed=false;classic.DefaultStartWidth=0;classic.DefaultEndWidth=0;
                var old=classic.Cast<ObjectId>().ToList();foreach(var id in old)tr.GetObject(id,OpenMode.ForWrite).Erase();
                foreach(var v in geometry.Vertices){var vertex=new Vertex2d {Position=new Point3d(v.Point.X,v.Point.Y,0),Bulge=v.Bulge,StartWidth=0,EndWidth=0};classic.AppendVertex(vertex);tr.AddNewlyCreatedDBObject(vertex,true);}
                classic.RecordGraphicsModified(true);
            } else throw new ArgumentException("Os mora biti 2D polilinija.");
            if(source.ExtensionDictionary.IsNull)source.CreateExtensionDictionary();
            var dictionary=(DBDictionary)tr.GetObject(source.ExtensionDictionary,OpenMode.ForWrite);
            string content=Snapshot(source,tr)+"\n#ALIGNMENT_XML\n";
            using(var writer=new System.IO.StringWriter(CultureInfo.InvariantCulture)){new System.Xml.Serialization.XmlSerializer(typeof(AlignmentDefinition)).Serialize(writer,geometry.Definition);content+=writer.ToString();}
            var values=new List<TypedValue>();for(int i=0;i<content.Length;i+=200)values.Add(new TypedValue((int)DxfCode.Text,content.Substring(i,Math.Min(200,content.Length-i))));
            Xrecord record;if(dictionary.Contains("VR_ALIGNMENT"))record=(Xrecord)tr.GetObject(dictionary.GetAt("VR_ALIGNMENT"),OpenMode.ForWrite);else{record=new Xrecord();dictionary.SetAt("VR_ALIGNMENT",record);tr.AddNewlyCreatedDBObject(record,true);}record.Data=new ResultBuffer(values.ToArray());
            AxisHelpers(source,tr,geometry);
        }
        static void AxisHelpers(Entity source,Transaction tr,AlignmentGeometry geometry)
        {
            var db=source.Database;double elevation=PolylineData(source,tr).Elevation;var space=(BlockTableRecord)tr.GetObject(source.OwnerId,OpenMode.ForWrite);string handle=source.Handle.ToString();
            var apps=(RegAppTable)tr.GetObject(db.RegAppTableId,OpenMode.ForRead);
            if(!apps.Has("VOZISCE_OS")){if(!apps.IsWriteEnabled)apps.UpgradeOpen();var app=new RegAppTableRecord {Name="VOZISCE_OS"};apps.Add(app);tr.AddNewlyCreatedDBObject(app,true);}
            foreach(ObjectId id in space){var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;if(entity==null)continue;using(var tag=entity.GetXDataForApplication("VOZISCE_OS")){if(tag==null)continue;var values=tag.AsArray();if(values.Length>1 && Convert.ToString(values[1].Value)==handle){entity.UpgradeOpen();entity.Erase();}}}
            Layer(db,tr,"VR_TANGENTNI_POLIGON",2);Layer(db,tr,"VR_OS_KRAJISCA",7);
            var polygon=new Polyline {Elevation=elevation,Layer="VR_TANGENTNI_POLIGON"};
            for(int i=0;i<geometry.TangentPolygon.Count;i++){var p=geometry.TangentPolygon[i];polygon.AddVertexAt(i,new Point2d(p.X,p.Y),0,0,0);}AddAxisEntity(space,tr,polygon,handle);
            for(int i=0;i<geometry.Elements.Count;i++) {
                var element=geometry.Elements[i];foreach(bool end in new[]{false,true}) {
                    double local=end?element.Length:0;XY point=element.Point(local),normal=element.Tangent(local).Normal;XY label=point+normal*(end?-1:1);
                    var circle=new Circle(new Point3d(point.X,point.Y,elevation),Vector3d.ZAxis,.3){Layer="VR_OS_KRAJISCA"};AddAxisEntity(space,tr,circle,handle);
                    string kind=element.Kind==AlignmentKind.Line?"P":element.Kind==AlignmentKind.Arc?"L":"K";
                    var text=new DBText {Position=new Point3d(label.X,label.Y,elevation),Height=.45,Layer="VR_OS_KRAJISCA",TextString=kind+(i+1)+(end?" K":" Z")+" s="+(element.Station+local).ToString("F3",CultureInfo.InvariantCulture)};AddAxisEntity(space,tr,text,handle);
                }
            }
            Layer(db,tr,"VR_OS_ROCAJI",6);
            var block=GripBlock(db,tr);
            foreach(var grip in AlignmentEditing.Grips(geometry)) {
                var marker=new BlockReference(new Point3d(grip.Point.X,grip.Point.Y,elevation),block){Layer="VR_OS_ROCAJI"};
                marker.XData=new ResultBuffer(new TypedValue(1001,"VOZISCE_OS"),new TypedValue(1000,handle),new TypedValue(1070,(short)grip.Element),new TypedValue(1000,grip.Kind.ToString()),new TypedValue(1040,grip.Fraction));
                space.AppendEntity(marker);tr.AddNewlyCreatedDBObject(marker,true);
            }
        }
        static ObjectId GripBlock(Database db,Transaction tr)
        {
            var table=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);const string name="VR_OS_ROCAJ";
            if(table.Has(name))return table[name];if(!table.IsWriteEnabled)table.UpgradeOpen();
            var block=new BlockTableRecord {Name=name};ObjectId id=table.Add(block);tr.AddNewlyCreatedDBObject(block,true);
            foreach(var entity in new Entity[]{new Line(new Point3d(-.6,0,0),new Point3d(.6,0,0)),new Line(new Point3d(0,-.6,0),new Point3d(0,.6,0)),new Circle(Point3d.Origin,Vector3d.ZAxis,.4)}){block.AppendEntity(entity);tr.AddNewlyCreatedDBObject(entity,true);}
            return id;
        }
        static bool ReadGrip(Entity entity,out string handle,out AlignmentGrip grip)
        {
            handle=null;grip=null;var marker=entity as BlockReference;if(marker==null)return false;
            using(var buffer=entity.GetXDataForApplication("VOZISCE_OS")) {
                if(buffer==null)return false;var values=buffer.AsArray();if(values.Length!=5)return false;
                AlignmentGripKind kind;if(!Enum.TryParse(Convert.ToString(values[3].Value),out kind))return false;
                handle=Convert.ToString(values[1].Value);grip=new AlignmentGrip {Element=Convert.ToInt32(values[2].Value),Kind=kind,Fraction=Convert.ToDouble(values[4].Value),Point=new XY(marker.Position.X,marker.Position.Y)};return true;
            }
        }
        static void AddAxisEntity(BlockTableRecord space,Transaction tr,Entity entity,string handle){entity.XData=new ResultBuffer(new TypedValue(1001,"VOZISCE_OS"),new TypedValue(1000,handle));space.AppendEntity(entity);tr.AddNewlyCreatedDBObject(entity,true);}
        static void Replace(CadDoc doc,ObjectId axisId,Result data)
        {
            using(doc.LockDocument())
            using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                string handle=axisId.Handle.ToString();
                if(data==null) {RemovePrevious(doc.Database,tr,handle);tr.Commit();doc.Editor.Regen();return;}
                var source=(Entity)tr.GetObject(axisId,OpenMode.ForRead);
                Axis axis;string snapshot;double elevation;ReadAxis(source,tr,out axis,out snapshot,out elevation);
                int units=Convert.ToInt32(CadApp.GetSystemVariable("INSUNITS"));
                if(units!=0 && units!=6) throw new ArgumentException("Risba mora biti v metrih.");
                data=Calculation.Run(axis,data.Settings,data.ConfirmedTransitions);
                var space=(BlockTableRecord)tr.GetObject(source.OwnerId,OpenMode.ForWrite);
                string run=Guid.NewGuid().ToString("N");
                Register(doc.Database,tr);
                Layer(doc.Database,tr,"VR_VOZISCE_LEVO",5);Layer(doc.Database,tr,"VR_VOZISCE_DESNO",30);
                Layer(doc.Database,tr,"VR_ROBNI_PAS",8);Layer(doc.Database,tr,"VR_OZNAKE",7);
                BaselineLayer(doc.Database,tr);
                if(axis.Design!=null)AxisHelpers(source,tr,axis.Design);
                else if(!axis.ChordApproximation)AxisHelpers(source,tr,AlignmentBuilder.Build(AlignmentDefinition.FromAxis(axis)));
                RemovePrevious(doc.Database,tr,handle);
                DrawBaseline(space,tr,axis,data,true,elevation,run,handle);
                DrawBaseline(space,tr,axis,data,false,elevation,run,handle);
                Draw(space,tr,axis,data,true,false,elevation,"VR_VOZISCE_LEVO",run,handle);
                Draw(space,tr,axis,data,false,false,elevation,"VR_VOZISCE_DESNO",run,handle);
                if(data.Settings.EdgeLeft>0) Draw(space,tr,axis,data,true,true,elevation,"VR_ROBNI_PAS",run,handle);
                if(data.Settings.EdgeRight>0) Draw(space,tr,axis,data,false,true,elevation,"VR_ROBNI_PAS",run,handle);
                var largest=data.Rows.OrderByDescending(r=>r.Widening).First();
                if(data.Settings.Annotate && largest.Widening>1e-9) {
                    var label=new DBText {Position=new Point3d(largest.LeftEdge.X,largest.LeftEdge.Y,elevation),Height=.7,Layer="VR_OZNAKE",TextString="s="+largest.Station.ToString("F2")+"  Δb="+largest.Widening.ToString("F2")+" m  "+data.Settings.Method};
                    label.XData=Tag(run,handle);space.AppendEntity(label);tr.AddNewlyCreatedDBObject(label,true);
                }
                if(data.Settings.DimensionWidening) Dimensions(space,tr,largest,data.Settings,elevation,run,handle);
                SaveRun(doc.Database,tr,run,data,handle,snapshot);
                tr.Commit();
                doc.Editor.Regen();
            }
        }
        [CommandMethod("VR_POMOC")]
        public void Help() {
            CadApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\nVR_OS – nova cestna os; VR_RAZSIRITEV – urejanje osi in širin; VR_OS_LOKA – izbor dveh fiksnih lokov v odprtem urejevalniku; VR_PRIMER – testna os.\nOdprta POLYLINE 2D ali LWPOLYLINE v WCS XY, dolžine v metrih. Prema, krožni lok in klotoida; tangentni poligon in oznake krajišč. SI 2005 / RAS-L prek Tirol 2003. Dokumentacija: README.md ob projektu.");
        }
        [CommandMethod("VR_PRIMER",CommandFlags.Modal)]
        public void Example() {
            var doc=CadApp.DocumentManager.MdiActiveDocument;
            using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                var space=(BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId,OpenMode.ForWrite);
                Layer(doc.Database,tr,"VR_PRIMER_OS",3);
                var p=new Polyline2d {Layer="VR_PRIMER_OS"};space.AppendEntity(p);tr.AddNewlyCreatedDBObject(p,true);
                var definition=new AlignmentDefinition {Elements=new List<AlignmentElement>{new AlignmentElement {Kind=AlignmentKind.Line,Length=80},new AlignmentElement {Kind=AlignmentKind.Arc,Length=Math.PI*50,Radius0=100},new AlignmentElement {Kind=AlignmentKind.Line,Length=100}}};
                ApplyAlignment(p,tr,AlignmentBuilder.Build(definition));tr.Commit();
            }
            doc.Editor.WriteMessage("\nTestna os R=100 m na sloju VR_PRIMER_OS. Za SI tovornjak+avto pričakuj Δb=0,400 m; vlačilec+vlačilec Δb=1,000 m.");
        }
        static void ReadAxis(Entity source,Transaction tr,out Axis axis,out string snapshot,out double elevation)
        {
            var p=PolylineData(source,tr);
            if(p.Closed) throw new ArgumentException("Zaprta os ni podprta; razdeli polilinijo.");
            if((p.Normal-Vector3d.ZAxis).Length>1e-8) throw new ArgumentException("Os mora ležati v ravnini WCS XY (normala +Z).");
            var points=new List<XY>();var bulges=new List<double>();var text=new System.Text.StringBuilder();
            elevation=p.Elevation;text.Append(elevation.ToString("R",CultureInfo.InvariantCulture));
            for(int i=0;i<p.Points.Count;i++) {
                var xy=p.Points[i];points.Add(xy);
                text.Append("|").Append(xy.X.ToString("R",CultureInfo.InvariantCulture)).Append(",").Append(xy.Y.ToString("R",CultureInfo.InvariantCulture));
                if(i<p.Bulges.Count) {double b=p.Bulges[i];bulges.Add(b);text.Append(",").Append(b.ToString("R",CultureInfo.InvariantCulture));}
            }
            snapshot=text.ToString();
            if(!source.ExtensionDictionary.IsNull) {
                var dictionary=(DBDictionary)tr.GetObject(source.ExtensionDictionary,OpenMode.ForRead);
                if(dictionary.Contains("VR_ALIGNMENT")) {
                    string content=RecordText((Xrecord)tr.GetObject(dictionary.GetAt("VR_ALIGNMENT"),OpenMode.ForRead));
                    string prefix=snapshot+"\n#ALIGNMENT_XML\n";
                    if(content.StartsWith(prefix,StringComparison.Ordinal)) {
                        using(var reader=new System.IO.StringReader(content.Substring(prefix.Length))) {
                            var definition=(AlignmentDefinition)new System.Xml.Serialization.XmlSerializer(typeof(AlignmentDefinition)).Deserialize(reader);
                            axis=AlignmentBuilder.Build(definition).ToAxis();return;
                        }
                    }
                }
            }
            axis=new Axis(points,bulges);
        }
        static void Layer(Database db,Transaction tr,string name,short color) {
            var table=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);
            if(table.Has(name)) {
                var existing=(LayerTableRecord)tr.GetObject(table[name],OpenMode.ForRead);
                if(existing.IsLocked||existing.IsFrozen||existing.IsOff) throw new InvalidOperationException("Sloj "+name+" je zaklenjen, zamrznjen ali izključen.");return;
            }
            table.UpgradeOpen();var layer=new LayerTableRecord {Name=name,Color=Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci,color)};
            table.Add(layer);tr.AddNewlyCreatedDBObject(layer,true);
        }
        static void Register(Database db,Transaction tr) {
            var table=(RegAppTable)tr.GetObject(db.RegAppTableId,OpenMode.ForRead);if(table.Has("VOZISCE"))return;
            table.UpgradeOpen();var item=new RegAppTableRecord {Name="VOZISCE"};table.Add(item);tr.AddNewlyCreatedDBObject(item,true);
        }
        static ResultBuffer Tag(string run,string handle) {return new ResultBuffer(new TypedValue(1001,"VOZISCE"),new TypedValue(1000,run),new TypedValue(1000,handle));}
        static void Draw(BlockTableRecord space,Transaction tr,Axis axis,Result data,bool left,bool edge,double z,string layer,string run,string handle) {
            DrawVertices(space,tr,Calculation.EdgeGeometry(axis,data,left,edge),z,layer,run,handle);
        }
        static void DrawVertices(BlockTableRecord space,Transaction tr,List<EdgeVertex> vertices,double z,string layer,string run,string handle) {
            var p=new Polyline();p.Elevation=z;p.Layer=layer;
            for(int i=0;i<vertices.Count;i++) {
                var vertex=vertices[i];p.AddVertexAt(i,new Point2d(vertex.Point.X,vertex.Point.Y),vertex.Bulge,0,0);
            }
            p.Linetype="ByLayer";p.Plinegen=true;
            p.XData=Tag(run,handle);space.AppendEntity(p);tr.AddNewlyCreatedDBObject(p,true);
        }
        static void BaselineLayer(Database db,Transaction tr) {
            const string type="VR_CRTKANO";
            var types=(LinetypeTable)tr.GetObject(db.LinetypeTableId,OpenMode.ForRead);
            if(!types.Has(type)) {
                types.UpgradeOpen();
                var record=new LinetypeTableRecord {Name=type,AsciiDescription="Osnovno vozišče: črta 1 m, presledek 0,5 m",PatternLength=1.5,NumDashes=2};
                record.SetDashLengthAt(0,1);record.SetDashLengthAt(1,-.5);types.Add(record);tr.AddNewlyCreatedDBObject(record,true);
            }
            Layer(db,tr,"VR_OSNOVNO_VOZISCE",8);
            var layers=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);
            var layer=(LayerTableRecord)tr.GetObject(layers["VR_OSNOVNO_VOZISCE"],OpenMode.ForWrite);
            layer.LinetypeObjectId=types[type];
        }
        static void DrawBaseline(BlockTableRecord space,Transaction tr,Axis axis,Result data,bool left,double z,string run,string handle) {
            DrawVertices(space,tr,Calculation.EdgeGeometry(axis,data,left,false,true),z,"VR_OSNOVNO_VOZISCE",run,handle);
        }
        static void Dimensions(BlockTableRecord space,Transaction tr,Row row,Settings settings,double z,string run,string handle)
        {
            XY n=(row.LeftPoint-row.Center).Unit;
            for(int side=0;side<2;side++) {
                double sign=side==0?1:-1;
                double baseWidth=side==0?settings.LeftWidth:settings.RightWidth;
                double delta=(side==0?row.Left:row.Right)-baseWidth;
                if(delta<1e-9) continue;
                XY a=row.Center+n*(sign*baseWidth),b=side==0?row.LeftPoint:row.RightPoint;
                XY mid=(a+b)*.5+n.Normal*2;
                var dim=new AlignedDimension(new Point3d(a.X,a.Y,z),new Point3d(b.X,b.Y,z),new Point3d(mid.X,mid.Y,z),
                    "Δb"+(side==0?"L":"D")+" = "+delta.ToString("F3",CultureInfo.InvariantCulture)+" m",space.Database.Dimstyle);
                dim.Layer="VR_OZNAKE";dim.Dimtxt=.35;dim.Dimasz=.15;dim.XData=Tag(run,handle);
                space.AppendEntity(dim);tr.AddNewlyCreatedDBObject(dim,true);
            }
        }
        static string RecordText(Xrecord record) {
            using(var buffer=record.Data) return buffer==null?"":string.Concat(buffer.AsArray().Select(v=>Convert.ToString(v.Value,CultureInfo.InvariantCulture)));
        }
        static Settings LoadSettings(Database db,Transaction tr,string handle)
        {
            return LoadProject(db,tr,handle).Settings;
        }
        static SavedProject LoadProject(Database db,Transaction tr,string handle)
        {
            var root=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
            if(!root.Contains("VR_RAZSIRITVE")) return new SavedProject();
            var runs=(DBDictionary)tr.GetObject(root.GetAt("VR_RAZSIRITVE"),OpenMode.ForRead);
            foreach(DBDictionaryEntry entry in runs) {
                string text=RecordText((Xrecord)tr.GetObject(entry.Value,OpenMode.ForRead));
                if(!text.Contains("Os: "+handle+Environment.NewLine)) continue;
                int marker=text.IndexOf("#PROJECT_XML"+Environment.NewLine,StringComparison.Ordinal);
                if(marker<0) continue;
                try {
                    using(var reader=new System.IO.StringReader(text.Substring(marker+"#PROJECT_XML".Length+Environment.NewLine.Length)))
                        return (SavedProject)new System.Xml.Serialization.XmlSerializer(typeof(SavedProject)).Deserialize(reader);
                } catch(System.Exception) {return new SavedProject();}
            }
            return new SavedProject();
        }
        static void RemovePrevious(Database db,Transaction tr,string handle)
        {
            var blocks=(BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead);
            foreach(ObjectId blockId in blocks) {
                var block=(BlockTableRecord)tr.GetObject(blockId,OpenMode.ForRead);
                if(block.IsFromExternalReference) continue;
                foreach(ObjectId id in block) {
                    var entity=tr.GetObject(id,OpenMode.ForRead) as Entity;if(entity==null) continue;
                    using(var tag=entity.GetXDataForApplication("VOZISCE")) {
                        if(tag==null) continue;var values=tag.AsArray();
                        if(values.Length<3 || Convert.ToString(values[2].Value)!=handle) continue;
                        var layer=(LayerTableRecord)tr.GetObject(entity.LayerId,OpenMode.ForRead);
                        if(layer.IsLocked) throw new InvalidOperationException("Prejšnji izris je na zaklenjenem sloju "+layer.Name+".");
                        entity.UpgradeOpen();entity.Erase();
                    }
                }
            }
            var root=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
            if(!root.Contains("VR_RAZSIRITVE"))return;
            var runs=(DBDictionary)tr.GetObject(root.GetAt("VR_RAZSIRITVE"),OpenMode.ForWrite);
            var remove=new List<string>();
            foreach(DBDictionaryEntry entry in runs)
                if(RecordText((Xrecord)tr.GetObject(entry.Value,OpenMode.ForRead)).Contains("Os: "+handle+Environment.NewLine)) remove.Add(entry.Key);
            foreach(string key in remove) {var record=tr.GetObject(runs.GetAt(key),OpenMode.ForWrite);runs.Remove(key);record.Erase();}
        }
        sealed class LiveSession
        {
            readonly CadDoc doc;readonly ObjectId axisId;readonly RoadWindow form;
            string snapshot;bool dirty,queued,busy,closed,suppressUndo,fixedDirty;
            Result pending;
            AlignmentGeometry pendingAxis;
            AlignmentGeometry lastDesign;
            readonly HashSet<ObjectId> movedGrips=new HashSet<ObjectId>();
            bool history;
            bool initialGrips;
            public ObjectId AxisId {get {return axisId;}}
            public AlignmentGeometry CurrentDesign {get {return lastDesign;}}
            public void MoveNode(AlignmentGeometry origin,AlignmentGrip grip,XY target) {
                var changed=AlignmentEditing.Move(origin,grip,target).Geometry;
                using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                    var source=(Entity)tr.GetObject(axisId,OpenMode.ForWrite);
                    busy=true;
                    try {ApplyAlignment(source,tr,changed);snapshot=Snapshot(source,tr);tr.Commit();}
                    finally {busy=false;}
                }
                lastDesign=changed;pendingAxis=null;dirty=false;movedGrips.Clear();history=false;
                form.UpdateAxis(changed.ToAxis());doc.Editor.Regen();
            }
            public LiveSession(CadDoc doc,ObjectId axisId,RoadWindow form,string snapshot) {
                this.doc=doc;this.axisId=axisId;this.form=form;this.snapshot=snapshot;
                try{lastDesign=AlignmentBuilder.Build(form.AlignmentDefinition);}catch(ArgumentException){lastDesign=null;}
                initialGrips=lastDesign!=null;
                fixedDirty=form.AlignmentDefinition.Elements.Any(row=>!string.IsNullOrEmpty(row.SourceHandle));
                form.DrawRequested+=Queue;form.ResultUpdated+=Queue;form.ResultInvalidated+=Clear;
                form.AxisDefinitionChanged+=QueueAxis;form.AxisDefinitionInvalid+=CancelAxisDraft;form.PickFixedArcsRequested+=Pick;
                form.MoveNodeRequested+=RequestMoveNode;
                form.FormClosed+=(s,e)=>Dispose();
                doc.Database.ObjectModified+=Modified;doc.Database.ObjectErased+=Erased;
                doc.CommandWillStart+=Starting;doc.CommandEnded+=Ended;doc.CommandCancelled+=Ended;doc.CommandFailed+=Ended;
                CadApp.Idle+=Idle;
                CadApp.DocumentManager.DocumentToBeDestroyed+=Destroying;
            }
            void Queue(Result result) {if(suppressUndo)return;pending=result;queued=true;}
            void QueueAxis(AlignmentGeometry geometry) {if(suppressUndo)return;pendingAxis=geometry;}
            void CancelAxisDraft() {pendingAxis=null;queued=false;}
            void Pick() {if(CadApp.DocumentManager.MdiActiveDocument==doc)doc.SendStringToExecute("_VR_OS_LOKA ",true,false,false);}
            void RequestMoveNode() {if(CadApp.DocumentManager.MdiActiveDocument==doc)doc.SendStringToExecute("_VR_OS_VOZLISCE ",true,false,false);}
            public void LoadAlignment(AlignmentDefinition value){form.LoadAlignment(value);}
            void Clear() {Queue(null);}
            void Changed(DBObject obj) {
                if(busy || obj.ObjectId.IsNull || obj.Database!=doc.Database)return;
                var entity=obj as Entity;string owner;AlignmentGrip grip;
                if(entity!=null && ReadGrip(entity,out owner,out grip) && owner==axisId.Handle.ToString()){movedGrips.Add(entity.ObjectId);return;}
                ObjectId id=obj is Vertex2d?obj.OwnerId:obj.ObjectId;if(id==axisId)dirty=true;else if(!id.IsNull && form.AlignmentDefinition.Elements.Any(row=>row.SourceHandle==id.Handle.ToString()))fixedDirty=true;
            }
            void Modified(object sender,ObjectEventArgs e) {Changed(e.DBObject);}
            void Erased(object sender,ObjectErasedEventArgs e) {Changed(e.DBObject);}
            void Starting(object sender,Bricscad.ApplicationServices.CommandEventArgs e) {
                string name=e.GlobalCommandName.ToUpperInvariant();
                suppressUndo=name=="U" || name.Contains("UNDO") || name.Contains("REDO");
                if(suppressUndo){queued=false;pending=null;pendingAxis=null;movedGrips.Clear();initialGrips=false;}
            }
            void Ended(object sender,Bricscad.ApplicationServices.CommandEventArgs e) {
                // UNDO/REDO owns database restoration; never regenerate its changes.
                if(suppressUndo) {dirty=true;history=true;fixedDirty=false;suppressUndo=false;form.PauseLive();form.ShowError("UNDO/REDO: ponovno izriši za nadaljnje sprotno urejanje.");queued=false;pendingAxis=null;movedGrips.Clear();}
            }
            void Destroying(object sender,Bricscad.ApplicationServices.DocumentCollectionEventArgs e) {if(e.Document==doc)Close();}
            void Idle(object sender,EventArgs args) {
                if(closed || busy || CadApp.DocumentManager.MdiActiveDocument!=doc || Convert.ToInt32(CadApp.GetSystemVariable("CMDACTIVE"))!=0)return;
                busy=true;
                try {
                    if(initialGrips && lastDesign!=null) {
                        initialGrips=false;
                        using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction()){AxisHelpers((Entity)tr.GetObject(axisId,OpenMode.ForRead),tr,lastDesign);tr.Commit();}
                    }
                    if(fixedDirty) {
                        fixedDirty=false;var definition=form.AlignmentDefinition;
                        using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                            double elevation=PolylineData((Entity)tr.GetObject(axisId,OpenMode.ForRead),tr).Elevation;
                            for(int i=0;i<definition.Elements.Count;i++) {
                                var row=definition.Elements[i];if(string.IsNullOrEmpty(row.SourceHandle))continue;
                                var id=doc.Database.GetObjectId(false,new Handle(long.Parse(row.SourceHandle,NumberStyles.HexNumber,CultureInfo.InvariantCulture)),0);
                                if(id.IsErased)throw new ArgumentException("Fiksni izvorni lok je izbrisan.");
                                definition.Elements[i]=FixedArc((Entity)tr.GetObject(id,OpenMode.ForRead),tr,row.SourceReversed,elevation);
                            }
                            tr.Commit();
                        }
                        form.LoadAlignment(definition);
                    }
                    if(pendingAxis!=null) {
                        var geometry=pendingAxis;pendingAxis=null;
                        using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                            var source=(Entity)tr.GetObject(axisId,OpenMode.ForWrite);ApplyAlignment(source,tr,geometry);snapshot=Snapshot(source,tr);tr.Commit();lastDesign=geometry;
                        }
                        dirty=false;doc.Editor.Regen();
                    }
                    if(dirty) {
                        dirty=false;
                        if(axisId.IsErased) {Clear();form.ShowError("Izvorna os je izbrisana.");}
                        else using(doc.LockDocument()) using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                            var source=(Entity)tr.GetObject(axisId,OpenMode.ForRead);string text=Snapshot(source,tr);
                            if(text!=snapshot && lastDesign!=null && !history) {
                                var previous=lastDesign;var raw=PolylineData(source,tr);AlignmentGeometry changed=null;string rejected=null;
                                try{changed=AlignmentEditing.NativeVertices(previous,raw.Points,raw.Bulges);}catch(ArgumentException ex){rejected=ex.Message;changed=previous;}
                                ApplyAlignment(source,tr,changed);snapshot=Snapshot(source,tr);tr.Commit();lastDesign=changed;movedGrips.Clear();form.UpdateAxis(changed.ToAxis());
                                doc.Editor.Regen();
                                if(rejected!=null)form.ShowError("Premik ni potrjen; os je obnovljena. "+rejected);
                            } else {
                                Axis axis;double z;ReadAxis(source,tr,out axis,out text,out z);tr.Commit();if(text!=snapshot){snapshot=text;lastDesign=axis.Design;form.UpdateAxis(axis);}
                            }
                            history=false;
                        }
                    }
                    if(movedGrips.Count>0) {
                        var ids=movedGrips.ToList();movedGrips.Clear();
                        if(lastDesign!=null)using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction()) {
                            var changes=new List<AlignmentGrip>();
                            foreach(var id in ids) {
                                if(id.IsErased)continue;string owner;AlignmentGrip grip;
                                if(ReadGrip((Entity)tr.GetObject(id,OpenMode.ForRead),out owner,out grip) && owner==axisId.Handle.ToString()) {
                                    var original=AlignmentEditing.Grips(lastDesign).FirstOrDefault(g=>g.Element==grip.Element && g.Kind==grip.Kind);
                                    if(original!=null && (original.Point-grip.Point).Length>1e-7)changes.Add(grip);
                                }
                            }
                            if(changes.Count>0) {
                                AlignmentGeometry changed=lastDesign;string rejected=null;
                                try {
                                    if(changes.Count!=1)throw new ArgumentException("Premikaj en značilni ročaj naenkrat. Za premik celotne osi izberi polilinijo.");
                                    changed=AlignmentEditing.Move(lastDesign,changes[0],changes[0].Point).Geometry;
                                }catch(ArgumentException ex){rejected=ex.Message;}
                                var source=(Entity)tr.GetObject(axisId,OpenMode.ForWrite);ApplyAlignment(source,tr,changed);snapshot=Snapshot(source,tr);tr.Commit();lastDesign=changed;form.UpdateAxis(changed.ToAxis());
                                doc.Editor.Regen();
                                if(rejected!=null)form.ShowError("Premik ni potrjen; ročaji so obnovljeni. "+rejected);
                            }else tr.Commit();
                        }
                    }
                    if(queued) {queued=false;Replace(doc,axisId,pending);}
                } catch(System.Exception ex) {
                    queued=false;
                    try {Replace(doc,axisId,null);} catch(System.Exception cleanup) {doc.Editor.WriteMessage("\nPrejšnji izris ni odstranjen: "+cleanup.Message);}
                    form.ShowError("Posodobitev ni uspela: "+ex.Message);doc.Editor.WriteMessage("\n"+ex.Message);
                }
                finally {busy=false;}
            }
            public void Close() {if(!form.IsDisposed)form.Close();Dispose();}
            void Dispose() {
                if(closed)return;closed=true;
                CadApp.Idle-=Idle;CadApp.DocumentManager.DocumentToBeDestroyed-=Destroying;
                doc.Database.ObjectModified-=Modified;doc.Database.ObjectErased-=Erased;
                doc.CommandWillStart-=Starting;doc.CommandEnded-=Ended;doc.CommandCancelled-=Ended;doc.CommandFailed-=Ended;
                form.DrawRequested-=Queue;form.ResultUpdated-=Queue;form.ResultInvalidated-=Clear;
                form.AxisDefinitionChanged-=QueueAxis;form.AxisDefinitionInvalid-=CancelAxisDraft;form.PickFixedArcsRequested-=Pick;
                form.MoveNodeRequested-=RequestMoveNode;
                sessions.Remove(doc);
            }
        }
        static void SaveRun(Database db,Transaction tr,string run,Result data,string handle,string snapshot) {
            var root=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForWrite);DBDictionary runs;
            if(root.Contains("VR_RAZSIRITVE")) runs=(DBDictionary)tr.GetObject(root.GetAt("VR_RAZSIRITVE"),OpenMode.ForWrite);
            else {runs=new DBDictionary();root.SetAt("VR_RAZSIRITVE",runs);tr.AddNewlyCreatedDBObject(runs,true);}
            var o=data.Settings;
            var content=new System.Text.StringBuilder();
            content.AppendLine("Vozisce 1.4 | "+DateTime.UtcNow.ToString("o"));content.AppendLine("Os: "+handle);content.AppendLine("Geometrija: "+snapshot);
            content.AppendLine("Metoda: "+o.Method+" | Stran: "+o.Side);
            content.AppendLine(string.Format(CultureInfo.InvariantCulture,"Širina={0:R}; robL={1:R}; robD={2:R}; s0={3:R}; korak={4:R}; hitrost={5:R}; Lročno={6:R}; predlog={7}; naklon={8:R}",o.BaseWidth,o.EdgeLeft,o.EdgeRight,o.StartStation,o.SampleStep,o.Speed,o.ManualTaper,o.AutomaticTaper,o.TaperSlope));
            content.AppendLine(string.Format(CultureInfo.InvariantCulture,"LeviPas={0:R}; DesniPas={1:R}",o.LeftWidth,o.RightWidth));
            foreach(var v in new[]{o.First,o.Second}) content.AppendLine(string.Format(CultureInfo.InvariantCulture,"Vozilo={0}; Lop={1:R}; D={2:R}; širina={3:R}; dolžina={4:R}; vir={5}",v.Name,v.Lop,v.D,v.Width,v.Length,v.Source));
            foreach(string note in data.Notes) content.AppendLine(note);
            foreach(var t in data.ConfirmedTransitions) content.AppendLine(string.Format(CultureInfo.InvariantCulture,"Prehodnica: od={0:R}; do={1:R}; k0={2:R}; k1={3:R}",t.From,t.To,t.K0,t.K1));
            content.AppendLine("#PROJECT_XML");
            using(var writer=new System.IO.StringWriter(CultureInfo.InvariantCulture)) {
                new System.Xml.Serialization.XmlSerializer(typeof(SavedProject)).Serialize(writer,new SavedProject {Settings=o,Transitions=data.ConfirmedTransitions,Snapshot=snapshot});
                content.Append(writer.ToString());
            }
            string value=content.ToString();var values=new List<TypedValue>();
            for(int i=0;i<value.Length;i+=200) values.Add(new TypedValue((int)DxfCode.Text,value.Substring(i,Math.Min(200,value.Length-i))));
            var record=new Xrecord {Data=new ResultBuffer(values.ToArray())};runs.SetAt(run,record);tr.AddNewlyCreatedDBObject(record,true);
        }
    }
}

