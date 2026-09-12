using System;
using System.IO;
using System.Text;

public static class ClientCompatibilityTests {
    static void Check(bool value,string text) {if(!value)throw new Exception(text);}
    public static void Run(string directory,string scratch) {
        byte[] original=File.ReadAllBytes(Path.Combine(directory,"Wow.exe"));
        string expected=ClientCompatibility.Fingerprint(original);
        int pe=(int)BitConverter.ToUInt32(original,60),opt=pe+24;
        int table=opt+BitConverter.ToUInt16(original,pe+20),count=BitConverter.ToUInt16(original,pe+6);
        int resource=0,code=0,data=0,resourceHeader=0;
        for(int i=0;i<count;i++) {
            int s=table+40*i;string name=Encoding.ASCII.GetString(original,s,8).TrimEnd('\0');
            int raw=(int)BitConverter.ToUInt32(original,s+20);
            if(name==".rsrc"){resource=raw;resourceHeader=s;}
            if(name==".text")code=raw;if(name==".data")data=raw;
        }
        Check(resource>0 && code>0 && data>0,"Compatibility fixture sections");
        byte[] changed=(byte[])original.Clone();changed[pe+8]^=1;changed[opt+64]^=1;changed[resource+32]^=1;
        Check(ClientCompatibility.Digest(changed)!=ClientCompatibility.Digest(original),"Fixture has a new file checksum");
        Check(ClientCompatibility.Fingerprint(changed)==expected,"Harmless metadata/resources preserve profile");
        string file=Path.Combine(scratch,"compatible-copy.exe");File.WriteAllBytes(file,changed);
        Check(ClientCompatibility.ValidateFile(file).StartsWith("Kompatibles Profil"),"Unknown hash with verified profile accepted");
        foreach(int offset in new[]{code+32,data+32,opt+16,original.Length-1}) {
            changed=(byte[])original.Clone();changed[offset]^=1;File.WriteAllBytes(file,changed);
            bool rejected=false;try{ClientCompatibility.ValidateFile(file);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Changed code/data/layout/trailing bytes must fail: "+offset);
        }
        changed=(byte[])original.Clone();changed[resourceHeader+39]|=0x20;
        bool invalid=false;try{ClientCompatibility.Fingerprint(changed);}catch(InvalidDataException){invalid=true;}
        Check(invalid,"Executable resource section rejected");
        invalid=false;try{ClientCompatibility.Fingerprint(new byte[1024]);}catch(InvalidDataException){invalid=true;}
        Check(invalid,"Malformed PE rejected");
        File.Delete(file);
    }
}
