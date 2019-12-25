using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SymbolCollector.Core;

namespace SymbolCollector.Server
{
    [Route("/image")]
    public class SymbolsController : Controller
    {
        private readonly long _fileSizeLimit;
        private readonly ISymbolGcsWriter _gcsWriter;
        private readonly ILogger<SymbolsController> _logger;
        private readonly char[] _invalidChars;
        private static long _filesCreated = 0;

        private static readonly FormOptions DefaultFormOptions = new FormOptions();

        public SymbolsController(IConfiguration config, ISymbolGcsWriter gcsWriter, ILogger<SymbolsController> logger)
        {
            _gcsWriter = gcsWriter;
            _logger = logger;
            _fileSizeLimit = config.GetValue<long>("FileSizeLimitBytes");
            // Don't allow file names with paths encoded.
            _invalidChars = Path.GetInvalidFileNameChars().Concat(new[] {'/', '\\'}).ToArray();
        }

        // TODO: HEAD to verify image is needed
        // TODO: Get to give status

        [HttpGet]
        public string Get() => "Received: " + _filesCreated;

        [HttpPut]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> UploadSymbol(CancellationToken token)
        {
            _logger.LogDebug("/image endpoint called by {userAgent}",
                Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown");

            if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                return BadRequest(ModelState);
            }

            var boundary = MultipartRequestHelper.GetBoundary(
                MediaTypeHeaderValue.Parse(Request.ContentType),
                DefaultFormOptions.MultipartBoundaryLengthLimit);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync(token);

            if (section == null)
            {
                return BadRequest("No file received.");
            }

            var filesCreated = 0;
            var sectionsCount = 0;
            var invalidContentDispositions = new List<string>();
            while (section != null)
            {
                sectionsCount++;
                if (ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition, out var contentDisposition) && MultipartRequestHelper
                        .HasFileContentDisposition(contentDisposition))
                {
                    var (fileName, data) =
                        await ProcessStreamedFile(section, contentDisposition, ModelState, _fileSizeLimit);

                    if (!ModelState.IsValid)
                    {
                        return BadRequest(ModelState);
                    }

                    _logger.LogInformation(
                        "Persisting file '{fileName}' with {bytes} bytes.", fileName, data.Length);

                    // TODO: Process the image: do we have it already? is it valid?

                    await _gcsWriter.WriteAsync(fileName, data, token);
                    await data.DisposeAsync();
                    filesCreated++;
                }
                else
                {
                    invalidContentDispositions.Add(section.ContentDisposition);
                    _logger.LogWarning("ContentDisposition not supported: {contentDisposition}", section.ContentDisposition);
                }

                section = await reader.ReadNextSectionAsync(token);
            }

            if (filesCreated == 0)
            {
                return BadRequest(new
                {
                    errorMessage="Invalid request. No file accepted.",
                    numberOfSectionsReceived=sectionsCount,
                    invalidContentDispositions
                });
            }

            _logger.LogInformation("------------------ Created: " + Interlocked.Increment(ref _filesCreated));

            return Created(nameof(SymbolsController), new { filesCreated });
        }

        public async ValueTask<(string fileName, Stream data)> ProcessStreamedFile(
            MultipartSection section, ContentDispositionHeaderValue contentDisposition,
            ModelStateDictionary modelState, long sizeLimit)
        {
            var memoryStream = new MemoryStream();
            string? fileName = null;
            try
            {
                fileName = contentDisposition.FileName.Value;
                await section.Body.CopyToAsync(memoryStream);
                memoryStream.Position = 0; // TODO: needs rewinding?

                if (memoryStream.Length == 0)
                {
                    modelState.AddModelError("File", "The file is empty.");
                }
                else if (memoryStream.Length > sizeLimit)
                {
                    var megabyteSizeLimit = sizeLimit / 1048576;
                    modelState.AddModelError("File", $"The file exceeds {megabyteSizeLimit:N1} MB.");
                    _logger.LogWarning("File name {fileName} is too large: {size} MB.", fileName, megabyteSizeLimit);
                }
                else if (fileName.Any(p => _invalidChars.Contains(p)))
                {
                    ModelState.AddModelError("File", "The file name specified contain invalid characters.");
                    _logger.LogWarning("File name {fileName} contain invalid characters.", fileName);
                }
                else
                {
                    return (fileName, memoryStream);
                }
            }
            catch (Exception ex)
            {
                modelState.AddModelError("File", $"The upload failed. Error: {ex.HResult}");
                _logger.LogError(ex, "Failed to process file {file}", fileName);
            }

            return ("error", memoryStream);
        }
    }

    // Copy pasta from: https://github.com/aspnet/AspNetCore.Docs/blob/master/aspnetcore/mvc/models/file-uploads/samples/2.x/SampleApp/Utilities/MultipartRequestHelper.cs
    public static class MultipartRequestHelper
    {
        // Content-Type: multipart/form-data; boundary="----WebKitFormBoundarymx2fSWqWSd0OxQqq"
        // The spec at https://tools.ietf.org/html/rfc2046#section-5.1 states that 70 characters is a reasonable limit.
        public static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit)
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;

            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new InvalidDataException("Missing content-type boundary.");
            }

            if (boundary.Length > lengthLimit)
            {
                throw new InvalidDataException(
                    $"Multipart boundary length limit {lengthLimit} exceeded.");
            }

            return boundary;
        }

        public static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                   && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool HasFileContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            // Content-Disposition: form-data; name="myfile1"; filename="Misc 002.jpg"
            return contentDisposition != null
                   && contentDisposition.DispositionType.Equals("form-data")
                   && (!string.IsNullOrEmpty(contentDisposition.FileName.Value)
                       || !string.IsNullOrEmpty(contentDisposition.FileNameStar.Value));
        }
    }
}
