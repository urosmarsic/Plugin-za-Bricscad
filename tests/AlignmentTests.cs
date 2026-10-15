using System;
using System.Linq;
using System.Collections.Generic;
using Vozisce;
static class AlignmentTests
{
    static int count;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("OK "+message);}
    static void Near(double a,double b,string message,double tolerance=1e-6){Check(Math.Abs(a-b)<tolerance,message);}
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch(ArgumentException){rejected=true;}Check(rejected,message);}
    static AlignmentDefinition Fixture(double k1)
    {
        var arc=new DesignedElement {Kind=AlignmentKind.Arc,Length=100,K0=.01,K1=.01};
        var spiral=new DesignedElement {Kind=AlignmentKind.Clothoid,Length=40,K0=.01,K1=k1,Start=arc.Point(70),Heading=arc.Angle(70)};
        var end=spiral.Point(40);double theta=spiral.Angle(40),back=theta-k1*10;
        XY start=end+new XY((Math.Sin(back)-Math.Sin(theta))/k1,(Math.Cos(theta)-Math.Cos(back))/k1);
        return new AlignmentDefinition {Elements=new List<AlignmentElement> {
            new AlignmentElement {Kind=AlignmentKind.Arc,Attachment=Attachment.Fixed,Length=100,Radius0=100},
            new AlignmentElement {Kind=AlignmentKind.Clothoid,Attachment=Attachment.BetweenFixed,Length=35},
            new AlignmentElement {Kind=AlignmentKind.Arc,Attachment=Attachment.Fixed,Length=80,Radius0=1/k1,X=start.X,Y=start.Y,Heading=back*180/Math.PI}
        }};
    }
    public static int Run()
    {
        count=0;
        var definition=new AlignmentDefinition {Elements=new List<AlignmentElement>{
            new AlignmentElement {Kind=AlignmentKind.Line,Length=80},new AlignmentElement {Kind=AlignmentKind.Clothoid,Length=40,Radius0=0,Radius1=100},
            new AlignmentElement {Kind=AlignmentKind.Arc,Length=80,Radius0=100},new AlignmentElement {Kind=AlignmentKind.Clothoid,Length=40,Radius0=100,Radius1=0},new AlignmentElement {Kind=AlignmentKind.Line,Length=40}
        }};
        var geometry=AlignmentBuilder.Build(definition);var axis=geometry.ToAxis();
        Near(geometry.Length,280,"Analitična dolžina prema–klotoida–lok–klotoida–prema");
        Near(axis.Curvature(90),.0025,"Linearna ukrivljenost klotoide");
        Near(Math.Atan2(axis.Tangent(120).Y,axis.Tangent(120).X),.2,"Kot tangent na koncu klotoide");
        XY reference=new XY();int intervals=4000;double h=40.0/intervals;
        for(int i=0;i<=intervals;i++){double angle=.000125*i*h*i*h,weight=i==0 || i==intervals?1:i%2==0?2:4;reference=reference+new XY(Math.Cos(angle),Math.Sin(angle))*(weight*h/3);}
        Near(axis.Point(120).X-80,reference.X,"Koordinata klotoide X proti neodvisni Simpsonovi integraciji",1e-9);
        Near(axis.Point(120).Y,reference.Y,"Koordinata klotoide Y proti neodvisni Simpsonovi integraciji",1e-9);
        Check(geometry.Vertices.Count>100 && geometry.Vertices.Any(v=>v.Bulge!=0),"Polilinija vsebuje vzorčeno klotoido in pravi krožni lok");
        Check(axis.Design!=null && !axis.ChordApproximation,"Izračun uporablja definirano analitično os");
        Check(Calculation.Detect(axis).Count==2 && Calculation.Detect(axis).All(t=>t.Confirmed),"Definirani klotoidi imata znane meje");
        var data=Calculation.Run(axis,new Settings(),Calculation.Detect(axis));
        Near(data.Rows.Max(r=>r.Widening),.4,"Razširitev na definirani osi");
        Check(geometry.TangentPolygon.Count>=6,"Tangentni poligon vsebuje presečišča tangent");
        foreach(var k in new[]{.005,-.01}) {
            var fixedDefinition=Fixture(k);var fixedGeometry=AlignmentBuilder.Build(fixedDefinition);
            var a=fixedGeometry.Elements[0];var s=fixedGeometry.Elements[1];var b=fixedGeometry.Elements[2];
            Near(a.Length,70,"Priključek na prvem fiksnem loku "+k,1e-3);
            Near(s.Length,40,"Dolžina proste klotoide "+k,1e-3);
            Near((a.Point(a.Length)-s.Start).Length,0,"Zveznost lok–klotoida "+k);
            Near((s.Point(s.Length)-b.Start).Length,0,"Zveznost klotoida–lok "+k,1e-5);
            Near(AlignmentBuilder.Wrap(s.Angle(s.Length)-b.Heading),0,"Tangentnost proste povezave "+k);
            Near(s.K1,b.K0,"Zveznost ukrivljenosti na drugem loku "+k);
            var original=fixedDefinition.Elements[2];
            XY oldCenter=new XY(original.X,original.Y)+new XY(-Math.Sin(original.Heading*Math.PI/180),Math.Cos(original.Heading*Math.PI/180))*original.Radius0;
            XY newCenter=b.Start+b.Tangent(0).Normal*(1/b.K0);
            Near((oldCenter-newCenter).Length,0,"Središče drugega fiksnega loka ostane "+k,1e-5);
        }
        var impossible=Fixture(.005);impossible.Elements[2].X+=10000;
        Reject(()=>AlignmentBuilder.Build(impossible),"Nemogoča povezava fiksnih lokov zavrnjena");
        var wrong=definition.Copy();wrong.Elements[1].Radius0=50;
        Reject(()=>AlignmentBuilder.Build(wrong),"Neujemajoča ukrivljenost prilepljene klotoide zavrnjena");
        var widths=new Settings {BaseWidth=7,LeftLaneWidth=3};var straight=new Axis(new[]{new XY(0,0),new XY(100,0)},new[]{0.0});
        var unequal=Calculation.Run(straight,widths,new Transition[0]);Near(unequal.Rows[0].Left,3,"Ločena širina levega pasu");Near(unequal.Rows[0].Right,4,"Ločena širina desnega pasu");
        Near(Calculation.EdgeGeometry(straight,unequal,false,false,true)[0].Point.Y,-4,"Črtkana osnova uporablja desni pas");
        using(var writer=new System.IO.StringWriter()) {
            var serializer=new System.Xml.Serialization.XmlSerializer(typeof(AlignmentDefinition));serializer.Serialize(writer,definition);
            using(var reader=new System.IO.StringReader(writer.ToString())) {
                var restored=(AlignmentDefinition)serializer.Deserialize(reader);
                Near((AlignmentBuilder.Build(restored).Point(120)-geometry.Point(120)).Length,0,"Obnovitev geometrije iz shranjene definicije");
            }
        }
        Console.WriteLine("PASS ALIGNMENT: "+count+" preverjanj");return count;
    }
}
