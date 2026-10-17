using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;

public sealed partial class BO6CheckpointBridge {
    const long LuaHookRva=0x4037B0;
    const int LuaHookLength=0x720;
    const string LuaHookHash="9862a33e05b09ee4be3ca7a4e950178851ecd235f1e2f3cf5091609e53b55b88";
    sealed class StartupReaderBusyException : IOException {}
    bool earlyProgressionPrepared;
    long earlyDllBase;
    string preparedHookHash;
    public bool EarlyProgressionPrepared {get{return earlyProgressionPrepared;}}
    public static Patch StartupSafehousePatch(long dllBase){
        return new Patch{Name="Restore native safehouse getters on future Lua loads",Address=dllBase+0x403BB0,Before=Hex("0F8542010000"),After=Hex("E94301000090")};
    }
    public static bool SafehouseCacheUnloaded(byte[] cache){
        if(cache==null||cache.Length!=0x100)throw new InvalidDataException("Unexpected safehouse cache block length");
        foreach(int offset in new int[]{0x48,0xC8,0x98,0xE0})if(BitConverter.ToInt64(cache,offset)!=0)return false;
        return true;
    }
    byte[] SafehouseCache(){return Read(earlyDllBase+0x4451B0,0x100);}
    void RequireSafehouseUnloaded(){
        byte[] cache=SafehouseCache();
        if(!SafehouseCacheUnloaded(cache)){
            Note("Safehouse transformed before early preparation: cache="+BitConverter.ToString(cache));
            throw new InvalidDataException(earlyProgressionPrepared?"После ранней подготовки обнаружена повторная подмена safehouse. Раннее изменение уже действует до закрытия игры. Закрой BO6 обычным способом; профиль и контрольные точки не удаляй.":"Ранний этап пропущен: Lua safehouse уже загружен с подменёнными функциями. Ранние изменения не применены. Закрой BO6 обычным способом и запускай через новый CMD; профиль и контрольные точки не удаляй.");
        }
    }
    public bool TryPrepareStartupProgression(long dllBase){
        if(earlyProgressionPrepared){VerifyStartupProgression();return true;}
        if(dllBase<65536)throw new ArgumentException("Invalid DLL base");
        earlyDllBase=dllBase;Stage="Early startup: read known Lua hook";
        byte[] code;
        try{
            code=Read(dllBase+LuaHookRva,LuaHookLength);
            if(Hash(code)!=LuaHookHash){LastReadinessMessage="Known Lua hook is not ready or the DLL build differs.";return false;}
            RequireSafehouseUnloaded();
        }catch(Win32Exception e){
            if(e.NativeErrorCode!=299&&e.NativeErrorCode!=487&&e.NativeErrorCode!=998)throw;
            LastReadinessMessage=e.Message;return false;
        }
        Patch patch=StartupSafehousePatch(dllBase);
        Patch caption=CreateCaptionTimeoutPatch(dllBase);Patch[] earlyPatches={patch,caption};
        Guard guard=new Guard{Address=dllBase+LuaHookRva,Length=LuaHookLength,Hash=LuaHookHash};
        for(int attempt=0;attempt<20;attempt++){
            List<HeldThread> held=null;bool mutationStarted=false;
            try{
                Stage="Early startup: hold and inspect threads";Note("Early preparation attempt "+(attempt+1));
                held=Freeze();
                if(ThreadsBusy(held,new Guard[]{guard}))throw new StartupReaderBusyException();
                Stage="Early startup: revalidate while frozen";
                if(Hash(Read(guard.Address,guard.Length))!=guard.Hash)throw new InvalidDataException("Lua hook changed during early preparation");
                RequireSafehouseUnloaded();
                foreach(Patch item in earlyPatches)if(!Equal(Read(item.Address,item.Before.Length),item.Before))throw new InvalidDataException("Early Lua transform changed: "+item.Name);
                VerifyExternalSuspensions(held);
                byte[] after=(byte[])code.Clone();foreach(Patch item in earlyPatches)Buffer.BlockCopy(item.After,0,after,(int)(item.Address-guard.Address),item.After.Length);
                try{
                    Stage="Early startup: selective safehouse gate and caption timeout";mutationStarted=true;foreach(Patch item in earlyPatches)WriteCode(item.Address,item.After);
                    if(!Equal(Read(guard.Address,guard.Length),after))throw new IOException("Selective gate verification failed");
                }catch{
                    Exception rollback=null;for(int i=earlyPatches.Length-1;i>=0;i--)try{WriteCode(earlyPatches[i].Address,earlyPatches[i].Before);}catch(Exception e){rollback=e;}
                    if(rollback!=null)throw new IOException("Early rollback failed; close BO6 normally",rollback);throw;
                }
                preparedHookHash=Hash(after);earlyProgressionPrepared=true;Stage="EarlyPrepared";
                Note("Early safehouse gate prepared before first transform; hook="+preparedHookHash);LastReadinessMessage="";return true;
            }catch(StartupReaderBusyException){Note("Lua reader active; retrying after resuming threads; no write performed");}
            catch(IOException e){
                if(mutationStarted||!e.Message.StartsWith("Game threads are still starting."))throw;
                Note("Game thread enumeration is changing during early startup; retrying before writes");
            }catch(Win32Exception e){
                if(mutationStarted||e.NativeErrorCode!=156||attempt==19)throw;
                Note("Early temporary thread signal refusal; no write performed");
            }finally{if(held!=null)Thaw(held);}
            Thread.Sleep(100);
        }
        throw new IOException("Не удалось безопасно выполнить ранний этап до загрузки Lua. Закрой BO6 обычным способом; данные сохранений оставлены без изменений.");
    }
    public void VerifyStartupProgression(){
        if(!earlyProgressionPrepared)throw new InvalidOperationException("Early safehouse preparation is required before profile installation");
        if(Hash(Read(earlyDllBase+LuaHookRva,LuaHookLength))!=preparedHookHash)throw new IOException("Safehouse gate was replaced after early preparation");
        // The skipped branch must never have decoded its patch constants.
        RequireSafehouseUnloaded();VerifyCaptionLeaf();
    }
    public static string DescribeProgressionDvar(byte[] data,ulong expected){
        if(data==null)return "length=null expected=0x"+expected.ToString("X16");
        if(data.Length!=0x58)return "length="+data.Length+" expected_length=88 expected_hash=0x"+expected.ToString("X16");
        return "hash=0x"+BitConverter.ToUInt64(data,0).ToString("X16")+" expected_hash=0x"+expected.ToString("X16")+" flags=0x"+BitConverter.ToUInt32(data,0x10).ToString("X8")+" type="+data[0x15]+" modified="+data[0x16]+" current="+data[0x28]+" latched="+data[0x38]+" reset="+data[0x48];
    }
    public static void ValidateProgressionDvar(byte[] data,ulong hash){
        string fail="";
        if(data==null||data.Length!=0x58)fail="length";
        else {
            if(BitConverter.ToUInt64(data,0)!=hash)fail+="hash,";
            if(BitConverter.ToUInt32(data,0x10)!=0)fail+="flags,";
            if(data[0x15]!=0)fail+="type,";
            if(data[0x28]>1)fail+="current_range,";
            if(data[0x38]>1)fail+="latched_range,";
            if(data[0x48]>1)fail+="reset_range,";
        }
        if(fail.Length!=0)throw new InvalidDataException("Unexpected progression bool dvar: failed="+fail.TrimEnd(',')+" "+DescribeProgressionDvar(data,hash));
    }
    void RejectProgressionOverrides(long cheat,long dev){
        int count=BitConverter.ToInt32(Read(image+0xE9BFC80,4),0);
        if(count<0||count>10)throw new InvalidDataException("Unexpected dvar override table count");
        for(int i=0;i<count;i++){
            long address=image+0xE9BAAE0+i*0x810L;byte[] entry=Read(address,0x11);long ptr=BitConverter.ToInt64(entry,0);
            if((ptr==cheat||ptr==dev)&&entry[0x10]!=0)
                throw new InvalidDataException("A separate dvar override forces campaign unlocks; no unsafe override-table edit was attempted");
        }
    }
    void PlanCheatDvars(List<Patch> undo){
        Stage="Validate cheat and developer unlock dvars";
        long cheat=BitConverter.ToInt64(Read(image+0xF89F370,8),0),dev=BitConverter.ToInt64(Read(image+0xF89F378,8),0);
        if(cheat==dev)throw new InvalidDataException("Progression dvar pointers unexpectedly alias");
        RejectProgressionOverrides(cheat,dev);
        ulong[] hashes={0xB8B8E25BB75B206DUL,0x56F16EE8BDEF18ABUL};int i=0;
        foreach(long ptr in new long[]{cheat,dev}){
            Writable(ptr,0x58);byte[] data=Read(ptr,0x58);ulong expected=hashes[i++];
            Note("Progression dvar before writes: pointer=0x"+ptr.ToString("X")+" "+DescribeProgressionDvar(data,expected));ValidateProgressionDvar(data,expected);
            undo.Add(new Patch{Name="Disable progression unlock dvar current",Address=ptr+0x28,Before=new byte[]{data[0x28]},After=new byte[]{0}});
            undo.Add(new Patch{Name="Disable progression unlock dvar latched",Address=ptr+0x38,Before=new byte[]{data[0x38]},After=new byte[]{0}});
            undo.Add(new Patch{Name="Disable progression unlock dvar reset",Address=ptr+0x48,Before=new byte[]{data[0x48]},After=new byte[]{0}});
        }
    }
    public void VerifyProgressionDvars(){
        long cheat=BitConverter.ToInt64(Read(image+0xF89F370,8),0),dev=BitConverter.ToInt64(Read(image+0xF89F378,8),0);
        RejectProgressionOverrides(cheat,dev);int i=0;
        foreach(long ptr in new long[]{cheat,dev}){
            byte[] data=Read(ptr,0x58);ValidateProgressionDvar(data,i++==0?0xB8B8E25BB75B206DUL:0x56F16EE8BDEF18ABUL);
            if(data[0x28]!=0||data[0x38]!=0||data[0x48]!=0)throw new IOException("Campaign cheat unlock was reenabled; local saving stopped");
        }
    }
}
