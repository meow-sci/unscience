using System;
using System.IO;
using System.Linq;
using MeowSci.ByoMusicLib;
using MeowSci.KsaAbstractions;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
var loop = new RepeatPlayback();
Check(loop.Advance(99, false, true, 2) == PlaybackAction.Play, "First play is immediate");
Check(loop.Advance(99, true, true, 2) == PlaybackAction.None, "Never start overlapping playback");
Check(loop.Advance(99, false, true, 2) == PlaybackAction.None && loop.Remaining == 2, "Gap starts after completion, not since previous frame");
Check(loop.Advance(1, false, true, 2) == PlaybackAction.None && loop.Remaining == 1, "Count actual silence");
Check(loop.Advance(.5, false, true, 1) == PlaybackAction.Play, "Live shorter gap takes account of elapsed silence");
Check(loop.Advance(0, false, true, 0) == PlaybackAction.Play, "Zero gap can restart immediately after a completed channel");
Check(loop.Advance(0, false, false, 0) == PlaybackAction.Finish, "No repeat finishes after sound ends");
var waiting = new RepeatPlayback();
waiting.Advance(0,false,true,10);
waiting.Advance(0,false,true,10);
Check(waiting.Advance(1,false,false,10)==PlaybackAction.Finish, "Turning repeat off while waiting finishes");
var invalidDt = new RepeatPlayback();
invalidDt.Advance(0,false,true,1);
invalidDt.Advance(0,false,true,1);
Check(invalidDt.Advance(double.NaN,false,true,1)==PlaybackAction.None && invalidDt.Remaining==1, "Invalid delta cannot poison gap timer");
string root = Path.Combine(Path.GetTempPath(), "unscience-media-check-" + Guid.NewGuid());
try
{
    Directory.CreateDirectory(root);
    var library = new SharedFileLibrary("sounds","Sound",new[]{".ogg",".wav",".mp3"},root);
    string source = Path.Combine(root,"voice.OGG");
    File.WriteAllText(source,"first");
    Check(library.Import(source,out var error)=="voice.OGG" && error==null,"Import case-insensitive supported extension");
    File.WriteAllText(source,"second");
    Check(library.Import(source,out error)=="voice (2).OGG", "Duplicate names never overwrite");
    File.Delete(source);
    Check(File.ReadAllText(library.FullPath("voice.OGG"))=="first", "Import remains independent of original source");
    Check(library.Import(library.FullPath("voice.OGG"),out error)=="voice.OGG", "Import from catalog does not duplicate");
    File.WriteAllText(library.FullPath(".hidden.ogg"),"hidden");
    File.WriteAllText(library.FullPath("skip.txt"),"skip");
    Check(library.Scan().Length==2 && library.Scan().All(library.Supports), "Catalog ignores unsupported and hidden files");
    Check(library.Import(source,out error)==null && error!=null,"Missing file has actionable error");
    Check(library.Import(library.FullPath("skip.txt"),out error)==null && error!=null,"Reject unsupported imports");
    try { library.FullPath("../escape.ogg"); throw new Exception("Accepted traversal"); } catch (ArgumentException) { }
    var png = new SharedFileLibrary("pngs","PNG",new[]{".png"},root);
    source = Path.Combine(root,"image.png");
    File.WriteAllText(source,"png");
    Check(png.Import(source,out error)=="image.png" && png.Scan().Single()=="image.png", "Same import flow retains PNG behavior in separate catalog");
}
finally { if(Directory.Exists(root)) Directory.Delete(root,true); }
Console.WriteLine("PASS: repeat completion/gaps/live edits; copied catalogs, collision names, formats, PNG compatibility and path safety");

namespace MeowSci.KsaAbstractions
{
    internal static class KsaPaths { public static string ModDataDir => throw new Exception("Tests must use isolated catalogs"); }
}
