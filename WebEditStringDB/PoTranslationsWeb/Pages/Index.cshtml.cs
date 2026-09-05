using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PoTranslationsWeb.Data;
using PoTranslationsWeb.Models;

namespace PoTranslationsWeb.Pages;

public sealed class IndexModel : PageModel
{
    private static readonly int[] AllowedPageSizes = [10, 25, 50, 100];

    private readonly ILogger<IndexModel> _logger;
    private readonly PoTranslationRepository _repository;

    public IndexModel(ILogger<IndexModel> logger, PoTranslationRepository repository)
    {
        _logger = logger;
        _repository = repository;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    [TempData]
    public string? FlashMessage { get; set; }

    [TempData]
    public string? FlashVariant { get; set; }

    public bool IsConfigured => _repository.IsConfigured;

    public string? SetupHint { get; private set; }

    public IReadOnlyList<PoTranslationRow> Rows { get; private set; } = Array.Empty<PoTranslationRow>();

    public long TotalCount { get; private set; }

    public int TotalPages { get; private set; } = 1;

    public long ShowingFrom { get; private set; }

    public long ShowingTo { get; private set; }

    public IReadOnlyList<int> PageSizes { get; } = AllowedPageSizes;

    public IReadOnlyList<int> VisiblePageNumbers { get; private set; } = Array.Empty<int>();

    public bool ShowLeadingPaginationEllipsis { get; private set; }

    public bool ShowTrailingPaginationEllipsis { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadPageAsync();
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        int id,
        string? suggestedTranslation,
        double? rating,
        string? search,
        int pageNumber,
        int pageSize)
    {
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        PageNumber = pageNumber < 1 ? 1 : pageNumber;
        PageSize = NormalizePageSize(pageSize);
        var isAjaxRequest = IsAjaxRequest();

        if (!_repository.IsConfigured)
        {
            const string message = "Chưa cấu hình connection string nên không thể cập nhật.";

            if (isAjaxRequest)
            {
                return new JsonResult(new
                {
                    success = false,
                    message
                })
                {
                    StatusCode = 400
                };
            }

            FlashMessage = message;
            FlashVariant = "warning";
            return RedirectToPage(new
            {
                search = Search,
                pageNumber = PageNumber,
                pageSize = PageSize,
            });
        }

        try
        {
            var normalizedSuggestedTranslation = string.IsNullOrWhiteSpace(suggestedTranslation)
                ? null
                : suggestedTranslation.Trim();

            var updated = await _repository.UpdateAsync(
                id,
                normalizedSuggestedTranslation,
                rating,
                HttpContext.RequestAborted);

            if (updated)
            {
                var message = $"Đã cập nhật ID {id}.";

                if (isAjaxRequest)
                {
                    return new JsonResult(new
                    {
                        success = true,
                        id,
                        suggestedTranslation = normalizedSuggestedTranslation ?? string.Empty,
                        rating = rating,
                        ratingDisplay = rating.HasValue ? rating.Value.ToString("0.###", CultureInfo.InvariantCulture) : string.Empty,
                        message
                    });
                }

                FlashMessage = message;
                FlashVariant = "success";
            }
            else
            {
                var message = $"Không tìm thấy bản ghi ID {id} để cập nhật.";

                if (isAjaxRequest)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        message
                    })
                    {
                        StatusCode = 404
                    };
                }

                FlashMessage = message;
                FlashVariant = "warning";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to update PoTranslations row {Id}", id);

            const string message = "Không thể cập nhật bản ghi. Hãy kiểm tra lại kết nối SQL Server hoặc dữ liệu nhập.";

            if (isAjaxRequest)
            {
                return new JsonResult(new
                {
                    success = false,
                    message
                })
                {
                    StatusCode = 500
                };
            }

            FlashMessage = message;
            FlashVariant = "danger";
        }

        if (isAjaxRequest)
        {
            return new JsonResult(new
            {
                success = false,
                message = "Không thể cập nhật bản ghi."
            })
            {
                StatusCode = 500
            };
        }

