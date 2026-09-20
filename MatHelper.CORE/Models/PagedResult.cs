namespace MatHelper.CORE.Models
{
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 50;
        public bool HasNextPage => TotalCount > 0 && Page > 0 && PageSize > 0 && (Page * PageSize) < TotalCount;
    }
}

