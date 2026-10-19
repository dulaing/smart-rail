param(
  [Parameter(Mandatory=$true)][string]$Reference,
  [Parameter(Mandatory=$true)][string]$Actual,
  [Parameter(Mandatory=$true)][string]$OutputPrefix,
  [int]$CropTop = 0,
  [int]$CropBottom = 0
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class DriverImageComparison {
  public static double[] Compare(Bitmap reference, Bitmap actual, string prefix) {
    int w=reference.Width,h=reference.Height;
    if(actual.Width!=w || actual.Height!=h) throw new ArgumentException("Capture dimensions differ");
    using(var overlay=new Bitmap(w,h,PixelFormat.Format32bppArgb))
    using(var difference=new Bitmap(w,h,PixelFormat.Format32bppArgb)) {
      var rect=new Rectangle(0,0,w,h);
      var a=reference.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
      var b=actual.LockBits(rect,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
      var o=overlay.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
      var d=difference.LockBits(rect,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
      int bytes=a.Stride*h;var aa=new byte[bytes];var bb=new byte[bytes];var oo=new byte[bytes];var dd=new byte[bytes];
      Marshal.Copy(a.Scan0,aa,0,bytes);Marshal.Copy(b.Scan0,bb,0,bytes);
      long total=0,changed=0;int maximum=0;
      for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
        int i=y*a.Stride+x*4;int peak=0;
        for(int c=0;c<3;c++) {int delta=Math.Abs(aa[i+c]-bb[i+c]);total+=delta;peak=Math.Max(peak,delta);maximum=Math.Max(maximum,delta);oo[i+c]=(byte)((aa[i+c]+bb[i+c])/2);dd[i+c]=(byte)delta;}
        oo[i+3]=255;dd[i+3]=255;if(peak>16)changed++;
      }
      Marshal.Copy(oo,0,o.Scan0,bytes);Marshal.Copy(dd,0,d.Scan0,bytes);
      reference.UnlockBits(a);actual.UnlockBits(b);overlay.UnlockBits(o);difference.UnlockBits(d);
      overlay.Save(prefix+".overlay.png",ImageFormat.Png);difference.Save(prefix+".diff.png",ImageFormat.Png);
      return new double[]{(double)total/(w*h*3), (double)changed/(w*h),maximum};
    }
  }
}
'@

$referenceImage = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $Reference).Path)
$actualImage = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $Actual).Path)
try {
  if ($referenceImage.Width -ne $actualImage.Width -or $referenceImage.Height -ne $actualImage.Height) { throw 'Capture dimensions differ' }
  $comparisonRect = [System.Drawing.Rectangle]::new(0,$CropTop,$referenceImage.Width,$referenceImage.Height-$CropTop-$CropBottom)
  $referenceCrop = $referenceImage.Clone($comparisonRect,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $actualCrop = $actualImage.Clone($comparisonRect,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  try {
    $prefixPath = [System.IO.Path]::GetFullPath($OutputPrefix)
    $values = [DriverImageComparison]::Compare($referenceCrop,$actualCrop,$prefixPath)
    $result = [ordered]@{ reference=$Reference; actual=$Actual; cropTop=$CropTop; cropBottom=$CropBottom; width=$referenceCrop.Width; height=$referenceCrop.Height; meanAbsoluteChannelError=$values[0]; pixelsWithChannelDeltaAbove16=$values[1]; maximumChannelDelta=$values[2]; interpretation='Diagnostic only. Review geometry and text separately; this is not a whole-image pass threshold.' }
    $result | ConvertTo-Json | Set-Content -LiteralPath ($prefixPath+'.metrics.json') -Encoding UTF8
    $result | ConvertTo-Json
  } finally { $referenceCrop.Dispose(); $actualCrop.Dispose() }
} finally { $referenceImage.Dispose(); $actualImage.Dispose() }