        return RedirectToPage(new
        {
            search = Search,
            pageNumber = PageNumber,
            pageSize = PageSize,
        });
    }

    public async Task<IActionResult> OnPostToggleLockAsync(
        int id,
        string? search,
        int pageNumber,
        int pageSize)
    {
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        PageNumber = pageNumber < 1 ? 1 : pageNumber;
        PageSize = NormalizePageSize(pageSize);
        var isAjaxRequest = IsAjaxRequest();

        if (!_repository.IsConfigured)
        {
            const string message = "Chưa cấu hình connection string nên không thể đổi trạng thái khóa.";

            if (isAjaxRequest)
            {
                return new JsonResult(new
                {
                    success = false,
                    message
                })
                {
                    StatusCode = 400
                };
            }

            FlashMessage = message;
            FlashVariant = "warning";
            return RedirectToPage(new
            {
                search = Search,
                pageNumber = PageNumber,
                pageSize = PageSize,
            });
        }

        try
        {
            var newLockedState = await _repository.ToggleLockAsync(id, HttpContext.RequestAborted);

            if (newLockedState.HasValue)
            {
                var message = newLockedState.Value
                    ? $"Đã khóa bản ghi ID {id}."
                    : $"Đã mở bản ghi ID {id}.";

                if (isAjaxRequest)
                {
                    return new JsonResult(new
                    {
                        success = true,
                        id,
                        locked = newLockedState.Value,
                        label = newLockedState.Value ? "Khóa" : "Mở",
                        message
                    });
                }

                FlashMessage = message;
                FlashVariant = "success";
            }
            else
            {
                var message = $"Không tìm thấy bản ghi ID {id} để đổi trạng thái.";

                if (isAjaxRequest)
                {
                    return new JsonResult(new
                    {
                        success = false,
                        message
                    })
                    {
                        StatusCode = 404
                    };
                }

                FlashMessage = message;
                FlashVariant = "warning";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to toggle lock for PoTranslations row {Id}", id);
            var message = "Không thể đổi trạng thái khóa. Hãy kiểm tra lại kết nối SQL Server hoặc dữ liệu.";

            if (isAjaxRequest)
            {
                return new JsonResult(new
                {
                    success = false,
                    message
                })
                {
                    StatusCode = 500
                };
            }

            FlashMessage = message;
            FlashVariant = "danger";
        }

        if (isAjaxRequest)
        {
            return new JsonResult(new
            {
                success = false,
                message = "Không thể đổi trạng thái khóa."
            })
            {
                StatusCode = 500
            };
        }

        return RedirectToPage(new
        {
            search = Search,
            pageNumber = PageNumber,
            pageSize = PageSize,
        });
    }

    private async Task LoadPageAsync()
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
        PageNumber = PageNumber < 1 ? 1 : PageNumber;
        PageSize = NormalizePageSize(PageSize);

        if (!_repository.IsConfigured)
        {
            SetupHint = "Chưa có connection string. Hãy đặt ConnectionStrings:PoTranslationsDb trong appsettings.json hoặc biến môi trường ConnectionStrings__PoTranslationsDb.";
            Rows = Array.Empty<PoTranslationRow>();
            TotalCount = 0;
            TotalPages = 1;
            ShowingFrom = 0;
            ShowingTo = 0;
            UpdatePaginationWindow();
            ApplyTopbarMetrics();
            return;
        }

        TotalCount = await _repository.CountAsync(Search, HttpContext.RequestAborted);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        PageNumber = Math.Min(PageNumber, TotalPages);
        Rows = await _repository.GetPageAsync(Search, PageNumber, PageSize, HttpContext.RequestAborted);

        if (TotalCount == 0)
        {
            ShowingFrom = 0;
            ShowingTo = 0;
        }
        else
        {
            ShowingFrom = ((PageNumber - 1) * (long)PageSize) + 1;
            ShowingTo = Math.Min(TotalCount, PageNumber * (long)PageSize);
        }

        UpdatePaginationWindow();
        ApplyTopbarMetrics();
    }

    private static int NormalizePageSize(int pageSize)
    {
        return AllowedPageSizes.Contains(pageSize) ? pageSize : 25;
    }

    private void UpdatePaginationWindow()
    {
        if (TotalPages <= 1)
        {
            VisiblePageNumbers = new[] { 1 };
            ShowLeadingPaginationEllipsis = false;
            ShowTrailingPaginationEllipsis = false;
            return;
        }

        if (TotalPages <= 7)
        {
            VisiblePageNumbers = Enumerable.Range(1, TotalPages).ToArray();
            ShowLeadingPaginationEllipsis = false;
            ShowTrailingPaginationEllipsis = false;
            return;
        }

        var start = Math.Max(1, PageNumber - 3);
        var end = Math.Min(TotalPages, PageNumber + 3);

        if (start == 1)
        {
            end = 7;
        }
        else if (end == TotalPages)
        {
            start = TotalPages - 6;
        }

        VisiblePageNumbers = Enumerable.Range(start, end - start + 1).ToArray();
        ShowLeadingPaginationEllipsis = start > 1;
        ShowTrailingPaginationEllipsis = end < TotalPages;
    }

    private void ApplyTopbarMetrics()
    {
        ViewData["TopbarTotalCount"] = TotalCount.ToString("N0", CultureInfo.InvariantCulture);
        ViewData["TopbarShowing"] = TotalCount == 0
            ? "0"
            : $"{ShowingFrom.ToString("N0", CultureInfo.InvariantCulture)} - {ShowingTo.ToString("N0", CultureInfo.InvariantCulture)}";
        ViewData["TopbarPage"] = $"{PageNumber} / {TotalPages}";
        ViewData["TopbarStatus"] = IsConfigured ? "Sẵn sàng" : "Chưa kết nối";
        ViewData["TopbarStatusClass"] = IsConfigured ? "topbar-stat--success" : "topbar-stat--warning";
    }

    private bool IsAjaxRequest()
    {
        return string.Equals(
            Request.Headers["X-Requested-With"],
            "XMLHttpRequest",
            StringComparison.OrdinalIgnoreCase);
    }
}
