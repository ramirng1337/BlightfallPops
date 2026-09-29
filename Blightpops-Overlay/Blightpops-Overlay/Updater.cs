using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace BlightfallPopsDesktop {
    internal sealed class ReleaseUpdate {
        public string Tag, DownloadUrl, Digest;
        public Version Version;
    }

    internal static class Updater {
        private const string ApiUrl="https://api.github.com/repos/ramirng1337/BlightfallPops/releases/latest";
        private const string AssetName="BlightfallPops.zip";
        private const string StagingPrefix="BlightfallPopsUpdate-";

        private static HttpWebRequest Request(string url){
            ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            var request=(HttpWebRequest)WebRequest.Create(url);
            request.UserAgent="BlightfallPops updater";
            request.Timeout=15000;
            request.ReadWriteTimeout=15000;
            request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
            return request;
        }

        internal static ReleaseUpdate Latest(){
            string json;
            using(var response=Request(ApiUrl).GetResponse())
            using(var reader=new StreamReader(response.GetResponseStream(),Encoding.UTF8))json=reader.ReadToEnd();
            var data=new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;
            if(data==null||!data.ContainsKey("tag_name"))throw new InvalidDataException("The release has no version tag.");
            string tag=Convert.ToString(data["tag_name"]);
            Version version;
            if(!Version.TryParse(tag.TrimStart('v','V'),out version))throw new InvalidDataException("Unrecognized release version: "+tag);
            var update=new ReleaseUpdate{Tag=tag,Version=version};
            var assets=data.ContainsKey("assets")?data["assets"] as IEnumerable:null;
            if(assets!=null)foreach(var item in assets){
                var asset=item as Dictionary<string,object>;
                if(asset==null||!asset.ContainsKey("name")||Convert.ToString(asset["name"])!=AssetName)continue;
                update.DownloadUrl=Convert.ToString(asset["browser_download_url"]);
                if(asset.ContainsKey("digest")&&asset["digest"]!=null)update.Digest=Convert.ToString(asset["digest"]);
                break;
            }
            return update;
        }

        internal static string Stage(ReleaseUpdate update){
            if(string.IsNullOrEmpty(update.DownloadUrl))throw new InvalidDataException("The latest release has no "+AssetName+" asset.");
            var uri=new Uri(update.DownloadUrl);
            if(uri.Scheme!="https"||!uri.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected release download address.");
            string directory=Path.Combine(Path.GetTempPath(),StagingPrefix+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string archive=Path.Combine(directory,AssetName);
            string stagedExe=Path.Combine(directory,"BlightfallPops.exe");
            try{
                using(var response=Request(update.DownloadUrl).GetResponse())
                using(var input=response.GetResponseStream())
                using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write)){
                    var buffer=new byte[65536];int read;long size=0;
                    while((read=input.Read(buffer,0,buffer.Length))>0){
                        size+=read;if(size>50000000)throw new InvalidDataException("The update is unexpectedly large.");
                        output.Write(buffer,0,read);
                    }
                }
                if(!string.IsNullOrEmpty(update.Digest)&&update.Digest.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)){
                    string actual;
                    using(var stream=File.OpenRead(archive))
                    using(var sha=SHA256.Create())actual=BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
                    if(!actual.Equals(update.Digest.Substring(7),StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("The downloaded ZIP failed its SHA-256 check.");
                }
                int matches=0;
                using(var stream=File.OpenRead(archive))
                using(var zip=new ZipArchive(stream,ZipArchiveMode.Read))foreach(var entry in zip.Entries){
                    if(!entry.Name.Equals("BlightfallPops.exe",StringComparison.OrdinalIgnoreCase))continue;
                    matches++;
                    using(var input=entry.Open())
                    using(var output=new FileStream(stagedExe,FileMode.Create,FileAccess.Write))input.CopyTo(output);
                }
                if(matches!=1||!File.Exists(stagedExe))throw new InvalidDataException("The ZIP must contain one BlightfallPops.exe.");
                if(!AssemblyName.GetAssemblyName(stagedExe).Version.Equals(update.Version))
                    throw new InvalidDataException("The EXE version does not match the release tag.");
                return directory;
            }catch{
                try{Directory.Delete(directory,true);}catch{}
                throw;
            }
        }

        internal static void StartInstaller(string directory,string target,bool framed){
            string stagedExe=Path.Combine(directory,"BlightfallPops.exe");
            var start=new ProcessStartInfo(stagedExe,
                "--apply-update \""+target+"\" "+Process.GetCurrentProcess().Id+" \""+directory+"\""+(framed?" --framed":""));
            start.UseShellExecute=false;
            start.WorkingDirectory=directory;
            Process.Start(start);
        }

        private static bool ValidStaging(string directory){
            string full=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
            string temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            return Path.GetDirectoryName(full).TrimEnd(Path.DirectorySeparatorChar).Equals(temp.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase)
                &&Path.GetFileName(full).StartsWith(StagingPrefix,StringComparison.OrdinalIgnoreCase);
        }

        internal static void Apply(string target,string pidText,string directory,bool framed){
            try{
                if(!ValidStaging(directory))throw new InvalidDataException("Invalid update location.");
                int pid;if(!int.TryParse(pidText,out pid))throw new InvalidDataException("Invalid process ID.");
                try{using(var original=Process.GetProcessById(pid))if(!original.WaitForExit(30000))
                    throw new IOException("Blightfall Pops did not close for the update.");}
                catch(ArgumentException){} // It already closed.
                string stagedExe=Path.Combine(directory,"BlightfallPops.exe");
                string backup=Path.Combine(directory,"previous.exe");
                File.Copy(target,backup,true);
                try{File.Copy(stagedExe,target,true);}
                catch{try{File.Copy(backup,target,true);}catch{}throw;}
                var start=new ProcessStartInfo(target,"--cleanup-update \""+directory+"\""+(framed?" --framed":""));
                start.WorkingDirectory=Path.GetDirectoryName(target);
                try{Process.Start(start);}
                catch{
                    try{File.Copy(backup,target,true);}catch{}
                    throw;
                }
            }catch(Exception ex){
                MessageBox.Show("The update could not be installed. If the overlay will not start, reinstall the previous release.\n\n"+ex.Message,
                    "Blightfall Pops update",MessageBoxButtons.OK,MessageBoxIcon.Error);
            }
        }

        internal static void CleanupLater(string directory){
            if(!ValidStaging(directory))return;
            Task.Run(delegate{
                for(int attempt=0;attempt<20;attempt++){
                    Thread.Sleep(1000);
                    try{Directory.Delete(directory,true);return;}catch{}
                }
            });
        }
    }
}
