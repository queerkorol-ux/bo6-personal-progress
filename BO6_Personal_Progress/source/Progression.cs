using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public sealed partial class BO6CheckpointBridge {
    public sealed class ProfileSet { public byte[][] Raw; public string Hash; }
    ProfileSet pendingProfiles;
    readonly long[] profileBuffers=new long[4], profileDefinitions=new long[4];
    static readonly int[] ProfileSlots={16,17,18,20};
    static readonly byte[] ProfileMagic=Encoding.ASCII.GetBytes("BO6PRF01");
    const int CampaignSize=21092, SharedSize=151, ProfileBytes=3*CampaignSize+SharedSize;
    const ulong CampaignGuid=0x1DE66D63295B4DD2UL, SharedGuid=0x61B7B3DCC9F4B066UL;
    static int ProfileSize(int index) { if(index<0||index>3)throw new ArgumentOutOfRangeException("index"); return index==3?SharedSize:CampaignSize; }
    static uint Rotate(uint n,int bits) { return (n<<bits)|(n>>(32-bits)); }
    static uint Round(uint n,uint input) { unchecked {return Rotate(n+input*2246822519U,13)*2654435761U;} }
    public static uint XxHash32(byte[] b,int start,int count) {
        if(b==null||start<0||count<0||start>b.Length-count)throw new ArgumentException("XXH32 bounds");
        unchecked {
            int p=start,end=start+count;uint h;
            if(count>=16) {
                uint v1=2654435761U+2246822519U,v2=2246822519U,v3=0,v4=0U-2654435761U;
                do {v1=Round(v1,BitConverter.ToUInt32(b,p));v2=Round(v2,BitConverter.ToUInt32(b,p+4));v3=Round(v3,BitConverter.ToUInt32(b,p+8));v4=Round(v4,BitConverter.ToUInt32(b,p+12));p+=16;} while(p<=end-16);
                h=Rotate(v1,1)+Rotate(v2,7)+Rotate(v3,12)+Rotate(v4,18);
            } else h=374761393U;
            h+=(uint)count;
            while(p<=end-4){h=Rotate(h+BitConverter.ToUInt32(b,p)*3266489917U,17)*668265263U;p+=4;}
            while(p<end){h=Rotate(h+b[p]*374761393U,11)*2654435761U;p++;}
            h^=h>>15;h*=2246822519U;h^=h>>13;h*=3266489917U;h^=h>>16;return h;
        }
    }
    static void Put(byte[] b,int offset,byte[] value) {Buffer.BlockCopy(value,0,b,offset,value.Length);}
    static bool Zero(byte[] b) {foreach(byte v in b)if(v!=0)return false;return true;}
    public static void ValidateProfile(byte[] b,int index,bool checksum) {
        if(b==null||b.Length!=ProfileSize(index))throw new InvalidDataException("Profile length");
        if(b[0]!=0x76||b[1]!=0x73||b[2]!=8||BitConverter.ToUInt64(b,3)!=(index==3?SharedGuid:CampaignGuid)||BitConverter.ToUInt16(b,11)!=0)
            throw new InvalidDataException("Profile DDL header/schema mismatch");
        if(checksum&&BitConverter.ToUInt32(b,17)!=XxHash32(b,22,b.Length-22))throw new InvalidDataException("Profile DDL checksum mismatch");
    }
    public static byte[] NormalizeProfile(byte[] b,int index) {
        ValidateProfile(b,index,false);byte[] c=(byte[])b.Clone();
        Put(c,17,BitConverter.GetBytes(XxHash32(c,22,c.Length-22)));return c;
    }
    public static byte[] EmptyProfile(int index) {
        byte[] b=new byte[ProfileSize(index)];b[0]=0x76;b[1]=0x73;b[2]=8;
        Put(b,3,BitConverter.GetBytes(index==3?SharedGuid:CampaignGuid));return NormalizeProfile(b,index);
    }
    public static ProfileSet NewProfile() {
        byte[][] a=new byte[4][];
        for(int i=0;i<4;i++)a[i]=EmptyProfile(i);
        // Schema enum index 1 = the first mission; 0 is the 'none' sentinel.
        // Leave all other missions locked, and all money/perks/challenges at zero.
        for(int i=0;i<3;i++)a[i][22+(0x28000+0x258)/8+1]=1;
        a[0][22+0x29268/8]|=1; // slot 1 exists; worldObject remains invalid/empty.
        a[3][22]=1; // activeSaveSlot=1 in its low four bits.
        a[3][22+8/8+1]=1;
        a[3][22+1024/8]|=1;
        for(int i=0;i<4;i++)a[i]=NormalizeProfile(a[i],i);
        return MakeSet(a);
    }
    static byte[] ProfilePayload(ProfileSet set) {
        if(set==null||set.Raw==null||set.Raw.Length!=4)throw new InvalidDataException("Four profiles required");
        byte[] b=new byte[ProfileBytes];int p=0;
        for(int i=0;i<4;i++){ValidateProfile(set.Raw[i],i,true);Put(b,p,set.Raw[i]);p+=set.Raw[i].Length;}
        int active=set.Raw[3][22]&15;
        if(active<1||active>3)throw new InvalidDataException("Unsupported active profile slot");
        return b;
    }
    public static ProfileSet MakeSet(byte[][] raw) {
        ProfileSet s=new ProfileSet {Raw=raw};s.Hash=Hash(ProfilePayload(s));return s;
    }
    public static void SaveProfiles(string path,string exeHash,ProfileSet set) {
        byte[] body=ProfilePayload(set),b=new byte[80+ProfileBytes];
        Put(b,0,ProfileMagic);Put(b,8,Hex(exeHash));Put(b,40,BitConverter.GetBytes(ProfileBytes));Put(b,44,BitConverter.GetBytes(1));Put(b,48,Hex(Hash(body)));Put(b,80,body);
        string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(FileStream f=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){f.Write(b,0,b.Length);f.Flush(true);}
            if(File.Exists(path))File.Replace(tmp,path,path+".bak",true);else File.Move(tmp,path);
        }finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    public static ProfileSet ReadProfiles(string path,string exeHash) {
        if(new FileInfo(path).Length!=80+ProfileBytes)throw new InvalidDataException("Profile file length");
        byte[] b=File.ReadAllBytes(path),magic=new byte[8],exe=new byte[32],digest=new byte[32],body=new byte[ProfileBytes];
        Buffer.BlockCopy(b,0,magic,0,8);Buffer.BlockCopy(b,8,exe,0,32);Buffer.BlockCopy(b,48,digest,0,32);Buffer.BlockCopy(b,80,body,0,ProfileBytes);
        if(!Equal(magic,ProfileMagic)||!Equal(exe,Hex(exeHash))||BitConverter.ToInt32(b,40)!=ProfileBytes||BitConverter.ToInt32(b,44)!=1||!Equal(digest,Hex(Hash(body))))throw new InvalidDataException("Profile signature/build/hash mismatch");
        byte[][] raw=new byte[4][];int p=0;
        for(int i=0;i<4;i++){raw[i]=new byte[ProfileSize(i)];Buffer.BlockCopy(body,p,raw[i],0,raw[i].Length);p+=raw[i].Length;}
        return MakeSet(raw);
    }
    static byte[] DdlContext(long buffer,int length,long definition) {
        byte[] c=new byte[64];Put(c,0,BitConverter.GetBytes(buffer));Put(c,8,BitConverter.GetBytes(length));Put(c,16,BitConverter.GetBytes(definition));
        c[24]=1;Put(c,28,BitConverter.GetBytes(1));Put(c,48,BitConverter.GetBytes(-1));return c;
    }
    void Writable(long address,int length) {
        Region r;
        if(address<65536||VirtualQueryEx(handle,new IntPtr(address),out r,new UIntPtr(48)).ToUInt64()==0||r.State!=0x1000||(r.Protect&0x100)!=0||(r.Protect&0xCC)==0||r.Base+r.Size<(ulong)address+(ulong)length)
            throw new InvalidDataException("Profile buffer is not writable");
    }
    long Definition(int i) {
        byte[] d=Read(image+RegistryRva+ProfileSlots[i]*0x80,0x80);
        ulong key=i==3?0x442131C8F4D6D455UL:0x3CF8AD0058A5EF4FUL;
        if(BitConverter.ToInt32(d,4)!=0||(BitConverter.ToUInt64(d,0x28)&0x7FFFFFFFFFFFFFFFUL)!=key||BitConverter.ToInt32(d,0x38)!=ProfileSize(i))throw new InvalidDataException("Unexpected profile registry entry");
        long ptr=BitConverter.ToInt64(d,0x40);
        if(ptr!=BitConverter.ToInt64(d,0x60))throw new InvalidDataException("Profile schema versions differ");
        byte[] def=Read(ptr,0x58);
        if(BitConverter.ToUInt64(def,0x18)!=(i==3?SharedGuid:CampaignGuid)||BitConverter.ToInt32(def,0x3c)!=ProfileSize(i)||BitConverter.ToInt32(def,0x48)!=176||BitConverter.ToInt32(def,0x4c)!=22||BitConverter.ToUInt16(def,0x50)!=0)
            throw new InvalidDataException("Unsupported DDL definition");
        string expected=i==3?"ddl/cer/sp/cer_sp_shared_progression.ddl":"ddl/cer/sp/cer_sp_progression.ddl";
        byte[] name=Read(BitConverter.ToInt64(def,8),expected.Length+1);
        if(Encoding.ASCII.GetString(name)!=expected+"\0")throw new InvalidDataException("Unexpected DDL name");
        return ptr;
    }
    public bool PollProgressionReady(){
        if(!PollReady())return false;
        try {
            for(int i=0;i<4;i++){
                byte[] entry=Read(image+RegistryRva+ProfileSlots[i]*0x80,0x80);
                if(BitConverter.ToUInt64(entry,0x28)==0||BitConverter.ToInt32(entry,0x38)==0||BitConverter.ToInt64(entry,0x40)==0||BitConverter.ToInt64(entry,0x60)==0){
                    LastReadinessMessage="Profile DDL registration is not initialized yet: index="+i;return false;
                }
                Definition(i); // Fully validate schema, name, sizes and version agreement.
                byte[] meta=Read(image+SlotsRva+ProfileSlots[i]*0x70,0x70);
                long ptr=BitConverter.ToInt64(meta,8);
                if(ptr<65536){LastReadinessMessage="Profile storage is not allocated yet: index="+i;return false;}
                Writable(ptr,ProfileSize(i));
            }
            if(BitConverter.ToInt32(Read(image+0xB19D090,4),0)!=0){LastReadinessMessage="Local controller zero is not ready yet.";return false;}
            LastReadinessMessage="";return true;
        } catch(System.ComponentModel.Win32Exception e){
            if(e.NativeErrorCode!=299&&e.NativeErrorCode!=487&&e.NativeErrorCode!=998)throw;
            LastReadinessMessage=e.Message;return false;
        }
    }
    void PlanProfiles(List<Patch> undo,ProfileSet set,Restore[] checkpoints) {
        ProfilePayload(set);
        // Native progression getters need controller zero to be the active local player.
        if(BitConverter.ToInt32(Read(image+0xB19D090,4),0)!=0)throw new InvalidDataException("Local controller zero is not ready during automatic installation; no profile writes were performed. Close BO6 normally and retain the log ZIP.");
        for(int i=0;i<4;i++) {
            long a=image+SlotsRva+ProfileSlots[i]*0x70;
            byte[] meta=Read(a,0x70);long ptr=BitConverter.ToInt64(meta,8),def=Definition(i);
            Writable(ptr,ProfileSize(i));byte[] original=Read(ptr,ProfileSize(i));
            // The examined offline loader leaves these buffers and contexts zeroed.
            // Refuse to overwrite an unexpected live profile or another patch.
            byte[] context=new byte[64];Buffer.BlockCopy(meta,0x28,context,0,64);
            if(BitConverter.ToInt32(meta,0x10)!=0||!Zero(context)||!Zero(original))throw new InvalidDataException("Live profile is not empty. Close BO6 and all older patch windows; start with Run_BO6_Personal_Progress.cmd.");
            byte[] replacement=(byte[])meta.Clone();byte[] raw=(byte[])set.Raw[i].Clone();uint checksum=Checksum(raw);
            Put(replacement,0,BitConverter.GetBytes(checksum));Put(replacement,4,BitConverter.GetBytes(checksum));Put(replacement,0x10,BitConverter.GetBytes(2));
            Put(replacement,0x28,DdlContext(ptr,raw.Length,def));Put(replacement,0x68,BitConverter.GetBytes(raw.Length));
            undo.Add(new Patch {Address=ptr,Before=original,After=raw});undo.Add(new Patch {Address=a,Before=meta,After=replacement});
            profileBuffers[i]=ptr;profileDefinitions[i]=def;
        }
        long activeAddress=image+0xAF2F160;int active=set.Raw[3][22]&15;
        undo.Add(new Patch {Address=activeAddress,Before=Read(activeAddress,4),After=BitConverter.GetBytes(active)});
        // Do not inherit a checkpoint from a different profile/session.
        for(int slot=1;slot<=3;slot++) {
            bool restored=false;foreach(Restore r in checkpoints)if(r.Slot==slot)restored=true;
            if(restored)continue;
            long a=SlotAddress(slot);byte[] meta=Read(a,0x70),replacement=(byte[])meta.Clone();
            Array.Clear(replacement,0,8);Put(replacement,0x10,BitConverter.GetBytes(0));Put(replacement,0x68,BitConverter.GetBytes(0));
            undo.Add(new Patch {Address=a,Before=meta,After=replacement});
        }
    }
    public void InstallProgression(Patch[] patches,Guard[] guards,Restore[] checkpoints,ProfileSet profiles) {
        ProfilePayload(profiles);pendingProfiles=profiles;
        try {Install(patches,guards,checkpoints);}finally{pendingProfiles=null;}
    }
    byte[][] ReadProfilePass() {
        byte[][] raw=new byte[4][];
        for(int i=0;i<4;i++) {
            long a=image+SlotsRva+ProfileSlots[i]*0x70;byte[] meta=Read(a,0x70);int state=BitConverter.ToInt32(meta,0x10);
            if(state<0||state>11||((0xB04>>state)&1)==0)return null;
            if(BitConverter.ToInt64(meta,8)!=profileBuffers[i]||BitConverter.ToInt64(meta,0x28)!=profileBuffers[i]||BitConverter.ToInt32(meta,0x30)!=ProfileSize(i)||BitConverter.ToInt64(meta,0x38)!=profileDefinitions[i]||meta[0x5c]!=0)
                throw new InvalidDataException("Profile context changed. Disk saving stopped; close BO6.");
            raw[i]=Read(profileBuffers[i],ProfileSize(i));
            if(!Equal(meta,Read(a,0x70)))return null;
            ValidateProfile(raw[i],i,false);
        }
        return raw;
    }
    public ProfileSet CaptureProfiles() {
        byte[][] first=ReadProfilePass();if(first==null)return null;
        byte[][] second=ReadProfilePass();if(second==null)return null;
        for(int i=0;i<4;i++){if(!Equal(first[i],second[i]))return null;first[i]=NormalizeProfile(first[i],i);}
        int active=BitConverter.ToInt32(Read(image+0xAF2F160,4),0);
        if(active<1||active>3)return null;
        // Active slot is also kept outside DDL. Reflect it only in the disk snapshot.
        first[3][22]=(byte)((first[3][22]&0xf0)|active);first[3]=NormalizeProfile(first[3],3);
        return MakeSet(first);
    }
    public static string ProfileSummary(ProfileSet s) {
        int slot=s.Raw[3][22]&15;byte[] b=s.Raw[slot-1];int baseByte=22+0x28000/8;
        int unlocked=0,complete=0;
        for(int i=1;i<23;i++){byte v=s.Raw[3][22+1+i];if(v>=1)unlocked++;if(v==2)complete++;}
        int money=BitConverter.ToInt32(b,baseByte+0x1180/8+4);
        return "slot="+slot+" money="+money+" shared_unlocked="+unlocked+" shared_complete="+complete;
    }
}
