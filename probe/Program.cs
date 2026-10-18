using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Probe;

internal static class Program
{
    private static int Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "list";
        try
        {
            switch (cmd)
            {
                case "list": List(args.Skip(1).ToArray()); break;
                case "logitech": Logitech(args.Skip(1).ToArray()); break;
                case "features": Features(args.Skip(1).ToArray()); break;
                case "featid": FeatId(args.Skip(1).ToArray()); break;
                case "bat": Bat(args.Skip(1).ToArray()); break;
                case "caps": Caps(args.Skip(1).ToArray()); break;
                case "mchose": Mchose(); break;
                case "watch": Watch(); break;
                case "mchose2": Mchose2(); break;
                case "scan": Scan(); break;
                case "soc": Soc(); break;
                case "listen": Listen(args.Skip(1).ToArray()); break;
                case "send": Send(args.Skip(1).ToArray()); break;
                case "feat": Feat(args.Skip(1).ToArray()); break;
                case "input": Input(args.Skip(1).ToArray()); break;
                default:
                    Console.WriteLine("commands: list [vid:pid] | logitech | listen vid pid [usagePage] | send vid pid hex... | feat vid pid | input vid pid");
                    return 2;
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR: " + ex);
            return 1;
        }
    }

    private static bool Match(HidInterface it, string[] a)
    {
        if (a.Length == 0) return true;
        string f = a[0].ToLowerInvariant();
        if (f.Contains(':'))
        {
            var p = f.Split(':');
            ushort vid = Convert.ToUInt16(p[0], 16), pid = Convert.ToUInt16(p[1], 16);
            return it.Vid == vid && it.Pid == pid;
        }
        return ("%04x:%04x".Length > 0) && (it.Vid.ToString("x4") + ":" + it.Pid.ToString("x4")).Contains(f)
               || (it.Product ?? "").ToLowerInvariant().Contains(f);
    }

    private static void List(string[] a)
    {
        var all = Hid.Enumerate();
        int i = 0;
        foreach (var it in all.Where(x => Match(x, a)))
        {
            Console.WriteLine("[{0}] {1}", i++, it);
        }
        Console.WriteLine("total={0}", all.Count);
    }

    private static string Hex(byte[] b)
    {
        return string.Join(" ", b.Select(x => x.ToString("X2")));
    }

