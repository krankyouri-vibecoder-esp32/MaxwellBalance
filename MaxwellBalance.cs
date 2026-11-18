using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;
[assembly: System.Reflection.AssemblyTitle("MaxwellBalance")]
[assembly: System.Reflection.AssemblyVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("0.2b")]

// Read-only RACE query, documented by junaydirfan/audezeforlinux:
// https://github.com/junaydirfan/audezeforlinux/blob/master/docs/PROTOCOL.md
// No commands which change headset settings are implemented.
static class Hid {
    [StructLayout(LayoutKind.Sequential)] struct Interface { public int size; public Guid guid; public int flags; public IntPtr reserved; }
    [StructLayout(LayoutKind.Sequential)] struct Attributes { public int size; public ushort vendor, product, version; }
    [StructLayout(LayoutKind.Sequential)] struct Caps {
        public ushort usage, page, input, output, feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] reserved;
        public ushort a,b,c,d,e,f,g,h,i,j;
    }
    [StructLayout(LayoutKind.Sequential)] struct Overlap { public IntPtr internalLow, internalHigh; public uint offset, offsetHigh; public IntPtr ev; }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attributes a);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_GetInputReport(SafeFileHandle h, [In,Out] byte[] b, int length);
    [DllImport("hid.dll", SetLastError=true)] static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] b, int length);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out Caps c);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, string e, IntPtr w, uint flags);
    [DllImport("setupapi.dll", SetLastError=true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, uint i, ref Interface data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref Interface d, IntPtr b, uint n, out uint required, IntPtr device);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string p, uint access, uint share, IntPtr security, uint mode, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadFile(SafeFileHandle h, IntPtr b, uint count, IntPtr read, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteFile(SafeFileHandle h, IntPtr b, uint count, IntPtr written, IntPtr ov);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out uint bytes, bool wait);
    [DllImport("kernel32.dll", SetLastError=true)] static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);

    internal sealed class Device : IDisposable {
        internal SafeFileHandle handle;
        internal ushort product;
        internal int input, output;
        internal byte route;
        public void Dispose() { handle.Dispose(); }
        internal void Diagnose(List<string> log) {
            foreach(byte target in new byte[]{route,0}) {
                foreach(byte[] command in new byte[][] {
                    new byte[]{5,0x5A,4,0,0x83,0x2C,0x0B,0},
                    new byte[]{5,0x5A,3,0,7,0x1C,0}}) {
                    log.Add("Route="+target.ToString("X2")+" command="+BitConverter.ToString(command));
                    byte[] request=new byte[output]; request[0]=6; request[1]=(byte)command.Length; request[2]=target;
                    Array.Copy(command,0,request,3,command.Length);
                    if(!HidD_SetOutputReport(handle,request,request.Length)) {log.Add("Control write failed="+Marshal.GetLastWin32Error());continue;}
                    var until=DateTime.UtcNow.AddSeconds(2); int empty=0;
                    while(DateTime.UtcNow<until) {
                        byte[] reply=new byte[input]; reply[0]=7;
                        bool ok=HidD_GetInputReport(handle,reply,reply.Length);
                        if(ok && reply[1]>0) log.Add("CONTROL "+BitConverter.ToString(reply));
                        else empty++;
                        try {
                            byte[] interrupt=new byte[input]; int n=Transfer(handle,interrupt,false,30);
                            log.Add("INTERRUPT "+BitConverter.ToString(interrupt,0,n));
                        } catch(IOException) {}
                        Thread.Sleep(20);
                    }
                    log.Add("Empty control reads="+empty);
                }
            }
        }
        internal int Query() {
            for(int i=0;i<8;i++) {
                byte[] old=new byte[input]; old[0]=7;
                if(!HidD_GetInputReport(handle,old,old.Length) || old[1]==0) break;
            }
            byte[] request = new byte[output];
            byte[] frame = {6,8,route,5,0x5A,4,0,0x83,0x2C,0x0B,0};
            Array.Copy(frame, request, frame.Length);
            Transfer(handle,request,true,600);
            var decoder=new BalanceDecoder();
            DateTime until=DateTime.UtcNow.AddMilliseconds(1500);
            while(DateTime.UtcNow<until) {
                Thread.Sleep(50);
                byte[] reply=new byte[input]; reply[0]=7;
                bool ok=HidD_GetInputReport(handle,reply,reply.Length);
                int value=ok?decoder.Feed(reply,reply.Length):-1;
                if(value>=0) return value;
            }
            throw new IOException("Geen geldig balansantwoord ontvangen");
        }
    }
    internal static int Parse(byte[] b, int n) {
        return new BalanceDecoder().Feed(b,n);
    }
    static int Transfer(SafeFileHandle h, byte[] bytes, bool write, int timeout) {
        using(var ev=new EventWaitHandle(false,EventResetMode.ManualReset)) {
            IntPtr data=Marshal.AllocHGlobal(bytes.Length), ov=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlap)));
            try {
                Marshal.Copy(bytes,0,data,bytes.Length);
                Marshal.StructureToPtr(new Overlap {ev=ev.SafeWaitHandle.DangerousGetHandle()},ov,false);
                bool ok=write ? WriteFile(h,data,(uint)bytes.Length,IntPtr.Zero,ov) : ReadFile(h,data,(uint)bytes.Length,IntPtr.Zero,ov);
                if(!ok && Marshal.GetLastWin32Error()!=997) throw new IOException("USB: "+Marshal.GetLastWin32Error());
                uint count;
                if(!ok && !ev.WaitOne(timeout)) {
                    CancelIoEx(h,ov);
                    GetOverlappedResult(h,ov,out count,true);
                    throw new IOException("Koptelefoon antwoordt niet");
                }
                if(!GetOverlappedResult(h,ov,out count,true)) throw new IOException("USB-verbinding verbroken");
                if(!write) Marshal.Copy(data,bytes,0,(int)count);
                return (int)count;
            } finally { Marshal.FreeHGlobal(data); Marshal.FreeHGlobal(ov); }
        }
    }
    internal static List<Device> Find() {
        var found=new List<Device>(); Guid guid; HidD_GetHidGuid(out guid);
        IntPtr set=SetupDiGetClassDevs(ref guid,null,IntPtr.Zero,18);
        if(set==new IntPtr(-1)) return found;
        try {
            for(uint i=0;;i++) {
                var info=new Interface {size=Marshal.SizeOf(typeof(Interface))};
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref info)) break;
                uint size; SetupDiGetDeviceInterfaceDetail(set,ref info,IntPtr.Zero,0,out size,IntPtr.Zero);
                if(size==0) continue;
                IntPtr buf=Marshal.AllocHGlobal((int)size);
                try {
                    Marshal.WriteInt32(buf,IntPtr.Size==8?8:6);
                    if(!SetupDiGetDeviceInterfaceDetail(set,ref info,buf,size,out size,IntPtr.Zero)) continue;
                    string path=Marshal.PtrToStringUni(IntPtr.Add(buf,4));
                    if(path.IndexOf("vid_3329",StringComparison.OrdinalIgnoreCase)<0) continue;
                    var h=CreateFile(path,0xC0000000,3,IntPtr.Zero,3,0x40000000,IntPtr.Zero);
                    bool keep=false;
                    try {
                        if(h.IsInvalid) continue;
                        var a=new Attributes {size=Marshal.SizeOf(typeof(Attributes))};
                        if(!HidD_GetAttributes(h,ref a) || a.vendor!=0x3329) continue;
                        if(a.product!=0x4B28 && a.product!=0x4B29 && a.product!=0x4B2E && a.product!=0x4B2A) continue;
                        IntPtr p; if(!HidD_GetPreparsedData(h,out p)) continue;
                        Caps caps; try { if(HidP_GetCaps(p,out caps)<0) continue; } finally { HidD_FreePreparsedData(p); }
                        if(caps.page!=0xFF13 || caps.usage!=1 || caps.input<62 || caps.output<62) continue;
                        found.Add(new Device {handle=h,product=a.product,input=caps.input,output=caps.output,route=(byte)((a.product==0x4B28||a.product==0x4B29)?0x80:0)});
                        keep=true;
                    } finally { if(!keep) h.Dispose(); }
                } finally { Marshal.FreeHGlobal(buf); }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return found;
    }
}

