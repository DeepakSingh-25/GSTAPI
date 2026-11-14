namespace Gst.Domain.Common
{
    // Paging / search / sort request
    public class DataGrid
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? SearchText { get; set; }
        public string? SortColumn { get; set; }
        public string? SortOrder { get; set; } = "asc";
    }

    public class DataGridResult<T>(IEnumerable<T> items, int totalCount)
    {
        public IEnumerable<T> Data { get; set; } = items;
        public int TotalCount { get; set; } = totalCount;
    }
}
