using System;
using System.Collections.Generic;
using System.Linq;

namespace Vozisce
{
    public enum AlignmentKind { Line, Arc, Clothoid }
    public enum Attachment { Append, Fixed, BetweenFixed }
    public sealed class AlignmentElement
    {
        public AlignmentKind Kind {get;set;}
        public Attachment Attachment {get;set;}
        public double Length {get;set;}
        public double Radius0 {get;set;}
        public double Radius1 {get;set;}
        public double X {get;set;}
        public double Y {get;set;}
        public double Heading {get;set;}
        public string SourceHandle {get;set;}
        public bool SourceReversed {get;set;}
        public AlignmentElement Copy() {return (AlignmentElement)MemberwiseClone();}
    }
    public sealed class AlignmentDefinition
    {
        public double X,Y,Heading,Step=.5;
        public List<AlignmentElement> Elements=new List<AlignmentElement>();
        public AlignmentDefinition Copy() {return new AlignmentDefinition {X=X,Y=Y,Heading=Heading,Step=Step,Elements=Elements.Select(e=>e.Copy()).ToList()};}
        public static AlignmentDefinition FromAxis(Axis axis)
        {
            if(axis.Design!=null)return axis.Design.Definition.Copy();
            var definition=new AlignmentDefinition {X=axis.Point(0).X,Y=axis.Point(0).Y,Heading=Math.Atan2(axis.Tangent(0).Y,axis.Tangent(0).X)*180/Math.PI};
            foreach(var e in axis.Segments) definition.Elements.Add(new AlignmentElement {Kind=e.Arc?AlignmentKind.Arc:AlignmentKind.Line,Length=e.L,Radius0=e.Arc?1/e.K:0});
            return definition;
        }
    }
    public sealed class DesignedElement
    {
        public AlignmentKind Kind;
        public XY Start;
        public double Heading,Length,K0,K1,Station;
        public double Angle(double s) {return Heading+K0*s+(K1-K0)*s*s/(2*Length);}
        public double Curvature(double s) {return K0+(K1-K0)*s/Length;}
        public XY Tangent(double s) {double a=Angle(s);return new XY(Math.Cos(a),Math.Sin(a));}
        public XY Point(double s)
        {
            s=Math.Max(0,Math.Min(Length,s));
            if(Math.Abs(K1-K0)<1e-15) {
                if(Math.Abs(K0)<1e-15)return Start+Tangent(0)*s;
                double a=K0*s/2,scale=2*Math.Sin(a)/K0;
                return Start+new XY(Math.Cos(Heading+a),Math.Sin(Heading+a))*scale;
            }
            // Eight-point Gauss quadrature; subdivisions limit phase change.
            double[] nodes={.1834346424956498,.5255324099163290,.7966664774136267,.9602898564975363};
            double[] weights={.3626837833783620,.3137066458778873,.2223810344533745,.1012285362903763};
            int parts=Math.Max(1,(int)Math.Ceiling(Math.Max(s/10,s*Math.Max(Math.Abs(K0),Math.Abs(K1))/.3)));
            XY sum=new XY();
            for(int part=0;part<parts;part++) {
                double half=s/(2*parts),mid=(part+.5)*s/parts;
                for(int j=0;j<4;j++) sum=sum+(Tangent(mid-half*nodes[j])+Tangent(mid+half*nodes[j]))*(half*weights[j]);
            }
            return Start+sum;
        }
        public DesignedElement Copy() {return (DesignedElement)MemberwiseClone();}
    }
    public sealed class AlignmentGeometry
    {
        public AlignmentDefinition Definition;
        public readonly List<DesignedElement> Elements=new List<DesignedElement>();
        public readonly List<EdgeVertex> Vertices=new List<EdgeVertex>();
        public readonly List<XY> TangentPolygon=new List<XY>();
        public readonly List<string> Notes=new List<string>();
        public readonly List<AlignmentJoint> Joints=new List<AlignmentJoint>();
        public double Length {get {var e=Elements.Last();return e.Station+e.Length;}}
        public DesignedElement At(double s) {return Elements.FirstOrDefault(e=>s<e.Station+e.Length-1e-10)??Elements.Last();}
        public XY Point(double s) {var e=At(s);return e.Point(s-e.Station);}
        public XY Tangent(double s) {var e=At(s);return e.Tangent(Math.Max(0,Math.Min(e.Length,s-e.Station)));}
        public double Curvature(double s) {var e=At(s);return e.Curvature(Math.Max(0,Math.Min(e.Length,s-e.Station)));}
        public List<Transition> Transitions() {return Elements.Where(e=>e.Kind==AlignmentKind.Clothoid).Select(e=>new Transition {From=e.Station,To=e.Station+e.Length,K0=e.K0,K1=e.K1,Confirmed=true,Fit=1,Description="Definirana klotoida"}).ToList();}
        public Axis ToAxis() {return new Axis(Vertices.Select(v=>v.Point).ToList(),Vertices.Take(Vertices.Count-1).Select(v=>v.Bulge).ToList(),this);}
    }
    public static class AlignmentBuilder
    {
        public static double Wrap(double value) {return Math.Atan2(Math.Sin(value),Math.Cos(value));}
        static double K(double radius) {if(!Settings.Finite(radius))throw new ArgumentException("Neveljaven radij.");if(radius==0)return 0;if(Math.Abs(radius)<1)throw new ArgumentException("Absolutni radij mora biti vsaj 1 m.");return 1/radius;}
        static DesignedElement Make(AlignmentElement row,XY start,double heading)
        {
            Settings.Positive(row.Length,"Dolžina elementa");
            if(row.Length>100000 || !Settings.Finite(start.X) || !Settings.Finite(start.Y) || !Settings.Finite(heading))throw new ArgumentException("Neveljavni parametri elementa.");
            if(!Enum.IsDefined(typeof(AlignmentKind),row.Kind)||!Enum.IsDefined(typeof(Attachment),row.Attachment))throw new ArgumentException("Neveljaven tip elementa.");
            double k0=row.Kind==AlignmentKind.Line?0:K(row.Radius0),k1=row.Kind==AlignmentKind.Clothoid?K(row.Radius1):k0;
            if(row.Kind==AlignmentKind.Arc && k0==0)throw new ArgumentException("Krožni lok potrebuje neničelni radij.");
            if(row.Kind==AlignmentKind.Clothoid && Math.Abs(k1-k0)<1e-12)throw new ArgumentException("Klotoida potrebuje različni začetno in končno ukrivljenost.");
            if(row.Length*Math.Max(Math.Abs(k0),Math.Abs(k1))>=2*Math.PI)throw new ArgumentException("Element zavije za prevelik kot; skrajšaj element.");
            return new DesignedElement {Kind=row.Kind,Start=start,Heading=heading,Length=row.Length,K0=k0,K1=k1};
        }
        public static AlignmentGeometry Build(AlignmentDefinition source)
        {
            if(source==null || source.Elements==null || source.Elements.Any(e=>e==null) || source.Elements.Count==0 || source.Elements.Count>500)throw new ArgumentException("Os mora vsebovati od 1 do 500 elementov.");
            Settings.Positive(source.Step,"Korak klotoide");if(source.Step<.02 || source.Step>5)throw new ArgumentException("Korak klotoide mora biti med 0,02 in 5 m.");
            var geometry=new AlignmentGeometry {Definition=source.Copy()};
            XY point=new XY(source.X,source.Y);double heading=source.Heading*Math.PI/180;
            for(int i=0;i<source.Elements.Count;i++) {
                var row=source.Elements[i];if(row==null)throw new ArgumentException("Manjka element osi.");
                if(row.Attachment==Attachment.BetweenFixed) {
                    if(row.Kind!=AlignmentKind.Clothoid || i==0 || i==source.Elements.Count-1 || source.Elements[i-1].Attachment!=Attachment.Fixed || source.Elements[i+1].Attachment!=Attachment.Fixed ||
                       source.Elements[i-1].Kind!=AlignmentKind.Arc || source.Elements[i+1].Kind!=AlignmentKind.Arc)throw new ArgumentException("Prosta klotoida mora biti med dvema fiksnima krožnima lokoma.");
                    var first=geometry.Elements.Last();var next=source.Elements[i+1];
                    var second=Make(next,new XY(next.X,next.Y),next.Heading*Math.PI/180);
                    double cut1,cut2,length;Solve(first,second,row.Length,out cut1,out cut2,out length);
                    first.Length=cut1;first.K1=first.K0;
                    var spiral=new DesignedElement {Kind=AlignmentKind.Clothoid,Start=first.Point(first.Length),Heading=first.Angle(first.Length),Length=length,K0=first.K0,K1=second.K0};
                    geometry.Elements.Add(spiral);
                    second.Start=second.Point(cut2);second.Heading=second.Angle(cut2);second.Length-=cut2;
                    geometry.Elements.Add(second);i++;
                    point=second.Point(second.Length);heading=second.Angle(second.Length);
                    geometry.Notes.Add("Fiksna loka ohranita središči in radija; priključka sta odrezana znotraj izbranih lokov. Izračunana dolžina klotoide: "+length.ToString("F3")+" m.");
                    continue;
                }
                var element=Make(row,row.Attachment==Attachment.Fixed?new XY(row.X,row.Y):point,row.Attachment==Attachment.Fixed?row.Heading*Math.PI/180:heading);
                geometry.Elements.Add(element);point=element.Point(element.Length);heading=element.Angle(element.Length);
            }
            geometry.Joints.AddRange(AlignmentContinuity.Check(geometry.Elements));
            AlignmentContinuity.Require(geometry.Joints);
            double station=0;
            foreach(var e in geometry.Elements) {
                e.Station=station;
                int count=e.Kind==AlignmentKind.Clothoid?Math.Max(2,(int)Math.Ceiling(e.Length/Math.Min(source.Step,.04/Math.Max(1e-8,Math.Max(Math.Abs(e.K0),Math.Abs(e.K1)))))):1;
                if(geometry.Vertices.Count+count>20000)throw new ArgumentException("Več kot 20.000 temen osi; povečaj korak ali razdeli os.");
                for(int j=0;j<count;j++) {
                    double s=e.Length*j/count;
                    geometry.Vertices.Add(new EdgeVertex {Point=e.Point(s),Station=station+s,Bulge=e.Kind==AlignmentKind.Arc?Math.Tan(e.K0*e.Length/4):0});
                }
                AddTangents(geometry,e);station+=e.Length;
            }
            geometry.Vertices.Add(new EdgeVertex {Point=point,Station=station});
            if(Calculation.SelfIntersects(geometry.Vertices.Select(v=>v.Point).ToList()))throw new ArgumentException("Os se križa sama s seboj.");
            return geometry;
        }
        static void AddPoint(List<XY> points,XY p) {if(points.Count==0 || (points.Last()-p).Length>1e-7)points.Add(p);}
        static void AddTangents(AlignmentGeometry geometry,DesignedElement element)
        {
            int pieces=Math.Max(1,(int)Math.Ceiling(Math.Abs(element.Angle(element.Length)-element.Heading)/(Math.PI*.8)));
            for(int i=0;i<pieces;i++) {
                double a=element.Length*i/pieces,b=element.Length*(i+1)/pieces;
                XY p=element.Point(a),q=element.Point(b),u=element.Tangent(a),v=element.Tangent(b);
                AddPoint(geometry.TangentPolygon,p);double cross=XY.Cross(u,v);
                if(Math.Abs(cross)>1e-8) AddPoint(geometry.TangentPolygon,p+u*(XY.Cross(q-p,v)/cross));
                else if(Math.Abs(XY.Cross(q-p,u))>1e-5) {
                    // Reverse spirals can have parallel end tangents. Use the middle tangent.
                    double mid=(a+b)/2;XY m=element.Point(mid),w=element.Tangent(mid);double c=XY.Cross(u,w),d=XY.Cross(w,v);
                    if(Math.Abs(c)>1e-8 && Math.Abs(d)>1e-8) {AddPoint(geometry.TangentPolygon,p+u*(XY.Cross(m-p,w)/c));AddPoint(geometry.TangentPolygon,m+w*(XY.Cross(q-m,v)/d));}
                    else geometry.Notes.Add("Vzporedni tangenti nimata končnega presečišča; tangentni poligon prikazuje krajišči.");
                }
                AddPoint(geometry.TangentPolygon,q);
            }
        }
        static double[] Residual(DesignedElement a,DesignedElement b,double s,double t,double length)
        {
            var spiral=new DesignedElement {Start=a.Point(s),Heading=a.Angle(s),Length=length,K0=a.K0,K1=b.K0};
            XY error=spiral.Point(length)-b.Point(t);
            return new[]{error.X,error.Y,Wrap(spiral.Angle(length)-b.Angle(t))*100};
        }
        static double Norm(double[] r) {return Math.Sqrt(r.Sum(x=>x*x));}
        static bool Linear(double[,] a,double[] rhs,out double[] x)
        {
            var m=new double[3,4];for(int i=0;i<3;i++){for(int j=0;j<3;j++)m[i,j]=a[i,j];m[i,3]=rhs[i];}
            for(int j=0;j<3;j++) {
                int pivot=j;for(int i=j+1;i<3;i++)if(Math.Abs(m[i,j])>Math.Abs(m[pivot,j]))pivot=i;
                if(Math.Abs(m[pivot,j])<1e-12){x=null;return false;}
                for(int k=j;k<4;k++){double value=m[j,k];m[j,k]=m[pivot,k];m[pivot,k]=value;}
                double scale=m[j,j];for(int k=j;k<4;k++)m[j,k]/=scale;
                for(int i=0;i<3;i++)if(i!=j){double factor=m[i,j];for(int k=j;k<4;k++)m[i,k]-=factor*m[j,k];}
            }
            x=new[]{m[0,3],m[1,3],m[2,3]};return true;
        }
        public static void Solve(DesignedElement a,DesignedElement b,double seed,out double cut1,out double cut2,out double length)
        {
            Settings.Positive(seed,"Začetna ocena dolžine klotoide");
            if(a.Kind!=AlignmentKind.Arc || b.Kind!=AlignmentKind.Arc || Math.Abs(a.K0-b.K0)<1e-10)throw new ArgumentException("Povezava zahteva različni ukrivljenosti krožnih lokov.");
            double maxLength=Math.Min((2*Math.PI-.01)/Math.Max(Math.Abs(a.K0),Math.Abs(b.K0)),Math.Min(10000,Math.Max(200,10*((a.Point(a.Length)-b.Start).Length+a.Length+b.Length))));
            foreach(double f in new[]{.9,.6,.3})foreach(double g in new[]{.1,.4,.7})foreach(double mult in new[]{1.0,.5,2.0}) {
                double[] x={a.Length*f,b.Length*g,Math.Min(maxLength,seed*mult)};
                for(int iteration=0;iteration<60;iteration++) {
                    var r=Residual(a,b,x[0],x[1],x[2]);double norm=Norm(r);
                    if(norm<1e-6 && x[0]>.001 && x[1]<b.Length-.001 && x[2]>.01) {cut1=x[0];cut2=x[1];length=x[2];return;}
                    var jac=new double[3,3];
                    for(int j=0;j<3;j++){var y=(double[])x.Clone();double h=1e-4*Math.Max(1,Math.Abs(x[j]));y[j]+=h;var rr=Residual(a,b,y[0],y[1],y[2]);for(int i=0;i<3;i++)jac[i,j]=(rr[i]-r[i])/h;}
                    double[] delta;if(!Linear(jac,r.Select(v=>-v).ToArray(),out delta))break;
                    bool accepted=false;
                    for(double damping=1;damping>=1.0/1024;damping/=2) {
                        var y=x.Zip(delta,(v,d)=>v+damping*d).ToArray();
                        if(y[0]<.001 || y[0]>a.Length || y[1]<0 || y[1]>b.Length-.001 || y[2]<.01 || y[2]>maxLength)continue;
                        if(Norm(Residual(a,b,y[0],y[1],y[2]))<norm){x=y;accepted=true;break;}
                    }
                    if(!accepted)break;
                }
            }
            throw new ArgumentException("Med izbranima fiksnima lokoma ni najdene tangentne klotoide znotraj njunih dolžin. Spremeni položaj ali dolžino lokov; središči in radija ostaneta fiksna.");
        }
    }
}
