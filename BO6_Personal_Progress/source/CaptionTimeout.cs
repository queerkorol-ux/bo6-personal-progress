using System;
using System.IO;
using System.Runtime.InteropServices;
public sealed partial class BO6CheckpointBridge {
 [DllImport("kernel32.dll",EntryPoint="VirtualAllocEx",SetLastError=true)]static extern IntPtr CaptionAlloc(IntPtr process,IntPtr address,UIntPtr size,uint allocation,uint protect);
 long captionRecord,captionCode,captionDll;
 byte[] captionLeaf;
 Patch captionPatch;
 public static byte[] CaptionLeaf(long record,long dll){
  byte[] code=BO6CaptionFixture.Leaf();Put(code,33,BitConverter.GetBytes(record));Put(code,360,BitConverter.GetBytes(dll+0x445200));Put(code,350,BitConverter.GetBytes(dll+0x403EC1));Put(code,377,BitConverter.GetBytes(dll+0x403E86));return code;
 }
 Patch CreateCaptionTimeoutPatch(long dll){
  if(captionRecord!=0){if(captionDll!=dll)throw new InvalidDataException("Caption loader DLL base changed before installation");return captionPatch;}
  BO6CaptionFixture.Validate();captionDll=dll;
  byte[] original=Hex("498b064c8b7424404839087542488b0d7a130400");
  if(!Equal(Read(dll+0x403E72,20),original))throw new InvalidDataException("Checkpoint caption loader splice differs from examined build");
  byte[] compressed=BO6CaptionFixture.Compressed(),raw=BO6CaptionFixture.Original(),patched=BO6CaptionFixture.Patched();
  int size=96+compressed.Length+raw.Length+patched.Length;
  IntPtr allocation=CaptionAlloc(handle,IntPtr.Zero,new UIntPtr((uint)size),0x3000,0x04);
  if(allocation==IntPtr.Zero)throw NativeError("VirtualAllocEx caption record",Marshal.GetLastWin32Error(),"bytes="+size);
  captionRecord=allocation.ToInt64();byte[] record=new byte[size];long compressedPtr=captionRecord+96,rawPtr=compressedPtr+compressed.Length,patchedPtr=rawPtr+raw.Length;
  Put(record,64,BitConverter.GetBytes(compressedPtr));Put(record,72,BitConverter.GetBytes(rawPtr));Put(record,80,BitConverter.GetBytes(patchedPtr));
  Put(record,96,compressed);Put(record,96+compressed.Length,raw);Put(record,96+compressed.Length+raw.Length,patched);Write(captionRecord,record);
  captionLeaf=CaptionLeaf(captionRecord,dll);allocation=CaptionAlloc(handle,IntPtr.Zero,new UIntPtr((uint)captionLeaf.Length),0x3000,0x04);
  if(allocation==IntPtr.Zero)throw NativeError("VirtualAllocEx caption leaf",Marshal.GetLastWin32Error(),"bytes="+captionLeaf.Length);
  captionCode=allocation.ToInt64();Write(captionCode,captionLeaf);uint old;
  if(!VirtualProtectEx(handle,new IntPtr(captionCode),new UIntPtr((uint)captionLeaf.Length),0x20,out old))throw NativeError("VirtualProtectEx caption leaf",Marshal.GetLastWin32Error(),"address="+captionCode.ToString("X"));
  if(!FlushInstructionCache(handle,new IntPtr(captionCode),new UIntPtr((uint)captionLeaf.Length)))throw NativeError("FlushInstructionCache caption leaf",Marshal.GetLastWin32Error(),"address="+captionCode.ToString("X"));
  byte[] jump=new byte[20];for(int i=0;i<jump.Length;i++)jump[i]=0x90;jump[0]=0xFF;jump[1]=0x25;for(int i=2;i<6;i++)jump[i]=0;Put(jump,6,BitConverter.GetBytes(captionCode));
  captionPatch=new Patch{Name="Checkpoint caption visual two-second timeout on future Lua loads",Address=dll+0x403E72,Before=original,After=jump};return captionPatch;
 }
 public sealed class CaptionState {
  public long Observed,Applied,AlreadyPatched,Mismatched,Cause,Header,Buffer;
  public string Summary {get{return "seen="+Observed+" applied="+Applied+" already="+AlreadyPatched+" mismatch="+Mismatched+" cause="+Cause+" header=0x"+Header.ToString("X")+" buffer=0x"+Buffer.ToString("X");}}
 }
 public CaptionState ReadCaptionState(){
  if(captionRecord==0)return new CaptionState();byte[] r=Read(captionRecord,56);
  return new CaptionState{Observed=BitConverter.ToInt64(r,0),Applied=BitConverter.ToInt64(r,8),AlreadyPatched=BitConverter.ToInt64(r,16),Mismatched=BitConverter.ToInt64(r,24),Cause=BitConverter.ToInt64(r,32),Header=BitConverter.ToInt64(r,40),Buffer=BitConverter.ToInt64(r,48)};
 }
 void VerifyCaptionLeaf(){if(captionCode==0||!Equal(Read(captionCode,captionLeaf.Length),captionLeaf))throw new IOException("Caption leaf code changed after preparation");}
 // Allocations belong to this game process and remain while the installed jump is reachable.
 // This leaf never calls any native Lua API and never modifies progression/storage/task state.
}
