using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.ComponentModel;

namespace Vozisce
{
    public enum Method { Slovenija2005, RasLTirol2003 }
    public enum Side { Inside, Symmetric, Outside }
    public struct XY
    {
        public double X, Y;
        public XY(double x, double y) { X=x; Y=y; }
        public static XY operator +(XY a, XY b) { return new XY(a.X+b.X,a.Y+b.Y); }
        public static XY operator -(XY a, XY b) { return new XY(a.X-b.X,a.Y-b.Y); }
        public static XY operator *(XY a, double b) { return new XY(a.X*b,a.Y*b); }
        public double Length { get { return Math.Sqrt(X*X+Y*Y); } }
        public XY Unit { get { if(Length<1e-12) throw new ArgumentException("Ničelni vektor."); return this*(1/Length); } }
        public XY Normal { get { return new XY(-Y,X); } }
        public static double Cross(XY a, XY b) { return a.X*b.Y-a.Y*b.X; }
        public static double Dot(XY a, XY b) { return a.X*b.X+a.Y*b.Y; }
    }
    public sealed class Vehicle
    {
        public string Name { get; set; }
        public double Lop { get; set; }
        public double D { get; set; }
        public double Width { get; set; }
        public double Length { get; set; }
        public double? Wheelbase { get; set; }
        public double? FrontOverhang { get; set; }
        public string Source { get; set; }
        public override string ToString() { return Name; }
        public Vehicle Copy() { return (Vehicle)MemberwiseClone(); }
        public static Vehicle[] Catalog()
        {
            // Lop is an effective dimension, NOT an actual wheelbase. D is a reduced drawbar dimension.
            return new [] {
                new Vehicle { Name="Osebni avtomobil", Lop=4, D=4.20, Width=1.75, Length=4.70 },
                new Vehicle { Name="Tovornjak – dvoosni", Lop=8, D=6.23, Width=2.50, Length=8.50 },
                new Vehicle { Name="Vlačilec s polpriklopnikom", Lop=10, D=9.30, Width=2.50, Length=16.50 },
                new Vehicle { Name="Linijski avtobus", Lop=8.5, D=8.70, Width=2.50, Length=12 },
                new Vehicle { Name="Podaljšan avtobus (SI)", Lop=9, D=0, Width=2.50, Length=18 }
            }.Select(v=> { v.Source="Lop: SI 24. člen; tipske mere: SI 8. člen; D: Tirol 2003 (izjema: podaljšan avtobus nima D). Dejanska medosna razdalja ni določena."; return v; }).ToArray();
        }
    }
    public sealed class Settings
    {
        public Method Method=Method.Slovenija2005;
        public Side Side=Side.Inside;
        public Vehicle First=Vehicle.Catalog()[1], Second=Vehicle.Catalog()[0];
        public double BaseWidth=6, EdgeLeft=.25, EdgeRight=.25, StartStation=0;
        public double Speed=50, SampleStep=.5, ManualTaper=30, TaperSlope=.02;
        public bool AutomaticTaper=true;
        public bool Annotate=true, DimensionWidening=true;
        public double LeftLaneWidth;
        public double LeftWidth {get {return LeftLaneWidth>0?LeftLaneWidth:BaseWidth/2;}}
        public double RightWidth {get {return BaseWidth-LeftWidth;}}
        public Settings Copy()
        {
            var copy=(Settings)MemberwiseClone();
            copy.First=First==null?null:First.Copy();
            copy.Second=Second==null?null:Second.Copy();
            return copy;
        }
        public void Validate()
        {
            if(!Enum.IsDefined(typeof(Method),Method) || !Enum.IsDefined(typeof(Side),Side)) throw new ArgumentException("Neveljavna metoda ali stran razširitve.");
            Positive(BaseWidth,"Osnovna širina"); Positive(Speed,"Hitrost"); Positive(SampleStep,"Korak");
            if(!Finite(LeftLaneWidth)||LeftLaneWidth<0)throw new ArgumentException("Neveljavna širina levega pasu.");
            Positive(LeftWidth,"Levi vozni pas");Positive(RightWidth,"Desni vozni pas");
            Positive(ManualTaper,"Dolžina prehoda"); Positive(TaperSlope,"Naklon razširjanja");
            if(SampleStep>5 || SampleStep<.02) throw new ArgumentException("Korak mora biti med 0,02 in 5 m.");
            if(TaperSlope>.1) throw new ArgumentException("Naklon razširjanja je največ 0,10 m/m.");
            if(!Finite(StartStation) || !Finite(EdgeLeft) || !Finite(EdgeRight) || EdgeLeft<0 || EdgeRight<0) throw new ArgumentException("Neveljavna stacionaža ali robna pasova.");
            int lane=0;foreach(var v in new[]{First,Second}) {
                if(v==null) throw new ArgumentException("Izberite obe vozili.");
                Positive(v.Width,"Širina vozila"); Positive(v.Length,"Dolžina vozila");
                Positive(Method==Method.Slovenija2005?v.Lop:v.D,"Lop / D vozila");
                if(v.Width>(lane++==0?LeftWidth:RightWidth)) throw new ArgumentException("Osnovni vozni pas je ožji od merodajnega vozila.");
            }
        }
        public static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
        public static void Positive(double x,string name) { if(!Finite(x)||x<=0) throw new ArgumentException(name+" mora biti pozitivno končno število."); }
    }
    public sealed class Segment
    {
        public XY A,B,Center;
        public double S,L,K,Angle;
        public bool Arc { get { return Math.Abs(K)>1e-12; } }
        public Segment(XY a,XY b,double bulge,double s)
        {
            A=a; B=b; S=s;
            double chord=(b-a).Length;
            Settings.Positive(chord,"Dolžina segmenta");
            if(!Settings.Finite(bulge)) throw new ArgumentException("Neveljaven bulge.");
            if(Math.Abs(bulge)<1e-12) { L=chord; return; }
            Angle=4*Math.Atan(bulge);
            K=4*bulge/(chord*(1+bulge*bulge));
            L=Angle/K;
            Center=(a+b)*.5+(b-a).Unit.Normal*(chord*(1-bulge*bulge)/(4*bulge));
        }
        public XY Point(double t) {
            if(!Arc) return A+(B-A)*(t/L);
            XY r=A-Center; double ang=K*t;
            return Center+new XY(r.X*Math.Cos(ang)-r.Y*Math.Sin(ang),r.X*Math.Sin(ang)+r.Y*Math.Cos(ang));
        }
        public XY Tangent(double t) { return Arc ? (Point(t)-Center).Unit.Normal*Math.Sign(K) : (B-A).Unit; }
    }
    public sealed class Axis
    {
        public readonly List<Segment> Segments=new List<Segment>();
        public AlignmentGeometry Design;
        public double Length { get { return Design!=null?Design.Length:Segments.Last().S+Segments.Last().L; } }
        public bool ChordApproximation;
        public double[] VertexK;
        public Axis(IList<XY> points,IList<double> bulges,AlignmentGeometry design=null)
        {
            if(points==null || bulges==null || points.Count<2 || bulges.Count!=points.Count-1) throw new ArgumentException("Os mora vsebovati vsaj dve točki.");
            double s=0;
            for(int i=0;i<bulges.Count;i++) {
                if(!Settings.Finite(points[i].X)||!Settings.Finite(points[i].Y)) throw new ArgumentException("Neveljavne koordinate.");
                var e=new Segment(points[i],points[i+1],bulges[i],s); Segments.Add(e); s+=e.L;
            }
            if(!Settings.Finite(points.Last().X)||!Settings.Finite(points.Last().Y)) throw new ArgumentException("Neveljavne koordinate.");
            ChordApproximation=Segments.Count>=3 && Segments.All(e=>!e.Arc);
            VertexK=new double[points.Count];
            if(design!=null) {
                Design=design;ChordApproximation=false;
                for(int i=0;i<Segments.Count;i++){Segments[i].S=design.Vertices[i].Station;Segments[i].L=design.Vertices[i+1].Station-design.Vertices[i].Station;}
                return;
            }
            if(ChordApproximation) {
                for(int i=1;i<points.Count-1;i++) {
                    XY a=points[i]-points[i-1], b=points[i+1]-points[i], c=points[i+1]-points[i-1];
                    double turn=Math.Atan2(XY.Cross(a,b),XY.Dot(a,b));
                    if(Math.Abs(turn)>.0872664626) throw new ArgumentException("Lom osi presega 5°. Za krivino uporabite krožni lok ali gostejšo polilinijo.");
                    VertexK[i]=2*XY.Cross(a,b)/(a.Length*b.Length*c.Length);
                }
                VertexK[0]=VertexK[1]; VertexK[points.Count-1]=VertexK[points.Count-2];
            } else {
                for(int i=1;i<Segments.Count;i++) {
                    XY a=Segments[i-1].Tangent(Segments[i-1].L), b=Segments[i].Tangent(0);
                    if(Math.Abs(Math.Atan2(XY.Cross(a,b),XY.Dot(a,b)))>1e-4) throw new ArgumentException("Elementi osi niso tangentno zvezni pri s="+Segments[i].S.ToString("F2")+" m.");
                }
            }
        }
        public int Index(double s) {
            int lo=0,hi=Segments.Count-1;
            while(lo<hi) {int m=(lo+hi)/2; if(s>=Segments[m].S+Segments[m].L) lo=m+1; else hi=m;}
            return lo;
        }
        public XY Point(double s) {if(Design!=null)return Design.Point(Math.Max(0,Math.Min(Length,s)));var e=Segments[Index(s)]; return e.Point(Math.Max(0,Math.Min(e.L,s-e.S))); }
        public double Curvature(double s) {
            if(Design!=null)return Design.Curvature(Math.Max(0,Math.Min(Length,s)));
            int i=Index(s); var e=Segments[i];
            if(!ChordApproximation) return e.K;
            double t=Math.Max(0,Math.Min(1,(s-e.S)/e.L));
            return VertexK[i]+(VertexK[i+1]-VertexK[i])*t;
        }
        public XY Tangent(double s) {
            if(Design!=null)return Design.Tangent(Math.Max(0,Math.Min(Length,s)));
            if(!ChordApproximation) {var e=Segments[Index(s)]; return e.Tangent(Math.Max(0,Math.Min(e.L,s-e.S)));}
            // Central secant avoids alternating offset vertices at sampled spiral chords.
            double h=Math.Min(.25,Length/1000);
            return (Point(Math.Min(Length,s+h))-Point(Math.Max(0,s-h))).Unit;
        }
    }
    public sealed class Transition
    {
        public bool Confirmed {get;set;}
        public double From {get;set;}
        public double To {get;set;}
        public double K0 {get;set;}
        public double K1 {get;set;}
        public double Fit {get;set;}
        public string Description {get;set;}
    }
    public sealed class Width
    {
        public double Left,Right;
        public Width(double l,double r) {Left=l;Right=r;}
        public Width Max(Width b) { return new Width(Math.Max(Left,b.Left),Math.Max(Right,b.Right)); }
        public static Width Lerp(Width a,Width b,double t) { return new Width(a.Left+(b.Left-a.Left)*t,a.Right+(b.Right-a.Right)*t); }
    }
    public sealed class Row
    {
        public double Station {get;set;}
        public double Radius {get;set;}
        public double Widening {get;set;}
        public double Width {get;set;}
        public double Left {get;set;}
        public double Right {get;set;}
        public double Taper {get;set;}
        public string Element {get;set;}
        public XY Center,LeftPoint,RightPoint,LeftEdge,RightEdge;
    }
    public sealed class Result
    {
        public List<Row> Rows=new List<Row>();
        public List<string> Notes=new List<string>();
        public List<Transition> ConfirmedTransitions=new List<Transition>();
        public Settings Settings;
    }
    public sealed class SavedProject
    {
        public Settings Settings=new Settings();
        public List<Transition> Transitions=new List<Transition>();
        public string Snapshot;
    }
    public sealed class EdgeVertex
    {
        public XY Point;
        public double Bulge,Station;
    }
    public static class Calculation
    {
        sealed class Ramp
        {
            public double From,To,Boundary,Length;
            public Width A,B;
        }
        public static double PerLane(double k, Vehicle v, Method method)
        {
            double ak=Math.Abs(k);
            if(ak<1e-12) return 0;
            double r=1/ak;
            if(method==Method.Slovenija2005) {
                if(r<=20) throw new ArgumentException("R ≤ 20 m: potreben je izračun sledi koles; poenostavljen izris ni dovoljen.");
                return v.Lop*v.Lop/(2*r);
            }
            if(r<=30) throw new ArgumentException("RAS-L / Tirol: za R ≤ 30 m je potrebna sled vozila.");
            if(v.D<=0 || v.D>=r) throw new ArgumentException("Neveljavna reducirana dolžina D.");
            return v.D*v.D/(r+Math.Sqrt(r*r-v.D*v.D));
        }
        public static Width Required(double k,Settings o)
        {
            double w=PerLane(k,o.First,o.Method)+PerLane(k,o.Second,o.Method);
            if(o.Side==Side.Symmetric) return new Width(w*.5,w*.5);
            bool left=k>0;
            if(o.Side==Side.Outside) left=!left;
            return left?new Width(w,0):new Width(0,w);
        }
        public static double Taper(double delta,Settings o)
        {
            if(!o.AutomaticTaper) return o.ManualTaper;
            // Project proposal from an explicitly editable edge slope; NOT a certified RAS rule.
            // The maximum derivative of the double parabola is 2*delta/L.
            return Math.Max(10,2*delta/o.TaperSlope);
        }
        public static double Blend(double t) {
            t=Math.Max(0,Math.Min(1,t));
            return t<=.5?2*t*t:1-2*(1-t)*(1-t);
        }
        public static List<Transition> Detect(Axis axis)
        {
            if(axis.Design!=null)return axis.Design.Transitions();
            var xs=new List<double>(); var ks=new List<double>();
            if(axis.ChordApproximation) {
                for(int i=0;i<axis.Segments.Count;i++) {xs.Add(axis.Segments[i].S);ks.Add(axis.VertexK[i]);}
                xs.Add(axis.Length);ks.Add(axis.VertexK.Last());
            } else {
                foreach(var e in axis.Segments) {xs.Add(e.S+e.L/2);ks.Add(e.K);}
            }
            var found=new List<Transition>(); int start=0;
            while(start<xs.Count-4) {
                int best=-1; double fit=0;
                for(int end=start+4;end<xs.Count;end++) {
                    double dk=ks[end]-ks[start];
                    if(Math.Abs(dk)<1e-5 || xs[end]-xs[start]<3) continue;
                    int sign=Math.Sign(dk); bool mono=true;
                    for(int j=start+1;j<=end;j++) if((ks[j]-ks[j-1])*sign < -Math.Abs(dk)*.04) mono=false;
                    if(!mono) break;
                    double maxerr=0;
                    for(int j=start;j<=end;j++) {
                        double expected=ks[start]+dk*(xs[j]-xs[start])/(xs[end]-xs[start]);
                        maxerr=Math.Max(maxerr,Math.Abs(ks[j]-expected)/Math.Abs(dk));
                    }
                    if(maxerr<=.06) {best=end;fit=1-maxerr;} else if(best>=0) break;
                }
                if(best>=0) {
                    found.Add(new Transition {From=xs[start],To=xs[best],K0=ks[start],K1=ks[best],Fit=fit,Confirmed=false,Description="Predlog klotoide – potrdi meje"});
                    start=best;
                } else start++;
            }
            return found;
        }
        static Width Base(double s,Axis axis,Settings o,IList<Transition> transitions)
        {
            foreach(var t in transitions) if(t.Confirmed && s>=t.From && s<=t.To)
                return Width.Lerp(Required(t.K0,o),Required(t.K1,o),Blend((s-t.From)/(t.To-t.From)));
            return Required(axis.Curvature(s),o);
        }
        public static Result Run(Axis axis,Settings o,IList<Transition> transitions)
        {
            if(axis==null || o==null || transitions==null || transitions.Any(t=>t==null)) throw new ArgumentException("Manjkajo os, nastavitve ali podatki prehodnic.");
            o=o.Copy();
            o.Validate();
            if(axis.Length/o.SampleStep>20000) throw new ArgumentException("Več kot 20.000 vzorcev. Povečajte korak ali razdelite os.");
            var confirmed=transitions.Where(t=>t.Confirmed).OrderBy(t=>t.From)
                .Select(t=>new Transition {Confirmed=true,From=t.From,To=t.To,K0=t.K0,K1=t.K1,Fit=t.Fit,Description=t.Description}).ToList();
            double prev=-1;
            foreach(var t in confirmed) {
                if(!Settings.Finite(t.From)||!Settings.Finite(t.To)||!Settings.Finite(t.K0)||!Settings.Finite(t.K1)||t.From<0||t.To>axis.Length||t.To<=t.From||t.From<prev) throw new ArgumentException("Neveljavne ali prekrivajoče se prehodnice.");
                double tolerance=Math.Max(.00005,Math.Abs(t.K1-t.K0)*.08);
                for(int j=0;j<=10;j++) {
                    double at=t.From+(t.To-t.From)*j/10;
                    double expected=t.K0+(t.K1-t.K0)*j/10;
                    if(Math.Abs(axis.Curvature(at)-expected)>tolerance) throw new ArgumentException("Potrjena prehodnica se ne ujema z ukrivljenostjo osi. Preveri meje in polmere.");
                }
                prev=t.To;
            }
            var stations=new SortedSet<double>(); stations.Add(0);stations.Add(axis.Length);
            for(double s=o.SampleStep;s<axis.Length;s+=o.SampleStep) stations.Add(s);
            foreach(var e in axis.Segments) {stations.Add(e.S);stations.Add(e.S+e.L);}
            foreach(var t in confirmed) {stations.Add(t.From);stations.Add(t.To);}
            foreach(var t in confirmed) for(int j=1;j<4;j++) stations.Add(t.From+(t.To-t.From)*j/4);
            // Discontinuities become side-specific conservative widening envelopes.
            var ramps=new List<Ramp>();
            foreach(var e in axis.Segments.Skip(1)) {
                if(axis.ChordApproximation || confirmed.Any(t=>e.S>=t.From && e.S<=t.To)) continue;
                double s=e.S,eps=Math.Min(.00001,e.L/1000);
                Width a=Base(s-eps,axis,o,confirmed),b=Base(s+eps,axis,o,confirmed);
                double delta=Math.Max(Math.Abs(a.Left-b.Left),Math.Abs(a.Right-b.Right));
                if(delta<1e-5) continue;
                double l=Taper(delta,o);
                var previous=axis.Segments[axis.Index(s-eps)];
                double from=Math.Max(previous.S,s-l),to=Math.Min(e.S+e.L,s+l);
                ramps.Add(new Ramp {From=from,To=to,Boundary=s,A=a,B=b,Length=l});
                stations.Add(from);stations.Add(to);
                for(int j=1;j<4;j++) {stations.Add(from+(s-from)*j/4);stations.Add(s+(to-s)*j/4);}
            }
            var result=new Result {Settings=o};
            result.ConfirmedTransitions=confirmed;
            result.Notes.Add(o.Method==Method.Slovenija2005?"SI 2005, 24. člen: vsota Lop²/(2R). Uporabnost predpisa za projekt preveri projektant.":"RAS-L 1995 prek Tirol 2003: vsota R-sqrt(R²-D²); konservativno brez zmanjšanja pri kratkih lokih in brez praga 0,20 m.");
            if(ramps.Count>0) result.Notes.Add("Brez prehodnice: dolžina je projektni vnos oziroma predlog iz naklona roba; skladnost z RAS ni potrjena. Razširitev je polna na tesnejšem loku. Prekrivanja uporabljajo največji odmik na vsaki strani.");
            if(o.Side!=Side.Inside) result.Notes.Add("Simetrična/zunanja razširitev je projektna možnost; notranja sled vozila s tem ni dokazana.");
            if(axis.ChordApproximation) result.Notes.Add("Os je aproksimirana z odseki; ukrivljenost je ocenjena iz trojk točk. Prehodnice so hipoteza do potrditve meja.");
            foreach(var ramp in ramps)
                if((ramp.B.Left>ramp.A.Left || ramp.B.Right>ramp.A.Right) && ramp.Boundary-ramp.From<ramp.Length-1e-8 ||
                   (ramp.A.Left>ramp.B.Left || ramp.A.Right>ramp.B.Right) && ramp.To-ramp.Boundary<ramp.Length-1e-8)
                    result.Notes.Add("Prehod pri s="+ramp.Boundary.ToString("F2")+" je omejen na sosednji element; za zahtevani naklon podaljšaj element ali spremeni dolžino prehoda.");
            double lastStation=-1;
            foreach(double s in stations) {
                if(s-lastStation<1e-8 && s!=axis.Length) continue;
                lastStation=s;
                Width w=Base(s,axis,o,confirmed); double taper=0;
                foreach(var r in ramps) {
                    double boundary=r.Boundary;
                    if(s<r.From||s>r.To) continue;
                    Width extra;
                    if(s<=boundary) extra=Width.Lerp(r.A,r.A.Max(r.B),Blend((s-r.From)/(boundary-r.From)));
                    else extra=Width.Lerp(r.A.Max(r.B),r.B,Blend((s-boundary)/(r.To-boundary)));
                    w=w.Max(extra);
                    bool changes=s<=boundary ? Math.Abs(r.A.Left-r.B.Left)>1e-10 && r.B.Left>r.A.Left || Math.Abs(r.A.Right-r.B.Right)>1e-10 && r.B.Right>r.A.Right
                        : Math.Abs(r.A.Left-r.B.Left)>1e-10 && r.A.Left>r.B.Left || Math.Abs(r.A.Right-r.B.Right)>1e-10 && r.A.Right>r.B.Right;
                    if(changes) taper=Math.Max(taper,s<=boundary?boundary-r.From:r.To-boundary);
                }
                double k=axis.Curvature(s),left=o.LeftWidth+w.Left,right=o.RightWidth+w.Right;
                XY p=axis.Point(s),n=axis.Tangent(s).Normal;
                if(k>0 && k*(left+o.EdgeLeft)>=.95 || k<0 && -k*(right+o.EdgeRight)>=.95) throw new ArgumentException("Notranji rob doseže središče krivine pri s="+s.ToString("F2"));
                result.Rows.Add(new Row {Station=o.StartStation+s,Radius=Math.Abs(k)<1e-12?0:1/Math.Abs(k),Widening=w.Left+w.Right,Width=o.BaseWidth+w.Left+w.Right,Left=left,Right=right,Taper=taper,Element=confirmed.Any(t=>s>=t.From&&s<=t.To)?"Potrjena prehodnica":Math.Abs(k)<1e-12?"Prema":"Krivina",Center=p,LeftPoint=p+n*left,RightPoint=p-n*right,LeftEdge=p+n*(left+o.EdgeLeft),RightEdge=p-n*(right+o.EdgeRight)});
            }
            if(SelfIntersects(result.Rows.Select(r=>r.Center).ToList()) || SelfIntersects(result.Rows.Select(r=>r.LeftEdge).ToList()) || SelfIntersects(result.Rows.Select(r=>r.RightEdge).ToList())) throw new ArgumentException("Os ali zunanji rob se križa sam s seboj. Razdelite os ali popravite geometrijo.");
            return result;
        }
        // Preserve exact circular offsets wherever the width does not change.
        // Only the variable-width double parabola is represented by sampled chords.
        public static double EdgeBulge(Axis axis,Result result,int index,bool left)
        {
            if(axis.ChordApproximation || index>=result.Rows.Count-1) return 0;
            var a=result.Rows[index];var b=result.Rows[index+1];
            double s0=a.Station-result.Settings.StartStation,s1=b.Station-result.Settings.StartStation;
            var e=axis.Segments[axis.Index((s0+s1)/2)];
            if(!e.Arc || s0<e.S-1e-8 || s1>e.S+e.L+1e-8) return 0;
            if(Math.Abs((left?a.Left:a.Right)-(left?b.Left:b.Right))>1e-10) return 0;
            return Math.Tan(e.K*(s1-s0)/4);
        }
        public static List<EdgeVertex> EdgeGeometry(Axis axis,Result result,bool left,bool edge,bool baseline=false)
        {
            var vertices=new List<EdgeVertex>();
            double sign=left?1:-1;
            Func<Row,double> width=r=>baseline?(left?result.Settings.LeftWidth:result.Settings.RightWidth):(left?r.Left:r.Right);
            double shoulder=edge?(left?result.Settings.EdgeLeft:result.Settings.EdgeRight):0;
            Func<Row,XY> point=r=>r.Center+axis.Tangent(r.Station-result.Settings.StartStation).Normal*(sign*(width(r)+shoulder));
            int i=0;
            while(i<result.Rows.Count-1) {
                var first=result.Rows[i];int end=i+1;
                double s0=first.Station-result.Settings.StartStation;
                double s1=result.Rows[end].Station-result.Settings.StartStation;
                var element=axis.Segments[axis.Index((s0+s1)/2)];
                bool constant=!axis.ChordApproximation && s0>=element.S-1e-8 && s1<=element.S+element.L+1e-8 && Math.Abs(width(first)-width(result.Rows[end]))<1e-10;
                if(constant) {
                    while(end+1<result.Rows.Count && result.Rows[end+1].Station-result.Settings.StartStation<=element.S+element.L+1e-8 &&
                          Math.Abs(width(first)-width(result.Rows[end+1]))<1e-10) end++;
                }
                double length=result.Rows[end].Station-first.Station;
                vertices.Add(new EdgeVertex {Point=point(first),Station=first.Station,Bulge=constant && element.Arc?Math.Tan(element.K*length/4):0});
                i=end;
            }
            var last=result.Rows.Last();vertices.Add(new EdgeVertex {Point=point(last),Station=last.Station});
            return vertices;
        }
        public static bool SelfIntersects(IList<XY> p)
        {
            // Spatial hashing bounds the usual cost for long sampled alignments.
            var cells=new Dictionary<string,List<int>>();
            double size=20;
            for(int i=0;i<p.Count-1;i++) {
                XY a=p[i],b=p[i+1];
                int x0=(int)Math.Floor(Math.Min(a.X,b.X)/size),x1=(int)Math.Floor(Math.Max(a.X,b.X)/size);
                int y0=(int)Math.Floor(Math.Min(a.Y,b.Y)/size),y1=(int)Math.Floor(Math.Max(a.Y,b.Y)/size);
                if((long)(x1-x0+1)*(y1-y0+1)>100000) throw new ArgumentException("Predolg segment za kontrolo križanj.");
                var checkedIds=new HashSet<int>();
                for(int x=x0;x<=x1;x++) for(int y=y0;y<=y1;y++) {
                    string key=x+":"+y; List<int> ids;
                    if(!cells.TryGetValue(key,out ids)) {ids=new List<int>();cells[key]=ids;}
                    foreach(int j in ids) if(j<i-1 && checkedIds.Add(j) && Intersects(a,b,p[j],p[j+1])) return true;
                    ids.Add(i);
                }
            }
            return false;
        }
        static bool Intersects(XY a,XY b,XY c,XY d) {
            double p=XY.Cross(b-a,c-a),q=XY.Cross(b-a,d-a),r=XY.Cross(d-c,a-c),s=XY.Cross(d-c,b-c);
            if(p*q<0 && r*s<0) return true;
            const double eps=1e-9;
            return Math.Abs(p)<eps && On(a,b,c) || Math.Abs(q)<eps && On(a,b,d) || Math.Abs(r)<eps && On(c,d,a) || Math.Abs(s)<eps && On(c,d,b);
        }
        static bool On(XY a,XY b,XY p) {return p.X>=Math.Min(a.X,b.X)-1e-9&&p.X<=Math.Max(a.X,b.X)+1e-9&&p.Y>=Math.Min(a.Y,b.Y)-1e-9&&p.Y<=Math.Max(a.Y,b.Y)+1e-9;}
    }
}

