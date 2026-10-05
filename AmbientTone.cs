using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Serialization;
using Microsoft.Win32;
using Windows.Devices.Sensors;
using Forms = System.Windows.Forms;
[assembly: System.Reflection.AssemblyTitle("原彩显示")]
[assembly: System.Reflection.AssemblyDescription("北京日照与手动屏幕色温调节")]
[assembly: System.Reflection.AssemblyProduct("原彩显示")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
namespace AmbientTone {
 public static class Beijing {
  public static DateTime Now {get{return DateTime.SpecifyKind(DateTime.UtcNow.AddHours(8),DateTimeKind.Unspecified);}}
  // NOAA approximate solar equations. Beijing center: 39.9042 N, 116.4074 E, UTC+8.
  public static double[] Sun(DateTime date) {
   double g=2*Math.PI/(DateTime.IsLeapYear(date.Year)?366:365)*(date.DayOfYear-1);
   double eq=229.18*(0.000075+0.001868*Math.Cos(g)-0.032077*Math.Sin(g)-0.014615*Math.Cos(2*g)-0.040849*Math.Sin(2*g));
   double decl=0.006918-0.399912*Math.Cos(g)+0.070257*Math.Sin(g)-0.006758*Math.Cos(2*g)+0.000907*Math.Sin(2*g)-0.002697*Math.Cos(3*g)+0.00148*Math.Sin(3*g);
   double lat=39.9042*Math.PI/180;
   double angle=Math.Acos(Model.Clamp(Math.Cos(90.833*Math.PI/180)/(Math.Cos(lat)*Math.Cos(decl))-Math.Tan(lat)*Math.Tan(decl),-1,1))*180/Math.PI;
   double noon=720-4*116.4074-eq+480;return new double[]{(noon-4*angle)/60,(noon+4*angle)/60};
  }
  public static string Clock(double hour){return TimeSpan.FromMinutes(Math.Round(hour*60)).ToString(@"hh\:mm");}
 }
 public static class Model {
  public static double Clamp(double v,double a,double b){return Math.Max(a,Math.Min(b,v));}
  public static double[] RGB(double kelvin) {
   double t=Clamp(kelvin,1000,40000)/100;
   double r=t<=66?255:329.698727446*Math.Pow(t-60,-0.1332047592);
   double g=t<=66?99.4708025861*Math.Log(t)-161.1195681661:288.1221695283*Math.Pow(t-60,-0.0755148492);
   double b=t>=66?255:t<=19?0:138.5177312231*Math.Log(t-10)-305.0447927307;
   return new double[]{Clamp(r,0,255)/255,Clamp(g,0,255)/255,Clamp(b,0,255)/255};
  }
  public static ushort[] Ramp(ushort[] original,double kelvin,double strength) {
   var rgb=RGB(kelvin);var white=RGB(6500);var result=new ushort[768];
   for(int c=0;c<3;c++){double gain=1+(Clamp(rgb[c]/white[c],0.55,1)-1)*Clamp(strength,0,1);for(int i=0;i<256;i++)result[c*256+i]=(ushort)Math.Round(original[c*256+i]*gain);}return result;
  }
  public static double Scheduled(double hour,double day,double night) {
   if(day==night)throw new ArgumentException("白天和夜晚开始时间不能相同。");
   double sinceDay=(hour-day+24)%24,sinceNight=(hour-night+24)%24;bool isDay=sinceDay<sinceNight;
   double p=Clamp(isDay?sinceDay:sinceNight,0,1);p=p*p*(3-2*p);return isDay?4200+1500*p:5700-1500*p;
  }
  public static double FromLux(double lux){return 4200+2000*Clamp(Math.Log10(Math.Max(0,lux)+1)/3,0,1);}
  public static bool Close(ushort[] a,ushort[] b,int tolerance) {
   if(a==null||b==null||a.Length!=768||b.Length!=768)return false;
   for(int i=0;i<768;i++)if(Math.Abs((int)a[i]-b[i])>tolerance)return false;return true;
  }
 }
 public static class Native {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]public struct DisplayDevice {
   public int cb;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Description;public uint Flags;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;
  }
  [StructLayout(LayoutKind.Sequential)]public struct Luid{public uint Low;public int High;}
  [StructLayout(LayoutKind.Sequential)]public struct AdvancedColor{public uint Type,Size;public Luid Adapter;public uint Id,Value,Encoding,Bits;}
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]public static extern bool EnumDisplayDevices(string name,uint index,ref DisplayDevice d,uint flags);
  [DllImport("gdi32.dll",CharSet=CharSet.Unicode)]static extern IntPtr CreateDC(string driver,string device,string output,IntPtr init);
  [DllImport("gdi32.dll")]static extern bool DeleteDC(IntPtr dc);
  [DllImport("gdi32.dll")]static extern bool GetDeviceGammaRamp(IntPtr dc,IntPtr ramp);
  [DllImport("gdi32.dll")]static extern bool SetDeviceGammaRamp(IntPtr dc,IntPtr ramp);
  [DllImport("user32.dll")]static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
  [DllImport("user32.dll")]static extern int QueryDisplayConfig(uint flags,ref uint paths,IntPtr pathArray,ref uint modes,IntPtr modeArray,IntPtr topology);
  [DllImport("user32.dll")]static extern int DisplayConfigGetDeviceInfo(ref AdvancedColor info);
  public static ushort[] Read(string name) {
   IntPtr dc=CreateDC("DISPLAY",name,null,IntPtr.Zero);if(dc==IntPtr.Zero)return null;IntPtr mem=Marshal.AllocHGlobal(1536);
   try{if(!GetDeviceGammaRamp(dc,mem))return null;var raw=new short[768];Marshal.Copy(mem,raw,0,768);return Array.ConvertAll(raw,x=>unchecked((ushort)x));}
   finally{Marshal.FreeHGlobal(mem);DeleteDC(dc);}
  }
  public static bool Write(string name,ushort[] ramp) {
   IntPtr dc=CreateDC("DISPLAY",name,null,IntPtr.Zero);if(dc==IntPtr.Zero)return false;IntPtr mem=Marshal.AllocHGlobal(1536);
   try{Marshal.Copy(Array.ConvertAll(ramp,x=>unchecked((short)x)),0,mem,768);return SetDeviceGammaRamp(dc,mem);}
   finally{Marshal.FreeHGlobal(mem);DeleteDC(dc);}
  }
  // Legacy gamma controls are only used after confirming that all active displays use SDR.
  public static bool? Hdr() {
   for(int retry=0;retry<3;retry++) {
    uint paths,modes;if(GetDisplayConfigBufferSizes(2,out paths,out modes)!=0||paths==0||paths>128||modes>512)return null;
    IntPtr p=Marshal.AllocHGlobal((int)paths*72),m=Marshal.AllocHGlobal((int)Math.Max(1,modes)*64);
    try {
     int hr=QueryDisplayConfig(2,ref paths,p,ref modes,m,IntPtr.Zero);if(hr==122)continue;if(hr!=0)return null;
     for(int i=0;i<paths;i++) {
      IntPtr target=IntPtr.Add(p,i*72+20);var info=new AdvancedColor();info.Type=9;info.Size=(uint)Marshal.SizeOf(typeof(AdvancedColor));
      info.Adapter.Low=unchecked((uint)Marshal.ReadInt32(target));info.Adapter.High=Marshal.ReadInt32(target,4);info.Id=unchecked((uint)Marshal.ReadInt32(target,8));
      if(DisplayConfigGetDeviceInfo(ref info)!=0)return null;if((info.Value&2)!=0)return true;
     }return false;
    }finally{Marshal.FreeHGlobal(p);Marshal.FreeHGlobal(m);}
   }return null;
  }
  public static List<DisplayState> Displays() {
   var list=new List<DisplayState>();
   for(uint i=0;i<32;i++) {
    var d=new DisplayDevice();d.cb=Marshal.SizeOf(typeof(DisplayDevice));if(!EnumDisplayDevices(null,i,ref d,0))break;if((d.Flags&1)==0||(d.Flags&8)!=0)continue;
    var monitor=new DisplayDevice();monitor.cb=d.cb;EnumDisplayDevices(d.Name,0,ref monitor,1);
    var ramp=Read(d.Name);if(ramp==null||ramp.All(x=>x==0))continue;
    list.Add(new DisplayState{Name=d.Name,Id=String.IsNullOrEmpty(monitor.Id)?d.Id:monitor.Id,Description=monitor.Description,Original=ramp});
   }return list;
  }
 }
 public class DisplayState{public string Name,Id,Description;public ushort[] Original,Last,Previous;}
 public class Journal{public int Owner;public List<DisplayState> Displays=new List<DisplayState>();}
 public class Settings {
  public bool Enabled=false,Tray=true,Solar=true;public int Mode=1,Manual=5000,Strength=55,Day=7,Night=19,Drift=0;
  public void Validate(){Mode=(int)Model.Clamp(Mode,0,2);Manual=(int)Model.Clamp(Manual,3600,6500);Strength=(int)Model.Clamp(Strength,0,100);Drift=(int)Model.Clamp(Drift,-1500,1500);Day=(int)Model.Clamp(Day,0,23);Night=(int)Model.Clamp(Night,0,23);if(Day==Night){Day=7;Night=19;}}
 }
 public static class Storage {
  public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AmbientTone");
  public static string PathFor(string name){return Path.Combine(Root,name);}
  public static T Load<T>(string name)where T:class{try{using(var s=File.OpenRead(PathFor(name)))return(T)new XmlSerializer(typeof(T)).Deserialize(s);}catch{return null;}}
  public static void Save<T>(string name,T data){Directory.CreateDirectory(Root);string path=PathFor(name),temp=path+".tmp";using(var s=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){new XmlSerializer(typeof(T)).Serialize(s,data);s.Flush(true);}if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
  public static void Log(string text){try{Directory.CreateDirectory(Root);File.AppendAllText(PathFor("events.log"),DateTime.Now.ToString("s")+" "+text+Environment.NewLine);}catch{}}
  public static string Recover(int expectedOwner) {
   using(var guard=new Mutex(false,"Local\\AmbientTone.Journal")) {
    try{guard.WaitOne();}catch(AbandonedMutexException){}
    try {
     var journal=Load<Journal>("recovery.xml");if(journal==null||(expectedOwner!=0&&expectedOwner!=journal.Owner))return "没有待恢复的显示设置。";
     if(Native.Hdr()!=false)return "HDR 开启或 SDR 状态未知，暂未恢复；关闭 HDR 后再次恢复。";var present=Native.Displays();var pending=new List<DisplayState>();int recovered=0,skipped=0;
     foreach(var d in journal.Displays) {
      var live=present.FirstOrDefault(x=>x.Id==d.Id&&x.Name==d.Name);if(live==null&&present.Count(x=>x.Id==d.Id)==1)live=present.First(x=>x.Id==d.Id);if(live==null){pending.Add(d);continue;}
      if(Model.Close(live.Original,d.Original,0)){recovered++;continue;}
      if(!Model.Close(live.Original,d.Last,512)&&!Model.Close(live.Original,d.Previous,512)){skipped++;continue;}
      if(Native.Write(live.Name,d.Original)&&Model.Close(Native.Read(live.Name),d.Original,512))recovered++;else pending.Add(d);
     }
     journal.Displays=pending;if(pending.Count==0){if(File.Exists(PathFor("recovery.xml")))File.Delete(PathFor("recovery.xml"));}else Save("recovery.xml",journal);
     string status="已恢复 "+recovered+" 块屏幕"+(skipped>0?"；跳过 "+skipped+" 块已被其他程序改动的屏幕":"")+(pending.Count>0?"；"+pending.Count+" 块屏幕待恢复，请接回后再次恢复":"")+"。";Log(status);return status;
    }finally{guard.ReleaseMutex();}
   }
  }
 }
 public sealed class LightReader {
  LightSensor sensor;public string Status;public double? Lux;public DateTime LastGood=DateTime.MinValue;
  public LightReader(){try{sensor=LightSensor.GetDefault();if(sensor==null){Status="此电脑未检测到可用环境光传感器，无法实时跟随房间灯光颜色。";return;}sensor.ReportInterval=Math.Max(1000u,sensor.MinimumReportInterval);Status="已检测到照度传感器；可按明暗调节，不能测量环境光的颜色。";Read();}catch(Exception e){Status="传感器不可用："+e.Message;Storage.Log(Status);}}
  public bool Available{get{return sensor!=null;}}
  public void Read(){try{if(sensor==null)return;var r=sensor.GetCurrentReading();if(r!=null&&!Single.IsNaN(r.IlluminanceInLux)&&!Single.IsInfinity(r.IlluminanceInLux)&&r.IlluminanceInLux>=0&&Math.Abs((DateTimeOffset.Now-r.Timestamp).TotalSeconds)<30){Lux=r.IlluminanceInLux;LastGood=DateTime.UtcNow;}}catch(Exception e){Storage.Log("Sensor: "+e.Message);}if((DateTime.UtcNow-LastGood).TotalSeconds>30)Lux=null;}
 }
 public sealed class Controller:IDisposable {
  public List<DisplayState> Displays;bool watchdog,pendingRecovery;public string Message="";
  public Controller(){Storage.Recover(0);pendingRecovery=File.Exists(Storage.PathFor("recovery.xml"));Displays=Native.Displays();}
  void Persist() {
   using(var guard=new Mutex(false,"Local\\AmbientTone.Journal")){try{guard.WaitOne();}catch(AbandonedMutexException){}try{Storage.Save("recovery.xml",new Journal{Owner=Process.GetCurrentProcess().Id,Displays=Displays.Where(x=>x.Last!=null).ToList()});}finally{guard.ReleaseMutex();}}
   if(!watchdog){var start=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--watchdog "+Process.GetCurrentProcess().Id);start.UseShellExecute=false;start.CreateNoWindow=true;start.WindowStyle=ProcessWindowStyle.Hidden;Process.Start(start);watchdog=true;}
  }
  public bool Apply(double temperature,double strength) { if(pendingRecovery){Message="还有未恢复的显示设置，请先接回显示器、关闭 HDR 并恢复。";return false;}
   bool? hdr=Native.Hdr();if(hdr!=false){Message=hdr==true?"HDR / 高级颜色已开启，调节已暂停。":"无法确认 SDR 状态，调节已暂停。";Restore();return false;}
   if(Displays.Count==0){Message="没有可调节的显示设备。";return false;}
   foreach(var d in Displays)if(!Model.Close(Native.Read(d.Name),d.Last??d.Original,512)){Message="颜色被其他程序或系统改动，已暂停。请关闭其他调色工具后重新开启。";Restore();return false;}
   foreach(var d in Displays){d.Previous=d.Last==null?d.Original:(ushort[])d.Last.Clone();d.Last=Model.Ramp(d.Original,temperature,strength);}Persist();
   foreach(var d in Displays)if(!Native.Write(d.Name,d.Last)||!Model.Close(Native.Read(d.Name),d.Last,512)){Message="显卡驱动未接受调节，已尝试恢复。可使用 Windows 夜间模式。";Restore();return false;}
   Message="正在调节 "+Displays.Count+" 块屏幕 · 已核对显卡颜色设置";return true;
  }
  public void Refresh(){Restore();Storage.Recover(0);pendingRecovery=File.Exists(Storage.PathFor("recovery.xml"));Displays=Native.Displays();}
  public string Restore(){string result=Storage.Recover(Process.GetCurrentProcess().Id);foreach(var d in Displays){d.Last=null;d.Previous=null;}pendingRecovery=File.Exists(Storage.PathFor("recovery.xml"));return result;}
  public void Dispose(){Restore();}
 }
 public sealed class UI {
  public Window Window;Settings settings;Controller controller;LightReader light;bool ready,exiting,paused;double current=6500;int tick;double lastTemp=-1,lastStrength=-1;
  DispatcherTimer timer;Forms.NotifyIcon tray;EventWaitHandle resetSignal;BrightnessUI brightness;
  T Get<T>(string name)where T:class{return Window.FindName(name)as T;}
  public UI(bool preview) {
   using(var stream=typeof(UI).Assembly.GetManifestResourceStream("MainWindow.xaml"))Window=(Window)XamlReader.Load(stream);using(var icon=typeof(UI).Assembly.GetManifestResourceStream("AppIcon.png"))Window.Icon=BitmapFrame.Create(icon,BitmapCreateOptions.None,BitmapCacheOption.OnLoad);
   settings=preview?new Settings():Storage.Load<Settings>("settings.xml")??new Settings();settings.Validate();
   if(!preview)controller=new Controller();light=new LightReader();
   Get<ComboBox>("ModeBox").SelectedIndex=settings.Mode;
   for(int h=0;h<24;h++){Get<ComboBox>("DayBox").Items.Add(h.ToString("00")+":00");Get<ComboBox>("NightBox").Items.Add(h.ToString("00")+":00");}
   Get<ComboBox>("DayBox").SelectedIndex=settings.Day;Get<ComboBox>("NightBox").SelectedIndex=settings.Night;
   Get<Slider>("TemperatureSlider").Value=settings.Manual;Get<Slider>("StrengthSlider").Value=settings.Strength;Get<Slider>("DriftSlider").Value=settings.Drift;Get<CheckBox>("TrayBox").IsChecked=settings.Tray;
   Get<CheckBox>("SolarBox").IsChecked=settings.Solar;Get<CheckBox>("EnableBox").IsChecked=settings.Enabled;Get<TextBlock>("SensorText").Text=light.Status;
   Get<ComboBoxItem>("SensorItem").IsEnabled=light.Available;
   if(!light.Available&&settings.Mode==2){settings.Mode=1;Get<ComboBox>("ModeBox").SelectedIndex=1;}
   Get<TextBlock>("StatusText").Text=preview?"预览 · 当前没有改变显示设置":(controller.Displays.Count+" 块屏幕可检测 · 调节前会保存原始颜色");
   Get<CheckBox>("StartupBox").IsChecked=IsStartup();
   Get<ComboBox>("ModeBox").SelectionChanged+=Changed;Get<ComboBox>("DayBox").SelectionChanged+=Changed;Get<ComboBox>("NightBox").SelectionChanged+=Changed;
   Get<Slider>("TemperatureSlider").ValueChanged+=Changed;Get<Slider>("StrengthSlider").ValueChanged+=Changed;Get<Slider>("DriftSlider").ValueChanged+=Changed;
   Get<CheckBox>("EnableBox").Checked+=Changed;Get<CheckBox>("EnableBox").Unchecked+=Changed;
   Get<CheckBox>("SolarBox").Checked+=Changed;Get<CheckBox>("SolarBox").Unchecked+=Changed;Get<CheckBox>("TrayBox").Checked+=Changed;Get<CheckBox>("TrayBox").Unchecked+=Changed;
   Get<CheckBox>("StartupBox").Checked+=StartupChanged;Get<CheckBox>("StartupBox").Unchecked+=StartupChanged;
   Get<Button>("WarmButton").Click+=delegate{Preset(4200);};Get<Button>("SoftButton").Click+=delegate{Preset(5000);};Get<Button>("NeutralButton").Click+=delegate{Preset(6500);};
   Get<Button>("DriftResetButton").Click+=delegate{Get<Slider>("DriftSlider").Value=0;};Get<Button>("ResetButton").Click+=delegate{Reset();};Get<Button>("ExitButton").Click+=delegate{Exit();};
   Get<Button>("NightLightButton").Click+=delegate{try{Process.Start("ms-settings:nightlight");}catch(Exception e){Get<TextBlock>("StatusText").Text=e.Message;}};
   Window.SourceInitialized+=delegate{FitWindow();};brightness=new BrightnessUI(Get<StackPanel>("BrightnessPanel"),Get<TextBlock>("BrightnessStatus"),Get<Button>("RefreshBrightnessButton"),preview);ready=true;UpdateLabels();if(preview)return;
   resetSignal=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\AmbientTone.Reset");
   tray=new Forms.NotifyIcon();tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Process.GetCurrentProcess().MainModule.FileName);tray.Text="原彩显示";tray.Visible=true;
   var menu=new Forms.ContextMenuStrip();menu.Items.Add("打开原彩显示",null,delegate{Show();});menu.Items.Add("恢复原始显示",null,delegate{Reset();});menu.Items.Add("恢复并退出",null,delegate{Exit();});tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{Show();};
   Window.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!exiting&&settings.Tray){e.Cancel=true;Window.Hide();tray.ShowBalloonTip(2500,"原彩显示正在托盘运行","右下角托盘可恢复颜色或退出。",Forms.ToolTipIcon.Info);}else Cleanup();};
   Window.StateChanged+=delegate{if(Window.WindowState==WindowState.Minimized&&settings.Tray)Window.Hide();};
   SystemEvents.DisplaySettingsChanged+=DisplayChanged;SystemEvents.PowerModeChanged+=PowerChanged;SystemEvents.SessionEnding+=SessionEnding;
   timer=new DispatcherTimer();timer.Interval=TimeSpan.FromMilliseconds(1000);timer.Tick+=delegate{Tick();};timer.Start();
  }
  void FitWindow(){var work=SystemParameters.WorkArea;double width=Math.Max(1,work.Width-24),height=Math.Max(1,work.Height-24);Window.MinWidth=Math.Min(560,width);Window.MinHeight=Math.Min(480,height);Window.Width=Math.Min(760,width);Window.Height=Math.Min(830,height);Window.MaxWidth=work.Width;Window.MaxHeight=work.Height;Window.WindowStartupLocation=WindowStartupLocation.Manual;Window.Left=work.Left+(work.Width-Window.Width)/2;Window.Top=work.Top+(work.Height-Window.Height)/2;}
  public int VerifyBrightness(string path){Reset();try{return brightness.Verify(path);}finally{Reset();exiting=true;Window.Close();}}
  public int VerifyUI(string path) {
   var lines=new List<string>();int failures=0;Action<bool,string> check=delegate(bool ok,string name){lines.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;};
   try {
    Get<Slider>("DriftSlider").Value=0;Reset();check(!settings.Enabled,"Reset checkbox and saved state agree");
    Get<Button>("SoftButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(settings.Mode==0&&settings.Manual==5000,"Preset button selects manual mode and temperature");
    Get<Slider>("StrengthSlider").Value=55;Get<CheckBox>("EnableBox").IsChecked=true;
    for(int i=0;i<9;i++)Tick();check(!paused&&current==5000,"Enable control applies smooth temperature transition");
    check(controller.Displays.Count>0&&controller.Displays.All(d=>Model.Close(Native.Read(d.Name),Model.Ramp(d.Original,5000,.55),512)),"Manual UI state reaches physical display ramps");
    Get<Slider>("DriftSlider").Value=-500;for(int i=0;i<5;i++)Tick();check(settings.Drift==-500&&current==4500&&!paused&&controller.Displays.All(d=>Model.Close(Native.Read(d.Name),Model.Ramp(d.Original,4500,.55),512)),"Negative drift changes physical display output to warmer target");
    check(Storage.Load<Settings>("settings.xml").Drift==-500,"Drift is persisted across restarts");
    Get<Button>("DriftResetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));for(int i=0;i<5;i++)Tick();check(settings.Drift==0&&current==5000&&!paused,"Zero drift button restores base target");
    Get<Slider>("TemperatureSlider").Value=3600;Get<Slider>("DriftSlider").Value=-1500;check(TargetTemperature()==3600,"Warm drift respects lower output limit");
    Get<Slider>("TemperatureSlider").Value=6500;Get<Slider>("DriftSlider").Value=1500;check(TargetTemperature()==6500,"Cool drift respects original-color output limit");
    Get<Slider>("DriftSlider").Value=-500;Get<Button>("NeutralButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));check(settings.Drift==0&&TargetTemperature()==6500,"Original-color preset clears drift");
    Get<Button>("SoftButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Get<ComboBox>("ModeBox").SelectedIndex=1;Get<CheckBox>("SolarBox").IsChecked=true;Tick();check(settings.Solar&&settings.Mode==1&&!Get<ComboBox>("DayBox").IsEnabled,"Beijing solar mode disables manual schedule inputs");
    Get<Slider>("DriftSlider").Value=300;for(int i=0;i<8;i++)Tick();check(settings.Mode==1&&!paused&&Math.Abs(current-TargetTemperature())<2&&controller.Displays.All(d=>Model.Close(Native.Read(d.Name),Model.Ramp(d.Original,TargetTemperature(),.55),512)),"Positive drift adjusts Beijing automatic output without switching modes");Get<Slider>("DriftSlider").Value=0;
    Get<CheckBox>("SolarBox").IsChecked=false;check(Get<ComboBox>("DayBox").IsEnabled,"Fixed Beijing schedule can be selected");
    Get<ComboBox>("DayBox").SelectedIndex=settings.Night;check(settings.Day!=settings.Night&&Get<ComboBox>("DayBox").SelectedIndex==settings.Day,"Conflicting schedule selections are reverted");
    Get<CheckBox>("SolarBox").IsChecked=true;Get<Button>("ResetButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    check(!settings.Enabled&&controller.Displays.All(d=>Model.Close(Native.Read(d.Name),d.Original,0)),"Reset button restores exact original display ramps");
    check(Get<Button>("ResetButton").IsVisible,"Reset button remains visible without scrolling");check(Window.ActualHeight<=SystemParameters.WorkArea.Height+1&&Window.ActualWidth<=SystemParameters.WorkArea.Width+1,"Window fits current screen work area at high scaling");
    if(!light.Available)check(!Get<ComboBoxItem>("SensorItem").IsEnabled,"Unsupported ambient sensor mode is disabled");
   }finally{Reset();exiting=true;Window.Close();File.WriteAllLines(path,lines);}
   return failures==0?0:1;
  }
  void Changed(object sender,RoutedEventArgs e) {
   if(!ready)return;bool previouslyEnabled=settings.Enabled;
   int day=Get<ComboBox>("DayBox").SelectedIndex,night=Get<ComboBox>("NightBox").SelectedIndex;
   if(day==night){ready=false;Get<ComboBox>("DayBox").SelectedIndex=settings.Day;Get<ComboBox>("NightBox").SelectedIndex=settings.Night;ready=true;Get<TextBlock>("StatusText").Text="白天和夜晚开始时间需要不同。";return;}
   settings.Solar=Get<CheckBox>("SolarBox").IsChecked==true;settings.Day=day;settings.Night=night;settings.Mode=Get<ComboBox>("ModeBox").SelectedIndex;settings.Manual=(int)Get<Slider>("TemperatureSlider").Value;settings.Strength=(int)Get<Slider>("StrengthSlider").Value;settings.Drift=(int)Get<Slider>("DriftSlider").Value;
   settings.Enabled=Get<CheckBox>("EnableBox").IsChecked==true;settings.Tray=Get<CheckBox>("TrayBox").IsChecked==true;
   if(settings.Enabled&&!previouslyEnabled&&controller!=null){controller.Refresh();paused=false;lastTemp=-1;}
   if(!settings.Enabled&&controller!=null){Get<TextBlock>("StatusText").Text=controller.Restore();current=6500;lastTemp=-1;}
   UpdateLabels();SaveSettings();
  }
  void SaveSettings(){try{Storage.Save("settings.xml",settings);}catch(Exception e){Get<TextBlock>("StatusText").Text="设置保存失败："+e.Message;}}
  void UpdateLabels() {
   Get<StackPanel>("ManualPanel").Visibility=settings.Mode==0?Visibility.Visible:Visibility.Collapsed;Get<Grid>("FixedScheduleGrid").Visibility=settings.Solar?Visibility.Collapsed:Visibility.Visible;Get<TextBlock>("ManualValue").Text=settings.Manual+" K";Get<TextBlock>("StrengthValue").Text=settings.Strength+"%";Get<TextBlock>("DriftValue").Text=settings.Drift.ToString("+0;-0;0")+" K";Get<TextBlock>("DriftTargetText").Text="基础 "+Math.Round(BaseTemperature()).ToString("0")+" K · 漂移 "+settings.Drift.ToString("+0;-0;0")+" K → 目标 "+Math.Round(TargetTemperature()).ToString("0")+" K";
   Get<Slider>("TemperatureSlider").IsEnabled=settings.Mode==0;Get<CheckBox>("SolarBox").IsEnabled=settings.Mode==1;Get<ComboBox>("DayBox").IsEnabled=settings.Mode==1&&!settings.Solar;Get<ComboBox>("NightBox").IsEnabled=settings.Mode==1&&!settings.Solar;var sun=Beijing.Sun(Beijing.Now);Get<TextBlock>("BeijingText").Text="北京时间 "+Beijing.Now.ToString("MM月dd日 HH:mm")+" · 日出约 "+Beijing.Clock(sun[0])+" / 日落约 "+Beijing.Clock(sun[1]);
   Get<TextBlock>("TemperatureText").Text=settings.Enabled&&!paused?(Math.Round(current/50)*50).ToString("0")+" K":"原始颜色";
   Get<TextBlock>("StateText").Text=!settings.Enabled?"原始显示 · 调节未开启":paused?"调节已暂停":settings.Mode==0?"手动调节 · 平滑适应":settings.Mode==1?settings.Solar?"北京日照 · 随日出日落适应":"昼夜自动 · 根据北京时间":"环境光适应 · 根据照度估算";
   Get<TextBlock>("ModeHint").Text=settings.Mode==2?"依据明暗估算冷暖，不能测量环境光颜色。":settings.Mode==1?"依据北京时间调节冷暖，无法感知房间灯光颜色。":"手动匹配灯光；6500 K 保持原始颜色。";
   double[] color=Model.RGB(settings.Enabled&&!paused?current:6500),white=Model.RGB(6500);double strength=settings.Enabled&&!paused?settings.Strength/100.0:0;
   Get<Border>("PreviewSwatch").Background=new SolidColorBrush(Color.FromRgb((byte)255,(byte)(255*(1+(Math.Min(1,color[1]/white[1])-1)*strength)),(byte)(255*(1+(Math.Min(1,color[2]/white[2])-1)*strength))));
  }
  double BaseTemperature(){var sun=Beijing.Sun(Beijing.Now);return settings.Mode==0?settings.Manual:settings.Mode==1?Model.Scheduled(Beijing.Now.TimeOfDay.TotalHours,settings.Solar?sun[0]:settings.Day,settings.Solar?sun[1]:settings.Night):Model.FromLux(light.Lux??0);}
  double TargetTemperature(){return Model.Clamp(BaseTemperature()+settings.Drift,3600,6500);}
  void Preset(int value){if(value==6500)Get<Slider>("DriftSlider").Value=0;Get<ComboBox>("ModeBox").SelectedIndex=0;Get<Slider>("TemperatureSlider").Value=value;}
  void Tick() {
   try {
    if(resetSignal.WaitOne(0))Reset();tick++;
    if(tick%5==0&&light.Available){light.Read();Get<TextBlock>("SensorText").Text=light.Status+(light.Lux.HasValue?" 当前 "+light.Lux.Value.ToString("0")+" lx":" 暂无有效读数。");}
    if(tick%30==0)UpdateLabels();if(!settings.Enabled||paused)return;
    if(settings.Mode==2&&!light.Lux.HasValue){Get<TextBlock>("StatusText").Text=controller.Restore()+"环境光读数不可用，已暂停；重新开启可重试。";paused=true;lastTemp=-1;UpdateLabels();return;}
    double target=TargetTemperature();
    current+=Model.Clamp(target-current,-180,180);if(Math.Abs(current-target)<2)current=target;
    bool? hdr=Native.Hdr();if(hdr!=false){controller.Restore();paused=true;Get<TextBlock>("StatusText").Text=hdr==true?"HDR / 高级颜色已开启。已暂停调节；关闭 HDR 后重新开启原彩显示。":"无法确认 SDR 状态，已暂停调节。";UpdateLabels();return;}
    if(Math.Abs(current-lastTemp)>=10||lastStrength!=settings.Strength){if(!controller.Apply(current,settings.Strength/100.0))paused=true;lastTemp=current;lastStrength=settings.Strength;Get<TextBlock>("StatusText").Text=controller.Message;}
    else if(tick%5==0){foreach(var d in controller.Displays)if(!Model.Close(Native.Read(d.Name),d.Last??d.Original,512)){controller.Restore();paused=true;Get<TextBlock>("StatusText").Text="系统或其他程序改变了屏幕颜色，调节已暂停；可重新开启。";break;}}
    UpdateLabels();if(tray!=null)tray.Text="原彩显示 · "+(paused?"已暂停":((int)current)+" K");
   }catch(Exception e){Storage.Log(e.ToString());controller.Restore();paused=true;Get<TextBlock>("StatusText").Text="调节出错，已尝试恢复："+e.Message;UpdateLabels();}
  }
  public void Reset(){ready=false;Get<CheckBox>("EnableBox").IsChecked=false;ready=true;settings.Enabled=false;SaveSettings();paused=false;current=6500;lastTemp=-1;Get<TextBlock>("StatusText").Text=controller.Restore();UpdateLabels();}
  public void Exit(){Reset();exiting=true;Window.Close();}
  public void Emergency(){try{if(controller!=null)controller.Restore();}catch{}}
  void Cleanup(){if(exiting&&timer==null)return;if(brightness!=null){brightness.Dispose();brightness=null;}exiting=true;if(timer!=null){timer.Stop();timer=null;}Emergency();SystemEvents.DisplaySettingsChanged-=DisplayChanged;SystemEvents.PowerModeChanged-=PowerChanged;SystemEvents.SessionEnding-=SessionEnding;if(tray!=null){tray.Visible=false;var ownedIcon=tray.Icon;tray.Dispose();if(ownedIcon!=null)ownedIcon.Dispose();tray=null;}if(resetSignal!=null)resetSignal.Dispose();}
  void Show(){Window.Show();Window.WindowState=WindowState.Normal;Window.Activate();}
  void DisplayChanged(object s,EventArgs e){Window.Dispatcher.BeginInvoke(new Action(delegate{if(exiting)return;Reset();controller.Refresh();FitWindow();brightness.Refresh();Get<TextBlock>("StatusText").Text="显示设备发生变化，已恢复并关闭调节；请重新开启。";}));}
  void PowerChanged(object s,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Suspend)Window.Dispatcher.Invoke(new Action(delegate{Reset();}));else if(e.Mode==PowerModes.Resume)Window.Dispatcher.BeginInvoke(new Action(delegate{if(exiting)return;controller.Refresh();brightness.Refresh();Get<TextBlock>("StatusText").Text="电脑已唤醒，调节保持关闭；可重新开启。";}));}
  void SessionEnding(object s,SessionEndingEventArgs e){Emergency();}
  bool IsStartup(){try{using(var key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))return key!=null&&key.GetValue("AmbientTone")!=null;}catch{return false;}}
  void StartupChanged(object sender,RoutedEventArgs e){if(!ready)return;try{using(var key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")){if(Get<CheckBox>("StartupBox").IsChecked==true)key.SetValue("AmbientTone","\""+Process.GetCurrentProcess().MainModule.FileName+"\" --tray");else key.DeleteValue("AmbientTone",false);}}catch(Exception ex){ready=false;Get<CheckBox>("StartupBox").IsChecked=IsStartup();ready=true;Get<TextBlock>("StatusText").Text="开机启动设置失败："+ex.Message;}}
 }
 public static class Program {
  [STAThread]public static int Main(string[] args) {
   try {
    if(args.Length>0&&args[0]=="--watchdog"){int pid=Int32.Parse(args[1]);try{using(var p=Process.GetProcessById(pid))p.WaitForExit();}catch(ArgumentException){}Thread.Sleep(300);Storage.Recover(pid);return 0;}
    if(args.Length>0&&args[0]=="--recovery-test")return RecoveryTest(args[1]);if(args.Length>0&&args[0]=="--self-test")return SelfTest(args.Length>1?args[1]:"test-results.txt");
    if(args.Length>0&&args[0]=="--diagnose")return Diagnose(args.Length>1?args[1]:"diagnostics.txt");
    if(args.Length>0&&args[0]=="--preview"){var ui=new UI(true);ui.Window.Show();ui.Window.UpdateLayout();var content=(FrameworkElement)ui.Window.Content;var bmp=new RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(content);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(args[1]))enc.Save(f);ui.Window.Close();return 0;}
    bool created;using(var mutex=new Mutex(true,"Local\\AmbientTone.Instance",out created)) {
     if(!created){if(args.Contains("--reset")){using(var signal=EventWaitHandle.OpenExisting("Local\\AmbientTone.Reset"))signal.Set();}else MessageBox.Show("原彩显示已经在运行。请从右下角托盘打开。","原彩显示");return 0;}
     try {
      if(args.Contains("--brightness-test")){var prior=Storage.Load<Settings>("settings.xml");try{var testUI=new UI(false);testUI.Window.Show();testUI.Window.UpdateLayout();return testUI.VerifyBrightness(args[1]);}finally{if(prior!=null)Storage.Save("settings.xml",prior);}}if(args.Contains("--display-test"))return DisplayTest(args[1]);if(args.Contains("--ui-test")){var prior=Storage.Load<Settings>("settings.xml");try{var testUI=new UI(false);testUI.Window.Show();testUI.Window.UpdateLayout();return testUI.VerifyUI(args[1]);}finally{if(prior!=null)Storage.Save("settings.xml",prior);else if(File.Exists(Storage.PathFor("settings.xml")))File.Delete(Storage.PathFor("settings.xml"));}}if(args.Contains("--crash-probe")){var c=new Controller();if(!c.Apply(5000,.55))return 1;File.WriteAllText(args[1],"Warm ramp applied; ending process without cleanup");Environment.Exit(23);}
      if(args.Contains("--reset")){MessageBox.Show(Storage.Recover(0),"原彩显示 · 恢复显示");return 0;}
      var app=new Application();app.ShutdownMode=ShutdownMode.OnMainWindowClose;var ui=new UI(false);
      app.DispatcherUnhandledException+=delegate(object s,DispatcherUnhandledExceptionEventArgs e){ui.Emergency();Storage.Log(e.Exception.ToString());MessageBox.Show("出现错误，已尝试恢复原始颜色。\n"+e.Exception.Message,"原彩显示");e.Handled=true;app.Shutdown(1);};
      app.MainWindow=ui.Window;ui.Window.Show();if(args.Contains("--tray"))ui.Window.Hide();app.Run();return 0;
     }finally{mutex.ReleaseMutex();}
    }
   }catch(Exception e){Storage.Log(e.ToString());try{Storage.Recover(0);}catch{}if(args.Length>0&&args[0].StartsWith("--")&&args.Length>1){try{File.WriteAllText(args[1]+".error.txt",e.ToString());}catch{}}else MessageBox.Show("原彩显示无法继续运行：\n"+e.Message,"原彩显示");return 1;}
  }
  static int Diagnose(string path){var light=new LightReader();var displays=Native.Displays();File.WriteAllText(path,"AmbientTone diagnostics\r\nBeijing time: "+Beijing.Now.ToString("yyyy-MM-dd HH:mm")+"\r\nBeijing sunrise / sunset (estimated): "+Beijing.Clock(Beijing.Sun(Beijing.Now)[0])+" / "+Beijing.Clock(Beijing.Sun(Beijing.Now)[1])+"\r\nOS: "+Environment.OSVersion+"\r\nSensor: "+light.Status+"\r\nHDR: "+Native.Hdr()+"\r\nDisplays: "+displays.Count+"\r\n"+String.Join("\r\n",displays.Select(x=>x.Name+" "+x.Description+" gamma available")));return 0;}
  static int RecoveryTest(string path) {
   var originals=Native.Displays();if(originals.Count==0||Native.Hdr()!=false){File.WriteAllText(path,"SKIP: SDR display not available");return 2;}
   var info=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--crash-probe \""+path+".probe\"");info.UseShellExecute=false;info.CreateNoWindow=true;info.WindowStyle=ProcessWindowStyle.Hidden;
   int exit;using(var p=Process.Start(info)){if(!p.WaitForExit(15000)){File.WriteAllText(path,"FAIL: crash probe timeout");return 1;}exit=p.ExitCode;}
   bool restored=false;for(int i=0;i<40;i++){Thread.Sleep(250);restored=originals.All(x=>Model.Close(Native.Read(x.Name),x.Original,0));if(restored&&!File.Exists(Storage.PathFor("recovery.xml")))break;}
   File.WriteAllText(path,(exit==23?"PASS ":"FAIL ")+"Probe exited without cleanup\r\n"+(restored?"PASS ":"FAIL ")+"Watchdog restored exact original display ramps\r\n"+(!File.Exists(Storage.PathFor("recovery.xml"))?"PASS ":"FAIL ")+"Recovery journal cleared after verified restore");
   return exit==23&&restored&&!File.Exists(Storage.PathFor("recovery.xml"))?0:1;
  }
  static int DisplayTest(string path) {
   var lines=new List<string>();if(Native.Hdr()!=false){File.WriteAllText(path,"SKIP: SDR mode not confirmed");return 2;}
   var originals=Native.Displays();bool applied=false,restored=false;var c=new Controller();
   try{applied=c.Apply(5000,0.75);lines.Add((applied?"PASS ":"FAIL ")+"Hardware accepts warm ramp and readback matches: "+c.Message);Thread.Sleep(500);}
   finally{lines.Add(c.Restore());restored=originals.Count>0&&originals.All(x=>Model.Close(Native.Read(x.Name),x.Original,0));lines.Add((restored?"PASS ":"FAIL ")+"All displays restored to their original ramps exactly");File.WriteAllLines(path,lines);}
   return applied&&restored?0:1;
  }
  static int SelfTest(string path) {
   var lines=new List<string>();int failures=0;Action<bool,string> check=delegate(bool ok,string name){lines.Add((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;};
   var linear=Enumerable.Range(0,768).Select(i=>(ushort)((i%256)*257)).ToArray();
   check(Model.Close(Model.Ramp(linear,6500,1),linear,0),"6500 K preserves original ramps exactly");
   check(Model.Close(Model.Ramp(linear,3600,0),linear,0),"Zero strength preserves original ramps exactly");
   var warm=Model.Ramp(linear,3600,1);check(warm[255]==65535&&warm[511]<65535&&warm[767]<warm[511],"Warm adjustment reduces blue without boosting channels");
   bool monotone=true;for(int c=0;c<3;c++)for(int i=1;i<256;i++)if(warm[c*256+i]<warm[c*256+i-1])monotone=false;check(monotone,"Ramps remain monotonic");
   check(Math.Abs(Model.Scheduled(12,7,19)-5700)<1&&Math.Abs(Model.Scheduled(1,7,19)-4200)<1,"Schedule crosses midnight correctly");
   check(Math.Abs(Model.Scheduled(7,7,19)-4200)<1&&Math.Abs(Model.Scheduled(8,7,19)-5700)<1&&Math.Abs(Model.Scheduled(19.5,7,19)-4950)<1,"Transitions are continuous and take 60 minutes");
   check(Math.Abs(Model.Scheduled(2,19,7)-5700)<1&&Math.Abs(Model.Scheduled(12,19,7)-4200)<1,"Reversed day/night intervals work");
   bool rejected=false;try{Model.Scheduled(12,7,7);}catch(ArgumentException){rejected=true;}check(rejected,"Equal schedule times are rejected");
   check(Model.FromLux(0)==4200&&Model.FromLux(10000)==6200,"Ambient fallback stays inside safe temperature range");
   check(!Model.Close(null,linear,512),"Missing display readback cannot be treated as success");
   var state=new Settings{Mode=99,Manual=-100,Strength=500,Day=40,Night=40};state.Validate();check(state.Mode==2&&state.Manual==3600&&state.Strength==100&&state.Day!=state.Night,"Invalid settings are constrained");
   var sample=new Journal{Owner=123,Displays=new List<DisplayState>{new DisplayState{Name="test",Id="identity",Original=linear,Last=warm,Previous=linear}}};
   using(var s=new MemoryStream()){new XmlSerializer(typeof(Journal)).Serialize(s,sample);s.Position=0;var loaded=(Journal)new XmlSerializer(typeof(Journal)).Deserialize(s);check(loaded.Owner==123&&Model.Close(loaded.Displays[0].Original,linear,0),"Recovery journal preserves full 16-bit ramps");}
   var summer=Beijing.Sun(new DateTime(2026,6,21));var winter=Beijing.Sun(new DateTime(2026,12,21));check(summer[0]>4.4&&summer[0]<5.1&&summer[1]>19.3&&summer[1]<20.1,"Beijing summer solar times within expected range");check(winter[0]>7.2&&winter[0]<8&&winter[1]>16.5&&winter[1]<17.2,"Beijing winter solar times within expected range");check(Math.Abs((Beijing.Now-DateTime.UtcNow).TotalHours-8)<0.01,"Beijing clock stays UTC+8 independent of system timezone");check(Math.Abs(Model.Scheduled(summer[0]+0.5,summer[0],summer[1])-4950)<1,"Solar transitions support fractional sunrise times");File.WriteAllLines(path,lines);return failures==0?0:1;
  }
 }
}