// A dongle may combine several RACE replies or split one across HID reports.
sealed class BalanceDecoder {
    readonly List<byte> stream=new List<byte>();
    internal int Feed(byte[] b,int n) {
        if(b==null || n<3 || n>b.Length || b[0]!=7 || b[1]>59 || b[1]+3>n) {stream.Clear();return -1;}
        for(int i=0;i<b[1];i++) stream.Add(b[i+3]);
        int found=-1;
        while(stream.Count>=4) {
            if(stream[0]!=5) {stream.RemoveAt(0);continue;}
            int length=4+stream[2]+256*stream[3];
            if(length<6 || length>1024) {stream.RemoveAt(0);continue;}
            if(stream.Count<length) break;
            if(length==10 && stream[1]==0x5B && stream[4]==0x83 && stream[5]==0x2C &&
                stream[6]==0 && stream[7]==0x0B && stream[8]==0 && stream[9]<=20) found=stream[9];
            stream.RemoveRange(0,length);
        }
        return found;
    }
}

// Connection/status changes must never masquerade as wheel movement.
sealed class BalanceChanges {
    int last=-1, candidate=-1, repeats;
    bool connected;
    internal bool Accept(int value) {
        if(value<0) { connected=false; candidate=-1; repeats=0; return false; }
        if(!connected) { connected=true; last=value; candidate=-1; repeats=0; return false; }
        if(value==last) { candidate=-1; repeats=0; return false; }
        if(candidate!=value) { candidate=value; repeats=1; return false; }
        if(++repeats<2) return false;
        last=value; candidate=-1; repeats=0; return true;
    }
}

