using System.Text;
using System.Text.RegularExpressions;
namespace Nashi.Core;
public static class RichTextAttachments
{
    public static async Task<string> Materialize(string folder,string work,CancellationToken token)
    {
        folder=Path.GetFullPath(folder);
        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("RTFD package cannot be a symbolic link.");
        var file=Path.Combine(folder,"TXT.rtf");if(!File.Exists(file)||new FileInfo(file).Length>20_000_000||(File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Choose a complete RTFD folder containing a regular TXT.rtf file.");
        long expanded=0;
        var rtf=Encoding.Latin1.GetString(await File.ReadAllBytesAsync(file,token));var result=Regex.Replace(rtf,@"\{\\NeXTGraphic\s+([^{}]+)\}",match=>
        {
            token.ThrowIfCancellationRequested();var name=Regex.Split(match.Groups[1].Value,@"\s+\\(?:width|height|baseline)",RegexOptions.None,TimeSpan.FromSeconds(1))[0].Trim().Replace("\\ "," ");var image=SafePaths.ArchiveEntry(folder,name);
            if(!File.Exists(image)||new FileInfo(image).Length>20_000_000||(File.GetAttributes(image)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("An RTFD attachment is missing or too large: "+name);
            for(var parent=Path.GetDirectoryName(image);parent is not null&&parent!=folder;parent=Path.GetDirectoryName(parent))
                if((File.GetAttributes(parent)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("RTFD attachment directories cannot be symbolic links.");
            var png=PhotoCodec.Png(image);expanded+=png.LongLength*2;if(expanded>100_000_000)throw new InvalidDataException("Expanded RTFD attachments exceed 100 MB.");using var dimensions=PhotoCodec.Read(image);return "{\\pict\\pngblip\\picw"+dimensions.Width+"\\pich"+dimensions.Height+"\\picwgoal"+(dimensions.Width*15)+"\\pichgoal"+(dimensions.Height*15)+"\n"+Convert.ToHexString(png)+"}";
        },RegexOptions.None,TimeSpan.FromSeconds(10));
        if(result.Contains("\\NeXTGraphic",StringComparison.Ordinal))throw new InvalidDataException("This RTFD attachment syntax could not be preserved.");var output=Path.Combine(work,"Rich document.rtf");await File.WriteAllBytesAsync(output,Encoding.Latin1.GetBytes(result),token);return output;
    }
}
