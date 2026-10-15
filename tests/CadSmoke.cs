using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Vozisce;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using CadApp=Bricscad.ApplicationServices.Application;

[assembly: CommandClass(typeof(CadSmoke))]
public class CadSmoke
{
    static readonly List<string> log=new List<string>();
    static object Call(string name,params object[] args) {
        return typeof(Commands).GetMethod(name,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,args);
    }
    static void Check(bool ok,string text) {if(!ok)throw new System.Exception(text);log.Add("OK "+text);}
    [CommandMethod("VR_CAD_TESTS")]
    public void Run() {
        log.Clear();
        try {
            // A separate transient database keeps the user's drawing untouched.
            using(var db=new Database(true,true)) {
                var axis=new Axis(new[]{new XY(0,0),new XY(80,0),new XY(180,100),new XY(180,200)},new[]{0,Math.Tan(Math.PI/8),0});
                var options=new Settings();var result=Calculation.Run(axis,options,new Transition[0]);
                ObjectId sourceId;
                using(var tr=db.TransactionManager.StartTransaction()) {
                    var space=(BlockTableRecord)tr.GetObject(db.CurrentSpaceId,OpenMode.ForWrite);
                    var source=new Polyline();source.AddVertexAt(0,new Point2d(0,0),0,0,0);source.AddVertexAt(1,new Point2d(80,0),0,0,0);
                    sourceId=space.AppendEntity(source);tr.AddNewlyCreatedDBObject(source,true);
                    Call("Register",db,tr);Call("Layer",db,tr,"VR_VOZISCE_LEVO",(short)5);Call("Layer",db,tr,"VR_OZNAKE",(short)7);
                    Call("BaselineLayer",db,tr);
                    Call("DrawBaseline",space,tr,axis,result,true,0.0,"test1","A");
                    Call("DrawBaseline",space,tr,axis,result,false,0.0,"test1","A");
                    Call("Draw",space,tr,axis,result,true,false,0.0,"VR_VOZISCE_LEVO","test1","A");
                    Call("Draw",space,tr,axis,result,false,false,0.0,"VR_VOZISCE_LEVO","other","B");
                    Call("Dimensions",space,tr,result.Rows.OrderByDescending(r=>r.Widening).First(),options,0.0,"test1","A");
                    Call("SaveRun",db,tr,"test1",result,"A","test geometry");tr.Commit();
                }
                using(var tr=db.TransactionManager.StartTransaction()) {
                    var settings=(Settings)Call("LoadSettings",db,tr,"A");
                    Check(settings.First.Lop==8 && settings.DimensionWidening,"Obnovljeni parametri iz Xrecord");
                    var space=(BlockTableRecord)tr.GetObject(db.CurrentSpaceId,OpenMode.ForRead);
                    var entities=space.Cast<ObjectId>().Select(id=>tr.GetObject(id,OpenMode.ForRead)).ToList();
                    Check(entities.Count==6,"Izris robov, osnovnega vozišča in kote v CAD podatkovni bazi");
                    var baselines=entities.OfType<Polyline>().Where(p=>p.Layer=="VR_OSNOVNO_VOZISCE").ToList();
                    Check(baselines.Count==2 && baselines.All(p=>p.NumberOfVertices==4),"Črtkana osnovna robova imata po tri geometrijske elemente");
                    var layers=(LayerTable)tr.GetObject(db.LayerTableId,OpenMode.ForRead);
                    var baselineLayer=(LayerTableRecord)tr.GetObject(layers["VR_OSNOVNO_VOZISCE"],OpenMode.ForRead);
                    var lineType=(LinetypeTableRecord)tr.GetObject(baselineLayer.LinetypeObjectId,OpenMode.ForRead);
                    Check(lineType.NumDashes==2 && lineType.DashLengthAt(0)>0 && lineType.DashLengthAt(1)<0,"Črtkani tip vsebuje črto in presledek");
                    var dimension=entities.OfType<AlignedDimension>().Single();
                    Check(dimension.DimensionText.Contains("0.400 m"),"Kota prikazuje dodatno širino");
                    Check(Math.Abs((dimension.XLine2Point-dimension.XLine1Point).Length-.4)<1e-8,"Geometrija kote meri dejansko razširitev");
                    Check(entities.OfType<Polyline>().Any(p=>Enumerable.Range(0,p.NumberOfVertices).Any(i=>p.GetBulgeAt(i)>0)),"CAD rob vsebuje natančne krožne odseke");
                    tr.Commit();
                }
                using(var tr=db.TransactionManager.StartTransaction()) {Call("RemovePrevious",db,tr,"A");tr.Commit();}
                using(var tr=db.TransactionManager.StartTransaction()) {
                    var space=(BlockTableRecord)tr.GetObject(db.CurrentSpaceId,OpenMode.ForRead);
                    Check(space.Cast<ObjectId>().Count()==2,"Odstranjeni samo rob in kota izbrane osi");
                    Check(!sourceId.IsErased,"Izvorna os ostane");
                    var root=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
                    var runs=(DBDictionary)tr.GetObject(root.GetAt("VR_RAZSIRITVE"),OpenMode.ForRead);
                    Check(!runs.Contains("test1"),"Odstranjen prejšnji zapis parametrov");tr.Commit();
                }
                using(var tr=db.TransactionManager.StartTransaction()) {
                    var space=(BlockTableRecord)tr.GetObject(db.CurrentSpaceId,OpenMode.ForWrite);
                    options.BaseWidth=7;result=Calculation.Run(axis,options,new Transition[0]);
                    Call("Draw",space,tr,axis,result,true,false,0.0,"VR_VOZISCE_LEVO","test2","A");
                    Call("SaveRun",db,tr,"test2",result,"A","test geometry");tr.Commit();
                }
                using(var tr=db.TransactionManager.StartTransaction()) {
                    Check(((Settings)Call("LoadSettings",db,tr,"A")).BaseWidth==7,"Nov izris shrani spremenjene parametre");tr.Commit();
                }
            }
            TestAlignmentStorage();
            log.Add("PASS: "+log.Count+" CAD preverjanj");
        } catch(System.Exception ex) {log.Add("FAIL: "+ex);}
        string path=Path.Combine(Path.GetDirectoryName(typeof(CadSmoke).Assembly.Location),"cad-test-results.txt");
        File.WriteAllLines(path,log);
        CadApp.DocumentManager.MdiActiveDocument.Editor.WriteMessage("\n"+string.Join("\n",log));
    }
    static void TestAlignmentStorage()
    {
        var definition=new AlignmentDefinition {Elements=new List<AlignmentElement>{new AlignmentElement {Kind=AlignmentKind.Line,Length=80},new AlignmentElement {Kind=AlignmentKind.Clothoid,Length=40,Radius1=100},new AlignmentElement {Kind=AlignmentKind.Arc,Length=80,Radius0=100}}};
        var geometry=AlignmentBuilder.Build(definition);
        foreach(bool classic in new[]{false,true})using(var db=new Database(true,true)) {
            ObjectId sourceId;
            using(var tr=db.TransactionManager.StartTransaction()) {
                var space=(BlockTableRecord)tr.GetObject(db.CurrentSpaceId,OpenMode.ForWrite);
                Entity source=classic?(Entity)new Polyline2d {Elevation=12}:new Polyline {Elevation=12};
                sourceId=space.AppendEntity(source);tr.AddNewlyCreatedDBObject(source,true);
                Call("ApplyAlignment",source,tr,geometry);tr.Commit();
            }
            using(var tr=db.TransactionManager.StartTransaction()) {
                var source=(Entity)tr.GetObject(sourceId,OpenMode.ForRead);
                var args=new object[]{source,tr,null,null,0.0};Call("ReadAxis",args);
                var restored=(Axis)args[2];
                Check(restored.Design!=null && Math.Abs(restored.Curvature(90)-.0025)<1e-9,"Obnovljena analitična klotoida: "+(classic?"POLYLINE2D":"LWPOLYLINE"));
                Check((double)args[4]==12,"Ohranjena višina osi: "+classic);
                if(classic)Check(((Polyline2d)source).Cast<ObjectId>().Count()==geometry.Vertices.Count,"POLYLINE2D vsebuje vsa temena in bulge");
                var space=(BlockTableRecord)tr.GetObject(db.CurrentSpaceId,OpenMode.ForRead);
                var entities=space.Cast<ObjectId>().Select(id=>tr.GetObject(id,OpenMode.ForRead)).OfType<Entity>().ToList();
                Check(entities.Count(e=>e.Layer=="VR_TANGENTNI_POLIGON")==1,"Izrisan tangentni poligon: "+classic);
                Check(entities.OfType<DBText>().Count(e=>e.Layer=="VR_OS_KRAJISCA")==6,"Označeni začetki in konci vseh elementov: "+classic);tr.Commit();
            }
            using(var tr=db.TransactionManager.StartTransaction()) {
                var space=(BlockTableRecord)tr.GetObject(db.CurrentSpaceId,OpenMode.ForRead);
                var markers=space.Cast<ObjectId>().Select(id=>tr.GetObject(id,OpenMode.ForRead)).OfType<BlockReference>().Where(e=>e.Layer=="VR_OS_ROCAJI").ToList();
                Check(markers.Count==AlignmentEditing.Grips(geometry).Count,"Ustvarjeni značilni ročaji: "+classic);
                bool endpoint=false;
                foreach(var marker in markers) {
                    var args=new object[]{marker,null,null};bool parsed=(bool)Call("ReadGrip",args);Check(parsed,"Ročaj vsebuje povezavo z osjo in elementom: "+classic);
                    var grip=(AlignmentGrip)args[2];if(grip.Element==0 && grip.Kind==AlignmentGripKind.End) {
                        var moved=AlignmentEditing.Move(geometry,grip,grip.Point+new XY(5,0)).Geometry;Check(moved.Joints.All(j=>j.Valid),"Premik CAD ročaja ohrani tangentnost: "+classic);endpoint=true;
                    }
                }
                Check(endpoint,"Krajiščni CAD ročaj je prepoznan: "+classic);tr.Commit();
            }
            definition.Elements[0].Length=90;geometry=AlignmentBuilder.Build(definition);
            using(var tr=db.TransactionManager.StartTransaction()){Call("ApplyAlignment",(Entity)tr.GetObject(sourceId,OpenMode.ForWrite),tr,geometry);tr.Commit();}
            using(var tr=db.TransactionManager.StartTransaction()) {
                var args=new object[]{(Entity)tr.GetObject(sourceId,OpenMode.ForRead),tr,null,null,0.0};Call("ReadAxis",args);
                Check(Math.Abs(((Axis)args[2]).Length-210)<1e-8,"Posodobitev iste osi ohrani identiteto: "+classic);
                var space=(BlockTableRecord)tr.GetObject(db.CurrentSpaceId,OpenMode.ForRead);
                Check(space.Cast<ObjectId>().Select(id=>tr.GetObject(id,OpenMode.ForRead)).OfType<DBText>().Count(e=>e.Layer=="VR_OS_KRAJISCA")==6,"Oznake se zamenjajo brez podvojitev: "+classic);tr.Commit();
            }
            definition.Elements[0].Length=80;geometry=AlignmentBuilder.Build(definition);
        }
    }
}
