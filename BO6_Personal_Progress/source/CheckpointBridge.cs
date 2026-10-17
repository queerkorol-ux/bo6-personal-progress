using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

public sealed partial class BO6CheckpointBridge : IDisposable {
    public sealed class Patch { public string Name; public long Address; public byte[] Before, After; }
    public sealed class Guard { public long Address; public int Length; public string Hash; }
    public sealed class Restore { public int Slot; public byte[] Raw; }
    public sealed class Snapshot { public int Slot; public byte[] Raw; public string Hash; }
    [StructLayout(LayoutKind.Sequential)] private struct Region {
        public ulong Base, AllocationBase; public uint AllocationProtect, Padding;
        public ulong Size; public uint State, Protect, Type, Padding2;
    }
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenProcess(uint a,bool i,int p);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,UIntPtr n,out UIntPtr r);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr p,IntPtr a,byte[] b,UIntPtr n,out UIntPtr r);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtectEx(IntPtr p,IntPtr a,UIntPtr n,uint f,out uint old);
    [DllImport("kernel32.dll",SetLastError=true)] static extern UIntPtr VirtualQueryEx(IntPtr p,IntPtr a,out Region r,UIntPtr n);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool FlushInstructionCache(IntPtr p,IntPtr a,UIntPtr n);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr OpenThread(uint a,bool i,uint id);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint SuspendThread(IntPtr t);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint ResumeThread(IntPtr t);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetThreadContext(IntPtr t,IntPtr c);
    [DllImport("kernel32.dll",SetLastError=true)] static extern uint WaitForSingleObject(IntPtr h,uint timeout);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationThread(IntPtr h,int informationClass,out uint value,uint length,out uint returned);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    readonly int pid; readonly long image; IntPtr handle;
    public readonly List<string> Diagnostics=new List<string>();
    public string Stage="Created";
    sealed class HeldThread { public IntPtr Handle; public int Id; public bool OwnSuspend; }
    uint SuspendCount(IntPtr thread,int id) {
        uint count,returned;
        int status=NtQueryInformationThread(thread,35,out count,4,out returned);
        if(status!=0||returned!=4)
            throw new IOException("Cannot confirm thread suspend count: thread="+id+" NTSTATUS=0x"+unchecked((uint)status).ToString("X8")+" returned="+returned);
        return count;
    }
    void VerifyExternalSuspensions(List<HeldThread> threads) {
        foreach(HeldThread t in threads)if(!t.OwnSuspend&&!Ended(t.Handle,t.Id)) {
            uint count=SuspendCount(t.Handle,t.Id);
            if(count!=127)throw new IOException("Externally suspended thread changed: thread="+t.Id+" count="+count+"; installation stopped");
        }
    }
    void Note(string text) { Diagnostics.Add(DateTime.UtcNow.ToString("o")+" "+text); }
    Win32Exception NativeError(string api,int code,string detail) {
        string message=api+" failed: Win32="+code+" (0x"+code.ToString("X")+") "+detail+"; "+new Win32Exception(code).Message;
        Note(message);return new Win32Exception(code,message);
    }
    bool Ended(IntPtr t,int id) {
        uint result=WaitForSingleObject(t,0);
        if(result==0)return true;
        if(result==258)return false;
        int error=Marshal.GetLastWin32Error();throw NativeError("WaitForSingleObject",error,"thread="+id);
    }
    const long SlotsRva=0xAB4E218, RegistryRva=0xF9CC530;
    const int Capacity=0xA00400, MaxBody=0x900000;
    static readonly byte[] FileMagic=System.Text.Encoding.ASCII.GetBytes("BO6LCL01");
    public BO6CheckpointBridge(int processId,long imageBase) {
        pid=processId; image=imageBase; handle=OpenProcess(0x438,false,pid);
        if(handle==IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public byte[] Read(long a,int n) {
        if(a<65536 || n<1 || n>16*1024*1024) throw new ArgumentException("Read bounds");
        byte[] b=new byte[n]; UIntPtr r;
        bool success=ReadProcessMemory(handle,new IntPtr(a),b,new UIntPtr((uint)n),out r);
        int error=success?0:Marshal.GetLastWin32Error();
        if(!success || r.ToUInt64()!=(ulong)n) {
            if(error==0)error=299;
            throw NativeError("ReadProcessMemory",error,"pid="+pid+" address=0x"+a.ToString("X")+" requested="+n+" read="+r.ToUInt64()+" stage="+Stage);
        }
        return b;
    }
    void Write(long a,byte[] b) {
        UIntPtr r;
        if(!WriteProcessMemory(handle,new IntPtr(a),b,new UIntPtr((uint)b.Length),out r) || r.ToUInt64()!=(ulong)b.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Cannot write game memory");
        if(!Equal(Read(a,b.Length),b)) throw new IOException("Memory write verification failed");
    }
    void WriteCode(long a,byte[] b) {
        uint old,unused;
        if(!VirtualProtectEx(handle,new IntPtr(a),new UIntPtr((uint)b.Length),0x40,out old)) throw NativeError("VirtualProtectEx",Marshal.GetLastWin32Error(),"address="+a.ToString("X"));
        try { Write(a,b); if(!FlushInstructionCache(handle,new IntPtr(a),new UIntPtr((uint)b.Length))) throw NativeError("FlushInstructionCache",Marshal.GetLastWin32Error(),"address="+a.ToString("X")); }
        finally { if(!VirtualProtectEx(handle,new IntPtr(a),new UIntPtr((uint)b.Length),old,out unused)) throw NativeError("VirtualProtectEx restore",Marshal.GetLastWin32Error(),"address="+a.ToString("X")); }
    }
    public static byte[] Hex(string s) { byte[] b=new byte[s.Length/2]; for(int i=0;i<b.Length;i++) b[i]=Convert.ToByte(s.Substring(i*2,2),16); return b; }
    public static string Hash(byte[] b) { using(SHA256 s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
    public static bool Equal(byte[] a,byte[] b) { if(a==null||b==null||a.Length!=b.Length)return false; for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true; }
    public static uint Checksum(byte[] b) {
        uint sum=0; int i=0;
        unchecked { for(;i+1<b.Length;i+=2)sum+=(uint)((b[i]<<8)|b[i+1]); if(i<b.Length)sum+=b[i]; }
        while((sum>>16)!=0)sum=(sum&65535)+(sum>>16);
        return (~sum)&65535;
    }
    public static void ValidateRaw(byte[] b) {
        if(b==null||b.Length<0x301||b.Length>Capacity)throw new InvalidDataException("Checkpoint size");
        if(BitConverter.ToUInt32(b,0)!=0x34C || BitConverter.ToUInt32(b,0x28)!=0xFA5897A5)throw new InvalidDataException("Checkpoint header");
        uint n=BitConverter.ToUInt32(b,0x50), extra=BitConverter.ToUInt32(b,0x2A0);
        if(n==0||n>MaxBody||extra!=0||b.Length!=0x300+n)throw new InvalidDataException("Unsupported checkpoint layout (extra blocks must be zero)");
        bool terminated=false;
        for(int i=0x54;i<0x94;i++) { if(b[i]==0){terminated=true;break;} if(b[i]<32||b[i]>126)throw new InvalidDataException("Invalid map name"); }
        if(!terminated||b[0x54]==0)throw new InvalidDataException("Map name missing");
    }
    public static byte[] ReadFile(string path,string exeHash,int slot) {
        FileInfo fi=new FileInfo(path);
        if(fi.Length<81||fi.Length>Capacity+80)throw new InvalidDataException("Local checkpoint file size");
        byte[] b=File.ReadAllBytes(path);
        for(int i=0;i<8;i++)if(b[i]!=FileMagic[i])throw new InvalidDataException("Local checkpoint signature");
        byte[] exe=Hex(exeHash);
        for(int i=0;i<32;i++)if(b[i+8]!=exe[i])throw new InvalidDataException("Checkpoint belongs to another EXE build");
        if(BitConverter.ToInt32(b,40)!=b.Length-80||BitConverter.ToInt32(b,44)!=slot)throw new InvalidDataException("Checkpoint slot/length");
        byte[] raw=new byte[b.Length-80];Buffer.BlockCopy(b,80,raw,0,raw.Length);ValidateRaw(raw);
        byte[] hash=Hex(Hash(raw));for(int i=0;i<32;i++)if(hash[i]!=b[48+i])throw new InvalidDataException("Checkpoint SHA256 mismatch");
        return raw;
    }
    public static void SaveFile(string path,string exeHash,int slot,byte[] raw) {
        ValidateRaw(raw);
        byte[] b=new byte[80+raw.Length];Buffer.BlockCopy(FileMagic,0,b,0,8);
        Buffer.BlockCopy(Hex(exeHash),0,b,8,32);Buffer.BlockCopy(BitConverter.GetBytes(raw.Length),0,b,40,4);
        Buffer.BlockCopy(BitConverter.GetBytes(slot),0,b,44,4);Buffer.BlockCopy(Hex(Hash(raw)),0,b,48,32);Buffer.BlockCopy(raw,0,b,80,raw.Length);
        string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(FileStream f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {f.Write(b,0,b.Length);f.Flush(true);}
            if(File.Exists(path)) File.Replace(tmp,path,path+".bak",true); else File.Move(tmp,path);
        } finally { if(File.Exists(tmp))File.Delete(tmp); }
    }
    long SlotAddress(int slot) { if(slot<1||slot>3)throw new ArgumentOutOfRangeException("slot");return image+SlotsRva+(12+slot)*0x70; }
    public string LastReadinessMessage="";
    // Only the initial, read-only polling loop may tolerate unavailable pages.
    // Installation still calls Ready() directly and aborts on any read failure.
    public bool PollReady() {
        if(Stage!="Created"&&Stage!="EarlyPrepared")throw new InvalidOperationException("Startup polling is only allowed before installation");
        try {
            bool ready=Ready();
            LastReadinessMessage=ready?"":"Storage is not initialized yet.";
            return ready;
        } catch(Win32Exception ex) {
            if(ex.NativeErrorCode!=299&&ex.NativeErrorCode!=487&&ex.NativeErrorCode!=998)throw;
            LastReadinessMessage=ex.Message;
            return false;
        }
    }
    public bool Ready() {
        if(BitConverter.ToInt32(Read(image+RegistryRva+0x1000,4),0)!=21)return false;
        for(int i=1;i<=3;i++) {
            byte[] d=Read(image+RegistryRva+(12+i)*0x80,0x80);
            if(BitConverter.ToInt32(d,4)!=2 || (BitConverter.ToUInt64(d,0x28)&0x7FFFFFFFFFFFFFFFUL)!=0x40176e9bca754047UL)return false;
            long p=BitConverter.ToInt64(Read(SlotAddress(i)+8,8),0);Region r;
            if(p<65536||VirtualQueryEx(handle,new IntPtr(p),out r,new UIntPtr(48)).ToUInt64()==0)return false;
            if(r.State!=0x1000||(r.Protect&0x100)!=0||(r.Protect&0xCC)==0||r.Base+r.Size<(ulong)p+Capacity)return false;
        }
        int active=BitConverter.ToInt32(Read(image+0xAF2F160,4),0);
        return active>=1&&active<=3;
    }
    public Snapshot Capture(int slot) {
        long a=SlotAddress(slot);byte[] meta=Read(a,0x70);int state=BitConverter.ToInt32(meta,0x10);
        if(state<0||state>11||((0xB04>>state)&1)==0)return null;
        int length=BitConverter.ToInt32(meta,0x68);if(length<0x301||length>Capacity)return null;
        long p=BitConverter.ToInt64(meta,8);byte[] raw=Read(p,length);
        if(!Equal(meta,Read(a,0x70)))return null;
        try {ValidateRaw(raw);}catch(InvalidDataException){return null;}
        uint sum=Checksum(raw);
        if(BitConverter.ToUInt32(meta,0)!=sum || BitConverter.ToUInt32(meta,4)!=sum)return null;
        return new Snapshot {Slot=slot,Raw=raw,Hash=Hash(raw)};
    }
    List<HeldThread> Freeze() {
        List<HeldThread> ts=new List<HeldThread>();
        try {
            HashSet<int> seen=new HashSet<int>();
            using(Process proc=Process.GetProcessById(pid)) for(int pass=0;pass<4;pass++) {
                proc.Refresh();int added=0;
                foreach(ProcessThread thread in proc.Threads) {
                    if(seen.Contains(thread.Id))continue;
                    int id=thread.Id;
                    // SYNCHRONIZE + QUERY_INFORMATION + GET_CONTEXT + SUSPEND_RESUME.
                    // Query access is needed to confirm a saturated suspend count.
                    IntPtr t=OpenThread(0x10004A,false,(uint)id);
                    if(t==IntPtr.Zero) {
                        int error=Marshal.GetLastWin32Error();
                        if(error==87){Note("Thread disappeared before OpenThread: "+id);continue;}
                        throw NativeError("OpenThread",error,"thread="+id);
                    }
                    bool retained=false;
                    try {
                        if(Ended(t,id)){Note("Skipping exited thread "+id);continue;}
                        bool ownSuspend=true;
                        if(SuspendThread(t)==uint.MaxValue){
                            int error=Marshal.GetLastWin32Error();
                            if(Ended(t,id)){Note("Thread exited during SuspendThread: "+id+" error="+error);continue;}
                            if(error!=156)throw NativeError("SuspendThread",error,"thread="+id);
                            uint count=SuspendCount(t,id);
                            Note("Signal refusal: thread="+id+" confirmed suspend count="+count);
                            if(count!=127)throw NativeError("SuspendThread",error,"thread="+id+" suspendCount="+count);
                            // This thread is already suspended to Windows' maximum.
                            // Keep it in all context/stack checks. Do not decrement
                            // a suspension count that this bridge did not increment.
                            ownSuspend=false;
                            Note("Retaining already suspended thread without ownership: "+id);
                        }
                        ts.Add(new HeldThread {Handle=t,Id=id,OwnSuspend=ownSuspend});retained=true;seen.Add(id);added++;
                    } finally {if(!retained)CloseHandle(t);}
                }
                if(added==0)break;
                if(pass==3)throw new IOException("Game threads are still starting. Wait for the campaign menu and retry.");
            }
            if(ts.Count==0)throw new IOException("No game threads");
            VerifyExternalSuspensions(ts);
            Note("Threads held or confirmed suspended: "+ts.Count);return ts;
        }catch{Thaw(ts);throw;}
    }
    void Thaw(List<HeldThread> ts) {
        Exception first=null;
        for(int i=ts.Count-1;i>=0;i--) {
            HeldThread t=ts[i];
            try {
                if(!t.OwnSuspend)continue;
                if(ResumeThread(t.Handle)==uint.MaxValue) {
                    int error=Marshal.GetLastWin32Error();
                    if(!Ended(t.Handle,t.Id))throw NativeError("ResumeThread",error,"thread="+t.Id+"; close BO6 before retrying");
                }
            } catch(Exception e){if(first==null)first=e;}
            finally{CloseHandle(t.Handle);}
        }
        if(first!=null)throw new IOException("Could not resume all game threads. Close BO6 before retrying.",first);
    }
    long[] ThreadPosition(HeldThread thread) {
        IntPtr block=Marshal.AllocHGlobal(1248);
        try {
            long aligned=(block.ToInt64()+15)&~15L;IntPtr context=new IntPtr(aligned);
            Marshal.Copy(new byte[1232],0,context,1232);
            Marshal.WriteInt32(context,48,0x100001);
            if(!GetThreadContext(thread.Handle,context)) {
                int error=Marshal.GetLastWin32Error();
                if(Ended(thread.Handle,thread.Id)){Note("Thread exited during GetThreadContext: "+thread.Id);return null;}
                throw NativeError("GetThreadContext",error,"thread="+thread.Id);
            }
            return new long[] {Marshal.ReadInt64(context,248),Marshal.ReadInt64(context,152)};
        }finally{Marshal.FreeHGlobal(block);}
    }
    bool ThreadsBusy(List<HeldThread> ts,Guard[] guards) {
        bool busy=false;
                foreach(HeldThread t in ts) {
                    long[] position=ThreadPosition(t);if(position==null)continue;
                    long ip=position[0],sp=position[1];
                    foreach(Guard g in guards)if(ip>=g.Address&&ip<g.Address+g.Length)busy=true;
                    // A helper may currently be executing on behalf of a function
                    // being replaced. Check return addresses as well as RIP.
                    Region stackRegion;
                    if(VirtualQueryEx(handle,new IntPtr(sp),out stackRegion,new UIntPtr(48)).ToUInt64()==0)
                        throw new IOException("Cannot inspect a suspended game thread stack");
                    int stackBytes=(int)Math.Min(65536UL,stackRegion.Base+stackRegion.Size-(ulong)sp);
                    byte[] stack=Read(sp,stackBytes);
                    for(int offset=0;offset+8<=stack.Length;offset+=8) {
                        long address=BitConverter.ToInt64(stack,offset);
                        foreach(Guard g in guards)if(address>=g.Address&&address<g.Address+g.Length)busy=true;
                    }
                }
        return busy;
    }
    public void Install(Patch[] patches,Guard[] guards,Restore[] restores) {
        VerifyStartupProgression();
        Stage="Validate code";Note(Stage);
        foreach(Guard g in guards)if(Hash(Read(g.Address,g.Length))!=g.Hash)throw new InvalidDataException("Game code mismatch at "+g.Address.ToString("X"));
        foreach(Restore r in restores)ValidateRaw(r.Raw);
        for(int attempt=0;attempt<20;attempt++) {
            List<HeldThread> ts=null;bool busy=false;bool mutationStarted=false;
            List<Patch> undoCode=new List<Patch>();List<Patch> undoData=new List<Patch>();
            try {
                Stage="Suspend threads";Note("Install attempt "+(attempt+1));
                ts=Freeze();Stage="Inspect thread contexts";
                busy=ThreadsBusy(ts,guards);
                if(!busy) {
                    Stage="Validate frozen process";
                    VerifyStartupProgression();
                    if(!Ready())throw new InvalidDataException("Storage changed during installation");
                    foreach(Guard g in guards)if(Hash(Read(g.Address,g.Length))!=g.Hash)throw new InvalidDataException("Game code changed during installation at "+g.Address.ToString("X"));
                    foreach(Patch p in patches)if(!Equal(Read(p.Address,p.Before.Length),p.Before))throw new InvalidDataException("Patch bytes changed: "+p.Name);
                    // Capture all rollback data before the first mutation.
                    foreach(Restore r in restores) {
                        long a=SlotAddress(r.Slot);byte[] meta=Read(a,0x70);long ptr=BitConverter.ToInt64(meta,8);
                        byte[] replacement=(byte[])meta.Clone();uint checksum=Checksum(r.Raw);
                        Buffer.BlockCopy(BitConverter.GetBytes(checksum),0,replacement,0,4);
                        Buffer.BlockCopy(BitConverter.GetBytes(checksum),0,replacement,4,4);
                        Buffer.BlockCopy(BitConverter.GetBytes(2),0,replacement,0x10,4);
                        Buffer.BlockCopy(BitConverter.GetBytes(r.Raw.Length),0,replacement,0x68,4);
                        undoData.Add(new Patch {Address=ptr,Before=Read(ptr,r.Raw.Length),After=r.Raw});
                        undoData.Add(new Patch {Address=a,Before=meta,After=replacement});
                    }
                    // Profile buffers and contexts participate in the same rollback
                    // transaction, before the disabled progression writer is restored.
                    if(pendingProfiles!=null) PlanProfiles(undoData,pendingProfiles,restores);
                    PlanCheatDvars(undoData);
                    VerifyExternalSuspensions(ts);
                    try {
                        mutationStarted=true;Stage="Restore checkpoint buffers";Note(Stage);
                        foreach(Patch p in undoData)Write(p.Address,p.After);
                        foreach(Patch p in patches){Stage="Write patch: "+p.Name;Note(Stage);undoCode.Add(p);WriteCode(p.Address,p.After);}
                    }catch {
                        Note("Rolling back memory changes");
                        // The caller must not continue with an incomplete installation.
                        Exception rollback=null;
                        for(int i=undoCode.Count-1;i>=0;i--)try{WriteCode(undoCode[i].Address,undoCode[i].Before);}catch(Exception e){rollback=e;}
                        for(int i=undoData.Count-1;i>=0;i--)try{Write(undoData[i].Address,undoData[i].Before);}catch(Exception e){rollback=e;}
                        if(rollback!=null)throw new IOException("Rollback failed; close BO6 before retrying",rollback);
                        throw;
                    }
                    Stage="Installed";Note(Stage);return;
                }
                Note("Save functions busy; retrying before any write");
            }catch(IOException e) {
                if(mutationStarted||!e.Message.StartsWith("Game threads are still starting."))throw;
                Note("Thread enumeration still starting before writes; resume and retry");
            }catch(Win32Exception e) {
                // Retry signal refusal only before any mutation. Never ignore a live thread.
                if(mutationStarted||e.NativeErrorCode!=156||attempt==19)throw;
                Note("Temporary signal refusal before writes; resume all threads and retry");
            }finally {if(ts!=null)Thaw(ts);}
            Thread.Sleep(500);
        }
        throw new IOException("Save functions remained busy. Pause BO6 before retrying.");
    }
    public void VerifyPatches(Patch[] patches) {VerifyStartupProgression();VerifyProgressionDvars();foreach(Patch p in patches)if(!Equal(Read(p.Address,p.After.Length),p.After))throw new IOException("A patch was replaced: "+p.Name);}
    public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
}