sealed class BalanceWindow : Form {
    [DllImport("user32.dll", SetLastError=true)]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer hideTimer=new System.Windows.Forms.Timer();
    readonly System.Windows.Forms.Timer topTimer=new System.Windows.Forms.Timer();
    readonly CancellationTokenSource stop=new CancellationTokenSource();
    int balance=-1;
    readonly BalanceChanges changes=new BalanceChanges();
    string status="Maxwell 2 zoeken...";
    bool pinned=false;
    protected override bool ShowWithoutActivation { get { return true; } }
    protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08000000|0x80; return p; } }
    public BalanceWindow() {
        Text="MaxwellBalance 0.2b · Game / Chat"; ClientSize=new Size(410,135);
        FormBorderStyle=FormBorderStyle.None; ShowInTaskbar=false; TopMost=true;
        BackColor=Color.FromArgb(22,25,34); ForeColor=Color.White;
        Font=new Font("Segoe UI",10); DoubleBuffered=true;
        var menu=new ContextMenuStrip();
        menu.Items.Add("Balans tonen",null,delegate { Reveal(); });
        var pin=new ToolStripMenuItem("Zichtbaar houden") {CheckOnClick=true};
        pin.CheckedChanged+=delegate { pinned=pin.Checked; if(pinned) Reveal(); else hideTimer.Start(); };
        menu.Items.Add(pin);
        menu.Items.Add("Afsluiten",null,delegate { Close(); });
        ContextMenuStrip=menu;
        topTimer.Interval=250;
        topTimer.Tick+=delegate { KeepOnTop(); };
        VisibleChanged+=delegate { topTimer.Enabled=Visible; };
        tray=new NotifyIcon { Icon=SystemIcons.Information, Text="Maxwell 2 balans", Visible=true, ContextMenuStrip=menu };
        tray.DoubleClick+=delegate { Reveal(); };
        hideTimer.Interval=4000;
        hideTimer.Tick+=delegate { if(!pinned) Hide(); hideTimer.Stop(); };
        Shown+=delegate { Hide(); Task.Run((Action)Poll); };
        FormClosed+=delegate { stop.Cancel(); hideTimer.Dispose(); topTimer.Dispose(); tray.Dispose(); };
    }
    void KeepOnTop() {
        if(Visible && IsHandleCreated && !ContextMenuStrip.Visible)
            // HWND_TOPMOST; preserve size, position, and keyboard/game focus.
            SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x0001|0x0002|0x0010);
    }
    void Reveal() {
        Rectangle screen=Screen.PrimaryScreen.WorkingArea;
        Location=new Point(screen.Left+(screen.Width-Width)/2,screen.Bottom-Height-24);
        Show(); KeepOnTop(); Invalidate(); hideTimer.Stop(); hideTimer.Start();
    }
    void Publish(int value,string message) {
        if(IsDisposed || stop.IsCancellationRequested) return;
        try { BeginInvoke((Action)delegate {
            bool changed=changes.Accept(value);
            balance=value; status=message;
            tray.Text=value<0?"Maxwell 2: geen verbinding":"Maxwell 2: "+Describe(value);
            if(changed) Reveal(); else if(Visible) Invalidate();
        }); } catch(InvalidOperationException) {}
    }
    void Poll() {
        Hid.Device active=null;
        try {
            while(!stop.IsCancellationRequested) {
                try {
                    if(active==null) {
                        var devices=Hid.Find();
                        foreach(var d in devices) {
                            if(active!=null) { d.Dispose(); continue; }
                            try { int v=d.Query(); active=d; Publish(v,""); }
                            catch(IOException) { d.Dispose(); }
                        }
                        if(active==null) Publish(-1,"Geen verbinding · zet je Maxwell 2 aan");
                    } else Publish(active.Query(),"");
                } catch(Exception ex) {
                    if(active!=null) { active.Dispose(); active=null; }
                    Publish(-1,ex is IOException?"Geen antwoord · controleer de verbinding":"Uitlezen mislukt");
                }
                if(stop.Token.WaitHandle.WaitOne(active==null?2500:300)) break;
            }
        } finally { if(active!=null) active.Dispose(); }
    }
    static string Describe(int v) { return v==10?"Game en chat in balans": v<10?"Meer game · "+(10-v)+" stappen":"Meer chat · "+(v-10)+" stappen"; }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e); var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var title=new Font("Segoe UI",9,FontStyle.Bold))
        using(var muted=new SolidBrush(Color.FromArgb(156,165,185))) {
            g.DrawString("MAXWELL 2  /  GAME–CHAT",title,muted,20,14);
            g.DrawString("Game",Font,Brushes.White,20,43);
            g.DrawString("Chat",Font,Brushes.White,354,43);
            using(var track=new SolidBrush(Color.FromArgb(55,61,76))) g.FillRectangle(track,24,76,362,6);
            g.DrawLine(Pens.Gray,205,69,205,89);
            if(balance>=0) {
                float x=24+362*balance/20f;
                using(var accent=new SolidBrush(Color.FromArgb(95,211,220))) g.FillEllipse(accent,x-7,72,14,14);
            }
            string label=balance>=0?Describe(balance):status;
            g.DrawString(label,Font,muted,new RectangleF(10,100,390,28),new StringFormat {Alignment=StringAlignment.Center});
        }
    }
}

