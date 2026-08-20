using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace Celeste.Mod.Akron;

// A hash container finds an entry by a number it stored when the entry went in,
// and that number does not survive a save and a reload.
//
// Nothing here is about collation. Three of the hash functions a mod session can
// end up with are seeded by the process rather than by the data:
//
//   - a culture-aware comparer hashes a string through a per-process seed, so
//     StringComparer.Create(culture, ...) and StringComparer.InvariantCulture
//     give a different number for the same string in the next run;
//   - string.GetHashCode is randomized per process, so a mod's own comparer that
//     calls it has the same problem with no CompareInfo anywhere near it;
//   - a reference key that does not override GetHashCode hashes by its identity
//     in this process, and a rebuilt room holds different objects.
//
// The reconstruction graph restores the stored numbers verbatim, so the rebuilt
// container enumerates every entry it should while Contains and ContainsKey miss
// all of them: present, countable, and unfindable. That is the failure this file
// exists to stop, and it is the same one ValidateAndNormalizeMembershipSet
// already stops for EntityList and ComponentList.
//
// The index is re-derived in place, and deliberately not by clearing the
// container and adding its contents back. Clearing compacts: a container the
// saved frame had removed something from comes back with a different _count and
// its entries at different indices, and _count is real state Verify has to keep
// checking. Only three things move here, and every one of them is a function of
// state the document still owns and Verify still checks:
//
//   _buckets            the bucket head for each hash slot
//   Entry.hashCode      the stored hash of the key at that slot
//   Entry.next          the chain link to the next entry in the same bucket
//
// Nothing else is touched: the entries keep their indices, their keys and their
// values; _count, _freeList, _freeCount, _comparer and _version are left exactly
// as the document set them, and the free-slot chain (next < -1) is stepped over
// rather than rewritten.
//
// Layouts are read by reflection because they are private, and every lookup is
// required rather than optional. If a future runtime renames a field this throws
// and the snapshot is refused, which is the outcome to want: the alternative is
// skipping the rebuild and restoring a container that lies about its contents.
//
// The rebuilt index is a valid one, not necessarily the one the runtime would
// have built. Walking the entries in index order puts a bucket's chain in
// descending index order, and the runtime's chain instead follows the order the
// entries were added, which differs once a container has had entries removed and
// the freed slots handed back out. Measured: a set with that churn came out with
// one bucket head and two chain links different, holding the same entries and
// answering the same lookups. Nothing observable reads a chain - enumeration
// walks the entries and never the buckets - and a container that already finds
// its entries is left alone by the check above, so this only ever applies to an
// index that was meaningless in this process anyway.
internal static class AkronHashIndex {
    private const BindingFlags InstanceFields =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly MethodInfo RebuildSetMethod =
        typeof(AkronHashIndex).GetMethod(nameof(RebuildSet), BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo RebuildMapMethod =
        typeof(AkronHashIndex).GetMethod(nameof(RebuildMap), BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly ConcurrentDictionary<Type, MethodInfo> Rebuilders =
        new ConcurrentDictionary<Type, MethodInfo>();
    private static readonly MethodInfo RebuildConcurrentMapMethod =
        typeof(AkronHashIndex).GetMethod(nameof(RebuildConcurrentMap), BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly ConcurrentDictionary<Type, IndexRole> IndexRoles =
        new ConcurrentDictionary<Type, IndexRole>();

    // Re-derive the hash index of one restored object, if it has one. Anything
    // else is left alone, so this is safe to call for every node in a graph -
    // including a null one, which a graph node's value is allowed to be. The one
    // caller filters nulls for its own reasons; the guard is here so the sentence
    // above stays true for the next caller.
    public static void Rebuild(object value) {
        if (value == null) {
            return;
        }
        MethodInfo rebuilder = Rebuilders.GetOrAdd(value.GetType(), FindRebuilder);
        if (rebuilder == null) {
            return;
        }
        try {
            rebuilder.Invoke(null, new[] { value });
        } catch (TargetInvocationException exception) when (exception.InnerException != null) {
            throw exception.InnerException;
        }
    }

    // The scalar fields Rebuild writes. Verify skips these and nothing else,
    // because after a restore they hold this process's numbers rather than the
    // saved process's. They cannot hide a corrupted restore: each one is a
    // function of the keys, the comparer and the bucket count, and Verify still
    // compares the entries, _count, _freeList, _freeCount and _comparer that
    // produce them. A key or value that came back wrong is still caught, at its
    // own path, because the rebuild leaves every entry at its saved index.
    //
    // ConcurrentDictionary is the one that gives up more. Its keys and values
    // live on Node objects the rebuild relinks rather than moves, so each one is
    // still compared at its own path, but which bucket holds which node is not.
    // What guards membership there instead is ValidateArrayAssignments, which
    // writes every bucket slot from the document and refuses a length that
    // differs, and the reach-against-Count check in RebuildConcurrentMap, which
    // refuses a table holding a node nothing points at.
    public static bool IsDerivedIndexField(Type ownerType, string fieldName) {
        if (ownerType == null) {
            return false;
        }
        switch (fieldName) {
            case "_hashcode":
            case "_next":
                return HasRole(ownerType, IndexRole.Node);
            case "hashCode":
            case "next":
            case "HashCode":
            case "Next":
                return HasRole(ownerType, IndexRole.Entry);
            default:
                return false;
        }
    }

    // The two arrays the rebuild rewrites the contents of. It never replaces
    // either array, so the field that points at one keeps being compared and the
    // array's length keeps being compared; only the positions inside it are this
    // process's rather than the saved process's.
    public static bool IsDerivedIndexArrayField(Type ownerType, string fieldName) {
        if (ownerType == null) {
            return false;
        }
        return fieldName switch {
            "_buckets" => HasRole(ownerType, IndexRole.Container | IndexRole.Tables),
            "_countPerLock" => HasRole(ownerType, IndexRole.Tables),
            _ => false
        };
    }

    // The name half of the check on its own. Verification asks this for every
    // array in the graph, and the name rules out all but a handful before a type
    // has to be resolved to answer the rest.
    public static bool IsDerivedIndexArrayFieldName(string fieldName) {
        return string.Equals(fieldName, "_buckets", StringComparison.Ordinal) ||
               string.Equals(fieldName, "_countPerLock", StringComparison.Ordinal);
    }

    // True for the private Entry struct of a HashSet or a Dictionary.
    public static bool IsHashEntryType(Type type) {
        return type != null && HasRole(type, IndexRole.Entry);
    }

    // A slot with a chain link below -1 is a hole the container keeps for its
    // free list. The rebuild steps over those, so their stored hash and their
    // link are still whatever the document holds.
    public static bool IsLiveHashEntry(object entry) {
        Type type = entry.GetType();
        FieldInfo chain = type.GetField("next", InstanceFields) ?? type.GetField("Next", InstanceFields);
        return chain != null && (int) chain.GetValue(entry) >= -1;
    }

    [Flags]
    private enum IndexRole {
        None = 0,
        Container = 1,
        Entry = 2,
        Tables = 4,
        Node = 8
    }

    private static bool HasRole(Type type, IndexRole roles) {
        return (IndexRoles.GetOrAdd(type, ClassifyIndexOwner) & roles) != IndexRole.None;
    }

    private static IndexRole ClassifyIndexOwner(Type type) {
        if (FindHashContainer(type) != null) {
            return IndexRole.Container;
        }
        Type declaring = type.DeclaringType;
        if (declaring == null || !declaring.IsGenericType) {
            return IndexRole.None;
        }
        Type definition = declaring.GetGenericTypeDefinition();
        if (type.IsValueType &&
            string.Equals(type.Name, "Entry", StringComparison.Ordinal) &&
            (definition == typeof(HashSet<>) || definition == typeof(Dictionary<,>))) {
            return IndexRole.Entry;
        }
        if (definition != typeof(ConcurrentDictionary<,>)) {
            return IndexRole.None;
        }
        return string.Equals(type.Name, "Tables", StringComparison.Ordinal) ? IndexRole.Tables
            : string.Equals(type.Name, "Node", StringComparison.Ordinal) ? IndexRole.Node
            : IndexRole.None;
    }

    private static MethodInfo FindRebuilder(Type type) {
        Type container = FindHashContainer(type);
        if (container == null) {
            return null;
        }
        Type definition = container.GetGenericTypeDefinition();
        MethodInfo rebuilder = definition == typeof(HashSet<>) ? RebuildSetMethod
            : definition == typeof(Dictionary<,>) ? RebuildMapMethod
            : RebuildConcurrentMapMethod;
        return rebuilder.MakeGenericMethod(container.GetGenericArguments());
    }

    // A mod can derive from Dictionary or HashSet, so walk the base chain rather
    // than matching the runtime type.
    private static Type FindHashContainer(Type type) {
        for (Type current = type; current != null; current = current.BaseType) {
            if (!current.IsGenericType || current.ContainsGenericParameters) {
                continue;
            }
            Type definition = current.GetGenericTypeDefinition();
            if (definition == typeof(HashSet<>) ||
                definition == typeof(Dictionary<,>) ||
                definition == typeof(ConcurrentDictionary<,>)) {
                return current;
            }
        }
        return null;
    }

    // ConcurrentDictionary keeps its entries as Node objects chained off a bucket
    // array, so the rebuild relinks the nodes it already has rather than moving
    // any key or value. Nodes are collected before the first bucket head moves,
    // because the chains are what the walk is reading.
    private static void RebuildConcurrentMap<TKey, TValue>(ConcurrentDictionary<TKey, TValue> map) {
        object tables = RequiredField(typeof(ConcurrentDictionary<TKey, TValue>), "_tables").GetValue(map);
        if (tables == null) {
            return;
        }
        Type tablesType = tables.GetType();
        Array buckets = (Array) RequiredField(tablesType, "_buckets").GetValue(tables);
        int[] countPerLock = (int[]) RequiredField(tablesType, "_countPerLock").GetValue(tables);
        object[] locks = (object[]) RequiredField(tablesType, "_locks").GetValue(tables);
        if (buckets == null || buckets.Length == 0 || locks == null || locks.Length == 0 ||
            countPerLock == null || countPerLock.Length != locks.Length) {
            return;
        }
        IEqualityComparer<TKey> comparer =
            (IEqualityComparer<TKey>) RequiredField(tablesType, "_comparer").GetValue(tables)
            ?? EqualityComparer<TKey>.Default;
        FieldInfo slotField = RequiredField(buckets.GetType().GetElementType(), "_node");
        Type nodeType = slotField.FieldType;
        FieldInfo keyField = RequiredField(nodeType, "_key");
        FieldInfo nextField = RequiredField(nodeType, "_next");
        FieldInfo hashField = RequiredField(nodeType, "_hashcode");

        // Walk the chains first and prove the table holds what it says it holds.
        // Count sums the per-lock counters while the chains are the only record
        // of which nodes are actually in the table, so a node the counters still
        // include but no chain reaches is a table that lies about its contents -
        // and rebuilding cannot recover a node nothing points at, so that is a
        // refusal. Nodes are collected by reference rather than counted, because
        // a slot pointing into another slot's chain would otherwise reach the
        // same node twice and make the totals agree while a different node was
        // lost; keeping the set also stops a chain that loops from spinning here.
        List<object> nodes = new List<object>();
        HashSet<object> distinct = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < buckets.Length; index++) {
            for (object node = slotField.GetValue(buckets.GetValue(index));
                 node != null;
                 node = nextField.GetValue(node)) {
                if (!distinct.Add(node)) {
                    throw new InvalidOperationException(
                        typeof(ConcurrentDictionary<TKey, TValue>).FullName +
                        " reaches one entry through more than one bucket");
                }
                nodes.Add(node);
            }
        }
        if (nodes.Count != map.Count) {
            throw new InvalidOperationException(
                typeof(ConcurrentDictionary<TKey, TValue>).FullName +
                " counts " + map.Count + " entries and can reach " + nodes.Count);
        }
        if (FindsEveryKey(map, map.ContainsKey)) {
            return;
        }

        for (int index = 0; index < buckets.Length; index++) {
            object slot = buckets.GetValue(index);
            slotField.SetValue(slot, null);
            buckets.SetValue(slot, index);
        }
        Array.Clear(countPerLock, 0, countPerLock.Length);

        foreach (object node in nodes) {
            int hash = comparer.GetHashCode((TKey) keyField.GetValue(node));
            int bucket = (int) ((uint) hash % (uint) buckets.Length);
            object slot = buckets.GetValue(bucket);
            hashField.SetValue(node, hash);
            nextField.SetValue(node, slotField.GetValue(slot));
            slotField.SetValue(slot, node);
            buckets.SetValue(slot, bucket);
            countPerLock[bucket % locks.Length]++;
        }

        // Count sums _countPerLock, so this also proves the per-lock counts the
        // loop above rewrote add back up to the table the walk above found.
        if (map.Count != nodes.Count) {
            throw new InvalidOperationException(
                typeof(ConcurrentDictionary<TKey, TValue>).FullName +
                " lost entries while its hash index was rebuilt");
        }
        if (!FindsEveryKey(map, map.ContainsKey)) {
            throw new InvalidOperationException(
                typeof(ConcurrentDictionary<TKey, TValue>).FullName +
                " could not find its own key after its hash index was rebuilt");
        }
    }

    // Ask the container to find each of the entries it is holding. This is the
    // whole test: a container whose stored hashes were computed by a hash
    // function this process reproduces finds all of them, and one whose hashes
    // came from another process's seed misses the first one it tries. It costs
    // one lookup per entry, allocates nothing, and stops looking up after the
    // first miss, so the common case - ordinal string keys, integer keys,
    // anything the runtime hashes the same way twice - pays for the check and
    // nothing else. The walk itself always runs to the end, because counting
    // what the enumerator yields is the other half of the check. The same walk
    // run after a rewrite is the proof that the rewrite worked.
    private static bool FindsEveryEntry<T>(HashSet<T> set) {
        int walked = 0;
        bool findsEveryEntry = true;
        foreach (T item in set) {
            walked++;
            findsEveryEntry = findsEveryEntry && set.Contains(item);
        }
        // Count is _count minus _freeCount and the walk skips the free slots, so
        // the two disagreeing means the container's own bookkeeping does not
        // describe its entries. Rebuilding an index over that would hide it.
        if (walked != set.Count) {
            throw new InvalidOperationException(
                typeof(HashSet<T>).FullName +
                " counts " + set.Count + " entries and can reach " + walked);
        }
        return findsEveryEntry;
    }

    private static bool FindsEveryKey<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> map, Func<TKey, bool> contains) {
        foreach (KeyValuePair<TKey, TValue> pair in map) {
            if (!contains(pair.Key)) {
                return false;
            }
        }
        return true;
    }

    private static bool FindsEveryKey<TKey, TValue>(Dictionary<TKey, TValue> map) {
        int walked = 0;
        bool findsEveryKey = true;
        foreach (KeyValuePair<TKey, TValue> pair in map) {
            walked++;
            findsEveryKey = findsEveryKey && map.ContainsKey(pair.Key);
        }
        if (walked != map.Count) {
            throw new InvalidOperationException(
                typeof(Dictionary<TKey, TValue>).FullName +
                " counts " + map.Count + " entries and can reach " + walked);
        }
        return findsEveryKey;
    }

    // HashSet stores a signed hash and names its entry fields in Pascal case.
    private static void RebuildSet<T>(HashSet<T> set) {
        if (FindsEveryEntry(set)) {
            return;
        }
        Type type = typeof(HashSet<T>);
        int[] buckets = (int[]) RequiredField(type, "_buckets").GetValue(set);
        Array entries = (Array) RequiredField(type, "_entries").GetValue(set);
        if (buckets == null || buckets.Length == 0 || entries == null) {
            return;
        }
        int count = (int) RequiredField(type, "_count").GetValue(set);
        IEqualityComparer<T> comparer =
            (IEqualityComparer<T>) RequiredField(type, "_comparer").GetValue(set) ?? EqualityComparer<T>.Default;
        Type entryType = entries.GetType().GetElementType();
        FieldInfo hashField = RequiredField(entryType, "HashCode");
        FieldInfo nextField = RequiredField(entryType, "Next");
        FieldInfo valueField = RequiredField(entryType, "Value");

        Array.Clear(buckets, 0, buckets.Length);
        for (int index = 0; index < count; index++) {
            object entry = entries.GetValue(index);
            if ((int) nextField.GetValue(entry) < -1) {
                continue;
            }
            T item = (T) valueField.GetValue(entry);
            int hash = item == null ? 0 : comparer.GetHashCode(item);
            int bucket = (int) ((uint) hash % (uint) buckets.Length);
            hashField.SetValue(entry, hash);
            nextField.SetValue(entry, buckets[bucket] - 1);
            entries.SetValue(entry, index);
            buckets[bucket] = index + 1;
        }

        if (!FindsEveryEntry(set)) {
            throw new InvalidOperationException(
                type.FullName + " could not find its own entry after its hash index was rebuilt");
        }
    }

    // Dictionary stores an unsigned hash and names its entry fields in camel case.
    private static void RebuildMap<TKey, TValue>(Dictionary<TKey, TValue> map) {
        if (FindsEveryKey(map)) {
            return;
        }
        Type type = typeof(Dictionary<TKey, TValue>);
        int[] buckets = (int[]) RequiredField(type, "_buckets").GetValue(map);
        Array entries = (Array) RequiredField(type, "_entries").GetValue(map);
        if (buckets == null || buckets.Length == 0 || entries == null) {
            return;
        }
        int count = (int) RequiredField(type, "_count").GetValue(map);
        IEqualityComparer<TKey> comparer =
            (IEqualityComparer<TKey>) RequiredField(type, "_comparer").GetValue(map) ?? EqualityComparer<TKey>.Default;
        Type entryType = entries.GetType().GetElementType();
        FieldInfo hashField = RequiredField(entryType, "hashCode");
        FieldInfo nextField = RequiredField(entryType, "next");
        FieldInfo keyField = RequiredField(entryType, "key");

        Array.Clear(buckets, 0, buckets.Length);
        for (int index = 0; index < count; index++) {
            object entry = entries.GetValue(index);
            if ((int) nextField.GetValue(entry) < -1) {
                continue;
            }
            uint hash = (uint) comparer.GetHashCode((TKey) keyField.GetValue(entry));
            int bucket = (int) (hash % (uint) buckets.Length);
            hashField.SetValue(entry, hash);
            nextField.SetValue(entry, buckets[bucket] - 1);
            entries.SetValue(entry, index);
            buckets[bucket] = index + 1;
        }

        if (!FindsEveryKey(map)) {
            throw new InvalidOperationException(
                type.FullName + " could not find its own key after its hash index was rebuilt");
        }
    }

    private static FieldInfo RequiredField(Type type, string name) {
        return type.GetField(name, InstanceFields)
               ?? throw new InvalidOperationException(
                   type.FullName + "." + name + " is unavailable, so its hash index cannot be rebuilt");
    }
}
