using Newtonsoft.Json.Linq;

namespace Younium;

public class YouniumPageResult
{
    public int pageNumber { get; set; }
    public int pageSize { get; set; }
    public int totalPages { get; set; }
    public int totalCount { get; set; }
    public Uri? lastPage { get; set; }
    public Uri? nextPage { get; set; }
    public List<JToken>? data { get; set; }

    public YouniumPageResult Add(YouniumPageResult other)
    {
        pageNumber = other.pageNumber;
        pageSize = other.pageSize;
        totalPages = other.totalPages;
        totalCount = other.totalCount;
        nextPage = other.nextPage;
        lastPage = other.lastPage;
        data ??= [];
        if (other.data is not null) data!.AddRange(other.data);

        return this;
    }
}