    // ---------------- Logitech HID++ 2.0 ----------------
    private static void Logitech(string[] a)
    {
        ushort vid = 0x046D;
        if (a.Length >= 1 && a[0].Contains(':'))
        {
            var p = a[0].Split(':');
            vid = Convert.ToUInt16(p[0], 16);
        }
        var ifaces = Hid.Enumerate().Where(x => x.Vid == vid && x.FeatureLen > 0 || x.Vid == vid).ToList();
        if (ifaces.Count == 0) { Console.WriteLine("no logitech interfaces"); return; }

        foreach (var it in ifaces)
        {
            Console.WriteLine("\n=== {0:X4}:{1:X4} up={2:X4}/{3:X4} in={4} out={5} feat={6} '{7}'",
                it.Vid, it.Pid, it.UsagePage, it.Usage, it.InputLen, it.OutputLen, it.FeatureLen, it.Product);
            if (it.UsagePage < 0xFF00) { Console.WriteLine("  skip (not vendor page)"); continue; }
            try
            {
                using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
                Console.WriteLine("  opened. probing long report 0x11 ...");
                for (int idx = 1; idx <= 6; idx++)
                {
                    var resp = Hidpp(dev, idx, 0x00, 0x00, new byte[] { 0x00, 0x10, 0x00, 0x00 }); // getFeature(0x1000)
                    Console.WriteLine("  dev {0}: root.getFeature(0x1000) -> {1}", idx, resp == null ? "no reply" : Hex(resp));
                    if (resp == null) continue;
                    byte featIdx = resp[4];
                    Console.WriteLine("      feature index 0x1000 = {0}", featIdx.ToString("X2"));
                    if (featIdx != 0)
                    {
                        var st = Hidpp(dev, idx, featIdx, 0x00, new byte[0]);
                        Console.WriteLine("      0x1000 fn0 getBatteryLevelStatus -> {0}", st == null ? "no reply" : Hex(st));
                    }
                    var f1001 = Hidpp(dev, idx, 0x00, 0x00, new byte[] { 0x01, 0x10, 0x00, 0x00 });
                    Console.WriteLine("      root.getFeature(0x1001) -> {0}", f1001 == null ? "no reply" : Hex(f1001));
                    if (f1001 != null && f1001[4] != 0)
                    {
                        var st = Hidpp(dev, idx, f1001[4], 0x00, new byte[0]);
                        Console.WriteLine("      0x1001 fn0 -> {0}", st == null ? "no reply" : Hex(st));
                    }
                    var f1004 = Hidpp(dev, idx, 0x00, 0x00, new byte[] { 0x04, 0x10, 0x00, 0x00 });
                    Console.WriteLine("      root.getFeature(0x1004) -> {0}", f1004 == null ? "no reply" : Hex(f1004));
                    if (f1004 != null && f1004[4] != 0)
                    {
                        var v = Hidpp(dev, idx, f1004[4], 0x00, new byte[0]);
                        Console.WriteLine("      0x1004 fn0 getVoltage -> {0}", v == null ? "no reply" : Hex(v));
                    }
                    var name = Hidpp(dev, idx, 0x00, 0x00, new byte[] { 0x05, 0x00, 0x00, 0x00 }); // root.getDeviceName? 0x0005 is DeviceName feature
                    // actually query feature 0x0005 (Device Name)
                    if (name != null && name[4] != 0)
                    {
                        var n = Hidpp(dev, idx, name[4], 0x00, new byte[0]);
                        Console.WriteLine("      deviceName fn0 -> {0}", n == null ? "no reply" : Hex(n));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("  open/io error: " + ex.Message);
            }
        }
    }

    private static byte[] Hidpp(HidDevice dev, int devIndex, int featIndex, int func, byte[] args)
    {
        bool shortPkt = dev.OutputLen == 7;
        int len = shortPkt ? 7 : 20;
        byte reportId = shortPkt ? (byte)0x10 : (byte)0x11;
        var pkt = new byte[len];
        pkt[0] = reportId;
        pkt[1] = (byte)devIndex;
        pkt[2] = (byte)featIndex;
        pkt[3] = (byte)((func << 4) | 0x0A); // swId 0xA
        for (int i = 0; i < args.Length && 4 + i < len; i++) pkt[4 + i] = args[i];
        dev.Drain();
        try { dev.WriteOutput(pkt); }
        catch (Exception ex) { Console.WriteLine("   write error: " + ex.Message); return null; }
        var deadline = Environment.TickCount64 + 400;
        while (Environment.TickCount64 < deadline)
        {
            var r = dev.Read(120);
            if (r == null) continue;
            if (r.Length >= 4 && r[0] == reportId && r[1] == devIndex) return r;
        }
        return null;
    }

    private static void FeatId(string[] a)
    {
        ushort vid = a.Length > 0 ? Convert.ToUInt16(a[0], 16) : (ushort)0x046D;
        var it = Hid.Enumerate().FirstOrDefault(x => x.Vid == vid && x.UsagePage == 0xFF00 && x.OutputLen == 20);
        if (it == null) { Console.WriteLine("no long interface"); return; }
        using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
        var fs = Hidpp(dev, 1, 0x00, 0x00, new byte[] { 0x00, 0x01, 0x00, 0x00 });
        Console.WriteLine("FeatureSet: " + Hex(fs));
        byte fidx = fs[4];
        for (int i = 0; i < 8; i++)
        {
            var f = Hidpp(dev, 1, fidx, 0x01, new byte[] { (byte)i });
            Console.WriteLine("idx {0}: {1}", i, f == null ? "null" : Hex(f));
        }
    }

    private static void Scan()
    {
        var it = Hid.Enumerate().FirstOrDefault(x => x.Vid == 0x046D && x.UsagePage == 0xFF00 && x.OutputLen == 20);
        if (it == null) { Console.WriteLine("no long interface"); return; }
        using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
        var fs = Hidpp(dev, 1, 0x00, 0x00, new byte[] { 0x00, 0x01, 0x00, 0x00 });
        if (fs == null) { Console.WriteLine("no FeatureSet"); return; }
        byte fidx = fs[4];
        int count = Hidpp(dev, 1, fidx, 0x00, new byte[0])[4];
        for (int i = 0; i < count; i++)
        {
            var f = Hidpp(dev, 1, fidx, 0x01, new byte[] { (byte)i });
            if (f == null) continue;
            int id = (f[4] << 8) | f[5];
            var r = Hidpp(dev, 1, i, 0x00, new byte[0]);
            if (r == null) { Console.WriteLine("0x{0:X4} idx{1}: no reply", id, i); continue; }
            if (r[2] == 0xFF) { Console.WriteLine("0x{0:X4} idx{1}: error", id, i); continue; }
            Console.WriteLine("0x{0:X4} idx{1}: {2}", id, i, Hex(r));
        }
    }

    private static void Soc()
    {
        var it = Hid.Enumerate().FirstOrDefault(x => x.Vid == 0x046D && x.UsagePage == 0xFF00 && x.OutputLen == 20);
        if (it == null) { Console.WriteLine("no interface"); return; }
        using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
        var fs = Hidpp(dev, 1, 0x00, 0x00, new byte[] { 0x00, 0x01, 0x00, 0x00 });
        if (fs == null) { Console.WriteLine("no FeatureSet"); return; }
        byte fidx = fs[4];
        int count = Hidpp(dev, 1, fidx, 0x00, new byte[0])[4];
        int idx = -1;
        for (int i = 0; i < count; i++)
        {
            var f = Hidpp(dev, 1, fidx, 0x01, new byte[] { (byte)i });
            if (f != null && ((f[4] << 8) | f[5]) == 0x1004) { idx = i; break; }
        }
        if (idx < 0) { Console.WriteLine("no 0x1004"); return; }
        Console.WriteLine("0x1004 at index {0}", idx);
        for (int fn = 0; fn <= 8; fn++)
        {
            var r = Hidpp(dev, 1, idx, (byte)fn, new byte[0]);
            Console.WriteLine("fn{0}: {1}", fn, r == null ? "null" : Hex(r));
        }
    }

    private static void Mchose2()
    {
        var it = Hid.Enumerate().FirstOrDefault(x => x.Vid == 0x3837 && x.Pid == 0x3033
                                                     && x.UsagePage == 0x0001 && x.Usage == 0x0000);
        if (it == null) { Console.WriteLine("MI_01 not found"); return; }
        using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
        Console.WriteLine("using out={0} in={1}", it.OutputLen, it.InputLen);

        (byte cmd, byte[] args)[] tests = BuildScanTests();

        foreach (var (cmd, args) in tests)
        {
            var buf = new byte[65];
            buf[0] = 0x00;          // report ID
            buf[1] = 0x55;          // 主机帧头
            buf[2] = cmd;
            buf[3] = 0x00;
            Array.Copy(args, 0, buf, 4, args.Length);
            Console.WriteLine(">>> 55 {0:X2} {1:X2} {2:X2}", cmd, args[0], args[1]);
            try { dev.Drain(); dev.WriteFileReport(buf); }
            catch (Exception ex) { Console.WriteLine("send fail: " + ex.Message); continue; }

            var end = Environment.TickCount64 + 900;
            while (Environment.TickCount64 < end)
            {
                var r = dev.Read(150);
                if (r == null) continue;
                if (r.Length >= 10 && r[1] == 0xAA)
                {
                    int len = r[9];
                    int start = 10;
                    int n = Math.Min(len, r.Length - start);
                    var data = new byte[Math.Max(n, 0)];
                    if (n > 0) Array.Copy(r, start, data, 0, n);
                    Console.WriteLine("<<< AA cmd={0:X2} addr={1:X2}{2:X2} len={3}", r[2], r[3], r[4], len);
                    Console.WriteLine("    full: " + Hex(data));
                    Console.WriteLine("    ascii: " + System.Text.Encoding.ASCII.GetString(data).Replace('\0', '.'));
                }
                else if (r.Length >= 10 && r[1] == 0xAB)
                {
                    Console.WriteLine("<<< AB (错误) cmd={0:X2}", r[2]);
                }
            }
        }
    }

    private static (byte, byte[])[] BuildScanTests()
    {
        return new (byte, byte[])[]
        {
            (0x05, new byte[] { 0x38, 0x38 }),
        };
    }

    private static void Watch()
    {
        var it = Hid.Enumerate().FirstOrDefault(x => x.Vid == 0x3837 && x.Pid == 0x3033
                                                     && x.UsagePage == 0x0001 && x.Usage == 0x0000);
        if (it == null) { Console.WriteLine("MI_01 not found"); return; }
        Console.WriteLine("watching {0} (in={1} out={2}) for 45s ...", it.Path, it.InputLen, it.OutputLen);
        using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);

        byte[] payload = { 0x01, 0x03, 0x00, 0x09, 0x04, 0x00, 0x00 };
        byte cs = 0;
        for (int i = 1; i < payload.Length; i++) cs ^= payload[i];
        var msg = payload.Concat(new[] { cs }).ToArray();

        var end = Environment.TickCount64 + 60000;
        int sent = 0, got = 0;
        while (Environment.TickCount64 < end)
        {
            var buf = new byte[it.OutputLen];
            buf[0] = 0x4D;
            Array.Copy(msg, 0, buf, 1, msg.Length);
            try { dev.WriteOutput(buf); sent++; }
            catch (Exception ex) { Console.WriteLine("send fail: " + ex.Message); break; }

            var readEnd = Environment.TickCount64 + 1300;
            while (Environment.TickCount64 < readEnd)
            {
                var r = dev.Read(200);
                if (r != null) { got++; Console.WriteLine("recv({0}): {1}", r.Length, Hex(r)); }
            }
        }
        Console.WriteLine("done. sent={0} received={1}", sent, got);
    }

    private static void Mchose()
    {
        var ifaces = Hid.Enumerate()
            .Where(x => x.Vid == 0x3837 && x.Pid == 0x3033 && x.OutputLen >= 65)
            .ToList();
        if (ifaces.Count == 0) { Console.WriteLine("no 65-byte vendor interface"); return; }

        byte[] payload = { 0x01, 0x03, 0x00, 0x09, 0x04, 0x00, 0x00 };
        byte cs = 0;
        for (int i = 1; i < payload.Length; i++) cs ^= payload[i];
        var msg = payload.Concat(new[] { cs }).ToArray();
        Console.WriteLine("message: " + Hex(msg));

        foreach (var it in ifaces)
        {
            Console.WriteLine("\n=== up={0:X4}/{1:X4} in={2} out={3} {4}",
                it.UsagePage, it.Usage, it.InputLen, it.OutputLen, it.Path);
            try
            {
                using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
                int len = Math.Max((int)it.OutputLen, 65);
                var variants = new (string, Action<byte[]>)[]
                {
                    ("rid 0x4D + msg", b => { b[0] = 0x4D; Array.Copy(msg, 0, b, 1, msg.Length); }),
                    ("rid 0x00 + msg", b => { b[0] = 0x00; Array.Copy(msg, 0, b, 1, msg.Length); }),
                    ("msg at 0, no rid", b => { Array.Copy(msg, 0, b, 0, msg.Length); }),
                };
                foreach (var (name, fill) in variants)
                {
                    var buf = new byte[len];
                    fill(buf);
                    try
                    {
                        dev.Drain();
                        for (int i = 0; i < 3; i++)
                        {
                            dev.WriteOutput(buf);
                            Thread.Sleep(120);
                        }
                        Console.WriteLine("  sent [{0}] x3", name);
                    }
                    catch (Exception ex) { Console.WriteLine("  send [{0}] failed: {1}", name, ex.Message); continue; }
                    var end = Environment.TickCount64 + 1200;
                    bool got = false;
                    while (Environment.TickCount64 < end)
                    {
                        var r = dev.Read(120);
                        if (r != null)
                        {
                            Console.WriteLine("    recv({0}): {1}", r.Length, Hex(r));
                            got = true;
                        }
                    }
                    if (!got) Console.WriteLine("    (no reply)");
                }
            }
            catch (Exception ex) { Console.WriteLine("  open fail: " + ex.Message); }
        }
    }

    private static void Caps(string[] a)
    {
        if (a.Length < 2) throw new ArgumentException("caps vid pid");
        ushort vid = Convert.ToUInt16(a[0], 16), pid = Convert.ToUInt16(a[1], 16);
        foreach (var it in Hid.Enumerate().Where(x => x.Vid == vid && x.Pid == pid))
        {
            Console.WriteLine("\n=== {0} up={1:X4}/{2:X4} in={3} out={4} feat={5}",
                it.Path.Split('#').Last(), it.UsagePage, it.Usage, it.InputLen, it.OutputLen, it.FeatureLen);
            using var h = Native.CreateFile(it.Path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) { Console.WriteLine("  open fail " + Marshal.GetLastWin32Error()); continue; }
            if (!Native.HidD_GetPreparsedData(h, out IntPtr pre) || pre == IntPtr.Zero)
            { Console.WriteLine("  no preparsed"); continue; }
            try
            {
                var caps = new Native.HIDP_CAPS();
                Native.HidP_GetCaps(pre, ref caps);
                foreach (var (type, name) in new[] { (0, "INPUT"), (2, "FEATURE"), (1, "OUTPUT") })
                {
                    ushort len = 0;
                    Native.HidP_GetValueCapsPtr(type, IntPtr.Zero, ref len, pre);
                    if (len == 0) { Console.WriteLine("  {0}: none", name); continue; }
                    int sz = Marshal.SizeOf<Native.HIDP_VALUE_CAPS>();
                    IntPtr arr = Marshal.AllocHGlobal(sz * len);
                    try
                    {
                        int st = Native.HidP_GetValueCapsPtr(type, arr, ref len, pre);
                        if (st != 0x00110000) { Console.WriteLine("  {0}: status {1:X}", name, st); continue; }
                        for (int i = 0; i < len; i++)
                        {
                            var vc = Marshal.PtrToStructure<Native.HIDP_VALUE_CAPS>(new IntPtr(arr.ToInt64() + i * sz));
                            string usage = vc.IsRange != 0
                                ? string.Format("{0:X4}-{1:X4}", vc.u.Range.UsageMin, vc.u.Range.UsageMax)
                                : string.Format("{0:X4}", vc.u.NotRange.Usage);
                            Console.WriteLine("  {0} rid={1:X2} page={2:X4} usage={3} bits={4} count={5} logical={6}..{7} link={8:X4}/{9:X4}",
                                name, vc.ReportID, vc.UsagePage, usage, vc.BitSize, vc.ReportCount,
                                vc.LogicalMin, vc.LogicalMax, vc.LinkUsagePage, vc.LinkUsage);
                        }
                    }
                    finally { Marshal.FreeHGlobal(arr); }
                }
            }
            finally { Native.HidD_FreePreparsedData(pre); }
        }
    }

    private static void Bat(string[] a)
    {
        ushort vid = a.Length > 0 ? Convert.ToUInt16(a[0], 16) : (ushort)0x046D;
        int devIdx = a.Length > 1 ? int.Parse(a[1]) : 1;
        var it = Hid.Enumerate().FirstOrDefault(x => x.Vid == vid && x.UsagePage == 0xFF00 && x.OutputLen == 20);
        if (it == null) { Console.WriteLine("no long interface"); return; }
        using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
        var fs = Hidpp(dev, 1, 0x00, 0x00, new byte[] { 0x00, 0x01, 0x00, 0x00 });
        if (fs == null) { Console.WriteLine("no FeatureSet"); return; }
        byte fidx = fs[4];
        int count = Hidpp(dev, devIdx, fidx, 0x00, new byte[0])[4];
        int bvolt = -1;
        for (int i = 0; i < count; i++)
        {
            var f = Hidpp(dev, devIdx, fidx, 0x01, new byte[] { (byte)i });
            if (f == null) continue;
            int id = (f[4] << 8) | f[5];
            if (id == 0x1004 || id == 0x1000) { bvolt = i; Console.WriteLine("battery feature 0x{0:X4} at idx {1}", id, i); }
        }
        if (bvolt < 0) { Console.WriteLine("no battery feature"); return; }
        var v = Hidpp(dev, devIdx, bvolt, 0x00, new byte[0]);
        Console.WriteLine("voltage report: {0}", Hex(v));
        if (v != null)
        {
            int mv = v[4] | (v[5] << 8);
            Console.WriteLine("  {0} mV, flags=0x{1:X2}", mv, v[6]);
        }
        var cap = Hidpp(dev, devIdx, bvolt, 0x01, new byte[0]);
        Console.WriteLine("capability: {0}", Hex(cap));
        if (cap != null)
        {
            int levels = cap[4];
            Console.WriteLine("  levels={0} default={1} flags=0x{2:X2}", levels, cap[5], cap[6]);
            for (int start = 0; start < levels; start += 16)
            {
                var t = Hidpp(dev, devIdx, bvolt, 0x02, new byte[] { (byte)start });
                Console.WriteLine("  table@{0}: {1}", start, t == null ? "null" : Hex(t));
                if (t == null) break;
                for (int e = 0; e < 6; e++)
                {
                    int off = 4 + e * 3;
                    if (off + 2 >= t.Length) break;
                    int volt = t[off] | (t[off + 1] << 8);
                    int pct = t[off + 2];
                    Console.WriteLine("    entry {0}: {1} mV -> {2}%", start + e, volt, pct);
                }
            }
        }
    }

    private static void Features(string[] a)
    {
        ushort vid = a.Length > 0 ? Convert.ToUInt16(a[0], 16) : (ushort)0x046D;
        var ifaces = Hid.Enumerate().Where(x => x.Vid == vid).ToList();
        foreach (var it in ifaces.Where(x => x.UsagePage == 0xFF00 && x.OutputLen > 0))
        {
            Console.WriteLine("\n=== {0} up={1:X4}/{2:X4} out={3}", it.Path.Split('#').Last(), it.UsagePage, it.Usage, it.OutputLen);
            try
            {
                using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
                for (int idx = 1; idx <= 3; idx++)
                {
                    var fs = Hidpp(dev, idx, 0x00, 0x00, new byte[] { 0x00, 0x01, 0x00, 0x00 });
                    if (fs == null) { Console.WriteLine("  dev {0}: no reply", idx); continue; }
                    byte fidx = fs[4];
                    Console.WriteLine("  dev {0}: FeatureSet index={1:X2} ver={2}.{3}", idx, fidx, fs[5], fs[6]);
                    if (fidx == 0) continue;
                    var cnt = Hidpp(dev, idx, fidx, 0x00, new byte[0]);
                    if (cnt == null) { Console.WriteLine("    no count reply"); continue; }
                    int n = cnt[4];
                    Console.WriteLine("    featureCount={0}", n);
                    var ids = new List<int>();
                    for (int i = 0; i < n; i++)
                    {
                        var f = Hidpp(dev, idx, fidx, 0x01, new byte[] { (byte)i });
                        if (f == null) continue;
                        int id = (f[4] << 8) | f[5];
                        ids.Add(id);
                        Console.WriteLine("    [{0,2}] feature 0x{1:X4} idx={2:X2} type={3:X2} ver={4}.{5}",
                            i, id, i, f[6], f[7], f[8]);
                    }
                    foreach (int want in new[] { 0x1000, 0x1001, 0x1004, 0x1D4B })
                    {
                        if (!ids.Contains(want)) { Console.WriteLine("    -- 0x{0:X4} not advertised", want); continue; }
                        int bi = 0;
                        for (int i = 0; i < n; i++)
                        {
                            var f = Hidpp(dev, idx, fidx, 0x01, new byte[] { (byte)i });
                            if (f != null && ((f[4] << 8) | f[5]) == want) { bi = i; break; }
                        }
                        Console.WriteLine("    feature 0x{0:X4} at index {1}", want, bi);
                        var f0 = Hidpp(dev, idx, bi, 0x00, new byte[0]);
                        Console.WriteLine("      fn0 -> {0}", f0 == null ? "null" : Hex(f0));
                        var f1 = Hidpp(dev, idx, bi, 0x01, new byte[0]);
                        Console.WriteLine("      fn1 -> {0}", f1 == null ? "null" : Hex(f1));
                        var f2 = Hidpp(dev, idx, bi, 0x02, new byte[0]);
                        Console.WriteLine("      fn2 -> {0}", f2 == null ? "null" : Hex(f2));
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("  error: " + ex.Message); }
        }
    }

    // ---------------- generic probing ----------------
    private static void Listen(string[] a)
    {
        if (a.Length < 2) throw new ArgumentException("listen vid pid [usagePage]");
        ushort vid = Convert.ToUInt16(a[0], 16), pid = Convert.ToUInt16(a[1], 16);
        int seconds = a.Length > 2 ? int.Parse(a[2]) : 15;
        var ifaces = Hid.Enumerate().Where(x => x.Vid == vid && x.Pid == pid).ToList();
        Console.WriteLine("found {0} interfaces", ifaces.Count);
        var devs = new List<(HidInterface, HidDevice)>();
        foreach (var it in ifaces)
        {
            Console.WriteLine("  {0} up={1:X4} in={2} out={3} feat={4}", it.Path.Split('#').Last(),
                it.UsagePage, it.InputLen, it.OutputLen, it.FeatureLen);
            try { devs.Add((it, new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen))); }
            catch (Exception ex) { Console.WriteLine("    open fail: " + ex.Message); }
        }
        Console.WriteLine("listening {0}s (move/press device now) ...", seconds);
        var end = Environment.TickCount64 + seconds * 1000;
        while (Environment.TickCount64 < end)
        {
            foreach (var (it, dev) in devs)
            {
                var r = dev.Read(60);
                if (r != null) Console.WriteLine("{0} up={1:X4}: {2}", it.Path.Split('#').Last(), it.UsagePage, Hex(r));
            }
        }
        foreach (var (_, d) in devs) d.Dispose();
    }

    private static void Send(string[] a)
    {
        if (a.Length < 3) throw new ArgumentException("send vid pid hexbytes...");
        ushort vid = Convert.ToUInt16(a[0], 16), pid = Convert.ToUInt16(a[1], 16);
        var data = a.Skip(2).Select(x => Convert.ToByte(x, 16)).ToArray();
        var ifaces = Hid.Enumerate().Where(x => x.Vid == vid && x.Pid == pid).ToList();
        foreach (var it in ifaces)
        {
            Console.WriteLine("=== {0} up={1:X4} in={2} out={3} feat={4}", it.Path.Split('#').Last(),
                it.UsagePage, it.InputLen, it.OutputLen, it.FeatureLen);
            try
            {
                using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
                dev.WriteOutput(data);
                Console.WriteLine("  sent {0} bytes", data.Length);
                var r = dev.Read(300);
                Console.WriteLine("  reply: {0}", r == null ? "none" : Hex(r));
            }
            catch (Exception ex) { Console.WriteLine("  fail: " + ex.Message); }
        }
    }

    private static void Feat(string[] a)
    {
        if (a.Length < 2) throw new ArgumentException("feat vid pid");
        ushort vid = Convert.ToUInt16(a[0], 16), pid = Convert.ToUInt16(a[1], 16);
        foreach (var it in Hid.Enumerate().Where(x => x.Vid == vid && x.Pid == pid))
        {
            Console.WriteLine("=== {0} up={1:X4} in={2} out={3} feat={4}", it.Path.Split('#').Last(),
                it.UsagePage, it.InputLen, it.OutputLen, it.FeatureLen);
            if (it.FeatureLen == 0) { Console.WriteLine("  no feature reports"); continue; }
            try
            {
                using var dev = new HidDevice(it.Path, it.InputLen, it.OutputLen, it.FeatureLen);
                for (int id = 0; id <= 0x20; id++)
                {
                    try
                    {
                        var f = dev.GetFeature(id);
                        if (f.Skip(1).Any(b => b != 0)) Console.WriteLine("  feature 0x{0:X2}: {1}", id, Hex(f));
                        else Console.WriteLine("  feature 0x{0:X2}: (zeros)", id);
                    }
                    catch (Exception ex) { /* not supported */ }
                }
            }
            catch (Exception ex) { Console.WriteLine("  open fail: " + ex.Message); }
        }
    }

    private static void Input(string[] a)
    {
        if (a.Length < 2) throw new ArgumentException("input vid pid");
        ushort vid = Convert.ToUInt16(a[0], 16), pid = Convert.ToUInt16(a[1], 16);
        foreach (var it in Hid.Enumerate().Where(x => x.Vid == vid && x.Pid == pid))
        {
            Console.WriteLine("=== {0} up={1:X4}/{2:X4} in={3}", it.Path.Split('#').Last(), it.UsagePage, it.Usage, it.InputLen);
        }
    }
}
