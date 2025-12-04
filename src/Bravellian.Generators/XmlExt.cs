using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Bravellian.Generators;

internal static class XmlExt
{
    public static IReadOnlyDictionary<string, string> GetAttributeDict(this XElement xml) =>
        xml.Attributes().ToDictionary(a => a.Name.ToString(), a => a.Value.ToString(), StringComparer.Ordinal);

    public static TValue? TryGetValue<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dict, TKey key)
        where TValue : class
        => dict.TryGetValue(key, out TValue value) ? value : null;
}
