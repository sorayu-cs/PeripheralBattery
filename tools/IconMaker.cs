using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class IconMaker
{
    private static GraphicsPath Round(float x, float y, float w, float h, float r)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(r * 2f, Math.Min(w, h));
        p.AddArc(x, y, d, d, 180f, 90f);
        p.AddArc(x + w - d, y, d, d, 270f, 90f);
        p.AddArc(x + w - d, y + h - d, d, d, 0f, 90f);
        p.AddArc(x, y + h - d, d, d, 90f, 90f);
        p.CloseFigure();
        return p;
    }

    private static Bitmap Render(int s)
    {
        Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float bw = s * 0.54f;
            float bh = s * 0.36f;
            float bx = s * 0.14f;
            float by = s * 0.32f;
            float whiteW = Math.Max(1f, s * 0.045f);

            using (GraphicsPath battery = Round(bx, by, bw, bh, s * 0.08f))
            {
                // 内部黑底 = 空电量
                g.FillPath(Brushes.Black, battery);

                // 白色电量条 = 80%
                float pad = s * 0.07f;
                float fillW = Math.Max(1f, (bw - pad * 2f) * 0.8f);
                using (GraphicsPath fill = Round(bx + pad, by + pad, fillW, bh - pad * 2f, s * 0.035f))
                    g.FillPath(Brushes.White, fill);

                // 黑白双色描边，保证明暗背景都可见
                float blackW = Math.Max(2f, s * 0.10f);
                using (Pen pen = new Pen(Color.Black, blackW) { LineJoin = LineJoin.Round })
                    g.DrawPath(pen, battery);
                using (Pen pen = new Pen(Color.White, whiteW) { LineJoin = LineJoin.Round })
                    g.DrawPath(pen, battery);
            }

            float capW = s * 0.08f;
            float capH = s * 0.16f;
            float capX = bx + bw + s * 0.015f;
            float capY = by + (bh - capH) / 2f;
            using (GraphicsPath cap = Round(capX, capY, capW, capH, capW / 2f))
            {
                g.FillPath(Brushes.Black, cap);
                float iw = capW - whiteW * 2f;
                float ih = capH - whiteW * 2f;
                if (iw > 0 && ih > 0)
                {
                    using (GraphicsPath inner = Round(capX + whiteW, capY + whiteW, iw, ih, iw / 2f))
                        g.FillPath(Brushes.White, inner);
                }
            }
        }

        return bmp;
    }

    public static void Make(string icoPath, string previewPrefix)
    {
        int[] sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };
        List<byte[]> pngs = new List<byte[]>();

        foreach (int s in sizes)
        {
            using (Bitmap bmp = Render(s))
            {
                if (s == 256) bmp.Save(previewPrefix + "_256.png", ImageFormat.Png);
                if (s == 32) bmp.Save(previewPrefix + "_32.png", ImageFormat.Png);
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    pngs.Add(ms.ToArray());
                }
            }
        }

        using (FileStream fs = File.Create(icoPath))
        using (BinaryWriter bw = new BinaryWriter(fs))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngs[i].Length);
                bw.Write((uint)offset);
                offset += pngs[i].Length;
            }

            foreach (byte[] png in pngs) bw.Write(png);
        }
    }
}
