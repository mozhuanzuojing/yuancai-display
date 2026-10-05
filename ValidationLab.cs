using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AmbientTone;
class Lab {
 static Controller control;
 static string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"验证");
 static ushort[][] originals;
 static TextBlock status; static FrameworkElement chart;
 static readonly int[,] colors=new int[,]{{255,255,255},{192,192,192},{128,128,128},{64,64,64},{16,16,16},{0,0,0},{255,0,0},{0,255,0},{0,0,255},{0,255,255},{255,0,255},{255,255,0},{194,150,130},{115,82,68},{230,125,35},{91,145,190},{72,112,64},{224,220,205}};
 [STAThread] static int Main(string[] args) {
  try {
   Directory.CreateDirectory(output);
   if(args.Length>0&&args[0]=="--watchdog"){int pid=int.Parse(args[1]);try{using(var p=Process.GetProcessById(pid))p.WaitForExit();}catch(ArgumentException){}Storage.Recover(pid);return 0;}
   if(args.Length>0&&args[0]=="--measure")return Measure(args[1],args[2]);
   bool fresh;using(var guard=new Mutex(true,"Local\\AmbientTone.Instance",out fresh)) {
    if(!fresh){MessageBox.Show("请先在原彩显示中恢复并退出，再打开验证窗口。","原彩显示 · 验证");return 2;}
    try {
     if(Native.Hdr()!=false){MessageBox.Show("此验证仅用于 SDR，请先关闭 HDR。","原彩显示 · 验证");return 2;}
     control=new Controller();originals=control.Displays.Select(d=>(ushort[])d.Original.Clone()).ToArray();
     if(originals.Length==0)throw new Exception("没有可测试的显示设备。");
     var app=new Application();var window=new Window{Title="原彩显示 · 固定色块验证",Width=760,Height=630,MinWidth=680,MinHeight=560,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(16,24,32)),FontFamily=new FontFamily("Microsoft YaHei UI"),FontSize=14};
     var grid=new Grid();grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});grid.RowDefinitions.Add(new RowDefinition());grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});window.Content=grid;
     var title=new StackPanel{Margin=new Thickness(24,16,24,8)};title.Children.Add(new TextBlock{Text="固定色块 · 截图与显示输出验证",FontSize=22,Foreground=Brushes.White});title.Children.Add(new TextBlock{Text="色块内容始终相同；只改变显卡输出曲线。这里不显示苹果测量结果。",Foreground=Brushes.LightGray,Margin=new Thickness(0,6,0,0),TextWrapping=TextWrapping.Wrap});grid.Children.Add(title);
     var bmp=Chart();var image=new Image{Source=bmp,Width=600,Height=300,Stretch=Stretch.None,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,SnapsToDevicePixels=true};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.NearestNeighbor);Grid.SetRow(image,1);grid.Children.Add(image);chart=image;
     var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var stream=File.Create(Path.Combine(output,"通用测试色块.png")))encoder.Save(stream);
     var footer=new StackPanel{Margin=new Thickness(24,8,24,20)};Grid.SetRow(footer,2);grid.Children.Add(footer);
     var buttons=new WrapPanel();footer.Children.Add(buttons);
     AddButton(buttons,"原始显示",delegate{Original();});AddButton(buttons,"测试暖色 4200 K",delegate{Warm();});AddButton(buttons,"比较实测文件",delegate{ImportMeasurements();});AddButton(buttons,"恢复并关闭",delegate{window.Close();});
     status=new TextBlock{Text="原始显示。请截取中间 600 × 300 色块区域。",Foreground=Brushes.LightGray,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0)};footer.Children.Add(status);
     window.Closing+=delegate{control.Restore();WriteMetrics("restored");};
     WriteMetrics("original");if(args.Contains("--screenshot-test")){window.Topmost=true;window.Loaded+=delegate{int phase=0;var timer=new System.Windows.Threading.DispatcherTimer();timer.Interval=TimeSpan.FromMilliseconds(850);timer.Tick+=delegate{try{if(phase==0){Original();Capture("原始截图.png");}else if(phase==1){Warm();}else if(phase==2){Capture("暖色截图.png");Original();}else if(phase==3){Capture("恢复截图.png");Compare();timer.Stop();window.Close();}phase++;}catch(Exception e){timer.Stop();control.Restore();File.WriteAllText(Path.Combine(output,"截图测试错误.txt"),e.ToString());window.Close();}};timer.Start();};}app.Run(window);return 0;
    }finally{if(control!=null)control.Dispose();guard.ReleaseMutex();}
   }
  }catch(Exception e){try{if(control!=null)control.Restore();File.WriteAllText(Path.Combine(output,"lab-error.txt"),e.ToString());}catch{}MessageBox.Show(e.Message,"验证失败");return 1;}
 }
 static void ImportMeasurements(){try{var picker=new Microsoft.Win32.OpenFileDialog{Title="选择苹果与电脑屏幕的实测文件",Filter="测量文件 (*.csv)|*.csv",InitialDirectory=output};if(picker.ShowDialog()!=true)return;string target=Path.Combine(output,"实测比较结果.csv");int result=Measure(picker.FileName,target);status.Text=result==0?"已计算配对测量的色度和亮度差异。结果已保存；仍需验证多种灯光与动态响应。":"缺少完整的两台设备实测数据，无法判断是否对齐。请查看测量模板。";}catch(Exception e){status.Text="测量文件无法分析："+e.Message;}}
 static void Capture(string name){
  chart.UpdateLayout();var point=chart.PointToScreen(new Point(0,0));var matrix=PresentationSource.FromVisual(chart).CompositionTarget.TransformToDevice;
  int width=(int)Math.Round(chart.ActualWidth*matrix.M11),height=(int)Math.Round(chart.ActualHeight*matrix.M22);
  using(var bmp=new System.Drawing.Bitmap(width,height,System.Drawing.Imaging.PixelFormat.Format24bppRgb)){
   using(var g=System.Drawing.Graphics.FromImage(bmp))g.CopyFromScreen((int)Math.Round(point.X),(int)Math.Round(point.Y),0,0,new System.Drawing.Size(width,height),System.Drawing.CopyPixelOperation.SourceCopy);
   bmp.Save(Path.Combine(output,name),System.Drawing.Imaging.ImageFormat.Png);
  }
 }
 static void Compare(){
  using(var a=new System.Drawing.Bitmap(Path.Combine(output,"原始截图.png")))using(var b=new System.Drawing.Bitmap(Path.Combine(output,"暖色截图.png")))using(var restored=new System.Drawing.Bitmap(Path.Combine(output,"恢复截图.png"))){
   if(a.Size!=b.Size||a.Size!=restored.Size)throw new Exception("截图尺寸变化，不能逐像素对比。");
   int changed=0,max=0,restoreChanges=0;long sum=0;var diff=new System.Drawing.Bitmap(a.Width,a.Height);
   for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++){var p=a.GetPixel(x,y);var q=b.GetPixel(x,y);var r=restored.GetPixel(x,y);int dr=Math.Abs(p.R-q.R),dg=Math.Abs(p.G-q.G),db=Math.Abs(p.B-q.B);int d=Math.Max(dr,Math.Max(dg,db));if(d>0)changed++;if(p.ToArgb()!=r.ToArgb())restoreChanges++;max=Math.Max(max,d);sum+=dr+dg+db;diff.SetPixel(x,y,System.Drawing.Color.FromArgb(dr,dg,db));}
   diff.Save(Path.Combine(output,"截图差异.png"),System.Drawing.Imaging.ImageFormat.Png);diff.Dispose();
   bool exact=control.Displays.Select((d,i)=>Model.Close(Native.Read(d.Name),originals[i],0)).All(v=>v);
   File.WriteAllText(Path.Combine(output,"截图比较.txt"),"Capture=Graphics.CopyFromScreen of test chart only\r\nSize="+a.Width+"x"+a.Height+"\r\nComparedPixels="+(a.Width*a.Height)+"\r\nChangedPixels="+changed+"\r\nMaxChannelDifference="+max+"\r\nMeanAbsoluteChannelDifference="+(sum/(double)(a.Width*a.Height*3)).ToString("F6",CultureInfo.InvariantCulture)+"\r\nRestoredScreenshotChangedPixels="+restoreChanges+"\r\nPhysicalGammaRestoreExact="+exact+"\r\nApplePhysicalMeasurements=NOT_AVAILABLE\r\nAppleAlignment=NOT_VERIFIED\r\nScreenshots describe framebuffer pixels, not measured screen light.");
  }
 }
 static void AddButton(Panel panel,string label,Action action){var b=new Button{Content=label,Padding=new Thickness(18,10,18,10),Margin=new Thickness(0,0,12,0)};b.Click+=delegate{action();};panel.Children.Add(b);}
 static BitmapSource Chart(){var pixels=new byte[600*300*3];for(int y=0;y<300;y++)for(int x=0;x<600;x++){int c=y/100*6+x/100,i=(y*600+x)*3;pixels[i]=(byte)colors[c,2];pixels[i+1]=(byte)colors[c,1];pixels[i+2]=(byte)colors[c,0];}return BitmapSource.Create(600,300,96,96,PixelFormats.Bgr24,null,pixels,1800);}
 static void Original(){string restored=control.Restore();WriteMetrics("original");status.Text="原始显示 · "+restored;}
 static void Warm(){control.Refresh();bool accepted=control.Apply(4200,1);WriteMetrics("warm");status.Text=accepted?"暖色输出已写入并读回验证。色块像素内容保持不变；请观察屏幕并截图。":control.Message;}
 static void WriteMetrics(string name){var lines=new List<string>{"state,display,channel,original_white,readback_white,changed_entries,max_absolute_change"};for(int d=0;d<control.Displays.Count;d++){var now=Native.Read(control.Displays[d].Name);if(now==null)continue;for(int c=0;c<3;c++){int count=0,max=0;for(int i=c*256;i<(c+1)*256;i++){int diff=Math.Abs((int)now[i]-originals[d][i]);if(diff>0)count++;max=Math.Max(max,diff);}lines.Add(name+","+control.Displays[d].Name+","+"RGB"[c]+","+originals[d][c*256+255]+","+now[c*256+255]+","+count+","+max);}}File.WriteAllLines(Path.Combine(output,name+"-gamma.csv"),lines);}
 // Measurements must come from a physical screen meter. Empty templates never imply a pass.
 static int Measure(string input,string target){
  var rows=File.ReadAllLines(input);if(rows.Length==0||rows[0].Trim().TrimStart((char)0xFEFF)!="device,lighting,patch,x,y,luminance_cd_m2,true_tone")throw new Exception("测量文件表头不符合模板。");var report=new List<string>{"lighting,patch,delta_uv_prime,luminance_difference_percent,status"};int paired=0,missing=0;var samples=new Dictionary<string,double[]>();
  foreach(var row in rows.Skip(1)){if(string.IsNullOrWhiteSpace(row))continue;var p=row.Split(',');if(p.Length!=7)throw new Exception("CSV 应为 device,lighting,patch,x,y,luminance_cd_m2,true_tone 七列。");
   double x,y,Y;if(!double.TryParse(p[3],NumberStyles.Float,CultureInfo.InvariantCulture,out x)||!double.TryParse(p[4],NumberStyles.Float,CultureInfo.InvariantCulture,out y)||!double.TryParse(p[5],NumberStyles.Float,CultureInfo.InvariantCulture,out Y)){missing++;continue;}
   if(double.IsNaN(x)||double.IsNaN(y)||double.IsNaN(Y)||double.IsInfinity(x)||double.IsInfinity(y)||double.IsInfinity(Y)||x<=0||y<=0||x+y>=1||Y<=0)throw new Exception("测量值必须是有效色度 x/y 和正亮度。");if(p[0]!="apple"&&p[0]!="windows")throw new Exception("device 必须为 apple 或 windows。");if(p[0]=="apple"&&p[6]!="on")throw new Exception("苹果参考必须记录 true_tone=on。");
   string key=p[0]+"|"+p[1]+"|"+p[2];if(samples.ContainsKey(key))throw new Exception("重复测量行："+key);samples[key]=new double[]{x,y,Y};}
  foreach(var key in samples.Keys.Where(k=>k.StartsWith("apple|"))){string suffix=key.Substring(6);double[] b;if(!samples.TryGetValue("windows|"+suffix,out b)){missing++;continue;}var a=samples[key];double da=-2*a[0]+12*a[1]+3,db=-2*b[0]+12*b[1]+3;double u=4*a[0]/da-4*b[0]/db,v=9*a[1]/da-9*b[1]/db;double delta=Math.Sqrt(u*u+v*v),lum=100*Math.Abs(b[2]-a[2])/a[2];report.Add(suffix.Replace('|',',')+","+delta.ToString("F6",CultureInfo.InvariantCulture)+","+lum.ToString("F3",CultureInfo.InvariantCulture)+",measured_only_no_apple_certification");paired++;}
  foreach(var key in samples.Keys.Where(k=>k.StartsWith("windows|")))if(!samples.ContainsKey("apple|"+key.Substring(8)))missing++;
  report.Add("# paired="+paired+", missing_or_blank="+missing+", matched_device_not_proven");File.WriteAllLines(target,report);return paired>0&&missing==0?0:2;
 }
}


