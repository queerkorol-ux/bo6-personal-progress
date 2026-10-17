using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public sealed class BO6CaptionNativePreflight:IDisposable {
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr VirtualAlloc(IntPtr address,UIntPtr size,uint allocation,uint protect);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualProtect(IntPtr address,UIntPtr size,uint protect,out uint old);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool VirtualFree(IntPtr address,UIntPtr size,uint free);
 [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr count);
 [UnmanagedFunctionPointer(CallingConvention.Winapi)]delegate int RunFixture(IntPtr fixture);
 readonly List<IntPtr> allocations=new List<IntPtr>();
 long fixture,record,header,raw,payload,cache,originalFixture,payloadFixture,patchedFixture,helper,baseline;
 RunFixture run;
 static byte[] Wrapper(){return Convert.FromBase64String("U1VWV0FUQVVBVkFXSIHssAAAAEiJjCSAAAAASItBKEiJRCQ4SI1EJPhIiYG4AAAASItZEEyNcQhIi4HYAAAAUJ1Ii0kYSLoi0iLSItIi0ki+UVFRUVFRUVFIv9HR0dHR0dHRSbgICAgICAgICEm5CQkJCQkJCQlJuhAQEBAQEBAQSbsREREREREREUm8EhISEhISEhJJvRMTExMTExMTSb8VFRUVFRUVFUi9vr6+vr6+vr5Ii4QkgAAAAEiLAP/Q/LgBAAAASIHEsAAAAEFfQV5BXUFcX15dW8M=");}
 const string WrapperHash="3007dd939ec5422c5bdef1263554e9aae6abed5ca314c5c45f87827d763de6c6";
 static byte[] Continuation(){return Convert.FromBase64String("UEiLhCSQAAAASIlYUEiLHCRIiVg4SIlIQEiJUEhIiXBYSIl4YEyJQGhMiUhwTIlQeEyJmIAAAABMiaCIAAAATImokAAAAEyJsJgAAABMibigAAAASImoqAAAAEiNXCQISImYwAAAAJxbSImYsAAAAEjHQDABAAAAWMM=");}
 const string ContinuationHash="336e25d6e58d33a9ae552e09e8857e0173a2711e8c8b384c0cea4ab4f09ce88d";
 long Alloc(int size){IntPtr p=VirtualAlloc(IntPtr.Zero,new UIntPtr((uint)size),0x3000,0x04);if(p==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());allocations.Add(p);return p.ToInt64();}
 static void Put(long p,byte[] b){Marshal.Copy(b,0,new IntPtr(p),b.Length);}
 static byte[] Read(long p,int count){byte[] b=new byte[count];Marshal.Copy(new IntPtr(p),b,0,count);return b;}
 static void Q(long p,long value){Marshal.WriteInt64(new IntPtr(p),value);}
 static long Q(long p){return Marshal.ReadInt64(new IntPtr(p));}
 long Code(byte[] b){long p=Alloc(b.Length);Put(p,b);uint old;if(!VirtualProtect(new IntPtr(p),new UIntPtr((uint)b.Length),0x20,out old)||!FlushInstructionCache(GetCurrentProcess(),new IntPtr(p),new UIntPtr((uint)b.Length)))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return p;}
 static void PatchQ(byte[] b,int offset,long value){Buffer.BlockCopy(BitConverter.GetBytes(value),0,b,offset,8);}
 static void Ensure(bool pass,string text){if(!pass)throw new InvalidOperationException("Native caption preflight: "+text);}
 BO6CaptionNativePreflight(){
  byte[] wrapper=Wrapper(),continuation=Continuation();Ensure(BO6CheckpointBridge.Hash(wrapper)==WrapperHash&&BO6CheckpointBridge.Hash(continuation)==ContinuationHash,"wrapper fixture hashes");
  fixture=Alloc(256);record=Alloc(96);header=Alloc(88);raw=Alloc(8192);payload=Alloc(4096);cache=Alloc(8);
  byte[] original=BO6CaptionFixture.Original(),compressed=BO6CaptionFixture.Compressed(),patched=BO6CaptionFixture.Patched();
  originalFixture=Alloc(original.Length);Put(originalFixture,original);payloadFixture=Alloc(compressed.Length);Put(payloadFixture,compressed);patchedFixture=Alloc(patched.Length);Put(patchedFixture,patched);
  long unmatched=Code(continuation);byte[] matched=(byte[])continuation.Clone();Buffer.BlockCopy(BitConverter.GetBytes(2),0,matched,116,4);long match=Code(matched);
  byte[] code=BO6CaptionFixture.Leaf();PatchQ(code,33,record);PatchQ(code,350,unmatched);PatchQ(code,360,cache);PatchQ(code,377,match);helper=Code(code);
  byte[] suffix=new byte[code.Length-331];Buffer.BlockCopy(code,331,suffix,0,suffix.Length);baseline=Code(suffix);
  run=(RunFixture)Marshal.GetDelegateForFunctionPointer(new IntPtr(Code(wrapper)),typeof(RunFixture));
 }
 void Init(int test){
  Put(fixture,new byte[256]);Put(record,new byte[96]);Put(header,new byte[88]);Put(raw,test==1?BO6CaptionFixture.Patched():BO6CaptionFixture.Original());Put(payload,BO6CaptionFixture.Compressed());
  ulong name=test==2?0x1111222233334444UL:test==3||test==11?0x75D36DFFED7F60B1UL:BO6CaptionFixture.AssetHash;
  Q(header,unchecked((long)name));Marshal.WriteInt32(new IntPtr(header+0x34),2568);Marshal.WriteByte(new IntPtr(header+0x39),1);Q(header+0x40,payload);
  Q(record+64,payloadFixture);Q(record+72,originalFixture);Q(record+80,patchedFixture);Q(fixture+8,header);Q(fixture+16,raw);Q(fixture+24,unchecked((long)0x75D36DFFED7F60B1UL));Q(fixture+40,0x1414141414141414L);Q(fixture+216,test>=10?0x602:0x202);Q(cache,0xD5E);
  if(test==4)Marshal.WriteInt32(new IntPtr(header+0x34),2567);
  if(test==5)Marshal.WriteByte(new IntPtr(header+0x39),0);
  if(test==6)Marshal.WriteByte(new IntPtr(payload+800),0x58);
  if(test==7)Marshal.WriteByte(new IntPtr(raw+900),0x58);
  if(test==8)Q(fixture+16,1);
  if(test==9)Q(header+0x40,1);
 }
 byte[] Run(long p){Q(fixture,p);Ensure(run(new IntPtr(fixture))==1,"fixture return");Ensure(Q(fixture+184)==Q(fixture+192),"stack pointer changed");return Read(fixture+48,136);}
 void Case(int test){
  Init(test);byte[] before=Read(raw,4653),expected=Run(baseline),actual=Run(helper);Ensure(BO6CheckpointBridge.Equal(expected,actual),"register/flags replay test="+test);
  bool apply=test==0||test==10;long cause=test==4||test==5?1:test==6?2:test==7?3:test==8||test==9?4:0;
  Ensure(Q(record+8)==(apply?1:0)&&Q(record+16)==(test==1?1:0)&&Q(record+32)==cause,"guard counters test="+test);
  Ensure(BO6CheckpointBridge.Equal(Read(raw,4653),apply?BO6CaptionFixture.Patched():before),"unexpected chunk write test="+test);
  Ensure(Q(fixture+48)==(test==3||test==11?2:1)&&Q(fixture+152)==0x1414141414141414L,"dispatch/R14 test="+test);
 }
 public void Dispose(){int firstError=0;for(int i=allocations.Count-1;i>=0;i--)if(!VirtualFree(allocations[i],UIntPtr.Zero,0x8000)&&firstError==0){firstError=Marshal.GetLastWin32Error();if(firstError==0)firstError=31;}allocations.Clear();if(firstError!=0)throw new System.ComponentModel.Win32Exception(firstError,"Native preflight allocation release failed");}
 public static int Run(){
  if(Environment.OSVersion.Platform!=PlatformID.Win32NT||!Environment.Is64BitProcess)throw new InvalidOperationException("Native preflight requires64-bitWindows; no game is launched");
  BO6CaptionFixture.Validate();BO6ProfileSelfTest.Run();
  Ensure(BO6CaptionFixture.Compressed()[800]!=0x58&&BO6CaptionFixture.Original()[900]!=0x58,"guard mutation fixture must differ from original");
  BO6CaptionNativePreflight test=null;
  try{test=new BO6CaptionNativePreflight();for(int i=0;i<12;i++)test.Case(i);GC.KeepAlive(test.run);return 12;}finally{if(test!=null)test.Dispose();}
 }
}
