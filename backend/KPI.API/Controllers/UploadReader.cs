using KPI.Application.Common;
using KPI.Application.Issues;
using KPI.Domain.Exceptions;
using KPI.Infrastructure.Services;

namespace KPI.API.Controllers;

internal static class UploadReader
{
    /// <summary>Reads uploaded files into memory, refusing oversized ones before they are read.</summary>
    public static async Task<List<IssueUpload>> ReadAsync(IFormFileCollection? files)
    {
        var uploads = new List<IssueUpload>();
        if (files is null) return uploads;

        foreach (var file in files)
        {
            if (file.Length > IssueService.MaxAttachmentBytes)
                throw new RequestValidationException(
                    $"ไฟล์ \"{file.FileName.ToSafeFileName()}\" ใหญ่เกิน {IssueService.MaxAttachmentBytes / (1024 * 1024)} MB");

            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            uploads.Add(new IssueUpload(file.FileName, file.ContentType, buffer.ToArray()));
        }
        return uploads;
    }
}
