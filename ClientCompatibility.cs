using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

// Compatibility fingerprint, not a signature or proof of publisher identity.
// Keep all file bytes, layout, code and initialized data. Normalize ONLY the
// COFF timestamp, PE checksum and the read-only resource section's payload.
public static class ClientCompatibility {
    const string September12Profile="78908DDA484A6AAA61A1EDA10B6E13A7683C1600DD24215974FF3C0F01A894B1";
    static void Require(bool ok) {if(!ok)throw new InvalidDataException("Ungültiges oder nicht unterstütztes WoW-Dateiformat.");}
    internal static string Digest(byte[] bytes) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
    internal static string Fingerprint(byte[] source) {
        Require(source!=null && source.Length>=1024 && source.Length<=32*1024*1024);
        byte[] b=(byte[])source.Clone();
        Require(b[0]==0x4d && b[1]==0x5a);
        uint pe=BitConverter.ToUInt32(b,60);Require(pe>=64 && pe<=(uint)b.Length-256);
        int p=(int)pe,o=p+24;
        Require(BitConverter.ToUInt32(b,p)==0x4550 && BitConverter.ToUInt16(b,p+4)==0x14c);
        int count=BitConverter.ToUInt16(b,p+6),optional=BitConverter.ToUInt16(b,p+20);
        Require(count>0 && count<=96 && optional>=224 && (long)o+optional+count*40<=b.Length);
        Require(BitConverter.ToUInt16(b,o)==0x10b && BitConverter.ToUInt32(b,o+28)==0x400000);
        Require(BitConverter.ToUInt32(b,o+92)>=16);
        uint headers=BitConverter.ToUInt32(b,o+60),resourceVa=BitConverter.ToUInt32(b,o+112),resourceSize=BitConverter.ToUInt32(b,o+116);
        Require(headers>=(uint)(o+optional+count*40) && headers<=b.Length);
        int resourceStart=0,resourceLength=0;ulong resourceEnd=0;
        var starts=new uint[count];var sizes=new uint[count];var vas=new uint[count];var spans=new uint[count];
        for(int i=0;i<count;i++) {
            int s=o+optional+i*40;string name=Encoding.ASCII.GetString(b,s,8).TrimEnd('\0');
            uint virtualSize=BitConverter.ToUInt32(b,s+8),va=BitConverter.ToUInt32(b,s+12),size=BitConverter.ToUInt32(b,s+16),raw=BitConverter.ToUInt32(b,s+20),flags=BitConverter.ToUInt32(b,s+36);
            Require(size==0 || (raw>=headers && (ulong)raw+size<=(ulong)b.Length));
            starts[i]=raw;sizes[i]=size;vas[i]=va;spans[i]=Math.Max(size,virtualSize);
            for(int j=0;j<i;j++) {
                Require(size==0 || sizes[j]==0 || (ulong)raw+size<=starts[j] || (ulong)starts[j]+sizes[j]<=raw);
                Require(spans[i]==0 || spans[j]==0 || (ulong)va+spans[i]<=vas[j] || (ulong)vas[j]+spans[j]<=va);
            }
            if(name==".rsrc") {
                Require(resourceLength==0 && size>0 && (flags&0xa0000020u)==0 && (flags&0x40000040u)==0x40000040u);
                Require(resourceVa==va && resourceSize>0 && resourceSize<=size && resourceSize<=virtualSize);
                resourceStart=(int)raw;resourceLength=(int)size;resourceEnd=(ulong)va+spans[i];
            }
        }
        Require(resourceLength>0);
        // Entry point and all other mapped directories must stay outside resources.
        uint entry=BitConverter.ToUInt32(b,o+16);Require(entry<resourceVa || entry>=resourceEnd);
        for(int i=0;i<16;i++) {
            if(i==2 || i==4)continue; // resources; certificate file offset (still hashed)
            uint va=BitConverter.ToUInt32(b,o+96+i*8),size=BitConverter.ToUInt32(b,o+100+i*8);
            Require(size==0 || (ulong)va+size<=resourceVa || va>=resourceEnd);
        }
        Array.Clear(b,p+8,4);Array.Clear(b,o+64,4);Array.Clear(b,resourceStart,resourceLength);
        return Digest(b);
    }
    public static string ValidateFile(string path) {
        byte[] bytes;
        using(var file=File.OpenRead(path)) {
            Require(file.Length>=1024 && file.Length<=32*1024*1024);
            bytes=new byte[(int)file.Length];int read=0;
            while(read<bytes.Length){int n=file.Read(bytes,read,bytes.Length-read);if(n==0)throw new EndOfStreamException();read+=n;}
        }
        if(LiveReader.SupportedHash(Digest(bytes)))return "Bekannte Clientdatei";
        if(Fingerprint(bytes)==September12Profile)return "Kompatibles Profil: nur Zeitstempel, Prüfsumme oder Ressourceninhalt verändert";
        throw new InvalidDataException("Programm- oder Datenprofil dieser Wow.exe ist noch nicht geprüft. Nach diesem Clientupdate wird ein passendes Scanner-Update benötigt.");
    }
}
