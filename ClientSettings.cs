using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

public sealed class ClientSettings {
    public int schema { get; set; }
    public string clientDirectory { get; set; }
    public static string DefaultFile { get {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AstralScanner","settings.json");} }
    public static string Load(string file) {
        if(!File.Exists(file))return null;
        if(new FileInfo(file).Length>8192)throw new InvalidDataException("Die gespeicherten Einstellungen sind ungültig. Bitte den WoW-Ordner neu auswählen.");
        var settings=new JavaScriptSerializer().Deserialize<ClientSettings>(File.ReadAllText(file));
        if(settings==null || settings.schema!=1)throw new InvalidDataException("Die gespeicherten Einstellungen sind ungültig. Bitte den WoW-Ordner neu auswählen.");
        return LiveReader.ValidateDirectory(settings.clientDirectory);
    }
    public static string Save(string file,string directory) {
        string valid=LiveReader.ValidateDirectory(directory);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
        string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new ClientSettings {schema=1,clientDirectory=valid}),Encoding.UTF8);
            if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
        } finally {if(File.Exists(temp))File.Delete(temp);}
        return valid;
    }
}
