using System;
using System.Linq;
using System.Collections.Generic;
using Vozisce;
static class EditingTests
{
    static int count;
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;Console.WriteLine("OK "+message);}
    static void Near(double a,double b,string message,double tolerance=1e-5){Check(Math.Abs(a-b)<tolerance,message);}
    static void Reject(Action action,string message){bool failed=false;try{action();}catch(ArgumentException){failed=true;}Check(failed,message);}
    static AlignmentGeometry Path(params AlignmentElement[] rows){return AlignmentBuilder.Build(new AlignmentDefinition {Elements=rows.ToList()});}
    static AlignmentElement Line(double length){return new AlignmentElement {Kind=AlignmentKind.Line,Length=length};}
    static AlignmentElement Arc(double length,double radius){return new AlignmentElement {Kind=AlignmentKind.Arc,Length=length,Radius0=radius};}
    public static int Run()
    {
        count=0;
        var first=new DesignedElement {Kind=AlignmentKind.Line,Length=10};
        var second=new DesignedElement {Kind=AlignmentKind.Line,Length=10,Start=new XY(11,0)};
        var check=AlignmentContinuity.Check(new[]{first,second})[0];
        Near(check.Gap,1,"Kontrola odkrije nezveznost krajišč");Check(!check.PositionOK && check.TangentOK,"Zveznost je ločena od tangentnosti");
        second.Start=new XY(10,0);second.Heading=.1;check=AlignmentContinuity.Check(new[]{first,second})[0];
        Check(check.PositionOK && !check.TangentOK,"Kontrola odkrije tangentni lom brez vrzeli");Near(check.AngleDegrees,.1*180/Math.PI,"Izpis dejanskega odklona tangent");
        var geometry=Path(Line(80),Arc(100,100),Line(50));
        Check(AlignmentEditing.Grips(geometry).Any(g=>g.Element==1 && g.Kind==AlignmentGripKind.Center),"Ročaj središča krožnega loka");
        Check(AlignmentEditing.Grips(geometry).Any(g=>g.Element==1 && g.Kind==AlignmentGripKind.Shape),"Ročaj oblike krožnega loka");
        var moved=AlignmentEditing.Move(geometry,new AlignmentGrip {Element=0,Kind=AlignmentGripKind.End},new XY(90,10)).Geometry;
        Near((moved.Elements[0].Point(moved.Elements[0].Length)-new XY(90,10)).Length,0,"Premik krajišča začetne preme");
        Check(moved.Joints.All(j=>j.Valid),"Prilepljeni elementi sledijo spremenjeni tangenti");
        var straight=Path(Line(30),Line(40),Line(20));
        var projected=AlignmentEditing.Move(straight,new AlignmentGrip {Element=1,Kind=AlignmentGripKind.End},new XY(60,15));
        Near(projected.Geometry.Elements[1].Length,30,"Priključena prema ohrani začetno tangento");Check(projected.Message.Contains("projicirano"),"Projekcija je pojasnjena uporabniku");
        var expectedArc=new DesignedElement {Kind=AlignmentKind.Arc,Start=geometry.Elements[1].Start,Heading=0,Length=110,K0=1.0/125,K1=1.0/125};
        var arcMoved=AlignmentEditing.Move(geometry,new AlignmentGrip {Element=1,Kind=AlignmentGripKind.Shape},expectedArc.Point(55)).Geometry;
        Near(1/arcMoved.Elements[1].K0,125,"Premik sredine spremeni radij loka");Near(arcMoved.Elements[1].Length,110,"Premik sredine spremeni dolžino loka");Check(arcMoved.Joints.All(j=>j.Valid),"Spremenjeni lok ostane tangentno priključen");
        Reject(()=>AlignmentEditing.Move(geometry,new AlignmentGrip {Element=1,Kind=AlignmentGripKind.Center},new XY(81,100)),"Premik središča s prekinitvijo priključka zavrnjen");Near(geometry.Elements[1].Start.X,80,"Zavrnjen premik ne spremeni izvorne definicije");
        var circle=Path(Arc(80,100));var translated=AlignmentEditing.Move(circle,new AlignmentGrip {Element=0,Kind=AlignmentGripKind.Center},new XY(10,97)).Geometry;
        Near(translated.Elements[0].Start.X,10,"Premik središča samostojnega loka prestavi lok");Near(1/translated.Elements[0].K0,100,"Premik središča ohrani radij");
        var spiral=Path(Line(80),new AlignmentElement {Kind=AlignmentKind.Clothoid,Length=40,Radius1=100},Arc(80,100));
        var targetSpiral=new DesignedElement {Kind=AlignmentKind.Clothoid,Start=new XY(80,0),Heading=0,Length=50,K0=0,K1=1.0/125};
        var spiralMoved=AlignmentEditing.Move(spiral,new AlignmentGrip {Element=1,Kind=AlignmentGripKind.End},targetSpiral.Point(50)).Geometry;
        Near(spiralMoved.Elements[1].Length,50,"Premik konca klotoide spremeni dolžino",1e-3);Near(1/spiralMoved.Elements[1].K1,125,"Premik konca klotoide spremeni končni radij",1e-3);
        Near(1/spiralMoved.Elements[2].K0,125,"Prilepljeni lok sledi novi ukrivljenosti");Check(spiralMoved.Joints.All(j=>j.Valid),"Klotoida in lok sta zvezna in tangentna");
        var points=spiral.Vertices.Select(v=>v.Point).ToList();var bulges=spiral.Vertices.Take(spiral.Vertices.Count-1).Select(v=>v.Bulge).ToList();
        int middle=spiral.Vertices.FindIndex(v=>Math.Abs(v.Station-100)<1e-8);points[middle]=targetSpiral.Point(25);
        var native=AlignmentEditing.NativeVertices(spiral,points,bulges);
        Near(native.Elements[1].Length,50,"Premik vzorčenega temena ohrani matematično klotoido",1e-3);Check(native.Joints.All(j=>j.Valid),"Po premiku temena so preverjeni vsi stiki");
        var shiftedPoints=geometry.Vertices.Select(v=>v.Point+new XY(5,7)).ToList();
        var shifted=AlignmentEditing.NativeVertices(geometry,shiftedPoints,geometry.Vertices.Take(geometry.Vertices.Count-1).Select(v=>v.Bulge).ToList());Near((shifted.Point(0)-new XY(5,7)).Length,0,"Premik vseh temen prestavi celotno os");
        var badPoints=geometry.Vertices.Select(v=>v.Point).ToList();badPoints[0]=badPoints[0]+new XY(1,0);badPoints[1]=badPoints[1]+new XY(0,1);
        Reject(()=>AlignmentEditing.NativeVertices(geometry,badPoints,geometry.Vertices.Take(geometry.Vertices.Count-1).Select(v=>v.Bulge).ToList()),"Neenoličen premik več temen zavrnjen");
        var nativeArc=AlignmentEditing.NativeVertices(circle,circle.Vertices.Select(v=>v.Point).ToList(),new[]{.3});
        Near(nativeArc.Vertices[0].Bulge,.3,"Urejanje bulge ohrani pravi krožni element");
        Reject(()=>AlignmentEditing.Move(geometry,new AlignmentGrip {Element=1,Kind=AlignmentGripKind.Tangent},new XY(80,15)),"Neustrezna smer priključne tangente zavrnjena");
        Reject(()=>AlignmentEditing.Move(geometry,new AlignmentGrip {Element=0,Kind=AlignmentGripKind.End},new XY(double.NaN,0)),"Neveljavna ciljna točka zavrnjena");
        Console.WriteLine("PASS EDITING: "+count+" preverjanj");return count;
    }
}
