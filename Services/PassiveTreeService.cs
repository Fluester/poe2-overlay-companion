using System.IO;
using System.IO.Compression;
using System.Text;
using Poe2OverlayCompanion.Models;

namespace Poe2OverlayCompanion.Services;

public sealed class PassiveTreeIndex
{
    public IReadOnlyList<PassiveNodeInfo> AllNodes { get; }
    public IReadOnlyDictionary<string, PassiveNodeInfo> ByNumericId { get; }
    public IReadOnlyDictionary<string, PassiveNodeInfo> ByStringId { get; }
    public double MinX { get; }
    public double MaxX { get; }
    public double MinY { get; }
    public double MaxY { get; }

    public PassiveTreeIndex(List<PassiveNodeInfo> nodes)
    {
        AllNodes = nodes;
        var num = new Dictionary<string, PassiveNodeInfo>(nodes.Count, StringComparer.OrdinalIgnoreCase);
        var str = new Dictionary<string, PassiveNodeInfo>(nodes.Count, StringComparer.OrdinalIgnoreCase);
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;

        foreach (var n in nodes)
        {
            if (!string.IsNullOrEmpty(n.NumericId)) num[n.NumericId] = n;
            if (!string.IsNullOrEmpty(n.StringId)) str[n.StringId] = n;
            if (n.X < minX) minX = n.X;
            if (n.X > maxX) maxX = n.X;
            if (n.Y < minY) minY = n.Y;
            if (n.Y > maxY) maxY = n.Y;
        }
        ByNumericId = num;
        ByStringId = str;
        MinX = minX;
        MaxX = maxX;
        MinY = minY;
        MaxY = maxY;
    }

    public PassiveNodeInfo? Resolve(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        if (ByStringId.TryGetValue(id, out var s)) return s;
        if (ByNumericId.TryGetValue(id, out var n)) return n;
        return null;
    }
}

/// <summary>
/// Завантажує дерево пасивок із компактного бінарного ресурсу poe2_tree_05.bin.gz (без JsonDocument алокацій)
/// і тримає кеш у WeakReference, щоб звільняти RAM при неактивності.
/// </summary>
public static class PassiveTreeService
{
    private static readonly object Sync = new();
    private static WeakReference<PassiveTreeIndex>? _weakCache;
    private static PassiveTreeIndex? _pinnedWhileActive;

    public static PassiveTreeIndex Index
    {
        get
        {
            lock (Sync)
            {
                if (_weakCache != null && _weakCache.TryGetTarget(out var cached))
                    return cached;

                var loaded = LoadBinaryTree();
                _weakCache = new WeakReference<PassiveTreeIndex>(loaded);
                _pinnedWhileActive = loaded;
                return loaded;
            }
        }
    }

    /// <summary>
    /// Відпускає сильне посилання на індекс (залишається WeakReference, який GC може звільнити при простої).
    /// </summary>
    public static void UnpinCache()
    {
        lock (Sync)
            _pinnedWhileActive = null;
    }

    private static PassiveTreeIndex LoadBinaryTree()
    {
        var nodes = new List<PassiveNodeInfo>(4600);
        try
        {
            var asm = typeof(PassiveTreeService).Assembly;
            string? res = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("poe2_tree_05.bin.gz", StringComparison.OrdinalIgnoreCase));
            if (res == null) return new PassiveTreeIndex(nodes);

            using Stream? stream = asm.GetManifestResourceStream(res);
            if (stream == null) return new PassiveTreeIndex(nodes);

            using var gz = new GZipStream(stream, CompressionMode.Decompress);
            using var buf = new BufferedStream(gz, 65536);
            using var br = new BinaryReader(buf, Encoding.UTF8);

            byte[] magic = br.ReadBytes(5);
            if (magic.Length != 5 || magic[0] != (byte)'P' || magic[1] != (byte)'O' || magic[4] != (byte)'T')
                return new PassiveTreeIndex(nodes);

            byte version = br.ReadByte();
            if (version != 1) return new PassiveTreeIndex(nodes);

            int count = br.ReadInt32();
            nodes.Capacity = count;

            for (int i = 0; i < count; i++)
            {
                string numId = br.ReadInt32().ToString();
                string strId = br.ReadString();
                string name = br.ReadString();
                var kind = (PassiveNodeKind)br.ReadByte();
                float x = br.ReadSingle();
                float y = br.ReadSingle();
                string asc = br.ReadString();

                byte statCount = br.ReadByte();
                var stats = statCount == 0 ? [] : new string[statCount];
                for (int s = 0; s < statCount; s++)
                    stats[s] = br.ReadString();

                byte connCount = br.ReadByte();
                var conns = connCount == 0 ? [] : new string[connCount];
                for (int c = 0; c < connCount; c++)
                    conns[c] = br.ReadInt32().ToString();

                nodes.Add(new PassiveNodeInfo
                {
                    NumericId = numId,
                    StringId = strId,
                    Name = name,
                    Kind = kind,
                    X = x,
                    Y = y,
                    Stats = stats,
                    AscendancyName = asc,
                    Connections = conns
                });
            }
        }
        catch { }
        return new PassiveTreeIndex(nodes);
    }

    public static PassiveDiffResult AnalyzeBuildPassives(
        string title,
        IReadOnlyList<LocalBuildPassive> current,
        IReadOnlyList<LocalBuildPassive>? previous = null,
        int? maxSteps = null)
    {
        var prevSet = new HashSet<string>(previous?.Select(p => p.Id) ?? [], StringComparer.OrdinalIgnoreCase);

        var curLimited = maxSteps is int limit
            ? current.Take(Math.Clamp(limit, 0, current.Count)).ToList()
            : current.ToList();

        var curSet = new HashSet<string>(curLimited.Select(p => p.Id), StringComparer.OrdinalIgnoreCase);

        int added = 0, kept = 0, removed = 0;
        var result = new PassiveDiffResult { Title = title, TotalPoints = curLimited.Count };
        var seenInList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var idx = Index;

        for (int i = 0; i < curLimited.Count; i++)
        {
            var p = curLimited[i];
            if (!seenInList.Add($"{p.Id}#{p.WeaponSet}#{i}")) continue;
            bool wasInPrev = prevSet.Count > 0 && prevSet.Contains(p.Id);
            var status = prevSet.Count == 0
                ? PassiveDiffStatus.Added
                : (wasInPrev ? PassiveDiffStatus.Keep : PassiveDiffStatus.Added);

            if (status == PassiveDiffStatus.Added) added++;
            else kept++;

            result.Entries.Add(new PassiveStepEntry(i + 1, p.Id, idx.Resolve(p.Id), status, p.WeaponSet, p.AdditionalText));
        }

        if (previous != null)
        {
            foreach (var p in previous)
            {
                if (!curSet.Contains(p.Id))
                {
                    removed++;
                    result.Entries.Add(new PassiveStepEntry(0, p.Id, idx.Resolve(p.Id), PassiveDiffStatus.Removed, p.WeaponSet, p.AdditionalText));
                }
            }
        }

        return new PassiveDiffResult
        {
            Title = title,
            TotalPoints = curLimited.Count,
            AddedCount = added,
            KeptCount = kept,
            RemovedCount = removed
        }.WithEntries(result.Entries);
    }

    private static PassiveDiffResult WithEntries(this PassiveDiffResult res, IEnumerable<PassiveStepEntry> entries)
    {
        res.Entries.AddRange(entries);
        return res;
    }
}
