using System;
using System.Linq;
using System.Collections.Generic;

namespace Vozisce
{
    public sealed class AlignmentJoint
    {
        public int Before,After;
        public double Gap,AngleDegrees,CurvatureGap;
        public bool PositionOK,TangentOK,CurvatureOK;
        public bool Valid {get{return PositionOK && TangentOK && CurvatureOK;}}
        public override string ToString(){return "Stik "+(Before+1)+"–"+(After+1)+": razmik="+Gap.ToString("0.######")+" m; odklon tangent="+AngleDegrees.ToString("0.######")+"°; Δk="+CurvatureGap.ToString("0.########")+" 1/m";}
    }
    public static class AlignmentContinuity
    {
        public const double PositionTolerance=1e-5,AngleTolerance=1e-6,CurvatureTolerance=1e-8;
        public static List<AlignmentJoint> Check(IList<DesignedElement> elements)
        {
            var results=new List<AlignmentJoint>();
            for(int i=1;i<elements.Count;i++) {
                var a=elements[i-1];var b=elements[i];double gap=(a.Point(a.Length)-b.Start).Length;
                double angle=Math.Abs(AlignmentBuilder.Wrap(a.Angle(a.Length)-b.Heading)),dk=Math.Abs(a.K1-b.K0);
                results.Add(new AlignmentJoint {Before=i-1,After=i,Gap=gap,AngleDegrees=angle*180/Math.PI,CurvatureGap=dk,
                    PositionOK=Settings.Finite(gap)&&gap<=PositionTolerance,TangentOK=Settings.Finite(angle)&&angle<=AngleTolerance,
                    CurvatureOK=Settings.Finite(dk)&&((a.Kind!=AlignmentKind.Clothoid && b.Kind!=AlignmentKind.Clothoid)||dk<=CurvatureTolerance)});
            }
            return results;
        }
        public static void Require(IList<AlignmentJoint> joints)
        {
            var bad=joints.Where(j=>!j.Valid).ToList();if(bad.Count>0)throw new ArgumentException("Neustrezen priključek. "+string.Join("; ",bad.Select(j=>j.ToString()))+". Zahtevani sta zveznost in tangentnost; ob klotoidi tudi zveznost ukrivljenosti.");
        }
    }
    public enum AlignmentGripKind { Start, End, Shape, Center, Tangent }
    public sealed class AlignmentGrip
    {
        public int Element;
        public AlignmentGripKind Kind;
        public XY Point;
        public double Fraction=.5;
        public string Label {get{return (Element+1)+" "+(Kind==AlignmentGripKind.Start?"začetek":Kind==AlignmentGripKind.End?"konec":Kind==AlignmentGripKind.Center?"središče":Kind==AlignmentGripKind.Tangent?"tangenta":"oblika");}}
    }
    public sealed class AlignmentEditResult
    {
        public AlignmentGeometry Geometry;
        public string Message;
    }
    public static class AlignmentEditing
    {
        public static List<AlignmentGrip> Grips(AlignmentGeometry geometry)
        {
            var grips=new List<AlignmentGrip>();
            for(int i=0;i<geometry.Elements.Count;i++) {
                var e=geometry.Elements[i];var row=geometry.Definition.Elements[i];if(row.Attachment==Attachment.BetweenFixed)continue;
                bool freeAfter=i+1<geometry.Elements.Count && geometry.Definition.Elements[i+1].Attachment==Attachment.BetweenFixed;
                bool freeBefore=i>0 && geometry.Definition.Elements[i-1].Attachment==Attachment.BetweenFixed;
                if(i==0 || row.Attachment==Attachment.Fixed && !freeBefore)grips.Add(new AlignmentGrip {Element=i,Kind=AlignmentGripKind.Start,Point=e.Start});
                if(!freeAfter)grips.Add(new AlignmentGrip {Element=i,Kind=AlignmentGripKind.End,Point=e.Point(e.Length)});
                if((e.Kind==AlignmentKind.Arc || e.Kind==AlignmentKind.Clothoid) && !freeBefore && !freeAfter)grips.Add(new AlignmentGrip {Element=i,Kind=AlignmentGripKind.Shape,Point=e.Point(e.Length/2)});
                if(e.Kind==AlignmentKind.Arc)grips.Add(new AlignmentGrip {Element=i,Kind=AlignmentGripKind.Center,Point=e.Start+e.Tangent(0).Normal*(1/e.K0)});
                grips.Add(new AlignmentGrip {Element=i,Kind=AlignmentGripKind.Tangent,Point=e.Start+e.Tangent(0)*Math.Max(3,Math.Min(15,e.Length/3))});
            }
            return grips;
        }
        static void SetPose(AlignmentDefinition definition,int index,XY start,double heading)
        {
            var row=definition.Elements[index];row.SourceHandle=null;
            if(index==0 && row.Attachment==Attachment.Append){definition.X=start.X;definition.Y=start.Y;definition.Heading=heading*180/Math.PI;}
            else {row.Attachment=Attachment.Fixed;row.X=start.X;row.Y=start.Y;row.Heading=heading*180/Math.PI;}
        }
        static void FollowCurvature(AlignmentDefinition definition,int index,double curvature)
        {
            if(index+1>=definition.Elements.Count)return;var next=definition.Elements[index+1];
            if(next.Attachment!=Attachment.Append)return;
            if(next.Kind==AlignmentKind.Clothoid) {next.Radius0=Math.Abs(curvature)<1e-12?0:1/curvature;next.SourceHandle=null;}
            else if(definition.Elements[index].Kind==AlignmentKind.Clothoid && next.Kind==AlignmentKind.Arc) {
                if(Math.Abs(curvature)<1e-12)throw new ArgumentException("Klotoida konča s premo, za njo pa je krožni lok. Spremeni priključni element.");
                next.Radius0=1/curvature;next.SourceHandle=null;FollowCurvature(definition,index+1,curvature);
            }
        }
        static void FitArc(DesignedElement old,XY target,double fraction,out double length,out double curvature)
        {
            XY chord=target-old.Start,u=old.Tangent(0);double squared=XY.Dot(chord,chord);
            if(squared<1e-8)throw new ArgumentException("Točka loka je preblizu začetku.");
            curvature=2*XY.Cross(u,chord)/squared;
            if(Math.Abs(curvature)<1e-8 || Math.Abs(curvature)>1)throw new ArgumentException("Premik bi izravnal lok ali ustvaril radij pod 1 m.");
            double angle=2*Math.Atan2(XY.Cross(u,chord),XY.Dot(u,chord));length=angle/curvature/fraction;
            if(length<=.001 || length*Math.Abs(curvature)>=2*Math.PI)throw new ArgumentException("Premik ustvari neveljaven krožni lok.");
        }
        static void FitSpiral(DesignedElement old,XY target,double fraction,out double length,out double endK)
        {
            double l=old.Length,k=old.K1;
            Func<double,double,XY> error=(a,b)=>new DesignedElement {Start=old.Start,Heading=old.Heading,Length=a,K0=old.K0,K1=b}.Point(a*fraction)-target;
            for(int iteration=0;iteration<45;iteration++) {
                XY r=error(l,k);if(r.Length<1e-6){length=l;endK=k;return;}
                double hl=Math.Max(.0001,l*.00001),hk=1e-7;
                XY dl=(error(l+hl,k)-r)*(1/hl),dk=(error(l,k+hk)-r)*(1/hk);double det=XY.Cross(dl,dk);
                if(Math.Abs(det)<1e-12)break;
                double stepL=-XY.Cross(r,dk)/det,stepK=-XY.Cross(dl,r)/det;bool accepted=false;
                for(double damping=1;damping>=1.0/1024;damping/=2) {
                    double a=l+damping*stepL,b=k+damping*stepK;
                    if(a<.02 || a>100000 || Math.Abs(b)>1 || a*Math.Max(Math.Abs(old.K0),Math.Abs(b))>=2*Math.PI || Math.Abs(b-old.K0)<1e-10)continue;
                    if(error(a,b).Length<r.Length){l=a;k=b;accepted=true;break;}
                }
                if(!accepted)break;
            }
            throw new ArgumentException("Za premaknjeno točko ni najdena klotoida z ohranjeno začetno tangento in ukrivljenostjo.");
        }
        public static AlignmentEditResult Move(AlignmentGeometry geometry,AlignmentGrip grip,XY target)
        {
            if(geometry==null || grip==null || grip.Element<0 || grip.Element>=geometry.Elements.Count || !Settings.Finite(target.X) || !Settings.Finite(target.Y))throw new ArgumentException("Neveljaven ročaj ali ciljna točka.");
            var definition=geometry.Definition.Copy();int index=grip.Element;var e=geometry.Elements[index];var row=definition.Elements[index];
            if(row.Attachment==Attachment.BetweenFixed)throw new ArgumentException("Prosto klotoido urejaj s premikom fiksnih lokov; priključni mesti določi reševalnik.");
            string message="Premik potrjen.";row.SourceHandle=null;
            if(grip.Kind==AlignmentGripKind.Start) {
                if(index==0 || row.Attachment==Attachment.Fixed)SetPose(definition,index,target,e.Heading);
                else return Move(geometry,new AlignmentGrip {Element=index-1,Kind=AlignmentGripKind.End},target);
            } else if(grip.Kind==AlignmentGripKind.Center) {
                if(e.Kind!=AlignmentKind.Arc)throw new ArgumentException("Središčni ročaj pripada krožnemu loku.");
                XY center=e.Start+e.Tangent(0).Normal*(1/e.K0);SetPose(definition,index,e.Start+target-center,e.Heading);
            } else if(grip.Kind==AlignmentGripKind.Tangent) {
                XY direction=target-e.Start;if(direction.Length<.001)throw new ArgumentException("Smerna točka je preblizu začetku.");
                SetPose(definition,index,e.Start,Math.Atan2(direction.Y,direction.X));
            } else {
                double fraction=grip.Kind==AlignmentGripKind.End?1:grip.Fraction;if(fraction<=.01 || fraction>1)throw new ArgumentException("Neveljavna točka na elementu.");
                if(e.Kind==AlignmentKind.Line) {
                    XY delta=target-e.Start;
                    if(index==0 || row.Attachment==Attachment.Fixed){Settings.Positive(delta.Length,"Dolžina preme");SetPose(definition,index,e.Start,Math.Atan2(delta.Y,delta.X));row.Length=delta.Length/fraction;}
                    else {double projection=XY.Dot(delta,e.Tangent(0));if(projection<.02)throw new ArgumentException("Krajišče mora ostati pred začetkom preme.");row.Length=projection/fraction;message="Krajišče je projicirano na priključno tangento.";}
                } else if(e.Kind==AlignmentKind.Arc) {
                    double length,k;FitArc(e,target,fraction,out length,out k);row.Length=length;row.Radius0=1/k;FollowCurvature(definition,index,k);
                } else {
                    double length,k;FitSpiral(e,target,fraction,out length,out k);row.Length=length;row.Radius1=Math.Abs(k)<1e-12?0:1/k;FollowCurvature(definition,index,k);
                }
            }
            var result=AlignmentBuilder.Build(definition);
            if(grip.Kind==AlignmentGripKind.End || grip.Kind==AlignmentGripKind.Shape) {
                var element=result.Elements[index];double fraction=grip.Kind==AlignmentGripKind.End?1:grip.Fraction;
                double distance=(element.Point(element.Length*fraction)-target).Length;
                if(distance>1e-4)message+=" Zaradi priključnih omejitev je točka prilagojena za "+distance.ToString("0.###")+" m.";
            }
            return new AlignmentEditResult {Geometry=result,Message=message+" Preverjeno "+result.Joints.Count+" stikov: zveznost in tangentnost ustrezata."};
        }
        public static AlignmentGeometry NativeVertices(AlignmentGeometry previous,IList<XY> points,IList<double> bulges)
        {
            if(points.Count!=previous.Vertices.Count || bulges.Count!=points.Count-1)throw new ArgumentException("Spremenjeno je število temen. Elementno os urejaj z značilnimi ročaji ali v zavihku Cestna os.");
            var changed=Enumerable.Range(0,points.Count).Where(i=>(points[i]-previous.Vertices[i].Point).Length>1e-7).ToList();
            var curved=Enumerable.Range(0,bulges.Count).Where(i=>Math.Abs(bulges[i]-previous.Vertices[i].Bulge)>1e-10).ToList();
            if(changed.Count==0 && curved.Count==0)return previous;
            if(changed.Count==points.Count && curved.Count==0) {
                XY delta=points[0]-previous.Vertices[0].Point;
                if(changed.All(i=>(points[i]-previous.Vertices[i].Point-delta).Length<1e-6)) {
                    var definition=previous.Definition.Copy();definition.X+=delta.X;definition.Y+=delta.Y;
                    foreach(var row in definition.Elements)if(row.Attachment==Attachment.Fixed){row.X+=delta.X;row.Y+=delta.Y;row.SourceHandle=null;}
                    return AlignmentBuilder.Build(definition);
                }
            }
            if(changed.Count==1 && curved.Count==0) {
                int vertex=changed[0];double station=previous.Vertices[vertex].Station;
                int index=previous.Elements.FindIndex(element=>station<=element.Station+element.Length+1e-8);var e=previous.Elements[index];
                double fraction=(station-e.Station)/e.Length;
                return Move(previous,new AlignmentGrip {Element=index,Kind=vertex==0?AlignmentGripKind.Start:fraction>=1-1e-8?AlignmentGripKind.End:AlignmentGripKind.Shape,Fraction=fraction},points[vertex]).Geometry;
            }
            if(changed.Count==0 && curved.Count==1) {
                int vertex=curved[0];int index=previous.Elements.FindIndex(e=>Math.Abs(e.Station-previous.Vertices[vertex].Station)<1e-8 && e.Kind==AlignmentKind.Arc);
                if(index>=0) {
                    var segment=new Segment(points[vertex],points[vertex+1],bulges[vertex],0);var definition=previous.Definition.Copy();var row=definition.Elements[index];
                    row.Length=segment.L;row.Radius0=1/segment.K;row.SourceHandle=null;SetPose(definition,index,segment.A,Math.Atan2(segment.Tangent(0).Y,segment.Tangent(0).X));
                    return AlignmentBuilder.Build(definition);
                }
            }
            throw new ArgumentException("Premik več neodvisnih temen ni enoličen. Uporabi značilne ročaje posameznega elementa; zveznost in tangentnost morata ostati ohranjeni.");
        }
    }
}
