using System;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Vozisce;
class UiSmoke
{
    [STAThread] static void Main(string[] args) {
        string output=args.Length>0?args[0]:"bin";
        System.IO.Directory.CreateDirectory(output);
        Application.EnableVisualStyles();
        var axis=new Axis(new[]{new XY(0,0),new XY(80,0),new XY(180,100),new XY(180,200)},new[]{0,Math.Tan(Math.PI/8),0});
        using(var form=new RoadWindow(axis,new Settings(),true)) {
            form.ShowInTaskbar=false;
            form.StartPosition=FormStartPosition.Manual;
            form.Location=new Point(-20000,-20000);
            form.Show();Application.DoEvents();
            typeof(RoadWindow).GetMethod("Calculate",BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null).Invoke(form,null);
            Application.DoEvents();
            var result=(Result)typeof(RoadWindow).GetField("calculated",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            if(result==null || Math.Abs(result.Rows.Max(r=>r.Widening)-.4)>1e-8) throw new Exception("Izračun vmesnika ni uspel.");
            string csv=System.IO.Path.Combine(output,"ui-report.csv");
            Report.Write(csv,result,new Transition[0]);
            string text=System.IO.File.ReadAllText(csv);
            if(!text.Contains("0.4;") || !text.Contains("Korak;0.5") || !text.Contains("stacionaza_m;")) throw new Exception("CSV nima pričakovanih podatkov.");
            var numbers=(System.Collections.Generic.Dictionary<string,NumericUpDown>)typeof(RoadWindow).GetField("numbers",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            numbers["width"].Value=7;
            if(typeof(RoadWindow).GetField("calculated",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form)!=null) throw new Exception("Sprememba parametra ni razveljavila rezultata.");
            if(result.Settings.BaseWidth!=6) throw new Exception("Sprememba parametra je spremenila prejšnje poročilo.");
            typeof(RoadWindow).GetMethod("Calculate",BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null).Invoke(form,null);
            using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(System.IO.Path.Combine(output,"ui-preview.png")); }
            Console.WriteLine("PASS: WinForms predogled, izračun in upodobitev");
        }
        using(var form=new RoadWindow(axis,new Settings(),false,true)) {
            form.ShowInTaskbar=false;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
            var calculate=typeof(RoadWindow).GetMethod("Calculate",BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null);
            calculate.Invoke(form,null);
            int draws=0,updates=0,invalidations=0;Result latest=null;
            form.DrawRequested+=r=> {draws++;latest=r;};
            form.ResultUpdated+=r=> {updates++;latest=r;};
            form.ResultInvalidated+=()=>invalidations++;
            var draw=(Button)typeof(RoadWindow).GetField("draw",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            draw.PerformClick();
            if(draws!=1 || form.IsDisposed) throw new Exception("Interaktivni izris je zaprl okno.");
            var numbers=(System.Collections.Generic.Dictionary<string,NumericUpDown>)typeof(RoadWindow).GetField("numbers",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            numbers["width"].Value=7;
            var refresh=(Timer)typeof(RoadWindow).GetField("refresh",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            typeof(Timer).GetMethod("OnTick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(refresh,new object[]{EventArgs.Empty});
            if(updates!=1 || latest.Settings.BaseWidth!=7) throw new Exception("Sprememba parametrov ni sprožila posodobitve.");
            var changed=new Axis(new[]{new XY(0,0),new XY(80,0),new XY(280,200),new XY(280,400)},new[]{0,Math.Tan(Math.PI/8),0});
            form.UpdateAxis(changed);
            if(updates!=2 || Math.Abs(latest.Rows.Max(r=>r.Widening)-.2)>1e-8) throw new Exception("Sprememba osi ni posodobila razširitve.");
            numbers["width"].Value=1;
            typeof(Timer).GetMethod("OnTick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(refresh,new object[]{EventArgs.Empty});
            if(invalidations!=1 || refresh.Enabled) throw new Exception("Neveljaven vhod ni varno razveljavil izrisa.");
            form.ShowError("Test napake");
            if(refresh.Enabled) throw new Exception("Napaka je sprožila neskončno posodabljanje.");
            Console.WriteLine("PASS: interaktivni parametri, sprememba osi in neveljavni podatki");
        }
        using(var form=new RoadWindow(axis,new Settings(),false,true)) {
            form.ShowInTaskbar=false;form.Location=new Point(-20000,-20000);form.Show();Application.DoEvents();
            var tabs=(TabControl)typeof(RoadWindow).GetField("tabs",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            if(!tabs.TabPages[0].Text.Contains("Cestna os") || !tabs.TabPages[1].Text.Contains("vozni pasovi"))throw new Exception("Vrstni red zavihkov osi in širin ni pravilen.");
            int edits=0,invalid=0;AlignmentGeometry generated=null;
            form.AxisDefinitionChanged+=g=> {edits++;generated=g;};form.AxisDefinitionInvalid+=()=>invalid++;
            var definition=new AlignmentDefinition {Elements=new System.Collections.Generic.List<AlignmentElement>{
                new AlignmentElement {Kind=AlignmentKind.Line,Length=80},new AlignmentElement {Kind=AlignmentKind.Clothoid,Length=40,Radius1=100},new AlignmentElement {Kind=AlignmentKind.Arc,Length=100,Radius0=100}
            }};
            form.LoadAlignment(definition);
            if(edits!=1 || generated.Elements.Count!=3)throw new Exception("Urejevalnik ni objavil sestavljene osi.");
            var numbers=(System.Collections.Generic.Dictionary<string,NumericUpDown>)typeof(RoadWindow).GetField("numbers",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            numbers["laneL"].Value=3.25m;numbers["laneR"].Value=3.75m;
            if(numbers["width"].Value!=7)throw new Exception("Skupna širina ni vsota obeh voznih pasov.");
            var calculate=typeof(RoadWindow).GetMethod("Calculate",BindingFlags.NonPublic|BindingFlags.Instance,null,Type.EmptyTypes,null);calculate.Invoke(form,null);
            var result=(Result)typeof(RoadWindow).GetField("calculated",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            if(result==null || result.Settings.LeftWidth!=3.25 || result.Settings.RightWidth!=3.75)throw new Exception("Ločeni širini pasov nista uporabljeni.");
            tabs.SelectedIndex=0;Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(System.IO.Path.Combine(output,"alignment-preview.png"));}
            definition.Elements[1].Radius1=0;form.LoadAlignment(definition);
            var draw=(Button)typeof(RoadWindow).GetField("draw",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            if(invalid!=1 || draw.Enabled)throw new Exception("Neveljavna definicija osi ni onemogočila izrisa.");
            definition.Elements[1].Radius1=100;form.LoadAlignment(definition);
            if(edits!=2 || !draw.Enabled)throw new Exception("Popravljena definicija osi ni obnovila izračuna.");
            var editor=(AlignmentEditor)typeof(RoadWindow).GetField("alignmentEditor",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(form);
            if(!editor.MoveGrip(new AlignmentGrip {Element=0,Kind=AlignmentGripKind.End},new XY(90,5)))throw new Exception("Premik krajišča v predogledu ni uspel.");
            var editTimer=(Timer)typeof(AlignmentEditor).GetField("timer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(editor);
            typeof(Timer).GetMethod("OnTick",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(editTimer,new object[]{EventArgs.Empty});
            if(edits!=3 || editor.Geometry.Joints.Any(j=>!j.Valid))throw new Exception("Ročaj ni objavil tangentno zvezne osi.");
            var lastGood=editor.Geometry;var element=lastGood.Elements[2];var center=element.Start+element.Tangent(0).Normal*(1/element.K0);
            if(editor.MoveGrip(new AlignmentGrip {Element=2,Kind=AlignmentGripKind.Center},center+new XY(1,0)))throw new Exception("Nezvezen premik je bil potrjen.");
            if(!editor.HasInvalidDraft || draw.Enabled || editor.Geometry!=lastGood)throw new Exception("Zavrnjen premik ni ohranil zadnje veljavne geometrije.");
            form.LoadAlignment(lastGood.Definition);tabs.SelectedIndex=0;Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(System.IO.Path.Combine(output,"grips-preview.png"));}
            Console.WriteLine("PASS: zavihki, sestavljanje osi, ločena pasova in neveljavna definicija");
            Console.WriteLine("PASS: interaktivni ročaji in zavrnitev nezveznega premika");
        }
    }
}

