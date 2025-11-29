using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Windows.UI.Xaml;

namespace Suspended.GameIconExtractor
{
    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
            [In] SIIGBF flags,
            out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [Flags]
    enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10,
        SIIGBF_ICONBACKGROUND = 0x00000080
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
    }

    public static class IconHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool DeleteObject(IntPtr hObject);

        public static Bitmap GetExeIcon(string exePath, int size, int cornerRadius = 6)
        {
            try
            {
                // Create the shell item
                Guid shellItemGuid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
                SHCreateItemFromParsingName(exePath, IntPtr.Zero, shellItemGuid, out IShellItem shellItem);

                var factory = (IShellItemImageFactory)shellItem;

                SIZE sz = new SIZE { cx = size, cy = size };

                // Request the icon with the specified size
                factory.GetImage(sz, SIIGBF.SIIGBF_BIGGERSIZEOK | SIIGBF.SIIGBF_ICONONLY, out IntPtr hBitmap);
                if (hBitmap == IntPtr.Zero)
                    return null;

                var icon = Icon.FromHandle(hBitmap);

                // Apply rounded corners if needed
                using (var temp = Bitmap.FromHbitmap(hBitmap))
                {
                    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);

                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);

                        using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                        {
                            path.AddArc(0, 0, cornerRadius * 2, cornerRadius * 2, 180, 90);
                            path.AddArc(bmp.Width - cornerRadius * 2, 0, cornerRadius * 2, cornerRadius * 2, 270, 90);
                            path.AddArc(bmp.Width - cornerRadius * 2, bmp.Height - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 0, 90);
                            path.AddArc(0, bmp.Height - cornerRadius * 2, cornerRadius * 2, cornerRadius * 2, 90, 90);
                            path.CloseFigure();

                            g.SetClip(path);
                            g.DrawImage(temp, 0, 0, bmp.Width, bmp.Height);
                        }
                    }

                    DeleteObject(hBitmap);
                    return bmp;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Converts a color Bitmap to grayscale
        /// </summary>
        public static Bitmap MakeGrayscale(Bitmap original)
        {
            Bitmap grayBmp = new Bitmap(original.Width, original.Height);

            using (Graphics g = Graphics.FromImage(grayBmp))
            {
                // Create grayscale color matrix
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix(
                    new float[][]
                    {
                new float[] {0.299f, 0.299f, 0.299f, 0, 0},
                new float[] {0.587f, 0.587f, 0.587f, 0, 0},
                new float[] {0.114f, 0.114f, 0.114f, 0, 0},
                new float[] {0, 0, 0, 1, 0},
                new float[] {0, 0, 0, 0, 1}
                    });

                var attributes = new ImageAttributes();
                attributes.SetColorMatrix(colorMatrix);

                g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height),
                    0, 0, original.Width, original.Height, GraphicsUnit.Pixel, attributes);
            }

            return grayBmp;
        }
    }
}
