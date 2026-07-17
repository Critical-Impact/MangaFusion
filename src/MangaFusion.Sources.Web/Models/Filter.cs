namespace MangaFusion.Sources.Web.Models;

/// <summary>Base type for a source filter — the C# port of Tachiyomi's <c>Filter</c> hierarchy. A
/// source advertises its filters via <c>GetFilterList()</c>; the UI renders them and passes the
/// populated list back into a search.</summary>
public abstract class Filter(string name)
{
    public string Name { get; } = name;
}

/// <summary>A filter carrying mutable <see cref="State"/> of type <typeparamref name="T"/>.</summary>
public abstract class Filter<T>(string name, T state) : Filter(name)
{
    public T State { get; set; } = state;
}

/// <summary>Non-interactive section header.</summary>
public class HeaderFilter(string name) : Filter<object?>(name, null);

/// <summary>Non-interactive visual separator.</summary>
public class SeparatorFilter() : Filter<object?>("", null);

/// <summary>Free-text input (e.g. author).</summary>
public class TextFilter(string name, string state = "") : Filter<string>(name, state);

/// <summary>Single-choice dropdown; <see cref="State"/> is the selected index into <see cref="Values"/>.</summary>
public class SelectFilter(string name, IReadOnlyList<string> values, int state = 0)
    : Filter<int>(name, state)
{
    public IReadOnlyList<string> Values { get; } = values;
}

/// <summary>On/off checkbox.</summary>
public class CheckBoxFilter(string name, bool state = false) : Filter<bool>(name, state);

/// <summary>Tri-state toggle: ignore / include / exclude — the common shape for genre filters.</summary>
public class TriStateFilter(string name, int state = TriStateFilter.Ignore) : Filter<int>(name, state)
{
    public const int Ignore = 0;
    public const int Include = 1;
    public const int Exclude = 2;

    public bool IsIgnored => State == Ignore;
    public bool IsIncluded => State == Include;
    public bool IsExcluded => State == Exclude;
}

/// <summary>A named group of child filters (e.g. a "Genres" group of <see cref="TriStateFilter"/>s).</summary>
public class GroupFilter(string name, IReadOnlyList<Filter> filters)
    : Filter<IReadOnlyList<Filter>>(name, filters);

/// <summary>Sort selector: a set of sortable fields plus the chosen field + direction.</summary>
public class SortFilter(string name, IReadOnlyList<string> values, SortFilter.Selection? state = null)
    : Filter<SortFilter.Selection?>(name, state)
{
    public IReadOnlyList<string> Values { get; } = values;

    public sealed record Selection(int Index, bool Ascending);
}

/// <summary>An ordered list of filters advertised by a source. Port of Tachiyomi's <c>FilterList</c>.</summary>
public sealed class FilterList : List<Filter>
{
    public FilterList() { }
    public FilterList(IEnumerable<Filter> filters) : base(filters) { }
    public FilterList(params Filter[] filters) : base(filters) { }
}
