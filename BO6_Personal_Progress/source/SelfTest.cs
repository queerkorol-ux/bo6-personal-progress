using System;
using System.IO;
public static class BO6ProfileSelfTest {
    static void Need(bool condition,string message){if(!condition)throw new InvalidDataException("Self-test: "+message);}
    public static void Run(){
        Need(BO6CheckpointBridge.XxHash32(new byte[0],0,0)==0x02cc5d05U,"XXH32 empty vector");
        byte[] campaign=BO6CheckpointBridge.EmptyProfile(0),shared=BO6CheckpointBridge.EmptyProfile(3);
        byte[] ch=BO6CheckpointBridge.Hex("767308d24d5b29636de61d000000000000c0f3703b00");
        byte[] sh=BO6CheckpointBridge.Hex("76730866b0f4c9dcb3b761000000000000576cf75900");
        for(int i=0;i<22;i++){Need(campaign[i]==ch[i],"native campaign header");Need(shared[i]==sh[i],"native shared header");}
        var set=BO6CheckpointBridge.NewProfile();Need(set.Raw.Length==4,"profile count");
        for(int i=0;i<4;i++)BO6CheckpointBridge.ValidateProfile(set.Raw[i],i,true);
        Need((set.Raw[3][22]&15)==1,"initial slot");
        for(int i=0;i<23;i++){
            Need(set.Raw[0][22+(0x28000+0x258)/8+i]==(i==1?1:0),"campaign mission defaults");
            Need(set.Raw[3][23+i]==(i==1?1:0),"shared mission defaults");
        }
        Need(BitConverter.ToInt32(set.Raw[0],22+(0x28000+0x1180)/8+4)==0,"initial money");
        byte[] raw=(byte[])campaign.Clone();raw[100]=55;
        byte[] normalized=BO6CheckpointBridge.NormalizeProfile(raw,0);
        Need(normalized[100]==55&&raw[17]==campaign[17],"normalization must not modify source");
        BO6CheckpointBridge.ValidateProfile(normalized,0,true);
    }
}
