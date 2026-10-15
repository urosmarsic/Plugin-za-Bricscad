using System;
using System.Collections.Generic;
using System.Linq;
using Vozisce;
class CoreTests
{
    static int count;
    static void Check(bool ok,string title) {if(!ok) throw new Exception(title);count++;Console.WriteLine("OK "+title);}
    static void Near(double a,double b,string title,double tolerance=1e-8) {Check(Math.Abs(a-b)<tolerance,title);}
    static void Reject(Action a,string title) {bool failed=false;try{a();}catch(ArgumentException){failed=true;}Check(failed,title);}
    static Axis Example(bool right=false) {
        double sign=right?-1:1;
        return new Axis(new[]{new XY(0,0),new XY(80,0),new XY(180,100*sign),new XY(180,200*sign)},new[]{0,sign*Math.Tan(Math.PI/8),0});
    }
    static void Main() {
        try {
            var o=new Settings();o.SampleStep=1;
            Near(Calculation.PerLane(.01,o.First,Method.Slovenija2005),.32,"SI tovornjak R100");
            Near(Calculation.PerLane(.01,o.Second,Method.Slovenija2005),.08,"SI avto R100");
            o.First=Vehicle.Catalog()[2];o.Second=Vehicle.Catalog()[2];
            Near(Calculation.Required(.01,o).Left,1,"SI dva vlačilca R100");
            o.Method=Method.RasLTirol2003;
            Near(Calculation.Required(.01,o).Left,2*(100-Math.Sqrt(10000-9.3*9.3)),"RAS dva vlačilca",1e-10);
            Reject(()=>Calculation.PerLane(.05,o.First,Method.Slovenija2005),"SI R20 zavrne");
            Reject(()=>Calculation.PerLane(1.0/30,o.First,Method.RasLTirol2003),"RAS R30 zavrne");
            o=new Settings();o.SampleStep=1;
            var a=Example();Near(a.Segments[1].L,Math.PI*50,"Dolžina krožnega loka");
            Near(a.Point(80+a.Segments[1].L).X,180,"Končna koordinata X");
            Near(a.Point(80+a.Segments[1].L).Y,100,"Končna koordinata Y");
            var data=Calculation.Run(a,o,new List<Transition>());
            Near(data.Rows.Max(r=>r.Widening),.4,"Skupna širitev SI");
            Near(data.Rows.First(r=>Math.Abs(r.Station-80)<1e-8).Widening,.4,"Polna širitev ob začetku loka");
            Check(data.Rows.Any(r=>r.Station<80&&r.Widening>0),"Širitev pred lokom");
            var reverse=Calculation.Run(Example(true),o,new List<Transition>());
            Near(reverse.Rows.Max(r=>r.Right),3.4,"Desna notranja stran");
            o.Side=Side.Symmetric;Near(Calculation.Required(.01,o).Left,.2,"Simetrija polovica");
            o.Side=Side.Outside;Near(Calculation.Required(.01,o).Right,.4,"Zunanja stran");
            Near(Calculation.Blend(.5),.5,"Parabolični prehod sredina");
            Near(Calculation.Blend(0),0,"Parabolični prehod začetek");
            Near(Calculation.Blend(1),1,"Parabolični prehod konec");
            Near(Calculation.Blend(.25),.125,"Prva polovica dvojne parabole");
            Near(Calculation.Blend(.75),.875,"Druga polovica dvojne parabole");
            Near(Calculation.Taper(.4,new Settings()),40,"Dolžina za največji naklon dvojne parabole");
            Near(data.Rows.First(r=>Math.Abs(r.Station-50)<1e-8).Widening,.05,"Dvojna parabola na četrtini vstopa");
            Near(data.Rows.First(r=>Math.Abs(r.Station-70)<1e-8).Widening,.35,"Dvojna parabola na treh četrtinah vstopa");
            Check(data.Rows.Where(r=>r.Station<40).All(r=>r.Widening==0 && r.Taper==0),"Pred prehodom je osnovna geometrija");
            Check(data.Rows.Where(r=>r.Station>80 && r.Station<230).All(r=>Math.Abs(r.Widening-.4)<1e-8 && r.Taper==0),"Krožni lok nima dodatnega prehoda");
            int arcIndex=data.Rows.FindIndex(r=>r.Station>100);
            var arcRow=data.Rows[arcIndex];var nextArcRow=data.Rows[arcIndex+1];
            double edgeBulge=Calculation.EdgeBulge(a,data,arcIndex,true);
            Check(edgeBulge>0,"Stalno razširjen rob je natančen krožni lok");
            var edgeArc=new Segment(arcRow.LeftPoint,nextArcRow.LeftPoint,edgeBulge,0);
            Near(1/edgeArc.K,96.6,"Točen radij notranjega roba",1e-6);
            Near((edgeArc.Center-a.Segments[1].Center).Length,0,"Koncentričen odmik loka",1e-6);
            int rampIndex=data.Rows.FindIndex(r=>r.Station==50);
            Near(Calculation.EdgeBulge(a,data,rampIndex,true),0,"Spremenljiva širina uporablja vzorčen prehod");
            int reverseIndex=reverse.Rows.FindIndex(r=>r.Station>100);
            Check(Calculation.EdgeBulge(Example(true),reverse,reverseIndex,false)<0,"Ohranjen desni krožni lok");
            var baseline=Calculation.EdgeGeometry(a,data,true,false,true);
            Check(baseline.Count==4,"Osnovni rob ima le štiri temena: prema, lok, prema");
            Near(baseline[1].Bulge,Math.Tan(Math.PI/8),"Osnovni krožni lok je en element polilinije");
            Near(baseline[0].Point.Y,3,"Črtkani osnovni rob nima razširitve");
            var outerGeometry=Calculation.EdgeGeometry(a,data,false,false);
            Check(outerGeometry.Count==4,"Nerazširjeni zunanji rob nima vzorčenih temen");
            var innerGeometry=Calculation.EdgeGeometry(a,data,true,false);
            Near(innerGeometry[1].Station,40,"Nerazširjena začetna prema je en element");
            Check(innerGeometry.Count(v=>Math.Abs(v.Bulge)>1e-12)==1,"Stalno razširjen lok je združen v en lok");
            Check(innerGeometry.Count>8,"Dvojna parabola ohrani vzorčena temena");
            var straight=new Axis(new[]{new XY(0,0),new XY(100,0)},new[]{0.0});
            var straightData=Calculation.Run(straight,new Settings(),new Transition[0]);
            Check(Calculation.EdgeGeometry(straight,straightData,true,true).Count==2,"Raven rob s pasom je ena linija");
            var major=new Axis(new[]{new XY(100,0),new XY(-100,0)},new[]{Math.Tan(3*Math.PI/8)});
            var majorData=Calculation.Run(major,new Settings(),new Transition[0]);
            var majorGeometry=Calculation.EdgeGeometry(major,majorData,true,false,true);
            Check(majorGeometry.Count==2,"Lok nad 180 stopinj ostane en krožni element");
            Near(majorGeometry[0].Bulge,Math.Tan(3*Math.PI/8),"Ohranjena orientacija velikega loka");
            o.BaseWidth=double.NaN;Reject(()=>o.Validate(),"NaN zavrnjen");
            Reject(()=>new Axis(new[]{new XY(0,0),new XY(0,0)},new[]{0.0}),"Ničelni segment zavrnjen");
            Check(Calculation.SelfIntersects(new[]{new XY(0,0),new XY(10,10),new XY(0,10),new XY(10,0)}),"Križanje robov");
            Check(!Calculation.SelfIntersects(new[]{new XY(0,0),new XY(10,0),new XY(20,0)}),"Prema brez križanja");
            var pts=new List<XY>();var bs=new List<double>();double x=0,y=0,theta=0,ds=1;
            pts.Add(new XY(x,y));
            for(int i=0;i<70;i++) {double k=i<10?0:i<50?(i-10)*.00025:.01;theta+=k*ds;x+=Math.Cos(theta)*ds;y+=Math.Sin(theta)*ds;pts.Add(new XY(x,y));bs.Add(0);}
            var spiral=new Axis(pts,bs);var detected=Calculation.Detect(spiral);
            Check(detected.Count>0,"Samodejno prepozna vzorčeno prehodnico");
            Check(detected.All(t=>!t.Confirmed),"Prehodnice zahtevajo pregled meja");
            o=new Settings();var transition=new Transition{Confirmed=true,From=10,To=50,K0=0,K1=.01};
            var spiralData=Calculation.Run(spiral,o,new[]{transition});
            Near(spiralData.Rows.First(r=>Math.Abs(r.Station-30)<1e-8).Widening,.2,"Dvojna parabola na sredini potrjene prehodnice");
            Near(spiralData.Rows.First(r=>Math.Abs(r.Station-20)<1e-8).Widening,.05,"Dvojna parabola na četrtini potrjene prehodnice");
            Near(spiralData.Rows.First(r=>Math.Abs(r.Station-40)<1e-8).Widening,.35,"Dvojna parabola na treh četrtinah potrjene prehodnice");
            Reject(()=>Calculation.Run(a,o,new[]{new Transition{Confirmed=true,From=10,To=5}}),"Napačne meje prehodnice");
            // Opposite curves: both side envelopes must be retained, never cancel by signed width.
            var sAxis=new Axis(new[]{new XY(0,0),new XY(80,0),new XY(180,100),new XY(280,200),new XY(380,200)},new[]{0,Math.Tan(Math.PI/8),-Math.Tan(Math.PI/8),0});
            var sData=Calculation.Run(sAxis,o,new List<Transition>());
            Check(sData.Rows.Any(r=>r.Left>3.3&&r.Right>3.3),"S-krivina ohrani obe razširitvi");
            o.StartStation=1000;var shifted=Calculation.Run(a,o,new List<Transition>());
            Near(shifted.Rows[0].Station,1000,"Začetna stacionaža");
            Near(shifted.Rows.Max(r=>r.Widening),.4,"Stacionaža ne spremeni izračuna");
            o.BaseWidth=10;o.First.Lop=12;
            Near(shifted.Settings.BaseWidth,6,"Rezultat ohrani osnovno širino");
            Near(shifted.Settings.First.Lop,8,"Rezultat ohrani podatke vozila");
            transition.K1=.02;
            Near(spiralData.ConfirmedTransitions[0].K1,.01,"Rezultat ohrani potrjeno prehodnico");
            Reject(()=>new Axis(null,new double[0]),"Manjkajoče točke zavrnjene");
            Reject(()=>Calculation.Run(a,new Settings(),null),"Manjkajoče prehodnice zavrnjene");
            var invalid=new Settings();invalid.Side=(Side)99;
            Reject(()=>invalid.Validate(),"Neveljavna stran zavrnjena");
            var scaledPoints=new[]{new XY(0,0),new XY(80,0),new XY(280,200),new XY(280,400)};
            var changedAxis=new Axis(scaledPoints,new[]{0,Math.Tan(Math.PI/8),0});
            Near(Calculation.Run(changedAxis,new Settings(),new Transition[0]).Rows.Max(r=>r.Widening),.2,"Sprememba radija spremeni razširitev");
            var shortAxis=new Axis(new[]{new XY(0,0),new XY(5,0),new XY(105,100),new XY(105,105)},new[]{0,Math.Tan(Math.PI/8),0});
            var shortData=Calculation.Run(shortAxis,new Settings(),new Transition[0]);
            Check(shortData.Notes.Any(n=>n.Contains("omejen na sosednji element")),"Prekratek element je označen v poročilu");
            var serializer=new System.Xml.Serialization.XmlSerializer(typeof(Settings));
            using(var writer=new System.IO.StringWriter()) {
                var original=new Settings {DimensionWidening=false,Annotate=false,ManualTaper=45};
                serializer.Serialize(writer,original);
                using(var reader=new System.IO.StringReader(writer.ToString())) {
                    var restored=(Settings)serializer.Deserialize(reader);
                    Check(!restored.DimensionWidening && !restored.Annotate && restored.ManualTaper==45 && restored.First.Lop==8,"Shranjevanje in obnovitev nastavitev");
                }
            }
            count+=AlignmentTests.Run();
            count+=EditingTests.Run();
            Console.WriteLine("PASS: "+count+" preverjanj");
        } catch(Exception e) {Console.Error.WriteLine("FAIL: "+e);Environment.Exit(1);}
    }
}