static class Program {
    [STAThread] static void Main(string[] args) {
        if(args.Length>0 && args[0]=="--diagnose") {
            var log=new List<string>(); var devices=Hid.Find();
            log.Add("Devices="+devices.Count);
            foreach(var device in devices) using(device) {
                log.Add("PID="+device.product.ToString("X4")); device.Diagnose(log);
            }
            File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"wireless-diagnostic.txt"),log);return;
        }
        if(args.Length>0 && args[0]=="--test") {
            var decoder=new BalanceDecoder();
            if(decoder.Feed(new byte[]{7,5,128,5,0x5B,6,0,0x83},8)!=-1 ||
               decoder.Feed(new byte[]{7,0,128},3)!=-1 ||
               decoder.Feed(new byte[]{7,5,128,0x2C,0,0x0B,0,15},8)!=15)
                throw new Exception("Fragmented wireless reply failed");
            byte[] combined={7,17,128,5,0x5B,3,0,0xD6,0x0C,0,5,0x5B,6,0,0x83,0x2C,0,0x0B,0,8};
            if(new BalanceDecoder().Feed(combined,combined.Length)!=8) throw new Exception("Combined replies failed");
            var changes=new BalanceChanges();
            foreach(int v in new[]{10,10,-1,10,10,11,10,10})
                if(changes.Accept(v)) throw new Exception("Spurious startup/reconnect/transient popup");
            if(changes.Accept(12) || !changes.Accept(12) || changes.Accept(12))
                throw new Exception("Confirmed change must notify exactly once");
            if(changes.Accept(-1) || changes.Accept(15) || changes.Accept(15))
                throw new Exception("Reconnect must be silent");
            byte[] reply={7,10,0x80,5,0x5B,6,0,0x83,0x2C,0,0x0B,0,10};
            foreach(int v in new[]{0,10,20}) {reply[12]=(byte)v; if(Hid.Parse(reply,13)!=v) throw new Exception("Valid response rejected");}
            reply[12]=21; if(Hid.Parse(reply,13)!=-1) throw new Exception("Range validation failed");
            reply[12]=10; reply[9]=1; if(Hid.Parse(reply,13)!=-1) throw new Exception("Error response accepted");
            reply[9]=0; if(Hid.Parse(reply,12)!=-1) throw new Exception("Truncated response accepted");
            reply[10]=7; if(Hid.Parse(reply,13)!=-1) throw new Exception("Wrong subcommand accepted");
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"PASS: response validation, fragmented and combined wireless replies; silent startup/reconnect/transients; confirmed movement notifies once.");
            return;
        }
        if(args.Length>0 && args[0]=="--probe") {
            var lines=new List<string>();
            var devices=Hid.Find(); lines.Add("Devices: "+devices.Count);
            foreach(var d in devices) using(d) {
                lines.Add("PID: "+d.product.ToString("X4")+" input="+d.input+" output="+d.output);
                try { lines.Add("Balance (0=game,10=center,20=chat): "+d.Query()); }
                catch(Exception ex) { lines.Add("Error: "+ex.Message); }
            }
            File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"probe-result.txt"),lines); return;
        }
        bool created;
        using(var mutex=new Mutex(true,"Local\\MaxwellBalanceViewer",out created)) {
            if(!created) return;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BalanceWindow());
        }
    }
}

