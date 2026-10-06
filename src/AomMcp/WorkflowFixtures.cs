using System.IO.Compression;
using System.Text;

namespace AomMcp;

/// <summary>Synthetic structural fixtures only; no proprietary records, game calls, or live scenario writes.</summary>
internal static class WorkflowFixtures
{
    internal static byte[] Bytes(Action<BinaryWriter> build)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        build(writer); writer.Flush(); return stream.ToArray();
    }
    internal static void Wide(BinaryWriter w, string value) { w.Write(value.Length); w.Write(Encoding.Unicode.GetBytes(value)); }
    internal static void Narrow(BinaryWriter w, string value) { var b = Encoding.Latin1.GetBytes(value); w.Write(b.Length); w.Write(b); }
    internal static void Block(BinaryWriter w, string tag, byte[] body) { w.Write(Encoding.ASCII.GetBytes(tag)); w.Write(body.Length); w.Write(body); }

    internal static byte[] Trigger(int workers = 1, byte selection = 0, byte trailer = 0)
    {
        void Element(BinaryWriter w, bool task)
        {
            w.Write(6); Narrow(w, task ? "Unit: Task" : "Always"); Narrow(w, task ? "Unit: Task" : "Always"); w.Write(task ? 3 : 0);
            if (task)
            {
                void Objects(string key, int count, uint first, uint player, string proto)
                {
                    w.Write(1); Narrow(w, key); Narrow(w, key); w.Write(4); w.Write(count);
                    for (uint i = 0; i < count; i++) Wide(w, (first + i).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    w.Write(count); for (uint i = 0; i < count; i++) { w.Write(first + i); w.Write(player); Wide(w, proto); }
                    w.Write(selection); w.Write(trailer);
                }
                Objects("SrcObject", workers, 100, 1, "VillagerAztec"); Objects("DstObject", 1, 200, 1, "Farm");
                w.Write(1); Narrow(w, "EventID"); Narrow(w, "EventID"); w.Write(8); w.Write(1); Wide(w, "-1"); w.Write((byte)0); w.Write((byte)0);
            }
            Narrow(w, "true"); w.Write(task ? 3 : 0);
            if (task)
            {
                Narrow(w, "trUnitSelectClear();"); w.Write((byte)0); w.Write(0);
                Narrow(w, "trUnitSelectByID(%SrcObject%);"); w.Write((byte)1); w.Write(1); Narrow(w, "SrcObject");
                Narrow(w, "trUnitDoWorkOnUnit(%DstObject%, %EventID%);"); w.Write((byte)0); w.Write(0);
            }
            w.Write((ushort)0);
        }
        var body = Bytes(w =>
        {
            w.Write(12); w.Write(0); w.Write(0); w.Write(0); w.Write(1);
            w.Write(9); w.Write(704); w.Write(0); w.Write(3); Wide(w, "Startup"); w.Write(0);
            w.Write(new byte[] { 0, 1, 0, 0, 0 }); Wide(w, "");
            w.Write(1); Element(w, false); w.Write(1); Element(w, true);
            w.Write(1); w.Write(1); w.Write(0); Narrow(w, "Ungrouped"); w.Write(1); w.Write(704);
        });
        return Bytes(w => { Block(w, "TR", body); w.Write(-1); });
    }

    internal static byte[] PlayerSection(string name) => Bytes(w =>
    {
        w.Write(0); w.Write(7);
        for (var id = 0; id < 7; id++)
        {
            w.Write((byte)1); Block(w, "BP", Bytes(p =>
            {
                p.Write(319);
                Block(p, "P1", Bytes(b => { b.Write(id); b.Write((byte)0); Wide(b, ""); Wide(b, name + id); Wide(b, ""); b.Write((byte)1); Wide(b, ""); }));
                Block(p, "P2", Bytes(b => { b.Write(0); b.Write(21); }));
                Block(p, "P3", Bytes(b => { b.Write(0); b.Write(0); b.Write(-1); b.Write(-1); b.Write(-1); b.Write(-1); b.Write(100); b.Write(0); }));
                Block(p, "P4", []);
                Block(p, "P5", Bytes(b => { b.Write(new byte[3]); Wide(b, ""); b.Write(new byte[18]); }));
                Block(p, "P6", Bytes(b => { b.Write(7); for (var j = 0; j < 7; j++) b.Write(j == id ? 0 : 2); b.Write(new byte[37]); }));
                Block(p, "P7", Bytes(b => { b.Write(new byte[5]); b.Write(100.0f); }));
                Block(p, "P8", new byte[12]);
            }));
        }
    });

    internal static byte[] Scene(CheckpointUnits.Unit[] units)
    {
        var protos = units.Select(u => u.Proto!).Distinct(StringComparer.Ordinal).ToArray();
        var world = Bytes(w =>
        {
            w.Write(444); Block(w, "PT", Bytes(p => { p.Write(0); p.Write(protos.Length); foreach (var proto in protos) Narrow(p, proto + "\0"); }));
            Block(w, "PL", PlayerSection("Fixture"));
            Block(w, "Z1", Bytes(z =>
            {
                z.Write(units.Length); z.Write((byte)1);
                foreach (var u in units)
                {
                    z.Write(u.UnitId); Block(z, "H1", Bytes(h =>
                    {
                        Block(h, "EN", Bytes(e => { e.Write(u.UnitId); e.Write(0); e.Write(u.Player); e.Write(16); e.Write(new byte[16]); e.Write(u.X); e.Write(u.Y); e.Write(u.Z); e.Write(new byte[36]); }));
                        Block(h, "P1", Bytes(p => { var index = Array.IndexOf(protos, u.Proto); p.Write(index); p.Write(index); p.Write(new byte[78]); }));
                        Block(h, "P2", new byte[15]); h.Write(new byte[24]); h.Write(10); h.Write(new byte[40]); h.Write((byte)(u.Note is null ? 0 : 1));
                        if (u.Note is not null) Wide(h, u.Note); h.Write(new byte[28]);
                    }));
                }
            }));
        });
        return Pack(world, Trigger());
    }
    static byte[] Pack(byte[] world, byte[] tr)
    {
        var standalone = CampaignTriggers.Parse(tr).Body;
        var embedded = Bytes(w => { w.Write(12); w.Write(0); w.Write(0); w.Write(standalone.AsSpan(4)); });
        var decoded = Bytes(w => { w.Write("BG"u8); w.Write(new byte[8]); Block(w, "J1", world); Block(w, "TR", embedded); w.Write((byte)0); });
        using var output = new MemoryStream(); output.Write("l33t"u8); output.Write(BitConverter.GetBytes(decoded.Length));
        using (var zip = new ZLibStream(output, CompressionMode.Compress, true)) zip.Write(decoded);
        return output.ToArray();
    }

    internal static byte[] Checkpoint(string? note = "塔 — Δέντρο 🌴", uint worldHeader = 444, int p1Size = 86,
        bool duplicateId = false, uint owner = 6, float x = 10, uint secondPrototype = 0, byte opaque = 0,
        byte? noteFlag = null, bool badUtf16 = false, string playerName = "Fixture", int taskWorkers = 1)
    {
        var world = Bytes(w =>
        {
            w.Write(worldHeader);
            Block(w, "PT", Bytes(p => { p.Write(0); p.Write(2); Narrow(p, "CinematicBlockSpawnPoint\0"); Narrow(p, "Farm\0"); }));
            Block(w, "PL", PlayerSection(playerName));
            Block(w, "Z1", Bytes(e =>
            {
                var entity = Bytes(h =>
                {
                    Block(h, "EN", Bytes(v =>
                    {
                        v.Write(100); v.Write(0); v.Write(owner); v.Write(16); v.Write(new byte[16]);
                        v.Write(x); v.Write(4.0f); v.Write(20.0f); v.Write(new byte[36]);
                    }));
                    Block(h, "P1", Bytes(p => { p.Write(0); p.Write(secondPrototype); p.Write(new byte[Math.Max(0, p1Size - 8)]); })); Block(h, "P2", new byte[15]);
                    h.Write(new byte[24]); h.Write(10); h.Write(new byte[40]); h.Write(noteFlag ?? (byte)(note is null ? 0 : 1));
                    if (badUtf16) { h.Write(1); h.Write((ushort)0xd800); } else if (note is not null) Wide(h, note);
                    h.Write(new byte[27]); h.Write(opaque); // Explicit opaque tail, not asserted decoded.
                });
                e.Write(duplicateId ? 2 : 1); e.Write((byte)1);
                for (var i = 0; i < (duplicateId ? 2 : 1); i++) { e.Write(100); Block(e, "H1", entity); }
            }));
            Block(w, "UA", new byte[4]); Block(w, "UA", new byte[4]);
        });
        return Pack(world, Trigger(taskWorkers));
    }
}
